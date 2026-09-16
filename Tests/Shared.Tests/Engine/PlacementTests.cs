using ClosedXML.Excel;
using Helpers;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// The venue's own three workbooks, placed. Charges.xlsx on the stock of MotherGrill.xlsx and
/// Palonnier.xlsx: 24 loads, 18 of them on a point line and hung straight on their mother line, the rest
/// on palonniers, and not one piece more than the inventory holds.
///
/// These are the only figures in the suite that come from real data rather than from a case written for a
/// test, and that is what they are for: a rule can be right on three charges drawn by hand and wrong on
/// the plan the venue actually rigs. A number here that changes is either a change to the workbooks or a
/// change to the optimiser, and both must be deliberate.
///
/// The plan the placement is handed comes from the Charges page, which Charges.xlsx fills; how that
/// workbook is read is pinned at the end of this file, because a load lost or misread there is a load the
/// placement never sees.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class PlacementTests : IDisposable
{
    private readonly VenuePlan _venue;

    // Each test writes its workbooks in its own folder under %TEMP%\PlacementTests and takes it away
    // again, so no test reads a file another test wrote and nothing depends on the order they run in.
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), nameof(PlacementTests), Guid.NewGuid().ToString("N"));

    public PlacementTests(VenuePlan venue) => _venue = venue;

    public void Dispose()
    {
        try { if (Directory.Exists(_work)) Directory.Delete(_work, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ── The venue's plan, placed ─────────────────────────────────

    [Fact]
    public void Every_charge_of_the_venue_s_plan_is_carried()
    {
        // A layout that leaves a load hanging on nothing is never saved (Pipeline.GenerateLayout), so an
        // uncovered charge here means the venue could not place its own plan with its own stock.
        Assert.True(_venue.Placed.Uncovered.Count == 0,
            "charge(s) qu'aucun palonnier ni aucune ligne de mother ne porte : "
            + string.Join(", ", _venue.Placed.Uncovered.Select(c => c.Name)));
    }

    [Fact]
    public void The_eighteen_charges_on_a_point_line_hang_straight_on_their_mother_line()
    {
        // A load already under a point line needs no palonnier: hanging one would add a truss, its mass
        // and two more hanging points for nothing. The studies hang these straight on the line.
        int direct = _venue.Placed.Mothers.Sum(m => m.DirectCharges.Count);

        Assert.True(direct == 18, $"{direct} charges posées directement sur une ligne au lieu de 18");
    }

    [Fact]
    public void Seven_mother_lines_carry_the_whole_plan()
    {
        // One continuous line per point line that carries something, not one per bay: the figure is
        // pinned because it is what the crew builds and what Tableau 2 orders.
        Assert.True(_venue.Placed.Mothers.Count == 7,
            $"{_venue.Placed.Mothers.Count} lignes de mother au lieu de 7 : "
            + string.Join(", ", _venue.Placed.Mothers.Select(m => m.ToString())));
    }

    [Fact]
    public void A_mother_line_runs_over_several_bays()
    {
        // Continuous across the beams, as in the studies - not one truss per bay. A layout that only ever
        // spanned one bay would read every value at a single bay's length and hide the real spans.
        Assert.True(_venue.Placed.Mothers.Any(m => m.BayCount >= 3),
            "aucune ligne ne couvre trois travées : "
            + string.Join(", ", _venue.Placed.Mothers.Select(m => m.BayCount)));
    }

    [Fact]
    public void Every_line_is_assembled_from_pieces_of_one_model()
    {
        // A truss is joined end to end from its own model's pieces. Mixing models would give the line a
        // load value no row of the inventory covers.
        var mixed = _venue.Placed.Mothers
            .Where(m => m.Pieces.Any(p => p.Type != m.Bar.Type))
            .Select(m => m.ToString())
            .ToList();

        Assert.True(mixed.Count == 0, "ligne(s) assemblées de plusieurs modèles : " + string.Join(" | ", mixed));
    }

    [Fact]
    public void Every_line_and_every_palonnier_is_long_enough_for_what_it_spans()
    {
        // Geometry.TOL is the 8 cm a truss may fall short of the distance it is drawn over - the play of a
        // hanging point, not a gap. Beyond that the truss does not reach, and the layout is a drawing.
        var tooShort = _venue.Placed.Mothers
            .Where(m => m.Bar.Length < m.Length - Geometry.TOL - 1e-9)
            .Select(m => $"{m} : {m.Bar.Length:0.##} m de poutre pour {m.Length:0.##} m tracés")
            .Concat(_venue.Placed.Palonniers
                .Where(q => q.Bar.Length < q.Span - Geometry.TOL - 1e-9)
                .Select(q => $"{q} : {q.Bar.Length:0.##} m de poutre pour {q.Span:0.##} m de portée"))
            .ToList();

        Assert.True(tooShort.Count == 0, "élément(s) trop courts : " + string.Join(" | ", tooShort));
    }

    [Fact]
    public void Every_loaded_bay_and_every_palonnier_stays_within_its_value()
    {
        // The rule the whole engine exists to respect: the loads between two neighbouring hanging points
        // against the charge ponctuelle au milieu of the model, read at the length immediately above the
        // span. A layout that breaks it is a layout that would be built.
        var over = _venue.Placed.Mothers
            .Where(m => !m.Fits(m.Points))
            .Select(m => $"{m} : travée {m.WorstBay(m.Points)}")
            .Concat(_venue.Placed.Palonniers
                .Where(q => !q.Allowed!.Carries(q.TotalWeight))
                .Select(q => $"{q} : {q.TotalWeight:0} kg pour {q.Allowed!.Kg:0} kg admissibles"))
            .ToList();

        Assert.True(over.Count == 0, "élément(s) au-delà de leur valeur admise : " + string.Join(" | ", over));
    }

    [Fact]
    public void The_attachment_points_carry_exactly_what_the_charges_weigh()
    {
        // Nothing added, nothing lost on the way up: the sum at the grid points is the sum of the plan.
        double carried = _venue.Charges.Sum(c => c.Weight);

        Assert.Equal(carried, _venue.Placed.TotalGrid, 2);
    }

    [Fact]
    public void No_line_is_shorter_than_the_bays_it_rests_on()
    {
        // There is no 85 % rule any more (owner's decision, 2026-09-11): a line reaches every beam it
        // rests on, end to end. Stopping short would leave the outermost bay carried by nothing.
        var stopShort = _venue.Placed.Mothers
            .Where(m => m.XR - m.XL < m.Columns[^1].X - m.Columns[0].X - 1e-9)
            .Select(m => $"{m} : {m.XR - m.XL:0.##} m tracés pour {m.Columns[^1].X - m.Columns[0].X:0.##} m de travées")
            .ToList();

        Assert.True(stopShort.Count == 0, "ligne(s) plus courtes que leurs travées : " + string.Join(" | ", stopShort));
    }

    [Fact]
    public void No_stock_row_is_used_more_often_than_its_Nombre()
    {
        // The stock is what the venue owns. A layout that uses a piece twice is a layout the crew cannot
        // build, and nothing downstream - the study, the truss table - would notice.
        var overuse = LayoutChecks.StockOveruse(_venue.Placed, _venue.Palonniers, _venue.Mothers);

        Assert.True(overuse is null, overuse);
    }

    // ── Orientations orthogonales uniquement ─────────────────────

    [Fact]
    public void With_the_orthogonal_option_the_venue_s_plan_uses_no_inclined_palonnier()
    {
        // Every hand-written study rigs along and across the beams only. The Settings page's
        // « orientations orthogonales uniquement » must produce exactly that - and still carry the plan.
        Assert.True(_venue.Orthogonal.Palonniers.All(q => q.Direction == PalonnierDirection.Y),
            "palonnier(s) inclinés malgré le réglage : "
            + string.Join(", ", _venue.Orthogonal.Palonniers.Where(q => q.Direction != PalonnierDirection.Y)));
        Assert.True(_venue.Orthogonal.Uncovered.Count == 0,
            "charge(s) non portées en orthogonal seul : "
            + string.Join(", ", _venue.Orthogonal.Uncovered.Select(c => c.Name)));
    }

    [Fact]
    public void A_diagonal_pair_goes_on_one_inclined_palonnier_unless_the_option_forbids_it()
    {
        // Two loads neither above one another nor side by side. One inclined truss carries both; with the
        // option on, the same pair must still be carried, on palonniers along the beams.
        var diagonal = new List<Charge>
        {
            new() { Name = "D1", X = 2.0, Y = 1.0, Weight = 150 },
            new() { Name = "D2", X = 4.0, Y = 2.6, Weight = 150 },
        };

        var free = Optimizer.Optimize(diagonal, _venue.Palonniers, _venue.Mothers, verbose: false);
        var orthogonal = Optimizer.Optimize(diagonal, _venue.Palonniers, _venue.Mothers, verbose: false,
            options: new OptimizerOptions { OrthogonalOnly = true });

        Assert.True(free.Palonniers.Any(q => q.Direction == PalonnierDirection.Biais),
            "un palonnier incliné doit être proposé sans le réglage : " + string.Join(", ", free.Palonniers));
        Assert.True(orthogonal.Uncovered.Count == 0,
            "le réglage ne doit pas laisser la paire sans support : "
            + string.Join(", ", orthogonal.Uncovered.Select(c => c.Name)));
        Assert.True(orthogonal.Palonniers.All(q => q.Direction == PalonnierDirection.Y),
            "palonnier(s) inclinés malgré le réglage : " + string.Join(", ", orthogonal.Palonniers));
    }

    // ── The plan the placement is handed: Charges.xlsx ───────────
    //
    // The workbook fills the Charges page, and the page is what the placement reads. A load dropped or
    // misread here is a load nobody hangs and nobody is told about, so the reader refuses rather than
    // guesses - and says, in French, which row and which column it could not read.

    [Fact]
    public void A_charges_file_with_a_header_row_gives_its_loads_never_its_header()
    {
        var path = ChargesBook("avec-entete.xlsx", ws =>
        {
            ws.Cell(1, 1).Value = "Nom"; ws.Cell(1, 2).Value = "X (m)";
            ws.Cell(1, 3).Value = "Y (m)"; ws.Cell(1, 4).Value = "Charge (kg)";
            ws.Cell(2, 1).Value = "T1"; ws.Cell(2, 2).Value = 1.0; ws.Cell(2, 3).Value = 2.0; ws.Cell(2, 4).Value = 100;
            ws.Cell(3, 1).Value = "T2"; ws.Cell(3, 2).Value = 3.0; ws.Cell(3, 3).Value = 4.0; ws.Cell(3, 4).Value = 200;
        });
        var notes = new List<string>();

        var charges = ExcelLoader.LoadCharges(path, notes);
        var (x, y) = StudyAxes.ToGrid(1.0, 2.0);

        Assert.True(charges.Count == 2, $"{charges.Count} charges lues au lieu de 2 : "
                                        + string.Join(", ", charges.Select(c => c.Name)));
        Assert.Equal("T1", charges[0].Name);
        Assert.Equal(x, charges[0].X, 9);
        Assert.Equal(y, charges[0].Y, 9);
        Assert.Equal(200.0, charges[1].Weight, 9);

        // Said out loud: the same silence on a file without a header would lose a real load.
        Assert.True(notes.Any(n => n.Contains("en-tête")),
            "la ligne d'en-tête lue comme telle doit être signalée : " + string.Join(" | ", notes));
    }

    [Fact]
    public void Columns_in_another_order_are_found_by_their_headings()
    {
        // Read by position instead, a Y would be read as a weight - and a wrong number is worse than an
        // unread file, because nothing downstream can notice it.
        var path = ChargesBook("colonnes-melangees.xlsx", ws =>
        {
            ws.Cell(1, 1).Value = "Charge (kg)"; ws.Cell(1, 2).Value = "Nom";
            ws.Cell(1, 3).Value = "Y"; ws.Cell(1, 4).Value = "X";
            ws.Cell(2, 1).Value = 250; ws.Cell(2, 2).Value = "T9"; ws.Cell(2, 3).Value = 4.0; ws.Cell(2, 4).Value = 3.0;
        });

        var charges = ExcelLoader.LoadCharges(path);
        var (x, y) = StudyAxes.ToGrid(3.0, 4.0);

        Assert.True(charges.Count == 1, $"{charges.Count} charges lues au lieu d'une");
        Assert.Equal("T9", charges[0].Name);
        Assert.Equal(250.0, charges[0].Weight, 9);
        Assert.Equal(x, charges[0].X, 9);
        Assert.Equal(y, charges[0].Y, 9);
    }

    [Fact]
    public void A_file_without_a_header_keeps_its_first_row_and_reads_a_number_typed_as_text()
    {
        // The files this application is given have no header - row 1 is already a load - and a file
        // exported from elsewhere has one, so only the file can say. Nothing skipped, nothing to report.
        var path = ChargesBook("sans-entete.xlsx", ws =>
        {
            ws.Cell(1, 1).Value = "T1"; ws.Cell(1, 2).Value = 1.0; ws.Cell(1, 3).Value = 2.0; ws.Cell(1, 4).Value = 100;
            ws.Cell(2, 1).Value = "T2"; ws.Cell(2, 2).Value = "3,5"; ws.Cell(2, 3).Value = 4.0; ws.Cell(2, 4).Value = 200;
        });
        var notes = new List<string>();

        var charges = ExcelLoader.LoadCharges(path, notes);
        var (x, y) = StudyAxes.ToGrid(3.5, 4.0);

        Assert.True(charges.Count == 2, $"{charges.Count} charges lues au lieu de 2");
        Assert.Equal("T1", charges[0].Name);
        Assert.Equal(x, charges[1].X, 9);
        Assert.Equal(y, charges[1].Y, 9);
        Assert.True(notes.Count == 0,
            "rien n'a été sauté, il n'y a rien à signaler : " + string.Join(" | ", notes));
    }

    [Fact]
    public void A_cell_that_is_not_a_number_names_its_row_and_its_column()
    {
        // Read tolerantly and refused by name, rather than letting the spreadsheet library throw its own
        // English exception on the first word it meets - the fix is made in the workbook.
        var path = ChargesBook("illisible.xlsx", ws =>
        {
            ws.Cell(1, 1).Value = "T1"; ws.Cell(1, 2).Value = 1.0; ws.Cell(1, 3).Value = 2.0; ws.Cell(1, 4).Value = 100;
            ws.Cell(2, 1).Value = "T2"; ws.Cell(2, 2).Value = 3.0; ws.Cell(2, 3).Value = "gauche"; ws.Cell(2, 4).Value = 200;
        });

        var refusal = Assert.Throws<InvalidDataException>(() => { ExcelLoader.LoadCharges(path); });

        Assert.True(refusal.Message.Contains("« T2 »") && refusal.Message.Contains("Y illisible"),
            "le refus doit nommer la charge et la colonne : " + refusal.Message);
    }

    [Fact]
    public void Two_charges_under_one_name_or_one_with_no_name_are_refused_naming_the_rows()
    {
        // A charge is known by its name everywhere after this - the optimiser, the stored ChargeMp, the
        // study - so two under one name would make one of them vanish without a trace. Names are compared
        // trimmed and without case, because « M » and « m  » are one name to a reader.
        var path = ChargesBook("doublons.xlsx", ws =>
        {
            ws.Cell(1, 1).Value = "M"; ws.Cell(1, 2).Value = 1.0; ws.Cell(1, 3).Value = 2.0; ws.Cell(1, 4).Value = 100;
            ws.Cell(2, 1).Value = "m "; ws.Cell(2, 2).Value = 3.0; ws.Cell(2, 3).Value = 2.0; ws.Cell(2, 4).Value = 120;
            ws.Cell(3, 2).Value = 5.0; ws.Cell(3, 3).Value = 2.0; ws.Cell(3, 4).Value = 90;
        });

        var refusal = Assert.Throws<InvalidDataException>(() => { ExcelLoader.LoadCharges(path); });

        Assert.True(refusal.Message.Contains("« m » : deux charges portent ce nom, aux lignes 1 et 2"),
            "le doublon doit nommer ses deux lignes : " + refusal.Message);
        Assert.True(refusal.Message.Contains("ligne 3 : charge sans nom"),
            "la ligne sans nom doit être nommée : " + refusal.Message);
    }

    [Fact]
    public void The_venue_s_own_charges_file_whose_names_all_differ_is_read()
    {
        // The other side of the refusal: the file the venue actually uses must go through it.
        Assert.Equal(24, _venue.Charges.Count);
        Assert.Equal(24, _venue.Charges.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private string ChargesBook(string name, Action<IXLWorksheet> fill)
    {
        Directory.CreateDirectory(_work);
        var path = Path.Combine(_work, name);

        using var wb = new XLWorkbook();
        fill(wb.AddWorksheet("Charges"));
        wb.SaveAs(path);
        return path;
    }
}

/// <summary>
/// The venue's own three workbooks, read and placed once for every engine test that needs them.
///
/// Shared as a collection fixture, and the engine's tests all belong to that collection, so the
/// optimiser's runs happen one after another. That is deliberate: the optimiser numbers its lines and
/// palonniers from counters it resets at the start of each run, and those numbers break ties when a
/// palonnier is hung from the lines it crosses. Two runs at once could interleave them and place the same
/// plan two different ways - a flake nobody would ever reproduce.
/// </summary>
public sealed class VenuePlan
{
    public VenuePlan()
    {
        Charges = ExcelLoader.LoadCharges(Fixtures.Charges);

        var (pals, mothers) = ExcelLoader.LoadInventory(Fixtures.Dir, null);
        Palonniers = pals;
        Mothers = mothers;

        Placed = Optimizer.Optimize(Charges, Palonniers, Mothers, verbose: false);
        Orthogonal = Optimizer.Optimize(Charges, Palonniers, Mothers, verbose: false,
            options: new OptimizerOptions { OrthogonalOnly = true });
        Stored = new StoredLayout(Placed);
    }

    /// <summary>The venue's plan of charges, in the grid's axes, as the reader returns it.</summary>
    public List<Charge> Charges { get; }

    /// <summary>The stock rows of Palonnier.xlsx, values with Nombre = 0 included.</summary>
    public List<BarSpec> Palonniers { get; }

    /// <summary>The stock rows of MotherGrill.xlsx, values with Nombre = 0 included.</summary>
    public List<BarSpec> Mothers { get; }

    /// <summary>The plan placed on that stock.</summary>
    public RiggingResult Placed { get; }

    /// <summary>The same plan with « orientations orthogonales uniquement » on.</summary>
    public RiggingResult Orthogonal { get; }

    /// <summary><see cref="Placed"/> as the database would hold it, and what the load calculation finds.</summary>
    public StoredLayout Stored { get; }
}

/// <summary>
/// The engine's tests, run one after another. See <see cref="VenuePlan"/> for why.
/// </summary>
[CollectionDefinition(EngineCollection.Name)]
public sealed class EngineCollection : ICollectionFixture<VenuePlan>
{
    public const string Name = "Le moteur d'implantation";
}

/// <summary>
/// What has to hold of any layout, whatever plan it came from: it uses no piece the stock does not hold,
/// it takes no bay and no palonnier past its value, and it names every charge it could not carry. Shared
/// with the stress tests, which check exactly these on hundreds of random plans.
/// </summary>
public static class LayoutChecks
{
    /// <summary>The first stock row used more often than the inventory holds, said in French; null when none.</summary>
    public static string? StockOveruse(
        RiggingResult result, IReadOnlyList<BarSpec> palonnierRows, IReadOnlyList<BarSpec> motherRows)
    {
        // BarSpec has no value equality, so a dictionary keyed on it counts by identity - which is what is
        // wanted: two inventory lines of the same model and length are two different lines, each with its
        // own Nombre.
        var used = new Dictionary<BarSpec, int>();
        foreach (var piece in result.Palonniers.SelectMany(q => q.Pieces)
                     .Concat(result.Mothers.SelectMany(m => m.Pieces)))
        {
            used[piece] = used.GetValueOrDefault(piece) + 1;
        }

        foreach (var (row, count) in used)
        {
            bool fromTheInventory = palonnierRows.Any(r => ReferenceEquals(r, row))
                                    || motherRows.Any(r => ReferenceEquals(r, row));
            if (!fromTheInventory) return $"une pièce qui ne vient pas de l'inventaire : {row}";
            if (count > row.StockInit)
                return $"{row.Type} de {row.Length} m utilisé {count} fois pour {row.StockInit} en stock";
        }

        return null;
    }

    /// <summary>The first bay or palonnier taken past what its model admits, said in French; null when none.</summary>
    public static string? ValuesRespected(RiggingResult result)
    {
        var bay = result.Mothers.FirstOrDefault(m => !m.Fits(m.Points));
        if (bay is not null) return $"une travée au-delà de sa valeur : {bay}";

        var palonnier = result.Palonniers.FirstOrDefault(q => !q.Allowed!.Carries(q.TotalWeight));
        if (palonnier is not null)
            return $"un palonnier au-delà de sa valeur : {palonnier} porte {palonnier.TotalWeight:0} kg";

        return null;
    }

    /// <summary>Everything the two above cover, plus the report; null when the layout holds.</summary>
    public static string? WhatIsWrong(
        RiggingResult result, IReadOnlyList<BarSpec> palonnierRows, IReadOnlyList<BarSpec> motherRows)
    {
        var overuse = StockOveruse(result, palonnierRows, motherRows);
        if (overuse is not null) return overuse;

        var over = ValuesRespected(result);
        if (over is not null) return over;

        // A charge left hanging on nothing is allowed - the stock may be too small - but it is never
        // allowed to happen in silence: the page has to be able to say which loads, and why.
        if (result.Uncovered.Count > 0 && result.Errors.Count < result.Uncovered.Count)
            return $"{result.Uncovered.Count} charge(s) non portées et seulement {result.Errors.Count} signalement(s)";

        return null;
    }
}
