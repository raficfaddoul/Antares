using Helpers;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// The example workbooks the application publishes at wwwroot/exemples, read by the readers that will
/// actually read them. They are offered to a user as "the expected format": one the reader refuses, or reads
/// differently from what it looks like, is worse than no example at all, and nothing else in the build opens
/// these files. The numbers pinned here are the ones the examples are drawn with - a deliberate change to a
/// file is a deliberate change to a test.
/// </summary>
public class ExampleFilesTests : IClassFixture<ExampleFilesTests.PublishedExamples>
{
    private readonly PublishedExamples _ex;

    public ExampleFilesTests(PublishedExamples ex) => _ex = ex;

    /// <summary>
    /// The three example workbooks read once for the whole class, each with the notes its reader wrote while
    /// reading it. Nothing is modified afterwards, so the tests may run in any order.
    /// </summary>
    public sealed class PublishedExamples
    {
        public IReadOnlyList<Charge> Charges { get; }

        /// <summary>What the charges reader noticed, in French: the notes the page shows the user.</summary>
        public IReadOnlyList<string> ChargeNotes { get; }

        public IReadOnlyList<(string Name, string ElementTypeName, double X1, double Y1,
            double X2, double Y2, string? Composition)> Elements { get; }

        public IReadOnlyList<string> ElementNotes { get; }

        /// <summary>MotherGrill.xlsx and Palonnier.xlsx, as the optimiser and the study read them.</summary>
        public Inventory Inventory { get; }

        public PublishedExamples()
        {
            // Say where, rather than let the spreadsheet library report a path nobody recognises: the
            // examples are found by walking up from the test assembly, so a move of the projects shows here.
            if (!Directory.Exists(Fixtures.ExamplesDir))
                throw new DirectoryNotFoundException(
                    $"Les exemples publiés sont introuvables : {Fixtures.ExamplesDir}");

            var chargeNotes = new List<string>();
            Charges = ExcelLoader.LoadCharges(Path.Combine(Fixtures.ExamplesDir, "Charges.xlsx"), chargeNotes);
            ChargeNotes = chargeNotes;

            var elementNotes = new List<string>();
            using (var stream = File.OpenRead(Path.Combine(Fixtures.ExamplesDir, "Elements.xlsx")))
                Elements = ExcelLoader.LoadMotherGridPalonniers(stream, elementNotes);
            ElementNotes = elementNotes;

            Inventory = InventoryFiles.Load(Fixtures.ExamplesDir, null);
        }
    }

    // ── Charges.xlsx ─────────────────────────────────────────────────────────

    [Fact]
    public void Charges_xlsx_reads_its_eight_loads()
    {
        Assert.Equal(8, _ex.Charges.Count);
    }

    [Fact]
    public void The_header_row_of_Charges_xlsx_is_not_read_as_a_load()
    {
        // "Nom | X | Y | Poids" weighs nothing and hangs nowhere: read as a load it would be placed.
        Assert.True(_ex.Charges.All(c => c.Name != "Nom"),
            "la ligne d'en-tête est prise pour une charge : " + string.Join(", ", _ex.Charges.Select(c => c.Name)));
    }

    [Fact]
    public void The_reader_says_it_took_the_first_line_of_Charges_xlsx_for_a_header()
    {
        // The example teaches the format, so it carries a header - and the reader must say out loud that it
        // skipped a line, because the same silence on a file without one would lose a real load.
        Assert.True(_ex.ChargeNotes.Any(n => n.Contains("en-tête")),
            "le lecteur ne signale pas la ligne d'en-tête : " + string.Join(" | ", _ex.ChargeNotes));
    }

    [Fact]
    public void The_columns_of_Charges_xlsx_are_found_by_their_headings_not_by_position()
    {
        // Columns found by heading are what lets a user add or move one. Falling back to fixed positions
        // still reads the file, so only the note tells the difference.
        Assert.True(!_ex.ChargeNotes.Any(n => n.Contains("Aucun en-tête reconnu")),
            "les colonnes ont été lues par position, pas par leur intitulé : "
            + string.Join(" | ", _ex.ChargeNotes));
    }

    [Fact]
    public void L1_lands_where_the_studies_axes_put_it()
    {
        // The file is written in the studies' axes - L1 at (-7,45 ; -2,99) on Figure 2 - and everything
        // downstream works in the grid's. The reader converts once, on the way in.
        var (x, y) = StudyAxes.ToGrid(-7.45, -2.99);
        var l1 = _ex.Charges.FirstOrDefault(c => c.Name == "L1");

        Assert.True(l1 is not null,
            "la charge L1 n'est plus dans Charges.xlsx : " + string.Join(", ", _ex.Charges.Select(c => c.Name)));
        Assert.Equal(x, l1!.X, 9);
        Assert.Equal(y, l1.Y, 9);
        Assert.Equal(154.0, l1.Weight, 9);
    }

    // ── Elements.xlsx ────────────────────────────────────────────────────────

    [Fact]
    public void Elements_xlsx_reads_its_four_elements()
    {
        Assert.Equal(4, _ex.Elements.Count);
    }

    [Fact]
    public void The_header_row_of_Elements_xlsx_is_not_read_as_an_element()
    {
        Assert.True(_ex.Elements.All(e => e.Name != "Nom"),
            "la ligne d'en-tête est prise pour un élément : "
            + string.Join(", ", _ex.Elements.Select(e => e.Name)));
    }

    [Fact]
    public void Every_element_carries_its_type_and_its_pieces()
    {
        // Without a type the element has no abaque, and without its pieces the study cannot weigh it or say
        // where it is joined: both are what the example is there to show.
        var incomplete = _ex.Elements
            .Where(e => e.ElementTypeName.Length == 0 || string.IsNullOrEmpty(e.Composition))
            .Select(e => $"{e.Name} : type « {e.ElementTypeName} », pièces « {e.Composition} »")
            .ToList();

        Assert.True(incomplete.Count == 0,
            "élément(s) sans type ou sans composition : " + string.Join(" | ", incomplete));
    }

    [Fact]
    public void The_columns_of_Elements_xlsx_are_found_by_their_headings()
    {
        Assert.True(!_ex.ElementNotes.Any(n => n.Contains("Aucun en-tête reconnu")),
            "les colonnes ont été lues par position, pas par leur intitulé : "
            + string.Join(" | ", _ex.ElementNotes));
    }

    [Fact]
    public void MG1_keeps_the_ends_the_file_gives_it()
    {
        // Written (0 ; -7,14) → (0 ; 14,41) in the studies' axes: a mother, across the beams, so one X and
        // two Y. Read back in the grid's axes, both ends must be where the file put them.
        var mg1 = _ex.Elements.FirstOrDefault(e => e.Name == "MG1");
        Assert.True(mg1.Name == "MG1",
            "l'élément MG1 n'est plus dans Elements.xlsx : " + string.Join(", ", _ex.Elements.Select(e => e.Name)));

        var (x1, y1) = StudyAxes.ToGrid(0.0, -7.14);
        var (x2, y2) = StudyAxes.ToGrid(0.0, 14.41);

        Assert.Equal(x1, mg1.X1, 9);
        Assert.Equal(y1, mg1.Y1, 9);
        Assert.Equal(x2, mg1.X2, 9);
        Assert.Equal(y2, mg1.Y2, 9);
    }

    [Fact]
    public void Every_element_s_pieces_reach_the_length_it_is_drawn_over()
    {
        // An element is drawn from end to end, and assembled from the pieces named beside it. Pieces that
        // fall short leave a gap the study reports on every page it appears. The centimetre of slack is the
        // rounding of a length written by hand.
        var tooShort = new List<string>();

        foreach (var e in _ex.Elements)
        {
            var pieces = TrussComposition.ParseText(e.Composition);
            var drawn = Math.Sqrt(Math.Pow(e.X2 - e.X1, 2) + Math.Pow(e.Y2 - e.Y1, 2));
            var total = pieces?.Sum() ?? 0;

            if (pieces is not { Count: > 0 } || total < drawn - 0.01)
                tooShort.Add($"{e.Name} : {total:0.##} m de pièces pour {drawn:0.##} m tracés");
        }

        Assert.True(tooShort.Count == 0,
            "élément(s) dont les pièces n'atteignent pas la longueur tracée : " + string.Join(" | ", tooShort));
    }

    // ── MotherGrill.xlsx and Palonnier.xlsx ──────────────────────────────────

    [Fact]
    public void The_example_inventory_is_found_and_read()
    {
        Assert.True(_ex.Inventory.MissingFiles.Count == 0,
            "fichier(s) d'inventaire absents de wwwroot/exemples : "
            + string.Join(", ", _ex.Inventory.MissingFiles));

        Assert.Equal(6, _ex.Inventory.Mothers.Count);
        Assert.Equal(7, _ex.Inventory.Palonniers.Count);
    }

    [Fact]
    public void The_example_inventory_reads_without_a_single_warning()
    {
        // A warning on the file that teaches the format is a lesson in the wrong thing: the user copies it,
        // and reads the same warning on their own inventory.
        Assert.True(_ex.Inventory.Warnings.Count == 0,
            "l'inventaire d'exemple doit se lire sans avertissement : "
            + string.Join(" | ", _ex.Inventory.Warnings));
    }

    [Fact]
    public void An_inventory_row_keeps_the_whole_of_its_line()
    {
        // Weight, capacity, material and brand all leave the row for somewhere a person reads them: the
        // study's truss table, the mass of the rig, the reference printed in the report.
        var row = _ex.Inventory.Mothers.FirstOrDefault(
            b => b.Type == "S500 Carrée Acier" && Math.Abs(b.Length - 5.6) < 1e-9);

        Assert.True(row is not null,
            "la ligne S500 Carrée Acier de 5,6 m a disparu de MotherGrill.xlsx : "
            + string.Join(" | ", _ex.Inventory.Mothers.Select(b => $"{b.Type} {b.Length}")));

        Assert.Equal(39, row!.StockInit);
        Assert.NotNull(row.WeightKg);
        Assert.Equal(95.6, row.WeightKg!.Value, 6);
        Assert.Equal(3429.0, row.Capacity, 6);
        Assert.Equal("Acier", row.Material);
        Assert.Equal("STACCO", row.Brand);
    }

    [Fact]
    public void The_Nombre_zero_row_is_kept_as_a_load_value_with_no_piece()
    {
        // Nombre = 0 means the row gives a value the load table needs over that span, and never a piece the
        // optimiser may take. Dropping it would make the table read the next longer row instead.
        var value = _ex.Inventory.Mothers.FirstOrDefault(
            b => Math.Abs(b.Length - 7.0) < 1e-9 && b.StockInit == 0);

        Assert.True(value is not null,
            "la ligne de 7 m à Nombre = 0 n'est pas conservée : "
            + string.Join(", ", _ex.Inventory.Mothers.Select(b => $"{b.Length}:{b.StockInit}")));
        Assert.Equal(2000.0, value!.Capacity, 6);
    }

    // ── The set, taken together ──────────────────────────────────────────────

    [Fact]
    public void The_example_files_taken_together_place_every_example_load()
    {
        // The examples are given out as a set, and a user's first run is this one. A load left uncovered
        // means the example inventory cannot carry the example charges: the first thing they would see is
        // a layout that is not saved.
        var (pals, mothers) = ExcelLoader.LoadInventory(Fixtures.ExamplesDir, null);

        var result = RiggingOptimizer.Core.Optimizer.Optimize(
            new List<Charge>(_ex.Charges), pals, mothers, verbose: false, massOf: b => b.WeightKg);

        Assert.True(result.Uncovered.Count == 0,
            "charge(s) qu'aucun palonnier ni aucune ligne de mother ne porte : "
            + string.Join(", ", result.Uncovered.Select(c => c.Name)));
    }
}
