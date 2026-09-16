using RiggingOptimizer.IO;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// The first row of an inventory workbook: whether it names the columns or is already the first truss.
/// One known heading is enough to call it a header row, and then a required heading that is missing stays
/// an error - read as data, a misspelt header would enter the word « Modèle » in the stock as a truss
/// model. A file a person made by copying a few lines out of another has no header at all, and is read in
/// the fixed column order, saying so.
/// The workbooks are written by <see cref="InventoryWorkbook"/>, which puts the header on row 1 and the
/// references from row 2.
/// </summary>
public sealed class ExcelHeaderTests : IDisposable
{
    // Each test writes its workbooks in its own folder under %TEMP%\ExcelHeaderTests and takes it away
    // again, so no test reads a header another test wrote.
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), nameof(ExcelHeaderTests), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_work)) Directory.Delete(_work, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Folder(string name)
    {
        var dir = Path.Combine(_work, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_file_written_without_a_header_row_is_read_in_the_inventory_s_own_column_order()
    {
        // Passing no heading leaves row 1 unused, and an unused row is not a row: the file reads as one
        // written straight from its first line, which is what a person's hand-made extract looks like.
        var dir = Folder("sansentete");
        InventoryWorkbook.Write(dir, InventoryFiles.MothersFileName, Array.Empty<string>(),
            new object?[] { "Mère test", 5.6, 12, 95.0, 3400.0, "Acier", "Essai", "MotherGrid" });
        InventoryWorkbook.Write(dir, InventoryFiles.PalonniersFileName, Array.Empty<string>(),
            new object?[] { "Palonnier test", 3.0, 8, 35.0, 2000.0, "Alu", "Essai", "Palonnier" });

        var inv = InventoryFiles.Load(dir, null);

        Assert.True(inv.Mothers.Count == 1 && inv.Palonniers.Count == 1,
            $"{inv.Mothers.Count} mothers et {inv.Palonniers.Count} palonniers lus au lieu d'un de chaque : "
            + string.Join(" | ", inv.Warnings));

        var mother = inv.Mothers[0];
        Assert.Equal("Mère test", mother.Type);
        Assert.Equal(5.6, mother.Length, 6);
        Assert.Equal(12, mother.StockInit);
        Assert.Equal(95.0, mother.WeightKg!.Value, 6);
        Assert.Equal(3400, mother.Capacity, 6);
        Assert.Equal("Palonnier test", inv.Palonniers[0].Type);

        // Read on an assumption, so the assumption is printed - for both files, since each is decided on
        // its own first row. A column order guessed in silence would put a stock in a load value.
        Assert.True(inv.Warnings.Count(w => w.Contains("en-tête")) == 2,
            "chaque fichier lu sans en-tête doit dire dans quel ordre il a lu ses colonnes : "
            + string.Join(" | ", inv.Warnings));
    }

    [Fact]
    public void A_file_with_a_header_row_is_read_exactly_as_before()
    {
        // The venue's own two workbooks do have a header. Reading a file without one must not have changed
        // anything for them: the studies' figures all come from these rows.
        var inv = InventoryFiles.Load(Fixtures.Dir, null);

        Assert.Equal(6, inv.Mothers.Count);
        Assert.Equal(9, inv.Palonniers.Count);
        Assert.True(inv.Warnings.Count == 0,
            "l'inventaire du site se lit sans avertissement : " + string.Join(" | ", inv.Warnings));
    }

    [Fact]
    public void A_heading_that_carries_its_unit_names_the_same_column()
    {
        // "Longueur (m)" is the heading a person writes. It must find the same column as "Longueur", or a
        // workbook that reads perfectly well to the eye is refused.
        var dir = Folder("entetesavecunites");
        InventoryWorkbook.Write(dir, InventoryFiles.MothersFileName,
            new[] { "Modèle", "Longueur (m)", "Nombre", "Poids propre (kg)", "Charge ponctuelle au milieu (kg)",
                    "Matériau", "Marque", "Usage" },
            new object?[] { "Mère test", 5.6, 12, 95.0, 3400.0, "Acier", "Essai", "MotherGrid" });
        InventoryWorkbook.Write(dir, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "Palonnier test", 3.0, 8, 35.0, 2000.0, "Alu", "Essai", "Palonnier" });

        var inv = InventoryFiles.Load(dir, null);

        Assert.True(inv.Mothers.Count == 1,
            $"{inv.Mothers.Count} lignes lues au lieu d'une : " + string.Join(" | ", inv.Warnings));
        Assert.Equal(5.6, inv.Mothers[0].Length, 6);
        Assert.Equal(3400, inv.Mothers[0].Capacity, 6);
        Assert.Equal(95.0, inv.Mothers[0].WeightKg!.Value, 6);
        Assert.True(inv.Warnings.Count == 0,
            "une unité dans l'en-tête ne doit rien avoir à signaler : " + string.Join(" | ", inv.Warnings));
    }

    [Fact]
    public void A_required_heading_that_is_absent_or_misspelled_is_refused_and_named()
    {
        // A header row that is there but does not name a required column is an error, not a file to read
        // as best one can: read as data the header would enter « Modèle » in the stock as a truss model,
        // and read past it every row would lose the column the whole verification rests on.

        // Absent: the file simply has no such column.
        var absent = Folder("sanscolonne");
        InventoryWorkbook.Write(absent, InventoryFiles.MothersFileName,
            new[] { "Modèle", "Longueur", "Nombre", "Poids propre" },
            new object?[] { "M", 2.8, 1, 10.0 });
        InventoryWorkbook.Write(absent, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "P", 2.0, 4, 20.0, 2500.0, "Alu", null, "Palonnier" });

        var missing = Assert.Throws<InvalidDataException>(() => { InventoryFiles.Load(absent, null); });
        Assert.True(missing.Message.Contains(InventoryFiles.MothersFileName)
                    && missing.Message.Contains("Charge ponctuelle au milieu"),
            "le refus doit nommer le fichier et la colonne manquante : " + missing.Message);

        // Misspelled: the heading is there but says something else, which is the same gap to the reader.
        var wrong = Folder("entetefaux");
        InventoryWorkbook.Write(wrong, InventoryFiles.MothersFileName,
            new[] { "Modèle", "Longueur", "Nombre", "Poids propre", "Charge ponctuelle", "Matériau", "Marque", "Usage" },
            new object?[] { "Mère test", 5.6, 12, 95.0, 3400.0, "Acier", "Essai", "MotherGrid" });
        InventoryWorkbook.Write(wrong, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "Palonnier test", 3.0, 8, 35.0, 2000.0, "Alu", "Essai", "Palonnier" });

        var misspelt = Assert.Throws<InvalidDataException>(() => { InventoryFiles.Load(wrong, null); });
        Assert.True(misspelt.Message.Contains(InventoryFiles.MothersFileName)
                    && misspelt.Message.Contains("Charge ponctuelle au milieu"),
            "un en-tête mal orthographié doit être refusé en nommant la colonne attendue : " + misspelt.Message);
    }
}
