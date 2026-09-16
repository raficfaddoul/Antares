// =============================================================
// Models.cs  — Modèles de données
// Équivalent Python: dicts charge/bar + classes Mother, Palonnier
//
// Coordinates here are the grid's (Helpers.StudyAxes): X across the beams, Y along them.
// =============================================================

using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.Models;

// ── Charge (point de charge) ─────────────────────────────────
public class Charge
{
    public string Name   { get; init; } = "";
    public double X      { get; init; }
    public double Y      { get; init; }
    public double Weight { get; init; }

    public override string ToString() => $"{Name}({X:F2},{Y:F2},{Weight:F0}kg)";
}

// ── Spécification d'une barre (Palonnier ou Mother) ──────────
public class BarSpec
{
    public string Type      { get; init; } = "";
    public double Length    { get; init; }
    public int    StockInit { get; init; }
    public int    Stock     { get; set; }
    public double Capacity  { get; init; }   // charge ponctuelle au milieu (kg)

    // Le reste de la ligne d'inventaire (MotherGrill.xlsx / Palonnier.xlsx).
    public double? WeightKg { get; init; }   // poids propre d'une barre (kg), null s'il n'est pas connu
    public string? Material { get; init; }
    public string? Brand    { get; init; }
    public string? Usage    { get; init; }

    public BarSpec Clone() => new()
    {
        Type      = Type,
        Length    = Length,
        StockInit = StockInit,
        Stock     = Stock,
        Capacity  = Capacity,
        WeightKg  = WeightKg,
        Material  = Material,
        Brand     = Brand,
        Usage     = Usage,
    };

    public override string ToString() => $"{Type} L={Length:F1}m cap={Capacity:F0}kg";
}

// ── Point de suspension sur une Mother ──────────────────────
public class SuspensionPoint
{
    public double X   { get; init; }
    public double F   { get; set; }     // force (kg)
    public double W   { get; init; }    // part du poids propre du palonnier (kg)
    public string Src { get; init; } = "";
}

// ── Mother : une ligne de mother au-dessus d'une ligne de points ─────
// As in the studies: one continuous line above a point line (1 to 9), across the beams, over as many
// bays as its loads need, hung at every beam it crosses and reaching past the outermost one where a load
// lies beyond it. Assembled from pieces of one model of MotherGrill.xlsx. Each bay is checked on its own:
// the sum of the loads between two neighbouring beams against the charge ponctuelle au milieu of the
// model at the length immediately above the bay (LoadTable).
public class Mother
{
    private static int _counter = 0;
    public static void ResetCounter() => _counter = 0;

    public int Id { get; } = ++_counter;

    /// <summary>The assembled truss as one bar: model, total length and mass; Capacity is its lowest bay value.</summary>
    public BarSpec Bar { get; set; } = null!;

    /// <summary>The stock pieces it is made of.</summary>
    public Assembly? Assembly { get; set; }

    public IReadOnlyList<BarSpec> Pieces =>
        Assembly?.Pieces ?? (Bar is null ? Array.Empty<BarSpec>() : new[] { Bar });

    public int    Row { get; init; }
    public double Y   { get; init; }    // y de la ligne de points

    /// <summary>The beams it rests on, left to right.</summary>
    public List<(string Col, double X)> Columns { get; init; } = new();

    public string ColL => Columns.Count > 0 ? Columns[0].Col : "";
    public string ColR => Columns.Count > 0 ? Columns[^1].Col : "";

    /// <summary>Its drawn ends, overhang included.</summary>
    public double XL { get; set; }
    public double XR { get; set; }
    public double Length => XR - XL;

    public List<SuspensionPoint> Points { get; } = new();

    /// <summary>Charges hung straight on the line, with no palonnier.</summary>
    public List<Charge> DirectCharges { get; } = new();

    /// <summary>What each bay may carry, at its span, for the chosen model: one entry per bay.</summary>
    public List<AllowedLoad> BayAllowed { get; } = new();

    public int BayCount => Math.Max(0, Columns.Count - 1);
    public double BayLength(int k) => Columns[k + 1].X - Columns[k].X;

    public double TotalLoad => Points.Sum(p => p.F);

    // Poids propre des palonniers accrochés à cette Mother (kg). L'étude le compte dans la charge
    // appliquée à la Mother : pour elle, c'est une charge comme une autre.
    public double SelfWeightLoad => Points.Sum(p => p.W);

    // La charge vers le bas, point par point, poids propre des palonniers compris. Un soulèvement en un
    // point ne libère aucune place ailleurs.
    public double DownwardLoad => Merge(Points).Sum(p => Math.Max(0, p.Load));

    /// <summary>A load this close to a beam is on it, and bends neither bay: the study's convention.</summary>
    public const double OnSupportM = 0.010;

    public double[] BayLoads() => BayLoadsOf(Points);

    /// <summary>The downward loads strictly inside each bay, as the study adds them up.</summary>
    public double[] BayLoadsOf(IEnumerable<SuspensionPoint> points)
    {
        var loads = new double[BayCount];
        foreach (var (x, load) in Merge(points))
        {
            if (load <= 0) continue;
            for (int k = 0; k < BayCount; k++)
            {
                if (x > Columns[k].X + OnSupportM && x < Columns[k + 1].X - OnSupportM)
                {
                    loads[k] += load;
                    break;
                }
            }
        }
        return loads;
    }

    /// <summary>The downward loads beyond the outermost beams: in cantilever, which no inventory value covers.</summary>
    public double CantileverLoad => Columns.Count == 0 ? 0 : Merge(Points)
        .Where(p => p.Load > 0 && (p.X < Columns[0].X - OnSupportM || p.X > Columns[^1].X + OnSupportM))
        .Sum(p => p.Load);

    /// <summary>The lowest value among its bays.</summary>
    public double Capacity => BayAllowed.Count == 0 ? (Bar?.Capacity ?? 0) : BayAllowed.Min(a => a.Kg ?? 0);

    /// <summary>Whether every loaded bay stays within its value with these points.</summary>
    public bool Fits(IEnumerable<SuspensionPoint> points) => WorstBay(points) is null;

    /// <summary>The first bay these points would take past its value, if any.</summary>
    public (int Bay, double Load, double Allowed)? WorstBay(IEnumerable<SuspensionPoint> points)
    {
        var loads = BayLoadsOf(points);
        (int, double, double)? worst = null;
        double worstRatio = 0;
        for (int k = 0; k < loads.Length; k++)
        {
            if (loads[k] <= 1e-9) continue;
            var allowed = k < BayAllowed.Count ? BayAllowed[k] : null;
            if (allowed is not null && allowed.Carries(loads[k])) continue;
            double a = allowed?.Kg ?? 0;
            double ratio = a > 0 ? loads[k] / a : double.PositiveInfinity;
            if (worst is null || ratio > worstRatio)
            {
                worst = (k, loads[k], a);
                worstRatio = ratio;
            }
        }
        return worst;
    }

    private static IEnumerable<(double X, double Load)> Merge(IEnumerable<SuspensionPoint> points) =>
        points.GroupBy(p => Math.Round(p.X, 3)).Select(g => (g.First().X, g.Sum(p => p.F + p.W)));

    public void AddPoint(double x, double f, string src = "", double w = 0)
        => Points.Add(new SuspensionPoint { X = x, F = f, W = w, Src = src });

    /// <summary>The scenic loads at each beam it rests on, after <see cref="Compute"/>.</summary>
    public Dictionary<string, double> ColumnLoads { get; } = new();

    // Charges scéniques à chaque poutre : entre les deux poutres qui l'encadrent, par bras de levier ; au-delà
    // des extrémités, par la même règle affine sur les deux plus proches - le porte-à-faux. La règle du calcul
    // charpente (Helpers.LoadTransmissionHelper), pour que l'optimiseur trouve ce que le calcul trouvera.
    public void Compute()
    {
        ColumnLoads.Clear();
        foreach (var c in Columns) ColumnLoads[c.Col] = 0;
        foreach (var p in Points)
            foreach (var (col, share) in Shares(p.X))
                ColumnLoads[col] += p.F * share;
        foreach (var col in ColumnLoads.Keys.ToList())
            ColumnLoads[col] = Math.Round(ColumnLoads[col], 3);
    }

    /// <summary>How a point load at <paramref name="x"/> divides between the beams. Shares sum to one.</summary>
    public IEnumerable<(string Col, double Share)> Shares(double x)
    {
        if (Columns.Count == 0) yield break;
        var nearest = Columns.OrderBy(c => Math.Abs(c.X - x)).ToList();
        if (Columns.Count == 1 || Math.Abs(nearest[0].X - x) < OnSupportM)
        {
            yield return (nearest[0].Col, 1.0);
            yield break;
        }

        var left = Columns.Where(c => c.X <= x).OrderByDescending(c => c.X).Cast<(string Col, double X)?>().FirstOrDefault();
        var right = Columns.Where(c => c.X > x).OrderBy(c => c.X).Cast<(string Col, double X)?>().FirstOrDefault();
        var (a, b) = left is { } l && right is { } r ? (l, r) : (nearest[0], nearest[1]);
        if (a.X > b.X) (a, b) = (b, a);

        double span = b.X - a.X;
        if (Math.Abs(span) < 1e-9)
        {
            yield return (a.Col, 0.5);
            yield return (b.Col, 0.5);
            yield break;
        }
        yield return (a.Col, (b.X - x) / span);
        yield return (b.Col, (x - a.X) / span);
    }

    // Kept for the exports written for one-bay mothers: the loads on the first and last beams.
    public double FL => ColumnLoads.GetValueOrDefault(ColL);
    public double FR => ColumnLoads.GetValueOrDefault(ColR);

    public string ShortType
    {
        get
        {
            var t = (Bar?.Type ?? "").Replace("Mother_", "").Replace("_500", "");
            return t[..Math.Min(10, t.Length)];
        }
    }

    public override string ToString()
        => $"M{Id}:{ShortType} L={(Bar?.Length ?? 0):F1}m {ColL}{Row}-{ColR}{Row}";
}

// ── Accrochage d'un Palonnier sur une Mother ─────────────────
public class PalonnierSupport
{
    public Mother Mother { get; init; } = null!;
    public double S { get; init; }      // distance depuis A le long du palonnier (m)
    public double X { get; init; }      // point d'accrochage (m)
    public double Y { get; init; }
    public double R { get; set; }       // réaction (kg)
    public double W { get; set; }       // part du poids propre du palonnier (kg)
}

// ── Mother croisée à laquelle un Palonnier n'est pas accroché ─
// L'y accrocher aurait porté une travée de Overloaded (le plus souvent cette Mother elle-même) à
// WouldCarryKg, au-delà de sa valeur admise CapacityKg.
public record SkippedCrossing(Mother Mother, double S, Mother Overloaded, double WouldCarryKg, double CapacityKg);

// ── Type de Palonnier ────────────────────────────────────────
public enum PalonnierDirection { Y, Biais }

// ── Palonnier (barre de suspension) ─────────────────────────
public class Palonnier
{
    private static int _counter = 0;
    public static void ResetCounter() => _counter = 0;

    public int    Id        { get; } = ++_counter;

    /// <summary>The assembled truss as one bar: model, total length and mass, and its value at its span.</summary>
    public BarSpec Bar      { get; init; } = null!;

    /// <summary>The stock pieces it is made of.</summary>
    public Assembly? Assembly { get; init; }

    /// <summary>What all its loads may add up to while it hangs from its two ends only.</summary>
    public AllowedLoad? Allowed { get; init; }

    public IReadOnlyList<BarSpec> Pieces =>
        Assembly?.Pieces ?? (Bar is null ? Array.Empty<BarSpec>() : new[] { Bar });

    public PalonnierDirection Direction { get; init; }
    public int    Level     { get; init; } = 1;

    // Points d'accrochage A (bas/gauche) et B (haut/droite)
    public (double X, double Y) A { get; init; }
    public (double X, double Y) B { get; init; }

    // Charges portées
    public List<Charge> Charges { get; init; } = new();

    // Poids propre (kg) : la somme de ses pièces ; 0 quand une masse n'est pas connue.
    public double SelfWeightKg { get; set; }

    // Réactions calculées
    public double RA { get; set; }
    public double RB { get; set; }

    // Lignes de mother auxquelles le Palonnier s'accroche à ses extrémités
    public Mother? MotherA { get; set; }
    public Mother? MotherB { get; set; }

    // Toutes les Mother auxquelles il est accroché, de A vers B : ses deux extrémités et chaque ligne
    // croisée entre elles (Optimizer.HangOnCrossings). Chaque charge va aux deux accroches qui l'encadrent.
    public List<PalonnierSupport> Supports { get; } = new();

    // Lignes croisées auxquelles il n'est pas accroché, parce qu'une travée aurait dépassé sa valeur.
    public List<SkippedCrossing> Skipped { get; } = new();

    // Propriétés calculées
    public double TotalWeight => Charges.Sum(c => c.Weight);
    public double Span => Math.Sqrt(Math.Pow(B.X - A.X, 2) + Math.Pow(B.Y - A.Y, 2));
    public double XFix => Direction == PalonnierDirection.Y ? A.X : (A.X + B.X) / 2;
    public double YMin => Math.Min(A.Y, B.Y);
    public double YMax => Math.Max(A.Y, B.Y);

    public IEnumerable<string> Names => Charges.Select(c => c.Name);

    // Calcule les réactions RA et RB par bras de levier
    public void ComputeReactions()
    {
        var cxyw = Charges.Select(c => (c.X, c.Y, c.Weight));
        (RA, RB) = Reactions2Pt(cxyw, A, B);
    }

    public override string ToString()
    {
        string n = string.Join("+", Names.Take(4));
        return Direction == PalonnierDirection.Y
            ? $"P{Id}(L{Level}) Y x={A.X:F2} [{n}]"
            : $"P{Id}(L{Level}) BIAIS A=({A.X:F2},{A.Y:F2})→B=({B.X:F2},{B.Y:F2}) [{n}]";
    }
}

// ── Groupe candidat (pour sélection gloutonne) ───────────────
public class CandidateGroup
{
    public PalonnierDirection Type    { get; init; }
    public HashSet<string>    Names   { get; init; } = new();
    public List<Charge>       Charges { get; init; } = new();
    public (double X, double Y) A     { get; init; }
    public (double X, double Y) B     { get; init; }
    public double Span     { get; init; }
    public double Total    { get; init; }

    /// <summary>The pieces taken from stock for it, once it is selected.</summary>
    public Assembly? Assembly { get; set; }
}

// ── Jusqu'où l'optimiseur cherche ────────────────────────────
/// <summary>
/// How hard one run looks for a layout. Named after what the run actually does, not after how hard it
/// tries: each level says what the answer it returns is worth.
/// <para>
/// The stored setting is a member's own name in lower case - "bounds" or "exact" - so
/// <c>Enum.TryParse&lt;SearchLevel&gt;(stored, ignoreCase: true, out var level)</c> reads it back and
/// nothing has to keep a second table of names in step with this one. Anything else is not a level: the
/// caller keeps <see cref="SearchLevel.Bounds"/> and says so, rather than guessing which was meant.
/// </para>
/// </summary>
public enum SearchLevel
{
    /// <summary>
    /// The greedy answer, with a lower bound computed beside it (Bounds.cs): one layout, built by taking
    /// the best palonnier group in hand and never looking back, and the floor no layout of this plan
    /// could go below, so a study can say how much room might be left. Nothing is proven optimal. Fast,
    /// and the level every generation has run at until now.
    /// </summary>
    Bounds,

    /// <summary>
    /// A proven search: the greedy layout first, then every way of grouping the charges that need a
    /// palonnier, keeping the best by the ranking (ExactSearch.cs). It can only return the greedy
    /// layout or something the ranking prefers to it. Slower, and it gives up on its budget rather than
    /// hang a page - in which case the answer is the best found, not a proven optimum.
    /// </summary>
    Exact,
}

// ── Options de l'optimiseur ──────────────────────────────────
public sealed record OptimizerOptions
{
    /// <summary>
    /// Palonniers along the beams and mothers across them only - no inclined palonnier, as in every
    /// hand-written study (the Settings page's "orientations orthogonales uniquement").
    /// </summary>
    public bool OrthogonalOnly { get; init; }

    /// <summary>
    /// Whether the run also computes the floor no layout of this plan could go below (Bounds), and
    /// carries it on the result. On by default: a bound costs a few milliseconds, and a result that
    /// could not say how good it was is the problem this was built to end. Turn it off only where the
    /// answer is thrown away - a search trying thousands of layouts, say.
    /// </summary>
    public bool WithBounds { get; init; } = true;

    /// <summary>
    /// The order the run weighs layouts in (Objective). Null means the order in force when nothing is
    /// stored, <see cref="Objective.DefaultRanking"/> - so a caller that has no opinion gets the
    /// argument written down in Objective.cs, never an empty one. The domain code never reads the
    /// setting itself: the pipeline parses what is stored (Objective.Parse) and passes it in here, so
    /// this file stays free of the database.
    /// </summary>
    public IReadOnlyList<Criterion>? Ranking { get; init; }

    /// <summary>The order actually in force for a run, resolved once so no caller has to remember the rule.</summary>
    public IReadOnlyList<Criterion> RankingInForce => Ranking ?? Objective.DefaultRanking;

    /// <summary>
    /// How hard the run looks. <see cref="SearchLevel.Bounds"/> by default: it is what every generation
    /// has done until now, and a page that generates a layout on a click must stay as quick as it was
    /// unless somebody asks for more.
    /// </summary>
    public SearchLevel Search { get; init; } = SearchLevel.Bounds;

    /// <summary>
    /// How long <see cref="SearchLevel.Exact"/> may look before it settles for the best it has found
    /// (ms). A plan with many charges has more groupings than any machine will enumerate, so the search
    /// needs a stop it cannot miss: this one is checked at every node. Two seconds - a generation is a
    /// click on a page, and a page that thinks for two seconds is working, while one that thinks for a
    /// minute is broken. Raise it in a console run where somebody is waiting on purpose.
    /// </summary>
    public int SearchBudgetMs { get; init; } = 2_000;

    /// <summary>
    /// How many nodes <see cref="SearchLevel.Exact"/> may open before it settles for the best it has
    /// found. The second half of the stop: the clock alone would let a machine ten times faster do ten
    /// times the work and return a different layout for the same plan, which a study could not explain.
    /// This count makes the search reproducible whatever it runs on.
    /// </summary>
    public int SearchBudgetNodes { get; init; } = 20_000;

    public static OptimizerOptions Default { get; } = new();
}

// ── Ce qui a réellement tourné ───────────────────────────────
/// <summary>
/// What the run did, kept beside what it produced. A study that cannot say which objective it was built
/// under, and whether anything was proven about it, is not traceable: two layouts of the same plan can
/// differ for no reason a reader can see, and the reason is always here.
/// </summary>
/// <param name="Ranking">The order in force, in its stored form (Objective.Format) - keys, not labels.</param>
/// <param name="Level">How hard the run looked.</param>
/// <param name="Proven">
/// Whether this layout is optimal for that ranking among everything the search covers. False whenever
/// the search did not run, gave up on its budget, or could not finish - never a claim made lightly.
/// </param>
/// <param name="LayoutsExamined">How many complete layouts were built and measured, the greedy one included.</param>
/// <param name="ElapsedMs">How long the placement took (ms), bounds excluded.</param>
public sealed record SearchOutcome(
    string      Ranking,
    SearchLevel Level,
    bool        Proven,
    int         LayoutsExamined,
    double      ElapsedMs)
{
    /// <summary>Before a run has said anything: the default order, nothing searched, nothing proven.</summary>
    public static SearchOutcome None { get; } = new("", SearchLevel.Bounds, false, 0, 0);

    /// <summary>The one line the console prints and the study quotes.</summary>
    public string Describe()
    {
        var fr = Objective.Fr;
        string level = Level == SearchLevel.Exact ? "Recherche exacte" : "Placement glouton";

        // Rien n'est affirmé de ce qui n'a pas été prouvé, et rien n'est avancé sur la RAISON pour
        // laquelle ça ne l'a pas été : le budget, une branche tombée, un plan sans couverture complète
        // s'arrêtent tous là, et la phrase serait fausse une fois sur trois.
        string proof = Level != SearchLevel.Exact
            ? "aucune optimalité n'est prouvée"
            : Proven
                ? "optimalité prouvée pour ce classement"
                : "la meilleure implantation trouvée, sans preuve d'optimalité";

        string ranking = string.IsNullOrWhiteSpace(Ranking) ? "non précisé" : Ranking;

        return $"{level} : {LayoutsExamined.ToString("N0", fr)} implantation(s) examinée(s) en "
             + $"{ElapsedMs.ToString("N0", fr)} ms — {proof}. Classement : {ranking}.";
    }

    public override string ToString() => Describe();
}

// ── Résultat global de l'optimisation ────────────────────────
public class RiggingResult
{
    public List<Charge>     Charges    { get; init; } = new();
    public List<Palonnier>  Palonniers { get; init; } = new();
    public List<Mother>     Mothers    { get; init; } = new();
    public Dictionary<(string Col, int Row), double> GridLoads { get; init; } = new();

    /// <summary>The stock rows as they stand after placement: StockInit what the inventory holds, Stock what is left.</summary>
    public List<BarSpec>    PalsStock  { get; init; } = new();
    public List<BarSpec>    MomsStock  { get; init; } = new();

    public double TotalCharges { get; init; }
    public double TotalGrid    { get; init; }
    public bool   BalanceOk    { get; init; }
    public List<string> Errors { get; init; } = new();

    // Charges que ni un palonnier ni une ligne de mother ne porte : pas de pièce assez longue ou assez forte
    // en stock. Une implantation qui en laisse n'est pas enregistrée (Pipeline.GenerateLayout).
    public List<Charge> Uncovered { get; init; } = new();

    public OptimizerOptions Options { get; init; } = OptimizerOptions.Default;

    /// <summary>
    /// What this layout costs, measured once on the finished result (Objective.LayoutCost): palonniers,
    /// lines, pieces, metres, mass, and the use of its tightest truss. Filled by Optimizer.Optimize at
    /// the end of the run, so everything that quotes a figure about the layout - the console, the study,
    /// a comparison between two runs - reads the same one, computed the same way. Empty until then.
    /// </summary>
    public LayoutCost Cost { get; set; } = LayoutCost.Empty;

    /// <summary>
    /// The floor no layout of this plan could go below (Bounds), so the study can say how much room the
    /// greedy answer may have left. Filled by Optimizer.Optimize when OptimizerOptions.WithBounds is on,
    /// null otherwise. It never proves a better layout exists - see Bounds.cs.
    /// </summary>
    public LayoutBound? Bound { get; set; }

    /// <summary>
    /// What actually ran to produce this layout: the ranking it was weighed in, the level it was looked
    /// for at, whether anything was proven, how many layouts were examined and how long it took. Filled
    /// by Optimizer.Optimize at the end of the run. <see cref="SearchOutcome.None"/> until then, which
    /// claims nothing.
    /// </summary>
    public SearchOutcome Search { get; set; } = SearchOutcome.None;
}
