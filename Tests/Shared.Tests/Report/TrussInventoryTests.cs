using AliantWebS.Models;
using AliantWebS.Reporting;
using ClosedXML.Excel;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Report;

/// <summary>
/// Tableau 2 - the pre-rig's bill of materials - and the inventory its masses are read from.
///
/// Two rules meet here and must never drift apart: Tableau 2 counts what the crew takes off the truck, which
/// is PIECES and not elements, and the mass it prints is the same mass the load transmission hangs on the
/// attachment points. A truss weighed wrong here is a wrong load on every point it feeds, in a document that
/// says it is a structural study, so a mass that cannot be sourced stays unknown rather than being guessed.
///
/// The scenario is built in code: no database, no scenario storage, nothing but the arguments. The two
/// inventory workbooks are written into a folder of the system temp directory and removed afterwards.
/// </summary>
public class TrussInventoryTests : IDisposable
{
    // ── The venue's axes, so the elements sit where they really would ──
    // Relative to Point0 = E5, as everything stored for an event is.
    private const double P0X = 33.103;
    private const double G = 18.697 - P0X, F = 25.947 - P0X, E = 0, D = 40.247 - P0X;
    private const double Row1 = 15.15, Row3 = 7.4, Row5 = 0, Row6 = -3.7;

    /// <summary>Its own folder, so nothing this class writes can be seen by another test or another run.</summary>
    private readonly string _temp = Path.Combine(
        Path.GetTempPath(), nameof(TrussInventoryTests), Guid.NewGuid().ToString("N"));

    public TrussInventoryTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); }
        catch (IOException) { /* a workbook still held open must not fail a passing test */ }
    }

    // ═══════════════════════════════════════════════════════════════
    // The catalogue
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_length_the_venue_owns_none_of_still_has_an_allowed_load_but_no_mass()
    {
        var scene = new Scene();

        // 8,4 m is listed with Nombre = 0: the optimiser may place none, yet a 8,4 m span is still read at
        // that row. Giving it a mass would put kilograms of truss nobody owns into Tableau 2.
        Assert.True(scene.Catalog.Find("S500", 8.4) is { UnitWeightKg: null, Stock: 0 },
            "une ligne Nombre = 0 ne doit donner ni masse ni stock");
        Assert.True(scene.Catalog.Find("S500", 5.6) is { UnitWeightKg: 160, Stock: 10 },
            "S500 de 5,6 m : 160 kg, 10 en stock");
    }

    // ═══════════════════════════════════════════════════════════════
    // Tableau 2
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Tableau_2_counts_the_pieces_of_every_element_not_the_elements()
    {
        var scene = new Scene();
        var inventory = scene.BuildInventory();

        // The mother is four 5,6 m pieces and the short palonnier three 4 m ones: what is loaded on the
        // truck is 4 + 1 and 3 + 4, not "one mother and two palonniers".
        Assert.Equal(5, inventory.Lines.Single(l => l.Label == "S500 lg:5,6m").Count);
        Assert.Equal(7, inventory.Lines.Single(l => l.Label == "P300 lg:4m").Count);
    }

    [Fact]
    public void Tableau_2_lists_every_element_made_of_several_pieces()
    {
        var scene = new Scene();

        Assert.Equal(4, scene.BuildInventory().Assembled.Count);
    }

    [Fact]
    public void Tableau_2_weighs_what_the_load_transmission_weighs()
    {
        var scene = new Scene();
        var inventory = scene.BuildInventory();
        var massOf = ReportDataBuilder.MassSource(scene.Catalog, scene.TypeNames);

        // The stored attachment-point loads are reconciled against TransmittedMassKg, so the two figures
        // have to be the same kilograms counted twice, not two independent sums.
        Assert.Equal(1230.0, inventory.TotalMassKg, 2);
        Assert.Equal(1070.0, inventory.TransmittedMassKg, 2);
        Assert.Equal(inventory.TransmittedMassKg, scene.Elements.Sum(e => massOf(e).Kg ?? 0), 2);
    }

    [Fact]
    public void A_mass_the_inventory_cannot_give_is_declared_missing_not_left_out_quietly()
    {
        var scene = new Scene();
        var inventory = scene.BuildInventory();

        // The 9,99 m piece and the 8,4 m one have no mass, so the total is an underestimate and must say so:
        // a total that silently omits kilograms reads as a lighter pre-rig than the one being hung.
        Assert.True(inventory.MassIncomplete, "la masse doit être annoncée incomplète (9,99 m et 8,4 m inconnus)");
    }

    [Fact]
    public void An_element_with_one_unknown_piece_weighs_nothing_never_its_known_part()
    {
        var scene = new Scene();
        var massOf = ReportDataBuilder.MassSource(scene.Catalog, scene.TypeNames);

        // Half a mass is worse than none: it would read low on every point the element feeds, and nothing
        // downstream could tell it apart from a truss that really is that light.
        Assert.Null(massOf(scene.Partial).Kg);

        var whole = massOf(scene.Mother);
        Assert.Equal(640.0, whole.Kg);
        Assert.Equal("S500 4 × 5,6 m", whole.Label);
    }

    [Fact]
    public void An_element_with_no_readable_composition_is_one_piece_of_the_length_in_its_name()
    {
        var scene = new Scene();

        // How the elements placed before compositions were recorded are counted - and the only reading that
        // keeps such an element in Tableau 2 at all.
        Assert.True(ReportDataBuilder.PiecesOf(scene.OldMother) is [8.4], "sans composition : une pièce lue dans le nom");
        Assert.True(ReportDataBuilder.PiecesOf(scene.Unreadable) is [2.8], "composition illisible : une pièce lue dans le nom");
    }

    [Fact]
    public void Tableau_2_warns_about_more_pieces_than_the_venue_owns()
    {
        var scene = new Scene();
        var warnings = new List<string>();
        scene.BuildInventory(warnings);

        Assert.True(warnings.Any(w => w.Contains("plus de pièces") && w.Contains("7 pièces pour 6")),
            "sept pièces de 4 m pour six en stock doit être signalé");
    }

    [Fact]
    public void A_piece_that_is_not_in_stock_at_all_says_what_to_do_about_it()
    {
        var scene = new Scene();
        var warnings = new List<string>();
        scene.BuildInventory(warnings);

        // Naming the piece without saying what to do leaves the reader with a report they cannot act on.
        Assert.True(
            warnings.Any(w => w.Contains("pas en stock") && w.Contains("lg:8,4m") && w.Contains("relancez le placement")),
            "une pièce hors stock doit être nommée et suivie de la marche à suivre");
    }

    [Fact]
    public void An_unreadable_composition_is_warned_about_once_and_names_its_element()
    {
        var scene = new Scene();
        var warnings = new List<string>();
        scene.BuildInventory(warnings);

        Assert.Single(warnings.Where(w => w.Contains("illisibles")));
        Assert.Contains(warnings, w => w.Contains("illisibles") && w.Contains("#7"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Pieces that do not describe the truss
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Pieces_shorter_than_the_truss_they_are_drawn_over_are_a_verification_gap()
    {
        var scene = new Scene();
        // Two 4 m pieces recorded on a palonnier drawn over 11,1 m: it is weighed as a bridge of 8 m, which
        // is not the one in the plan, and every point it feeds is loaded from that wrong bridge.
        var shortPal = El("P300 L=12,0m #3", scene.P300Id, false, 5.0, Row3, 5.0, Row6, "[400,400]");
        var warnings = new List<string>();

        var inventory = ReportDataBuilder.BuildTrussInventory(
            new List<MotherGridPalonnier> { shortPal }, scene.TypeNames, scene.Catalog, warnings);

        Assert.Equal(
            "Les pièces enregistrées de « P300 L=12,0m #3 » (P300), 2 × 4 m, n’atteignent pas la longueur sur "
            + "laquelle il est dessiné, 11,1 m : il est pesé comme un pont de 8 m, qui n’est pas celui du plan, "
            + "et la charge de chaque point d’accroche qu’il alimente en est faussée. Corrigez ses pièces sur la "
            + "page MotherGrid, puis relancez le calcul charpente.",
            Assert.Single(ReportDataBuilder.PieceProblemGaps(inventory.PieceProblems)));
        Assert.Contains(warnings, w => w.Contains("plus courtes"));
    }

    [Fact]
    public void Pieces_that_cannot_be_read_at_all_are_a_verification_gap()
    {
        var scene = new Scene();
        var badComposition = El("S500 L=5,6m #8", scene.S500Id, true, -1.0, -3.7, 6.0, -3.7, "[560,abc]");
        var warnings = new List<string>();

        var inventory = ReportDataBuilder.BuildTrussInventory(
            new List<MotherGridPalonnier> { badComposition }, scene.TypeNames, scene.Catalog, warnings);

        Assert.Equal(
            "Les pièces enregistrées de « S500 L=5,6m #8 » (S500) sont illisibles (« [560,abc] ») : il est compté "
            + "comme une seule pièce de 5,6 m, pour une longueur dessinée de 7 m, et son poids propre, comme la "
            + "charge de chaque point d’accroche qu’il alimente, ne repose sur aucune pièce enregistrée. Corrigez "
            + "ses pièces sur la page MotherGrid, puis relancez le calcul charpente.",
            Assert.Single(ReportDataBuilder.PieceProblemGaps(inventory.PieceProblems)));
        Assert.Contains(warnings, w => w.Contains("illisibles"));
    }

    [Fact]
    public void An_element_whose_pieces_are_both_unreadable_and_short_is_one_problem_not_two()
    {
        var scene = new Scene();
        var shortPal = El("P300 L=12,0m #3", scene.P300Id, false, 5.0, Row3, 5.0, Row6, "[400,400]");
        // Unreadable, and short once it is counted as the single piece of its name: still one element to fix.
        var badComposition = El("S500 L=5,6m #8", scene.S500Id, true, -1.0, -3.7, 6.0, -3.7, "[560,abc]");

        var inventory = ReportDataBuilder.BuildTrussInventory(
            new List<MotherGridPalonnier> { shortPal, badComposition }, scene.TypeNames, scene.Catalog,
            new List<string>());

        Assert.Equal(2, inventory.PieceProblems.Count);
    }

    [Fact]
    public void Only_the_elements_whose_pieces_are_wrong_are_reported_as_problems()
    {
        var scene = new Scene();

        // The main scenario has one: the « oops » composition. The mother assembled from four pieces, the
        // element with no composition at all and the one with an unweighed piece are all legitimate.
        var problem = Assert.Single(scene.BuildInventory().PieceProblems);
        Assert.True(problem is { Unreadable: true, Element: "S500 L=2,8m #7", Stored: "oops" },
            "seul l'élément « oops » doit être signalé");
    }

    [Fact]
    public void A_long_unreadable_composition_is_quoted_to_forty_characters()
    {
        var scene = new Scene();
        // Whatever a person pasted into the field, the gap has to stay a sentence someone can read.
        var longJunk = El("S500 L=2,8m #10", scene.S500Id, true, 0, 0, 2.8, 0, new string('x', 60));

        var gap = Assert.Single(ReportDataBuilder.PieceProblemGaps(ReportDataBuilder.BuildTrussInventory(
            new List<MotherGridPalonnier> { longJunk }, scene.TypeNames, scene.Catalog, new List<string>())
            .PieceProblems));

        Assert.Contains("(« " + new string('x', 40) + "… »)", gap);
    }

    // ═══════════════════════════════════════════════════════════════
    // The inventory workbooks: rows that cannot be read
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_row_that_cannot_be_read_is_skipped_and_recorded_never_read_as_a_zero()
    {
        var inv = LoadWrittenInventory();

        // Five rows: two lengths, three charges. A row read as 0 would enter the inventory as a truss that
        // carries nothing, which the optimiser and the study would both believe.
        Assert.Equal(5, inv.Skipped.Count);
        Assert.True(inv.Skipped.SingleOrDefault(s => s.Mothers && s.Line == 4) is
                { File: "MotherGrill.xlsx", Model: "S500", LengthM: 7.5, Reason: "charge ponctuelle au milieu illisible" },
            "MotherGrill.xlsx ligne 4 : S500 de 7,5 m, charge illisible");
        Assert.True(inv.Skipped.SingleOrDefault(s => s.Mothers && s.Line == 5) is
                { Model: "S500", LengthM: null, Reason: "longueur illisible" },
            "MotherGrill.xlsx ligne 5 : S500, longueur illisible");
        Assert.True(inv.Skipped.SingleOrDefault(s => !s.Mothers && s.Line == 3) is
                { File: "Palonnier.xlsx", Model: "P300", LengthM: 6.0 },
            "Palonnier.xlsx ligne 3 : P300 de 6 m");
    }

    [Fact]
    public void A_skipped_row_is_also_a_warning_naming_the_file_and_the_line()
    {
        var inv = LoadWrittenInventory();

        Assert.Contains("MotherGrill.xlsx, ligne 4 (« S500 » de 7,5 m) : charge ponctuelle au milieu illisible, ligne ignorée.",
            inv.Warnings);
        Assert.Contains("MotherGrill.xlsx, ligne 5 (« S500 ») : longueur illisible, ligne ignorée.", inv.Warnings);
    }

    [Fact]
    public void A_skipped_row_of_a_model_in_use_is_a_verification_gap_word_for_word()
    {
        var scene = new Scene();
        var inv = LoadWrittenInventory();

        var gaps = ReportDataBuilder.SkippedRowGaps(inv.Skipped, scene.Elements, scene.TypeNames);

        Assert.Equal(
            "MotherGrill.xlsx, ligne 4 (« S500 » de 7,5 m) : charge ponctuelle au milieu illisible, ligne ignorée. "
            + "Des mothers de ce modèle sont utilisés : leurs charges admissibles sont lues sans cette ligne et "
            + "peuvent ne pas être celles de l’inventaire. Corrigez cette ligne, puis relancez le placement "
            + "(Générer l’implantation) et le calcul charpente.",
            gaps.FirstOrDefault());
        Assert.Contains(gaps, g => g.StartsWith(
            "Palonnier.xlsx, ligne 3 (« P300 » de 6 m) : charge ponctuelle au milieu illisible, ligne ignorée. "
            + "Des palonniers de ce modèle"));
    }

    [Fact]
    public void A_skipped_row_of_a_model_nobody_uses_in_that_role_is_not_a_gap()
    {
        var scene = new Scene();
        var inv = LoadWrittenInventory();

        // Three of the five rows matter: S500 lines 4 and 5 of MotherGrill.xlsx and P300 line 3 of
        // Palonnier.xlsx. Not the Q100, which nothing uses, and not the P300 row of MotherGrill.xlsx: no
        // mother of the scenario is a P300, so that row cannot have changed any value the study prints.
        Assert.Equal(3, ReportDataBuilder.SkippedRowGaps(inv.Skipped, scene.Elements, scene.TypeNames).Count);
        Assert.Single(ReportDataBuilder.SkippedRowGaps(inv.Skipped, scene.Elements.Where(e => !e.Famille), scene.TypeNames));
    }

    [Fact]
    public void A_model_is_matched_without_case_after_trimming()
    {
        var scene = new Scene();
        var inv = LoadWrittenInventory();

        // A type named "  s500 " on the ElementType page is the same truss as the inventory's "S500"; a gap
        // that disappears because of a stray space is a check silently not performed.
        var shouting = new Dictionary<Guid, string> { [scene.S500Id] = "  s500 ", [scene.P300Id] = "p300" };

        Assert.Equal(3, ReportDataBuilder.SkippedRowGaps(inv.Skipped, scene.Elements, shouting).Count);
    }

    // ═══════════════════════════════════════════════════════════════
    // One piece, one weight
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void One_piece_with_two_weights_in_two_files_keeps_the_heavier_in_both()
    {
        var inv = LoadWrittenInventory();

        // S500 Alu 2,8 m is 30 kg in MotherGrill.xlsx and 49 kg in Palonnier.xlsx. It is one truss, so it has
        // one weight everywhere, and it is the heavier: that is what the attachment points actually carry.
        var alu = inv.Mothers.Single(b => b.Type == "S500 Alu");
        Assert.True(alu is
            {
                WeightKg: 49, Length: 2.8, StockInit: 5, Stock: 5, Capacity: 1500,
                Material: "Alu", Brand: "Stacco", Usage: "mother",
            },
            "la ligne mother garde ses propres champs et prend le poids le plus lourd");
        Assert.Equal(49.0, inv.Palonniers.Single(b => b.Type == "S500 Alu").WeightKg);
        Assert.Contains(
            "« S500 Alu » de 2,8 m a deux poids propres dans MotherGrill.xlsx et Palonnier.xlsx (30 et 49 kg) : "
            + "le plus lourd, 49 kg, est retenu.",
            inv.Warnings);
    }

    [Fact]
    public void One_piece_with_two_weights_in_one_file_keeps_the_heavier_too()
    {
        var inv = LoadWrittenInventory();

        Assert.True(inv.Mothers.Where(b => b.Type == "S500" && Math.Abs(b.Length - 5.6) < 1e-9).All(b => b.WeightKg == 170),
            "deux lignes S500 de 5,6 m, 160 et 170 kg : les deux à 170 kg");
        Assert.Contains(
            "« S500 » de 5,6 m a deux poids propres dans MotherGrill.xlsx (160 et 170 kg) : le plus lourd, 170 kg, "
            + "est retenu.",
            inv.Warnings);
    }

    [Fact]
    public void A_weight_nobody_entered_stays_unknown_rather_than_becoming_zero()
    {
        var inv = LoadWrittenInventory();

        Assert.Null(inv.Palonniers.Single(b => b.Type == "P300" && Math.Abs(b.Length - 3.0) < 1e-9).WeightKg);
    }

    [Fact]
    public void The_catalogue_and_the_load_table_read_the_reconciled_weight()
    {
        var inv = LoadWrittenInventory();

        // Reconciled once, before anything reads the rows, so the optimiser, the load table and the study can
        // never disagree about what a piece weighs - and the catalogue adds no second warning about it.
        Assert.True(inv.Catalog.Find("S500 Alu", 2.8) is { UnitWeightKg: 49, Stock: 8 }, "S500 Alu : 49 kg, 5 + 3 en stock");
        Assert.True(inv.Catalog.Find("s500", 5.6) is { UnitWeightKg: 170, Stock: 12 }, "S500 de 5,6 m : 170 kg, 10 + 2 en stock");
        Assert.Equal(49 / 2.8, inv.MotherTable.At("S500 Alu", 4.0).KgPerM!.Value, 9);
        Assert.Equal(2, inv.Warnings.Count(w => w.Contains("poids propres")));
    }

    [Fact]
    public void The_catalogue_alone_keeps_the_heavier_whichever_row_comes_first()
    {
        // Rows arrive in whatever order a workbook holds them; the weight kept must not depend on it, and the
        // warning must read the same way round either way.
        foreach (var order in new[] { new double[] { 49, 30 }, new double[] { 30, 49 } })
        {
            var warnings = new List<string>();

            var catalog = TrussCatalog.FromInventory(
                order.Select(w => new BarSpec
                    { Type = "A", Length = 2.8, StockInit = 1, Stock = 1, Capacity = 100, WeightKg = w }),
                warnings);

            Assert.Equal(49.0, catalog.Find("A", 2.8)?.UnitWeightKg);
            Assert.Equal(
                new[] { "« A » de 2,8 m a deux poids propres dans l'inventaire (30 et 49 kg) : le plus lourd, 49 kg, est retenu." },
                warnings);
        }
    }

    // ── The workbooks, written for the test and read back ─────────

    /// <summary>
    /// The two inventory workbooks, as a person keeps them: with rows that cannot be read, a model that is
    /// both a mother and a palonnier, and one piece given two weights.
    /// </summary>
    private Inventory LoadWrittenInventory()
    {
        WriteInventory(InventoryFiles.MothersFileName,
            new object?[] { "S500", 2.8, 10.0, 80.0, 3000.0 },                                  // line 2
            new object?[] { "S500", 5.6, 10.0, 160.0, 2500.0 },                                 // 3
            new object?[] { "S500", 7.5, 0.0, 200.0, "abc" },                                   // 4: charge unreadable
            new object?[] { "S500", "x", 5.0, 80.0, 2000.0 },                                   // 5: length unreadable
            new object?[] { "S500", 8.4, 0.0, 240.0, 2286.0 },                                  // 6
            new object?[] { "S500 Alu", 2.8, 5.0, 30.0, 1500.0, "Alu", "Stacco", "mother" },    // 7: 49 kg in Palonnier.xlsx
            new object?[] { "S500", 5.6, 2.0, 170.0, 2600.0 },                                  // 8: 160 kg on line 3
            new object?[] { "Q100", "y", 1.0, 10.0, 100.0 },                                    // 9: unreadable, model unused
            new object?[] { "P300", 4.0, 1.0, 50.0, "?" });                                     // 10: unreadable, no mother is a P300
        WriteInventory(InventoryFiles.PalonniersFileName,
            new object?[] { "P300", 4.0, 6.0, 50.0, 900.0 },                                    // 2
            new object?[] { "P300", 6.0, 2.0, 75.0, null },                                     // 3: charge blank
            new object?[] { "S500 Alu", 2.8, 3.0, 49.0, 1400.0, "Alu", "Stacco", "palonnier" }, // 4
            new object?[] { "P300", 3.0, 4.0, null, 1000.0 });                                  // 5: weight blank

        return InventoryFiles.Load(_temp, null);
    }

    private void WriteInventory(string file, params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Inventaire");

        string[] headers =
        {
            "Modèle", "Longueur", "Nombre", "Poids propre", "Charge ponctuelle au milieu",
            "Matériau", "Marque", "Usage",
        };
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                XLCellValue value = rows[r][c] switch
                {
                    null => Blank.Value,
                    double d => d,
                    string s => s,
                    var other => other.ToString() ?? "",
                };
                ws.Cell(r + 2, c + 1).Value = value;
            }

        wb.SaveAs(Path.Combine(_temp, file));
    }

    // ── The scenario ─────────────────────────────────────────────

    private static MotherGridPalonnier El(
        string name, Guid type, bool mother, double x1, double y1, double x2, double y2, string? composition) => new()
    {
        MotherGridPalonnierId = Guid.NewGuid(), ElementTypeId = type, Famille = mother, Name = name,
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Composition = composition,
    };

    /// <summary>
    /// A pre-rig of six elements, built in code so the rules are checked against arguments and nothing else.
    /// A fresh one per test: no test may see what another one did.
    /// </summary>
    private sealed class Scene
    {
        public readonly Guid S500Id = Guid.NewGuid();
        public readonly Guid P300Id = Guid.NewGuid();

        public readonly Dictionary<Guid, string> TypeNames;
        public readonly TrussCatalog Catalog;

        public readonly MotherGridPalonnier Mother, Palonnier, LongPalonnier, OldMother, Unreadable, Partial;
        public readonly List<MotherGridPalonnier> Elements;

        public Scene()
        {
            TypeNames = new Dictionary<Guid, string> { [S500Id] = "S500", [P300Id] = "P300" };

            var motherRows = new List<BarSpec>
            {
                new() { Type = "S500", Length = 2.8, StockInit = 10, Stock = 10, Capacity = 3000, WeightKg = 80 },
                new() { Type = "S500", Length = 5.6, StockInit = 10, Stock = 10, Capacity = 2500, WeightKg = 160 },
                // The length as a spreadsheet formula gives it: 8,399999999999999, keyed in whole centimetres.
                new() { Type = "S500", Length = 8.399999999999999, StockInit = 0, Stock = 0, Capacity = 2286, WeightKg = 240 },
            };
            var palonnierRows = new List<BarSpec>
            {
                new() { Type = "P300", Length = 4.0, StockInit = 6, Stock = 6, Capacity = 900, WeightKg = 50 },
            };
            Catalog = TrussCatalog.FromInventory(motherRows.Concat(palonnierRows), new List<string>());

            Mother = El("S500 L=22,4m #1", S500Id, true, G, Row3, D, Row3, "[560,560,560,560]");
            Palonnier = El("P300 L=12,0m #1", P300Id, false, -10.8, Row3, -10.8, Row6, "[400,400,400]");
            LongPalonnier = El("P300 L=16,0m #2", P300Id, false, -5.0, Row1, -5.0, Row5, "[400,400,400,400]");
            OldMother = El("S500 L=8,4m #9", S500Id, true, F, -11.05, E, -11.05, null);
            Unreadable = El("S500 L=2,8m #7", S500Id, true, -1.0, -15.151, 1.0, -15.151, "oops");
            // 9,99 m of truss the inventory does not weigh, assembled with a 5,6 m piece it does.
            Partial = El("S500 L=15,6m #5", S500Id, true, D, 3.7, D + 15.0, 3.7, "[999,560]");

            Elements = new List<MotherGridPalonnier> { Mother, Palonnier, LongPalonnier, OldMother, Unreadable, Partial };
        }

        public TrussInventory BuildInventory(List<string>? warnings = null) =>
            ReportDataBuilder.BuildTrussInventory(Elements, TypeNames, Catalog, warnings ?? new List<string>());
    }
}
