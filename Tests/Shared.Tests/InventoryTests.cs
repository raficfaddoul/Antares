using AliantWebS.Models;
using AliantWebS.Reporting;
using ClosedXML.Excel;
using RiggingOptimizer.IO;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// The inventory as the application reads it: MotherGrill.xlsx and Palonnier.xlsx, the eight columns a
/// reference is made of, the catalogue a truss is weighed from, and what the reader says about a row it
/// cannot trust. The venue's own two workbooks are read here - every figure the studies quote comes from
/// them, so a row read differently is a study read differently - and small workbooks are written for the
/// cases the venue's own files do not contain.
/// </summary>
public sealed class InventoryTests : IDisposable
{
    // Each test writes its workbooks in its own folder under %TEMP%\InventoryTests and takes it away
    // again: a test reads no file another test wrote, so nothing depends on the order they run in.
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), nameof(InventoryTests), Guid.NewGuid().ToString("N"));

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

    private static Inventory Venue() => InventoryFiles.Load(Fixtures.Dir, null);

    // ── The venue's own two workbooks ────────────────────────────

    [Fact]
    public void Both_workbooks_are_found_and_read_without_a_warning()
    {
        var inv = Venue();

        Assert.True(inv.MissingFiles.Count == 0,
            "fichier d'inventaire introuvable : " + string.Join(", ", inv.MissingFiles));

        // A warning is the reader saying it could not trust a row. The venue's own files must raise none:
        // a warning here means a reference silently lost its length, its value or its mass.
        Assert.True(inv.Warnings.Count == 0,
            "l'inventaire du site doit se lire sans avertissement : " + string.Join(" | ", inv.Warnings));

        Assert.Equal(6, inv.Mothers.Count);
        Assert.Equal(9, inv.Palonniers.Count);
    }

    [Fact]
    public void A_length_written_as_a_formula_is_read_as_its_value()
    {
        var inv = Venue();
        var s500 = inv.Mothers.Where(b => b.Type == "S500 Carrée Acier").OrderBy(b => b.Length).ToList();
        var longest = s500.FirstOrDefault(b => Math.Abs(b.Length - 5.6) < 0.005);

        // That row's Longueur is typed "=2*B3". Read as text it is no length at all, and the model loses
        // the row every span between 2,8 m and 5,6 m is held to.
        Assert.True(longest is not null,
            "la ligne de 5,6 m (Longueur écrite « =2*B3 ») n'a pas été lue ; longueurs lues : "
            + string.Join(", ", s500.Select(b => b.Length)));

        Assert.Equal(5.6, longest!.Length, 6);

        // The rest of its row comes with it, or the piece is right and its stock and its value are not.
        Assert.True(longest.Stock == 39 && longest.StockInit == 39, $"Nombre lu : {longest.Stock}");
        Assert.Equal(95.6, longest.WeightKg!.Value, 6);
        Assert.Equal(3429, longest.Capacity, 6);
    }

    [Fact]
    public void A_row_carries_the_whole_inventory_line_it_came_from()
    {
        var inv = Venue();
        var shortest = inv.Mothers.Where(b => b.Type == "S500 Carrée Acier").OrderBy(b => b.Length).First();

        // Marque and Matériau name the reference in the study ("STACCO Acier"); Usage is what tells the
        // reader a row sits in the wrong workbook. None of the three may be dropped on the way in.
        Assert.Equal("STACCO", shortest.Brand);
        Assert.Equal("Acier", shortest.Material);
        Assert.Equal("MotherGrid", shortest.Usage);

        // A copy travels through the optimiser in place of the row; it must stay the same piece.
        var copy = shortest.Clone();
        Assert.True(copy.Brand == shortest.Brand && copy.Material == shortest.Material
                    && copy.Usage == shortest.Usage && copy.WeightKg == shortest.WeightKg
                    && copy.Capacity == shortest.Capacity && copy.StockInit == shortest.StockInit,
            $"la copie a perdu une partie de sa ligne : {copy.Brand} / {copy.Material} / {copy.Usage} / "
            + $"{copy.WeightKg} kg / {copy.Capacity} kg / {copy.StockInit} pièces");
    }

    [Fact]
    public void Accented_and_punctuated_model_names_survive_the_read()
    {
        var inv = Venue();

        // A model name is the key everything else is looked up by - the abaque, the catalogue, the element
        // type - so "Carrée" read as "Carr?e" would quietly leave the reference unverified.
        Assert.True(inv.Palonniers.Any(b => b.Type == "S500 Carrée Alu"),
            "modèles lus dans Palonnier.xlsx : " + string.Join(", ", inv.Palonniers.Select(b => b.Type).Distinct()));
        Assert.True(inv.Mothers.Any(b => b.Type == "C500F / TABMA"),
            "modèles lus dans MotherGrill.xlsx : " + string.Join(", ", inv.Mothers.Select(b => b.Type).Distinct()));
    }

    [Fact]
    public void The_catalogue_gives_each_reference_its_mass_and_its_printed_name()
    {
        var inv = Venue();

        // The database records which trusses a scenario uses, never what one weighs: every mass in the
        // study is this catalogue's, read from the same rows as the stock and the load values.
        Assert.Equal(31, inv.Catalog.Find("S500 Carrée Alu", 2.8)!.UnitWeightKg!.Value, 6);
        Assert.Equal(47.8, inv.Catalog.Find("C500F / TABMA", 5.6)!.UnitWeightKg!.Value, 6);

        // Printed brand first, as the studies name a truss.
        Assert.Equal("ASD SC500 lg:2,8m", inv.Catalog.Find("SC500", 2.8)!.Label);

        // Found whatever the case of the model, since the name travels through the database and back.
        Assert.Equal(35, inv.Catalog.Find("s500 carrée alu", 3)!.UnitWeightKg!.Value, 6);

        // The length typed as a formula is catalogued at the metre it evaluates to, not at some neighbour.
        Assert.Equal(95.6, inv.Catalog.Find("S500 Carrée Acier", 5.6)!.UnitWeightKg!.Value, 6);
    }

    [Fact]
    public void A_model_is_only_read_from_the_workbook_of_its_role()
    {
        var inv = Venue();

        // The two files are two roles, not one stock listed twice: a palonnier's values must never be
        // lent to a mother line, which spans bays several times as long.
        Assert.True(inv.PalonnierAbaques.ContainsKey("ALC500"), "ALC500 est un palonnier de Palonnier.xlsx");
        Assert.False(inv.MotherAbaques.ContainsKey("ALC500"), "ALC500 ne figure pas dans MotherGrill.xlsx");
        Assert.True(inv.MotherAbaques.ContainsKey("C500F / TABMA"), "C500F / TABMA est un mother de MotherGrill.xlsx");
        Assert.False(inv.PalonnierAbaques.ContainsKey("C500F / TABMA"), "C500F / TABMA ne figure pas dans Palonnier.xlsx");
    }

    // ── How a row is read ────────────────────────────────────────

    /// <summary>
    /// A workbook as a person writes one: the columns in another order, the headings in another case,
    /// without accents and with extra spaces, an unknown column among them, and numbers typed as text
    /// with either decimal separator.
    /// </summary>
    private Inventory Loose()
    {
        var dir = Folder("loose");
        InventoryWorkbook.Write(dir, InventoryFiles.MothersFileName,
            new[] { "usage", "CHARGE  PONCTUELLE AU MILIEU", "modele", "nombre", "Longueur", "poids propre", "Commentaire" },
            new object?[] { "MotherGrid", 4000.0, "M texte", "3", "2,8", "31,5", "ok" },
            new object?[] { "MotherGrid", "3500", "M texte", 2, "5.6", null, "" },
            new object?[] { "MotherGrid", 3000.0, "M zéro", 1, 8.4, 0.0, "" });
        InventoryWorkbook.Write(dir, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "P", 2.0, 4, 20.0, 2500.0, "Alu", null, "Palonnier" });
        return InventoryFiles.Load(dir, null);
    }

    [Fact]
    public void Columns_are_found_by_their_heading_in_any_order_case_or_accents()
    {
        var inv = Loose();
        var rows = inv.Mothers.OrderBy(b => b.Length).ToList();

        Assert.True(rows.Count == 3,
            $"{rows.Count} lignes lues au lieu de 3 : " + string.Join(" | ", inv.Warnings));
        Assert.Equal("M texte", rows[0].Type);
        Assert.Equal(4000, rows[0].Capacity, 6);
        Assert.Equal(3, rows[0].Stock);
    }

    [Fact]
    public void A_number_typed_as_text_is_read_with_either_decimal_separator()
    {
        var inv = Loose();
        var rows = inv.Mothers.OrderBy(b => b.Length).ToList();

        // A length or a value read as text would skip the row, and the model would lose that length
        // altogether - the study would then hold the span to the next longer row without saying so.
        Assert.Equal(3, rows.Count);
        Assert.Equal(2.8, rows[0].Length, 6);    // "2,8"
        Assert.Equal(5.6, rows[1].Length, 6);    // "5.6"
        Assert.Equal(3500, rows[1].Capacity, 6); // "3500"
        Assert.Equal(31.5, rows[0].WeightKg!.Value, 6);  // "31,5"
    }

    [Fact]
    public void A_blank_or_zero_poids_propre_is_an_unknown_mass_never_a_weightless_truss()
    {
        var inv = Loose();
        var rows = inv.Mothers.OrderBy(b => b.Length).ToList();

        Assert.Equal(3, rows.Count);
        Assert.True(rows[1].WeightKg is null, $"Poids propre vide lu comme {rows[1].WeightKg} kg");

        // A 0 typed as a placeholder would otherwise make the truss weightless in every load it feeds,
        // and in every value computed past the model's longest row.
        Assert.True(rows[2].WeightKg is null, $"Poids propre de 0 lu comme {rows[2].WeightKg} kg");
        Assert.True(inv.Catalog.Find("M zéro", 8.4) is { UnitWeightKg: null },
            "le catalogue doit laisser la masse inconnue plutôt que de peser 0 kg");
    }

    [Fact]
    public void Without_a_brand_a_truss_is_printed_as_its_model_and_its_length()
    {
        var inv = Loose();
        var type = Guid.NewGuid();
        var line = ReportDataBuilder.BuildTrussInventory(
            new List<MotherGridPalonnier> { NewElement(type, "M texte L=2,8m #1", mother: true, 0, 0, 2.8, 0) },
            new Dictionary<Guid, string> { [type] = "M texte" },
            inv.Catalog,
            new List<string>()).Lines.Single();

        // Tableau 2 is what the crew loads on the truck: a line with no name and no mass cannot be checked
        // against the stock.
        Assert.Equal("M texte lg:2,8m", line.Label);
        Assert.Equal(31.5, line.UnitWeightKg!.Value, 6);
    }

    // ── Rows the reader cannot trust ─────────────────────────────

    /// <summary>
    /// One workbook holding each kind of doubtful row: an unreadable length, a missing value, a row whose
    /// Usage belongs to the other file, a model and length listed twice, and a row with no model at all.
    /// </summary>
    private Inventory Doubtful()
    {
        var dir = Folder("bad");
        InventoryWorkbook.Write(dir, InventoryFiles.MothersFileName, InventoryWorkbook.Header,
            new object?[] { "M", "deux", 1, 10.0, 3000.0, "Acier", "X", "MotherGrid" },
            new object?[] { "M", 2.8, 1, 10.0, null, "Acier", "X", "MotherGrid" },
            new object?[] { "M", 5.6, 1, 10.0, 3000.0, "Acier", "X", "Palonnier" },
            new object?[] { "M", 5.6, 2, 10.0, 3000.0, "Acier", "X", "MotherGrid" },
            new object?[] { null, 7.0, 1, 10.0, 3000.0, "Acier", "X", "MotherGrid" });
        InventoryWorkbook.Write(dir, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "P", 2.0, 4, 20.0, 2500.0, "Alu", null, "Palonnier" });
        return InventoryFiles.Load(dir, null);
    }

    [Fact]
    public void A_row_that_cannot_be_read_is_skipped_and_its_line_named()
    {
        var inv = Doubtful();
        var said = string.Join(" | ", inv.Warnings);

        // Named by its spreadsheet line, because the fix is made in the workbook, not in the application.
        Assert.True(inv.Warnings.Any(w => w.Contains("ligne 2") && w.Contains("longueur illisible")),
            "une longueur illisible doit nommer sa ligne : " + said);
        Assert.True(inv.Warnings.Any(w => w.Contains("ligne 3") && w.Contains("charge ponctuelle au milieu")),
            "une charge ponctuelle au milieu manquante doit nommer sa ligne : " + said);

        // And the row goes, rather than staying with a value nobody wrote: no charge is ever compared
        // against an invented limit.
        Assert.True(inv.Mothers.All(b => b.Capacity > 0), "aucune ligne ne doit rester sans charge admissible");
    }

    [Fact]
    public void A_row_whose_usage_belongs_to_the_other_workbook_is_flagged()
    {
        var inv = Doubtful();

        // The row is still read as a mother - the file it sits in decides - but a person is told, because
        // a palonnier's value read as a mother's is read over a bay several times as long.
        Assert.True(inv.Warnings.Any(w => w.Contains("ligne 4") && w.Contains("usage « Palonnier »")),
            "l'usage en désaccord avec le fichier doit être signalé : " + string.Join(" | ", inv.Warnings));
    }

    [Fact]
    public void A_model_and_length_listed_twice_is_flagged()
    {
        var inv = Doubtful();

        // Two rows for one piece: the lower value is kept, and the pieces of both rows stay in stock. A
        // person has to know, because neither reading is obviously the one that was meant.
        Assert.True(inv.Warnings.Any(w => w.Contains("ligne 5") && w.Contains("plusieurs fois")),
            "une longueur répétée doit être signalée : " + string.Join(" | ", inv.Warnings));
    }

    [Fact]
    public void A_row_with_no_model_is_ignored()
    {
        var inv = Doubtful();

        // A row with no Modèle is a blank line a person left behind, not a reference: it is passed over in
        // silence, and only the two readable rows remain.
        Assert.True(inv.Mothers.Count == 2,
            $"{inv.Mothers.Count} lignes retenues au lieu de 2 : "
            + string.Join(", ", inv.Mothers.Select(b => $"{b.Type} {b.Length} m")));
    }

    [Fact]
    public void One_piece_has_one_weight_and_the_heavier_is_the_one_kept()
    {
        // The same truss serves as a mother and as a palonnier: one piece, so one weight. The heavier is
        // the safe one - it is what the attachment points carry - and the two files are named, because the
        // disagreement is theirs to settle.
        var dir = Folder("deuxpoids");
        InventoryWorkbook.Write(dir, InventoryFiles.MothersFileName, InventoryWorkbook.Header,
            new object?[] { "M double", 5.6, 4, 90.0, 3000.0, "Acier", "Essai", "MotherGrid" });
        InventoryWorkbook.Write(dir, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "M double", 5.6, 2, 95.0, 2500.0, "Acier", "Essai", "Palonnier" });

        var inv = InventoryFiles.Load(dir, null);
        var said = string.Join(" | ", inv.Warnings);

        Assert.Equal(95.0, inv.Mothers.Single().WeightKg!.Value, 6);
        Assert.Equal(95.0, inv.Palonniers.Single().WeightKg!.Value, 6);
        Assert.Equal(95.0, inv.Catalog.Find("M double", 5.6)!.UnitWeightKg!.Value, 6);
        Assert.True(inv.Warnings.Any(w => w.Contains("poids propres") && w.Contains("le plus lourd")),
            "les deux poids propres doivent être signalés : " + said);
    }

    // ── Where the files are read from ────────────────────────────

    [Fact]
    public void The_scenario_s_own_copy_is_read_before_the_shared_one()
    {
        var scenario = Folder("scenario");
        var shared = Folder("shared");
        InventoryWorkbook.Write(scenario, "palonnier.XLSX", InventoryWorkbook.Header,
            new object?[] { "P scénario", 2.0, 4, 20.0, 2500.0, "Alu", null, "Palonnier" });
        InventoryWorkbook.Write(shared, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header,
            new object?[] { "P partagé", 2.0, 4, 20.0, 2500.0, "Alu", null, "Palonnier" });
        InventoryWorkbook.Write(shared, InventoryFiles.MothersFileName, InventoryWorkbook.Header,
            new object?[] { "M partagé", 5.6, 4, 90.0, 3000.0, "Acier", null, "MotherGrid" });

        var inv = InventoryFiles.Load(scenario, shared);

        // A scenario that carries its own stock is read from it, whatever the case of the file's name -
        // the same scenario opened on another machine must read the same inventory.
        Assert.Equal("P scénario", inv.Palonniers.Single().Type);

        // What it does not carry comes from the shared folder, so a common stock is kept in one place.
        Assert.Equal("M partagé", inv.Mothers.Single().Type);
    }

    [Fact]
    public void A_workbook_found_nowhere_is_reported_rather_than_thrown()
    {
        var inv = InventoryFiles.Load(Folder("vide"), Folder("vide2"));

        // The study can still be written, saying what it could not check. It is the optimiser, which can
        // place nothing without stock, that refuses instead.
        Assert.Equal(
            new[] { InventoryFiles.MothersFileName, InventoryFiles.PalonniersFileName },
            inv.MissingFiles);
    }

    [Fact]
    public void The_optimiser_refuses_to_run_without_the_inventory_naming_the_file()
    {
        var dir = Folder("chargesseules");
        File.Copy(Fixtures.Charges, Path.Combine(dir, "Charges.xlsx"));

        var error = Assert.Throws<FileNotFoundException>(() => { ExcelLoader.LoadInventory(dir, null); });

        // Named, and named in the folder the user is actually looking at: an optimisation run against half
        // an inventory would place trusses the venue does not own.
        Assert.True(error.Message.Contains(InventoryFiles.PalonniersFileName),
            "le fichier manquant doit être nommé : " + error.Message);
    }

    [Fact]
    public void An_inventory_whose_workbook_holds_no_readable_line_is_refused()
    {
        // The file is there and its header is right, but not one row could be read. There is nothing to
        // place: better to stop here than to return an empty plan that looks like an answer.
        var dir = Folder("aucuneligne");
        InventoryWorkbook.Write(dir, InventoryFiles.MothersFileName, InventoryWorkbook.Header,
            new object?[] { "M", 5.6, 4, 90.0, 3000.0, "Acier", "Essai", "MotherGrid" });
        InventoryWorkbook.Write(dir, InventoryFiles.PalonniersFileName, InventoryWorkbook.Header);

        var error = Assert.Throws<InvalidDataException>(() => { ExcelLoader.LoadInventory(dir, null); });

        Assert.True(error.Message.Contains(InventoryFiles.PalonniersFileName)
                    && error.Message.Contains("aucune ligne lisible"),
            "le refus doit nommer le fichier vide : " + error.Message);
    }

    private static MotherGridPalonnier NewElement(Guid type, string name, bool mother,
        double x1, double y1, double x2, double y2) => new()
    {
        MotherGridPalonnierId = Guid.NewGuid(),
        ElementTypeId = type,
        Name = name,
        Famille = mother,
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
    };
}

/// <summary>
/// Inventory workbooks written for a test. The eight headings are the ones the readers expect, in the
/// order a file written without a header row has to put its columns in. The header goes on row 1 and the
/// references from row 2, so passing no heading at all leaves row 1 unused - which is exactly a file
/// written without a header row, since an unused row is not a row.
/// Used by <see cref="InventoryTests"/> and <see cref="ExcelHeaderTests"/>, which write the same files.
/// </summary>
internal static class InventoryWorkbook
{
    public static readonly string[] Header =
    {
        "Modèle", "Longueur", "Nombre", "Poids propre", "Charge ponctuelle au milieu", "Matériau", "Marque", "Usage",
    };

    public static string Write(string dir, string file, string[] header, params object?[][] rows)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, file);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Feuil1");
        for (int c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];

        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                switch (rows[r][c])
                {
                    case null: break;
                    case string s when s.StartsWith("="): cell.FormulaA1 = s[1..]; break;
                    case string s: cell.Value = s; break;
                    case int i: cell.Value = i; break;
                    case double d: cell.Value = d; break;
                }
            }

        wb.SaveAs(path);
        return path;
    }
}
