// =============================================================
// ExcelExporter.cs  — Export du rapport Excel multi-feuilles
// Équivalent Python: export_excel()
// Dépendance NuGet: ClosedXML
//
// What Generate Layout placed, one sheet per subject: a summary, the palonniers, the mother lines and
// each of their bays, the charges hung straight on a line, where each palonnier hangs, the loads at the
// attachment points, the stock pieces used, and what remains to be checked.
//
// Every coordinate here is in the studies' axes (Helpers.StudyAxes): origin E5, X along the beams,
// positive towards point 9, Y across them, positive towards beam J. The engine works in the grid's axes;
// they are converted here, where they meet a reader, and nowhere else.
//
// Every check shown is the engine's own: a bay's load is Mother.BayLoads (the charges and the
// palonniers' own weight strictly between two neighbouring beams), its value the AllowedLoad the line
// was sized with (LoadTable), a palonnier's value its Allowed at its span A–B. Nothing the engine
// decided is recomputed here.
//
// Numbers are written unrounded and shown through the cell's format, so the only rounding a reader sees
// is Excel's, once. The figures written into sentences round the way Excel does (half away from zero),
// so a sentence and a cell never disagree.
// =============================================================

using System.Globalization;
using ClosedXML.Excel;
using Helpers;
using RiggingOptimizer.Core;
using RiggingOptimizer.Models;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.IO;

public static class ExcelExporter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // Couleurs
    private static readonly XLColor ColorHeader  = XLColor.FromHtml("#1a237e");
    private static readonly XLColor ColorOk      = XLColor.FromHtml("#c8e6c9");
    private static readonly XLColor ColorWarn    = XLColor.FromHtml("#fff9c4");
    private static readonly XLColor ColorErr     = XLColor.FromHtml("#ffcdd2");
    private static readonly XLColor ColorSection = XLColor.FromHtml("#e8eaf6");
    private static readonly XLColor ColorCaption = XLColor.FromHtml("#555555");
    private static readonly XLColor ColorWhite   = XLColor.White;

    // Excel number formats. The codes are written the invariant way and Excel shows them with the
    // reader's own separators: "#,##0" reads "4 237" in French.
    private const string FmtKg  = "#,##0";
    private const string FmtM   = "0.000";
    private const string FmtLen = "0.00";
    private const string FmtPct = "0.0%";

    private const string AxesNote =
        "Coordonnées en m, dans les axes des études : origine E5, X le long des poutres (positif vers le point 9), "
        + "Y en travers des poutres (positif vers la poutre J).";

    public static void Export(RiggingResult result, string outPath)
    {
        using var wb = new XLWorkbook();

        var warnings = CollectWarnings(result);

        AddSheetSummary      (wb, result, warnings.Count);
        AddSheetPalonniers   (wb, result);
        AddSheetMothers      (wb, result);
        AddSheetBays         (wb, result);
        AddSheetDirectCharges(wb, result);
        AddSheetAccrochages  (wb, result);
        AddSheetGridLoads    (wb, result);
        AddSheetStock        (wb, result);
        AddSheetWarnings     (wb, warnings);

        wb.SaveAs(outPath);
        Console.WriteLine($"✅ Excel: {outPath}");
    }

    // ── Feuille 1: Synthèse ───────────────────────────────────────
    private static void AddSheetSummary(XLWorkbook wb, RiggingResult r, int warningCount)
    {
        var ws = wb.Worksheets.Add("Synthèse");

        var title = ws.Cell(1, 1);
        title.Value = "Implantation générée — Antarès";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        Caption(ws.Cell(2, 1), AxesNote);
        Caption(ws.Cell(3, 1), "Charges en kg. Les charges aux points d'accroche sont les charges scéniques seules : "
                               + "le poids propre des palonniers et des mothers n'y est pas compris.");

        int nY      = r.Palonniers.Count(p => p.Direction == PalonnierDirection.Y);
        int nB      = r.Palonniers.Count(p => p.Direction == PalonnierDirection.Biais);
        int nOnPal  = r.Palonniers.Sum(p => p.Charges.Count);
        int nDirect = r.Mothers.Sum(m => m.DirectCharges.Count);
        double carried = CarriedKg(r);
        var (palUsed, palTotal) = PiecesUsed(r.PalsStock);
        var (momUsed, momTotal) = PiecesUsed(r.MomsStock);

        int row = 5;
        void Line(string label, object? value, string? format = null, XLColor? fill = null)
        {
            var name = ws.Cell(row, 1);
            name.Value = label;
            name.Style.Font.Bold = true;

            var cell = ws.Cell(row, 2);
            if (value is not null) cell.Value = XLCellValue.FromObject(value);
            if (format is not null) cell.Style.NumberFormat.Format = format;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            foreach (var c in new[] { name, cell })
            {
                c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (fill is not null) c.Style.Fill.BackgroundColor = fill;
            }
            row++;
        }

        // A remark between two blocks, in the same italic grey as the sheet's own captions on rows 1–3.
        // Written in column 1 alone, where it spills over the empty cell beside it rather than squeezing
        // the two columns the figures need.
        void Remark(string text)
        {
            Caption(ws.Cell(row, 1), text);
            row++;
        }

        Line("Charges", r.Charges.Count);
        Line("Poids total des charges (kg)", N(r.TotalCharges), FmtKg);
        Line("Charges sur palonnier", nOnPal);
        Line("Charges directement sur une ligne de mother", nDirect);
        Line("Charges non portées", r.Uncovered.Count, fill: r.Uncovered.Count > 0 ? ColorErr : null);
        row++;
        Line("Lignes de mother", r.Mothers.Count);
        Line("Palonniers le long des poutres", nY);
        Line("Palonniers inclinés", nB);
        Line("Orientations orthogonales uniquement", r.Options.OrthogonalOnly ? "oui" : "non");
        row++;
        Line("Charges portées (kg)", N(carried), FmtKg);
        Line("Total aux points d'accroche (kg)", N(r.TotalGrid), FmtKg);
        if (NothingToBalance(r))
            Line("Équilibre", "sans objet : aucune charge");
        else
            Line("Équilibre", r.BalanceOk ? "vérifié" : $"écart de {KgText(r.TotalGrid - carried)} kg",
                 fill: r.BalanceOk ? ColorOk : ColorErr);
        row++;
        Line("Pièces de palonnier utilisées", $"{palUsed} sur {palTotal}");
        Line("Pièces de mother utilisées", $"{momUsed} sur {momTotal}");
        Line("Avertissements (feuille Avertissements)", warningCount, fill: warningCount > 0 ? ColorWarn : ColorOk);

        // ── Ce que coûte cette implantation ───────────────────────
        // The quantities the optimiser's own ranking compares (Objective.LayoutCost), read off the result
        // and never re-counted here: this sheet, the console line of Generate Layout and a comparison
        // between two runs then quote one and the same figure. A few of them appear above under their own
        // names; they are repeated here so that the block is the whole cost and not a part of it.
        var cost = r.Cost;
        row++;
        Remark("Coût mesuré de cette implantation : les quantités que le classement de l'optimiseur compare, "
               + "mesurées une seule fois sur le résultat. Certaines figurent déjà ci-dessus sous leur propre "
               + "nom ; elles sont reprises ici pour que le coût soit complet.");
        Line("Palonniers (nombre)", cost.Palonniers);
        Line("Lignes de mother tracées (nombre)", cost.Lines);
        Line("Pièces assemblées (nombre)", cost.Pieces);
        Line("Longueur tracée (m)", N(cost.DrawnMetres), FmtLen);
        Line("Longueur assemblée, sortie du stock (m)", N(cost.AssembledMetres), FmtLen);
        Line("Masse de poutre suspendue (kg)", N(cost.TrussMassKg), FmtKg);
        Line("Travée la plus chargée (% de la charge admise)", N(cost.WorstBayUse), FmtPct);
        Line("Charges sur palonnier (nombre)", cost.ChargesOnPalonniers);
        Line("Charges en direct sur une ligne (nombre)", cost.ChargesDirect);

        // ── Le plancher théorique et l'écart ──────────────────────
        // Bounds.cs computes these per run and they are carried on the result; a run asked not to compute
        // them (OptimizerOptions.WithBounds) leaves Bound null, and then the sheet says so rather than
        // showing zeros a reader would take for a floor.
        row++;
        Remark("Plancher théorique : le minimum au-dessous duquel aucune implantation de ce plan ne peut "
               + "descendre. Ce n'est pas la preuve qu'une meilleure implantation existe, et ce n'est pas un "
               + "objectif : un plancher dit seulement qu'on ne peut pas faire moins. L'écart au plancher est "
               + "donc ce que cette réponse a peut-être laissé sur la table, jamais ce qu'elle a perdu.");
        if (r.Bound is { } bound)
        {
            var gap = bound.Gap(cost);
            Line("Palonniers, au moins (nombre)", bound.Palonniers);
            Line("Pièces, au moins (nombre)", bound.Pieces);
            Line("Longueur de ligne de mother, au moins (m)", N(bound.MotherMetres), FmtLen);
            Line("Écart au plancher : palonniers (nombre)", gap.Palonniers);
            Line("Écart au plancher : pièces (nombre)", gap.Pieces);
            Line("Écart au plancher : longueur tracée (m)", N(gap.Metres), FmtLen);
            Remark("L'écart en mètres compare toute la longueur tracée — palonniers compris — au plancher des "
                   + "seules lignes de mother, faute d'un plancher honnête pour la portée d'un palonnier. "
                   + "C'est donc une limite haute de ce qui pourrait être gagné : l'essentiel de cet écart est "
                   + "de la longueur de palonnier qu'il fallait bien tracer.");
        }
        else
        {
            Line("Plancher théorique", "non calculé pour cette exécution");
        }

        ws.Column(1).Width = 46;
        ws.Column(2).Width = 22;
    }

    // ── Feuille 2: Palonniers ─────────────────────────────────────
    private static void AddSheetPalonniers(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Palonniers",
            "Un palonnier par ligne. Sa charge totale est comparée à la charge ponctuelle au milieu de son modèle, lue "
            + "à la longueur immédiatement supérieure à sa portée A–B (calculée à moment égal au-delà de la plus grande "
            + "longueur). RA et RB : ce que reprennent les lignes de mother à ses extrémités ; ses accroches aux lignes "
            + "croisées sont dans la feuille Accrochages. " + AxesNote,
            new Column("Palonnier"),
            new Column("Élément en base"),
            new Column("Direction"),
            new Column("Modèle"),
            new Column("Pièces"),
            new Column("Longueur assemblée (m)", FmtLen),
            new Column("Portée A–B (m)", FmtM),
            new Column("Charges portées", Width: 30, Wrap: true),
            new Column("Charge totale (kg)", FmtKg),
            new Column("Admissible sur A–B (kg)", FmtKg),
            new Column("Source de la valeur", Width: 46, Wrap: true),
            new Column("Utilisation", FmtPct),
            new Column("Poids propre (kg)", FmtKg),
            new Column("XA (m)", FmtM),
            new Column("YA (m)", FmtM),
            new Column("RA (kg)", FmtKg),
            new Column("XB (m)", FmtM),
            new Column("YB (m)", FmtM),
            new Column("RB (kg)", FmtKg),
            new Column("Ligne en A"),
            new Column("Ligne en B"),
            new Column("Accroché aux lignes", Width: 30, Wrap: true),
            new Column("Lignes croisées non accrochées", Width: 32, Wrap: true),
            new Column("État", Width: 28, Wrap: true));

        if (result.Palonniers.Count == 0) t.Note("Aucun palonnier.");

        for (int i = 0; i < result.Palonniers.Count; i++)
        {
            var p = result.Palonniers[i];
            var (xa, ya) = StudyAxes.ToStudy(p.A.X, p.A.Y);
            var (xb, yb) = StudyAxes.ToStudy(p.B.X, p.B.Y);

            var allowed = p.Allowed;
            double? allowedKg = allowed is { Known: true, Kg: { } kg } ? kg : null;
            var (state, fill) = allowedKg is null ? ("sans valeur admise", ColorErr)
                              : allowed!.Carries(p.TotalWeight) ? ("dans la valeur admise", ColorOk)
                              : ("au-delà de la valeur admise", ColorErr);

            // Every line it hangs from, from A to B. Before Optimizer.HangOnCrossings has run there are only its ends.
            var hungFrom = p.Supports.Count > 0
                ? p.Supports.Select(s => s.Mother)
                : new[] { p.MotherA, p.MotherB }.OfType<Mother>();

            t.Row(new object?[]
            {
                PalName(p),
                p.Bar is null ? "" : ElementName(p.Bar, i),
                DirectionText(p.Direction),
                p.Bar?.Type ?? "",
                p.Assembly?.Describe() ?? "",
                N(p.Assembly?.LengthM ?? p.Bar?.Length),
                N(p.Span),
                string.Join(", ", p.Names),
                N(p.TotalWeight),
                N(allowed?.Kg),
                SourceText(allowed),
                allowedKg is { } a ? N(p.TotalWeight / a) : "sans valeur",
                SelfWeight(p.Assembly, p.Bar),
                N(xa), N(ya), N(p.RA),
                N(xb), N(yb), N(p.RB),
                LineRef(p.MotherA),
                LineRef(p.MotherB),
                string.Join(", ", hungFrom.Select(m => LineRef(m))),
                string.Join(", ", p.Skipped.Select(k => LineRef(k.Mother))),
                state,
            }, fill);
        }

        t.Finish();
    }

    // ── Feuille 3: Lignes de mother ───────────────────────────────
    private static void AddSheetMothers(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Lignes de mother",
            "Une ligne de mother par ligne : continue au-dessus d'une ligne de points, en travers des poutres, appuyée sur "
            + "chaque poutre qu'elle croise. Chaque travée, entre deux poutres voisines, est vérifiée seule : la somme de "
            + "ses charges (charges scéniques et poids propre des palonniers) contre la charge ponctuelle au milieu du "
            + "modèle à la longueur immédiatement supérieure à la travée ; le détail est dans la feuille Travées. Y1 : "
            + "l'extrémité du côté de la première poutre, Y2 : celle du côté de la dernière. " + AxesNote,
            new Column("Ligne"),
            new Column("Élément en base"),
            new Column("Modèle"),
            new Column("Pièces"),
            new Column("Longueur assemblée (m)", FmtLen),
            new Column("Ligne de points"),
            new Column("X (m)", FmtM),
            new Column("Première poutre"),
            new Column("Dernière poutre"),
            new Column("Y1 (m)", FmtM),
            new Column("Y2 (m)", FmtM),
            new Column("Longueur dessinée (m)", FmtLen),
            new Column("Travées : charge / admissible", Width: 40, Wrap: true),
            new Column("Utilisation max", FmtPct),
            new Column("Travée la plus sollicitée"),
            new Column("Porte-à-faux (kg)", FmtKg),
            new Column("Charges directes"),
            new Column("Palonniers accrochés"),
            new Column("Charge portée (kg)", FmtKg),
            new Column("Charges scéniques aux poutres (kg)", Width: 36, Wrap: true),
            new Column("Poids propre (kg)", FmtKg),
            new Column("État", Width: 30, Wrap: true));

        if (result.Mothers.Count == 0) t.Note("Aucune ligne de mother.");

        for (int i = 0; i < result.Mothers.Count; i++)
        {
            var m = result.Mothers[i];
            var check = LineCheck.Of(m);
            int hung = result.Palonniers.Count(p => p.Supports.Any(s => ReferenceEquals(s.Mother, m)));

            object? utilisation = check.Unverified ? "sans valeur"
                                : check.Bays.Count == 0 ? null
                                : N(check.MaxRatio);

            t.Row(new object?[]
            {
                LineName(m),
                m.Bar is null ? "" : ElementName(m.Bar, i),
                m.Bar?.Type ?? "",
                m.Assembly?.Describe() ?? "",
                N(m.Assembly?.LengthM ?? m.Bar?.Length),
                m.Row,
                N(StudyAxes.StudyX(m.Y)),
                m.ColL,
                m.ColR,
                N(StudyAxes.StudyY(m.XL)),
                N(StudyAxes.StudyY(m.XR)),
                N(m.Length),
                string.Join("\n", check.Bays.Select(BayText)),
                utilisation,
                check.Worst?.Label ?? "",
                N(check.Overhangs.Sum(o => o.Load)),
                m.DirectCharges.Count,
                hung,
                N(m.DownwardLoad),
                string.Join("\n", m.Columns.Select(c =>
                    $"{c.Col}{m.Row} : {KgText(m.ColumnLoads.GetValueOrDefault(c.Col))}")),
                SelfWeight(m.Assembly, m.Bar),
                check.State,
            }, check.Fill);
        }

        t.Finish();
    }

    // ── Feuille 4: Travées ────────────────────────────────────────
    private static void AddSheetBays(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Travées",
            "Chaque travée de chaque ligne de mother, entre deux poutres voisines, puis ce qui pend au-delà de sa première "
            + "ou de sa dernière poutre (porte-à-faux). Charge : les charges scéniques et le poids propre des palonniers "
            + "entre les deux poutres ; une charge à moins de "
            + $"{(Mother.OnSupportM * 100).ToString("0.#", Fr)} cm d'une poutre est sur la poutre et ne charge aucune "
            + "travée. " + AxesNote,
            new Column("Ligne"),
            new Column("Modèle"),
            new Column("Ligne de points"),
            new Column("Travée"),
            new Column("Y1 (m)", FmtM),
            new Column("Y2 (m)", FmtM),
            new Column("Portée (m)", FmtM),
            new Column("Charge (kg)", FmtKg),
            new Column("Admissible (kg)", FmtKg),
            new Column("Source de la valeur", Width: 46, Wrap: true),
            new Column("Utilisation", FmtPct),
            new Column("État", Width: 28, Wrap: true));

        if (result.Mothers.Count == 0) t.Note("Aucune ligne de mother.");

        foreach (var m in result.Mothers)
        {
            var check = LineCheck.Of(m);
            foreach (var b in check.Bays)
            {
                var (state, fill) = BayState(b);
                t.Row(new object?[]
                {
                    LineName(m),
                    m.Bar?.Type ?? "",
                    m.Row,
                    b.Label,
                    N(b.Y1),
                    N(b.Y2),
                    N(b.SpanM),
                    N(b.Load),
                    N(b.Allowed?.Kg),
                    SourceText(b.Allowed),
                    b.Ratio is { } ratio ? N(ratio) : b.Loaded ? "sans valeur" : null,
                    state,
                }, fill);
            }

            foreach (var o in check.Overhangs)
            {
                t.Row(new object?[]
                {
                    LineName(m),
                    m.Bar?.Type ?? "",
                    m.Row,
                    $"porte-à-faux au-delà de {o.Beam}",
                    N(StudyAxes.StudyY(o.BeamX)),
                    N(StudyAxes.StudyY(o.FarX)),
                    N(o.ReachM),
                    N(o.Load),
                    null,
                    "aucune valeur : l'inventaire donne la charge ponctuelle au milieu d'une travée, pas de valeur en porte-à-faux",
                    null,
                    "non vérifié",
                }, ColorWarn);
            }
        }

        t.Finish();
    }

    private static (string State, XLColor? Fill) BayState(BayCheck b)
    {
        if (b.Over) return ("au-delà de la valeur admise", ColorErr);
        if (b.Unverified) return ("sans valeur admise", ColorErr);
        if (b.Loaded) return ("dans la valeur admise", ColorOk);
        return ("non chargée", null);
    }

    // ── Feuille 5: Charges sur mother ─────────────────────────────
    private static void AddSheetDirectCharges(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Charges sur mother",
            "Les charges accrochées directement à une ligne de mother, sans palonnier : celles qui se trouvent à moins de "
            + $"{(TOL * 100).ToString("0.#", Fr)} cm d'une ligne de points. Position sur la ligne : sa distance depuis "
            + "l'extrémité Y1 de la ligne, comme elle est enregistrée. Report aux points : ce que la charge donne à "
            + "chaque poutre de la ligne, par bras de levier (négatif : soulèvement). " + AxesNote,
            new Column("Charge"),
            new Column("Poids (kg)", FmtKg),
            new Column("Ligne"),
            new Column("Ligne de points"),
            new Column("X (m)", FmtM),
            new Column("Y (m)", FmtM),
            new Column("Écart à la ligne de points (m)", FmtM),
            new Column("Position sur la ligne (m)", FmtM),
            new Column("Emplacement"),
            new Column("Report aux points (kg)", Width: 30, Wrap: true));

        int count = 0;
        foreach (var m in result.Mothers)
        {
            foreach (var c in m.DirectCharges)
            {
                var (x, y) = StudyAxes.ToStudy(c.X, c.Y);
                t.Row(new object?[]
                {
                    c.Name,
                    N(c.Weight),
                    LineName(m),
                    m.Row,
                    N(x),
                    N(y),
                    N(Math.Abs(c.Y - m.Y)),
                    N(c.X - m.XL),          // the line is stored from XL (Pipeline.BuildChargeMps)
                    PlaceOnLine(m, c.X),
                    string.Join("\n", m.Shares(c.X).Select(s => $"{s.Col}{m.Row} : {KgText(c.Weight * s.Share)}")),
                });
                count++;
            }
        }

        if (count == 0) t.Note("Aucune charge accrochée directement à une ligne de mother.");
        t.Finish();
    }

    // ── Feuille 6: Accrochages ────────────────────────────────────
    private static void AddSheetAccrochages(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Accrochages",
            "Où pend chaque palonnier : ses extrémités A et B et chaque ligne de mother croisée entre elles à laquelle il "
            + "est accroché ; chaque charge va aux deux accroches qui l'encadrent. Réaction : sa part des charges "
            + "scéniques ; poids propre repris : sa part du poids propre du palonnier. Suivent les lignes croisées "
            + "auxquelles il n'est pas accroché, avec la raison. " + AxesNote,
            new Column("Palonnier"),
            new Column("Direction"),
            new Column("Modèle"),
            new Column("Charges", Width: 30, Wrap: true),
            new Column("Accroche"),
            new Column("X (m)", FmtM),
            new Column("Y (m)", FmtM),
            new Column("Distance depuis A (m)", FmtM),
            new Column("Réaction (kg)", FmtKg),
            new Column("Poids propre repris (kg)", FmtKg),
            new Column("Ligne"),
            new Column("Modèle de la ligne"),
            new Column("Ligne de points"),
            new Column("Poutres de la ligne"),
            new Column("Emplacement sur la ligne"),
            new Column("Remarque", Width: 50, Wrap: true));

        if (result.Palonniers.Count == 0) t.Note("Aucun palonnier.");

        foreach (var p in result.Palonniers)
        {
            string dir = DirectionText(p.Direction);
            string names = string.Join(", ", p.Names);

            // Every line it hangs from: its two ends and each line crossed between them. Before
            // Optimizer.HangOnCrossings has run there are only the two ends.
            var hooks = new List<(string Label, double X, double Y, double S, double R, double? W, Mother Line)>();
            if (p.Supports.Count > 0)
            {
                foreach (var s in p.Supports) hooks.Add((HookLabel(p, s), s.X, s.Y, s.S, s.R, s.W, s.Mother));
            }
            else
            {
                if (p.MotherA is not null) hooks.Add(("A", p.A.X, p.A.Y, 0, p.RA, null, p.MotherA));
                if (p.MotherB is not null) hooks.Add(("B", p.B.X, p.B.Y, p.Span, p.RB, null, p.MotherB));
            }

            foreach (var h in hooks)
            {
                var (x, y) = StudyAxes.ToStudy(h.X, h.Y);
                t.Row(new object?[]
                {
                    PalName(p), dir, p.Bar?.Type ?? "", names,
                    h.Label, N(x), N(y), N(h.S), N(h.R), N(h.W),
                    LineName(h.Line), h.Line.Bar?.Type ?? "", h.Line.Row, $"{h.Line.ColL}–{h.Line.ColR}",
                    PlaceOnLine(h.Line, h.X), "",
                });
            }

            // Lines it crosses but is not hung from, because a bay would have gone past its value. The engine
            // records how far along the palonnier the crossing is; the point is found from that.
            foreach (var k in p.Skipped)
            {
                double along = p.Span > 1e-9 ? k.S / p.Span : 0;
                double gx = p.A.X + along * (p.B.X - p.A.X), gy = p.A.Y + along * (p.B.Y - p.A.Y);
                var (x, y) = StudyAxes.ToStudy(gx, gy);
                t.Row(new object?[]
                {
                    PalName(p), dir, p.Bar?.Type ?? "", names,
                    "non accroché", N(x), N(y), N(k.S), null, null,
                    LineName(k.Mother), k.Mother.Bar?.Type ?? "", k.Mother.Row, $"{k.Mother.ColL}–{k.Mother.ColR}",
                    PlaceOnLine(k.Mother, gx), SkipReason(k),
                }, ColorWarn);
            }
        }

        t.Finish();
    }

    private static string HookLabel(Palonnier p, PalonnierSupport s) =>
        ReferenceEquals(s.Mother, p.MotherA) && Math.Abs(s.S) < 1e-6 ? "A"
        : ReferenceEquals(s.Mother, p.MotherB) && Math.Abs(s.S - p.Span) < 1e-6 ? "B"
        : "croisement";

    private static string SkipReason(SkippedCrossing k)
    {
        if (k.WouldCarryKg <= 0) return "non accroché au placement";
        string bay = ReferenceEquals(k.Overloaded, k.Mother)
            ? "une travée de cette ligne"
            : $"une travée de {LineRef(k.Overloaded)}";
        return $"{bay} aurait porté {KgText(k.WouldCarryKg)} kg pour {KgText(k.CapacityKg)} kg admissibles";
    }

    // ── Feuille 7: Points d'accroche ──────────────────────────────
    private static void AddSheetGridLoads(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Points grille",
            "La charge scénique à chaque point d'accroche de la charpente (poutre et ligne de points), la plus forte "
            + "d'abord, et ce que chaque ligne de mother y apporte. Le poids propre des palonniers et des mothers n'y est "
            + "pas compris. " + AxesNote,
            new Column("Point"),
            new Column("Poutre"),
            new Column("Ligne de points"),
            new Column("X (m)", FmtM),
            new Column("Y (m)", FmtM),
            new Column("Charge scénique (kg)", FmtKg),
            new Column("Apports des lignes (kg)", Width: 30, Wrap: true),
            new Column("Remarque"));

        if (result.GridLoads.Count == 0) t.Note("Aucun point d'accroche chargé.");

        foreach (var ((col, gridRow), load) in result.GridLoads
                     .OrderByDescending(kv => kv.Value)
                     .ThenBy(kv => kv.Key.Col, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Row))
        {
            object? x = null, y = null;
            if (Grid.TryGetValue((col, gridRow), out var g))
            {
                var (sx, sy) = StudyAxes.ToStudy(g.X, g.Y);
                (x, y) = (N(sx), N(sy));
            }

            var contributions = result.Mothers
                .Where(m => m.Row == gridRow && Math.Abs(m.ColumnLoads.GetValueOrDefault(col)) > 0.5)
                .Select(m => $"{LineName(m)} : {KgText(m.ColumnLoads[col])}");

            bool uplift = load < -0.5;
            t.Row(new object?[]
            {
                $"{col}{gridRow}", col, gridRow, x, y, N(load),
                string.Join("\n", contributions),
                uplift ? "soulèvement" : "",
            }, uplift ? ColorWarn : null);
        }

        double carried = CarriedKg(result);

        // The gap is written as it is shown, to the kilogram, like the summary's sentence: the engine rounds
        // each point to the gram, so the unrounded gap is a few grams either way and would read "-0".
        var gap = N(Math.Round(result.TotalGrid - carried, MidpointRounding.AwayFromZero));
        t.Blank();
        t.Total("Total aux points d'accroche", 6, N(result.TotalGrid), FmtKg);
        t.Total("Charges portées", 6, N(carried), FmtKg);
        if (NothingToBalance(result))
            t.Total("Écart (aucune charge)", 6, gap, FmtKg);
        else
            t.Total(result.BalanceOk ? "Écart (équilibre vérifié)" : "Écart (déséquilibre)", 6,
                    gap, FmtKg, result.BalanceOk ? ColorOk : ColorErr);

        t.Finish();
    }

    // ── Feuille 8: Stock barres ───────────────────────────────────
    private static void AddSheetStock(XLWorkbook wb, RiggingResult result)
    {
        var t = new Table(wb, "Stock barres",
            "Les pièces en stock après le placement : les lignes des fichiers d'inventaire dont le Nombre dépasse 0. "
            + "Utilisées : les pièces prises par les palonniers et les lignes de mother, qui en assemblent parfois "
            + "plusieurs. Les lignes de Nombre = 0 ne donnent qu'une valeur à la table des charges et ne sont pas des "
            + "pièces : elles ne figurent pas ici. Les lignes épuisées sont surlignées.",
            new Column("Modèle"),
            new Column("Longueur (m)", FmtLen),
            new Column("Nombre"),
            new Column("Utilisées"),
            new Column("Restantes"),
            new Column("Charge ponctuelle au milieu (kg)", FmtKg),
            new Column("Poids propre (kg)", FmtKg),
            new Column("État"));

        StockSection(t, $"Palonniers — {InventoryFiles.PalonniersFileName}", result.PalsStock);
        t.Blank();
        StockSection(t, $"Mothers — {InventoryFiles.MothersFileName}", result.MomsStock);

        t.Finish();
    }

    private static void StockSection(Table t, string title, IEnumerable<BarSpec> rows)
    {
        t.Section(title);

        var stock = rows
            .Where(b => b.StockInit > 0)
            .OrderBy(b => b.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Length)
            .ToList();
        if (stock.Count == 0)
        {
            t.Note("Aucune pièce en stock.");
            return;
        }

        foreach (var b in stock)
        {
            int left = Math.Max(0, b.Stock);
            int used = b.StockInit - left;
            bool exhausted = left == 0;
            t.Row(new object?[]
            {
                b.Type, N(b.Length), b.StockInit, used, left, N(b.Capacity),
                b.WeightKg is { } w ? N(w) : "inconnu",
                exhausted ? "épuisé" : "",
            }, exhausted ? ColorWarn : null);
        }

        t.Row(new object?[]
        {
            "Total", null,
            stock.Sum(b => b.StockInit),
            stock.Sum(b => b.StockInit - Math.Max(0, b.Stock)),
            stock.Sum(b => Math.Max(0, b.Stock)),
            null, null, null,
        }, bold: true);
    }

    // ── Feuille 9: Avertissements ─────────────────────────────────
    private static void AddSheetWarnings(XLWorkbook wb, List<(string Origin, string Message)> warnings)
    {
        var t = new Table(wb, "Avertissements",
            "Ce que l'optimiseur signale, puis ce que cet export relève et qui reste à vérifier.",
            new Column("Origine"),
            new Column("Message", Width: 120, Wrap: true));

        if (warnings.Count == 0) t.Note("Aucun avertissement.");
        foreach (var (origin, message) in warnings) t.Row(new object?[] { origin, message }, ColorWarn);

        t.Finish();
    }

    /// <summary>
    /// The optimiser's own messages, then what no value covers: a loaded bay or a palonnier without an allowed
    /// load, loads in cantilever, trusses of unknown weight, and uplift at an attachment point.
    /// </summary>
    private static List<(string Origin, string Message)> CollectWarnings(RiggingResult r)
    {
        const string optimiser = "optimiseur", export = "export";
        var list = r.Errors.Select(e => (optimiser, e)).ToList();

        foreach (var m in r.Mothers)
        {
            var check = LineCheck.Of(m);
            foreach (var b in check.Bays.Where(b => b.Unverified))
            {
                list.Add((export, $"{LineRef(m)}, travée {b.Label} ({MText(b.SpanM)} m) : {KgText(b.Load)} kg sans "
                                  + "valeur admise" + (b.Allowed?.Reason is { } why ? $" ({why})." : ".")));
            }
            foreach (var o in check.Overhangs)
            {
                list.Add((export, $"{LineRef(m)} : {KgText(o.Load)} kg en porte-à-faux au-delà de la poutre {o.Beam}, "
                                  + $"jusqu'à {MText(o.ReachM)} m ; l'inventaire ne donne pas de valeur en porte-à-faux : "
                                  + "à vérifier."));
            }
            if ((m.Assembly?.WeightKg ?? m.Bar?.WeightKg) is null)
                list.Add((export, $"{LineRef(m)} : poids propre inconnu pour au moins une de ses pièces ({m.Bar?.Type})."));
        }

        foreach (var p in r.Palonniers)
        {
            if (p.Allowed is not { Known: true })
            {
                list.Add((export, $"{PalName(p)} : aucune valeur admise sur sa portée de {MText(p.Span)} m"
                                  + (p.Allowed?.Reason is { } why ? $" ({why})." : ".")));
            }
            if ((p.Assembly?.WeightKg ?? p.Bar?.WeightKg) is null)
            {
                list.Add((export, $"{PalName(p)} : poids propre inconnu pour au moins une de ses pièces ({p.Bar?.Type}) ; "
                                  + "ce qui manque n'est pas compté dans la charge des lignes de mother auxquelles il "
                                  + "est accroché."));
            }
        }

        foreach (var ((col, row), load) in r.GridLoads.Where(kv => kv.Value < -0.5).OrderBy(kv => kv.Value))
        {
            list.Add((export, $"{col}{row} : charge scénique négative ({KgText(load)} kg), un soulèvement que les valeurs "
                              + "de l'inventaire ne couvrent pas."));
        }

        return list;
    }

    // ── Vérification d'une ligne de mother ───────────────────────
    /// <summary>One bay of a line: where it is, its span, what is in it and the value the line was sized with.</summary>
    private sealed record BayCheck(string Label, double Y1, double Y2, double SpanM, double Load, AllowedLoad? Allowed)
    {
        public bool Loaded => Load > 1e-9;
        public bool HasValue => Allowed is { Known: true };
        public double? Ratio => Allowed is { Known: true, Kg: { } kg } ? Load / kg : null;

        // Mother.WorstBay's reading: a loaded bay is within its value only when that value carries it.
        public bool Over => Loaded && HasValue && !Allowed!.Carries(Load);
        public bool Unverified => Loaded && !HasValue;
    }

    /// <summary>The loads hung past the first or the last beam of a line, in cantilever (grid X).</summary>
    private sealed record Overhang(string Beam, double BeamX, double FarX, double Load)
    {
        public double ReachM => Math.Abs(FarX - BeamX);
    }

    private sealed class LineCheck
    {
        private LineCheck(List<BayCheck> bays, List<Overhang> overhangs)
        {
            Bays = bays;
            Overhangs = overhangs;
        }

        public List<BayCheck> Bays { get; }
        public List<Overhang> Overhangs { get; }

        public bool Over => Bays.Any(b => b.Over);
        public bool Unverified => Bays.Any(b => b.Unverified);
        public bool Cantilever => Overhangs.Count > 0;

        public double MaxRatio => Bays.Select(b => b.Ratio ?? 0).DefaultIfEmpty(0).Max();

        /// <summary>A loaded bay without a value first, else the loaded bay nearest its value.</summary>
        public BayCheck? Worst => Bays.FirstOrDefault(b => b.Unverified)
                                  ?? Bays.Where(b => b.Loaded).OrderByDescending(b => b.Ratio ?? 0).FirstOrDefault();

        public string State
        {
            get
            {
                var parts = new List<string>();
                if (Over) parts.Add("au-delà de la valeur admise");
                if (Unverified) parts.Add("travée sans valeur admise");
                if (Cantilever) parts.Add("porte-à-faux non vérifié");
                return parts.Count == 0 ? "dans les valeurs admises" : string.Join(", ", parts);
            }
        }

        public XLColor Fill => Over || Unverified ? ColorErr : Cantilever ? ColorWarn : ColorOk;

        public static LineCheck Of(Mother m)
        {
            var loads = m.BayLoads();
            var bays = new List<BayCheck>();
            for (int k = 0; k < m.BayCount && k < loads.Length; k++)
            {
                var (c0, x0) = m.Columns[k];
                var (c1, x1) = m.Columns[k + 1];
                bays.Add(new BayCheck($"{c0}–{c1}", StudyAxes.StudyY(x0), StudyAxes.StudyY(x1), m.BayLength(k), loads[k],
                                      k < m.BayAllowed.Count ? m.BayAllowed[k] : null));
            }

            // As Mother.CantileverLoad adds them up: the points merged by position, downward loads only,
            // past the outermost beams by more than Mother.OnSupportM. Split by end, so each is named.
            var overhangs = new List<Overhang>();
            if (m.Columns.Count > 0)
            {
                var merged = m.Points
                    .GroupBy(p => Math.Round(p.X, 3))
                    .Select(g => (X: g.First().X, Load: g.Sum(p => p.F + p.W)))
                    .Where(p => p.Load > 0)
                    .ToList();

                var (firstCol, firstX) = m.Columns[0];
                var (lastCol, lastX) = m.Columns[^1];

                var before = merged.Where(p => p.X < firstX - Mother.OnSupportM).ToList();
                if (before.Count > 0)
                    overhangs.Add(new Overhang(firstCol, firstX, before.Min(p => p.X), before.Sum(p => p.Load)));

                var after = merged.Where(p => p.X > lastX + Mother.OnSupportM).ToList();
                if (after.Count > 0)
                    overhangs.Add(new Overhang(lastCol, lastX, after.Max(p => p.X), after.Sum(p => p.Load)));
            }

            return new LineCheck(bays, overhangs);
        }
    }

    // "F–E : 606 / 4 237 kg", as the studies write a bay's check.
    private static string BayText(BayCheck b)
    {
        if (b.Allowed is { Known: true, Kg: { } kg } allowed)
        {
            return $"{b.Label} : {KgText(b.Load)} / {KgText(kg)} kg"
                   + (allowed.Source == AllowedSource.Computed ? " (calculée)" : "");
        }
        return $"{b.Label} : {KgText(b.Load)} kg, aucune valeur admise";
    }

    private static string SourceText(AllowedLoad? a)
    {
        if (a is null) return "aucune valeur";
        return a.Source switch
        {
            AllowedSource.Table => $"inventaire : charge ponctuelle au milieu à {MText(a.RowLengthM)} m",
            AllowedSource.Computed => $"calculée à moment égal depuis la longueur de {MText(a.RowLengthM)} m "
                                      + $"({KgText(a.RowKg)} kg, poids propre {MText(a.KgPerM)} kg/m)",
            _ => "aucune valeur" + (string.IsNullOrWhiteSpace(a.Reason) ? "" : $" : {a.Reason}"),
        };
    }

    // Where a point of a line falls, by the rule Mother.BayLoads counts it with.
    private static string PlaceOnLine(Mother m, double gridX)
    {
        if (m.Columns.Count == 0) return "";
        foreach (var (col, x) in m.Columns)
            if (Math.Abs(gridX - x) <= Mother.OnSupportM) return $"sur la poutre {col}";
        if (gridX < m.Columns[0].X) return $"porte-à-faux au-delà de {m.Columns[0].Col}";
        if (gridX > m.Columns[^1].X) return $"porte-à-faux au-delà de {m.Columns[^1].Col}";
        for (int k = 0; k < m.BayCount; k++)
        {
            if (gridX > m.Columns[k].X && gridX < m.Columns[k + 1].X)
                return $"travée {m.Columns[k].Col}–{m.Columns[k + 1].Col}";
        }
        return "";
    }

    // ── Helpers ────────────────────────────────────────────────────
    private static string LineName(Mother m) => $"M{m.Id}";
    private static string PalName(Palonnier p) => $"P{p.Id}";
    private static string LineRef(Mother? m) => m is null ? "" : $"M{m.Id} (ligne {m.Row})";

    private static string DirectionText(PalonnierDirection d) =>
        d == PalonnierDirection.Biais ? "incliné" : "le long des poutres";

    // The name the element is stored under, as Pipeline.BuildElementEntities writes it, so that a row here can be
    // found on the MotherGrid page. Same format, same culture: both run in the same Generate Layout call.
    private static string ElementName(BarSpec bar, int index) => $"{bar.Type} L={bar.Length:F1}m #{index + 1}";

    // The truss's own weight: every piece's mass added up; "inconnu" when any piece's is not known.
    private static object SelfWeight(Assembly? assembly, BarSpec? bar) =>
        (assembly?.WeightKg ?? bar?.WeightKg) is { } w ? w : "inconnu";

    // What the placed palonniers and lines carry: every charge but those left uncovered.
    private static double CarriedKg(RiggingResult r) => r.TotalCharges - r.Uncovered.Sum(c => c.Weight);

    // A result with no charge at all has nothing to balance: said as such, not as a balance or an imbalance.
    private static bool NothingToBalance(RiggingResult r) => r.Charges.Count == 0 && r.GridLoads.Count == 0;

    private static (int Used, int Total) PiecesUsed(IEnumerable<BarSpec> stock)
    {
        var rows = stock.Where(b => b.StockInit > 0).ToList();
        return (rows.Sum(b => b.StockInit - Math.Max(0, b.Stock)), rows.Sum(b => b.StockInit));
    }

    /// <summary>
    /// A number for a cell, unrounded - its format rounds it for display, once; blank when there is none.
    /// Floating-point dust around zero (a balance of 1e-13 kg) is written as 0, so no cell reads "-0".
    /// </summary>
    private static object? N(double? value) =>
        value is { } v && double.IsFinite(v) ? (Math.Abs(v) < 1e-9 ? 0.0 : v) : null;

    // Rounded as Excel shows "#,##0": half away from zero.
    private static string KgText(double? kg) =>
        kg is { } v && double.IsFinite(v)
            ? (Math.Round(v, MidpointRounding.AwayFromZero) + 0.0).ToString("N0", Fr)
            : "?";

    private static string MText(double? metres) =>
        metres is { } v && double.IsFinite(v) ? v.ToString("0.##", Fr) : "?";

    private static void Caption(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontColor = ColorCaption;
    }

    private sealed record Column(string Header, string? Format = null, double? Width = null, bool Wrap = false);

    /// <summary>
    /// A sheet laid out as one table: a caption on row 1 saying what it holds, the headers on row 2 and the
    /// rows below them. Numbers are written as numbers, so they can be summed and filtered.
    /// </summary>
    private sealed class Table
    {
        private const int HeaderRow = 2;
        private const double MaxAutoWidth = 60;

        private readonly IXLWorksheet _ws;
        private readonly Column[] _cols;
        private int _row = HeaderRow + 1;

        public Table(XLWorkbook wb, string name, string caption, params Column[] cols)
        {
            _ws = wb.Worksheets.Add(name);
            _cols = cols;

            Caption(_ws.Cell(1, 1), caption);
            for (int j = 0; j < cols.Length; j++)
            {
                var cell = _ws.Cell(HeaderRow, j + 1);
                cell.Value = cols[j].Header;
                cell.Style.Fill.BackgroundColor = ColorHeader;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ColorWhite;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            _ws.SheetView.FreezeRows(HeaderRow);
        }

        public void Row(object?[] values, XLColor? fill = null, bool bold = false)
        {
            for (int j = 0; j < _cols.Length; j++)
            {
                var cell = _ws.Cell(_row, j + 1);
                var value = j < values.Length ? values[j] : null;
                bool number = value is double or int or long;

                if (value is not null) cell.Value = XLCellValue.FromObject(value);
                if (number && _cols[j].Format is { } format) cell.Style.NumberFormat.Format = format;

                cell.Style.Alignment.Horizontal = number ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                if (_cols[j].Wrap) cell.Style.Alignment.WrapText = true;
                if (fill is not null) cell.Style.Fill.BackgroundColor = fill;
                if (bold) cell.Style.Font.Bold = true;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            _row++;
        }

        public void Note(string text)
        {
            var cell = _ws.Cell(_row++, 1);
            cell.Value = text;
            cell.Style.Font.Italic = true;
        }

        public void Section(string text)
        {
            for (int j = 0; j < _cols.Length; j++)
                _ws.Cell(_row, j + 1).Style.Fill.BackgroundColor = ColorSection;
            var cell = _ws.Cell(_row++, 1);
            cell.Value = text;
            cell.Style.Font.Bold = true;
        }

        public void Blank() => _row++;

        public void Total(string label, int column, object? value, string? format = null, XLColor? fill = null)
        {
            var name = _ws.Cell(_row, 1);
            name.Value = label;
            name.Style.Font.Bold = true;

            var cell = _ws.Cell(_row, column);
            if (value is not null) cell.Value = XLCellValue.FromObject(value);
            if (format is not null) cell.Style.NumberFormat.Format = format;
            cell.Style.Font.Bold = true;

            if (fill is not null)
            {
                name.Style.Fill.BackgroundColor = fill;
                cell.Style.Fill.BackgroundColor = fill;
            }
            _row++;
        }

        /// <summary>Widths: fixed where given, else fitted from the headers down - the caption spills over instead.</summary>
        public void Finish()
        {
            for (int j = 0; j < _cols.Length; j++)
            {
                var column = _ws.Column(j + 1);
                if (_cols[j].Width is { } width)
                {
                    column.Width = width;
                    continue;
                }
                column.AdjustToContents(HeaderRow);
                column.Width = Math.Clamp(column.Width, 8, MaxAutoWidth);
            }
        }
    }
}
