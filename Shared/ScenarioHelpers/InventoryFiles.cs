// =============================================================
// InventoryFiles.cs — The truss inventory: MotherGrill.xlsx and Palonnier.xlsx.
//
// Every truss reference the rig may use, one row each, in two workbooks: the mothers in MotherGrill.xlsx,
// the palonniers in Palonnier.xlsx. With Charges.xlsx these are the only input files the application
// names. The scenario's own copy is read first, then the shared one in _common/data.
//
// First sheet. A header row is expected and its columns are found by their heading, so they may be moved
// or added to; a file whose first row says none of the headings below is read without one, in this order,
// from its very first row. The headings:
//   Modèle                        the reference's name, which becomes its element type
//   Longueur                      the bar's length, metres
//   Nombre                        how many pieces are in stock; 0 means the row gives a value only
//                                 (a span the load table needs) and is never a piece
//   Poids propre                  the mass of ONE bar, kg; left blank when unknown
//   Charge ponctuelle au milieu   the most the loads on the bar may add up to, in any position, kg
//   Matériau, Marque, Usage       optional. Marque names the reference in the study; Usage is checked
//                                 against the file the row is in.
// =============================================================

using System.Globalization;
using ClosedXML.Excel;
using RiggingOptimizer.Models;

namespace RiggingOptimizer.IO;

/// <summary>
/// An inventory row that could not be read and was left out. Its model loses that length: the load table
/// reads the next longer row instead, or computes past a shorter one, and the catalogue has no mass or stock
/// for it. So a study that uses the model cannot rely on what the other rows say.
/// </summary>
/// <param name="File">The workbook, as named: MotherGrill.xlsx or Palonnier.xlsx.</param>
/// <param name="Mothers">True for a row of the mothers' workbook, false for the palonniers'.</param>
/// <param name="Model">The Modèle of the row, trimmed.</param>
/// <param name="LengthM">Its length, when that at least could be read.</param>
/// <param name="Line">The spreadsheet row number, as Excel shows it.</param>
/// <param name="Reason">What could not be read, in French: "longueur illisible".</param>
public sealed record SkippedRow(string File, bool Mothers, string Model, double? LengthM, int Line, string Reason);

/// <summary>What the two inventory workbooks hold, and what could not be read from them.</summary>
public sealed class Inventory
{
    public List<BarSpec> Mothers { get; init; } = new();
    public List<BarSpec> Palonniers { get; init; } = new();

    /// <summary>Rows skipped or doubtful, in words a person can act on.</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    /// The rows left out because their length or their charge ponctuelle au milieu could not be read. Named
    /// in <see cref="Warnings"/> too; here as data, so that the study can tell whether a model it checks lost a
    /// row, which a warning alone cannot make it do.
    /// </summary>
    public List<SkippedRow> Skipped { get; init; } = new();

    /// <summary>Inventory files found neither in the scenario's folder nor in the shared one.</summary>
    public List<string> MissingFiles { get; init; } = new();

    /// <summary>The mass and printed name of each reference, from the same rows.</summary>
    public AliantWebS.Reporting.TrussCatalog Catalog { get; init; } = AliantWebS.Reporting.TrussCatalog.Empty;

    /// <summary>The abaque of each palonnier model: its rows by length.</summary>
    public Dictionary<string, List<BarSpec>> PalonnierAbaques => ByType(Palonniers);

    /// <summary>The abaque of each mother model: its rows by length.</summary>
    public Dictionary<string, List<BarSpec>> MotherAbaques => ByType(Mothers);

    /// <summary>What a palonnier of each model may carry over a span: LoadTable over every palonnier row.</summary>
    public LoadTable PalonnierTable => _palonnierTable ??= LoadTable.From(Palonniers);

    /// <summary>What a mother of each model may carry over a span: LoadTable over every mother row.</summary>
    public LoadTable MotherTable => _motherTable ??= LoadTable.From(Mothers);

    private LoadTable? _palonnierTable, _motherTable;

    private static Dictionary<string, List<BarSpec>> ByType(IEnumerable<BarSpec> bars) =>
        bars.GroupBy(b => b.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Length).ToList(), StringComparer.OrdinalIgnoreCase);
}

public static class InventoryFiles
{
    public const string MothersFileName = "MotherGrill.xlsx";
    public const string PalonniersFileName = "Palonnier.xlsx";

    /// <summary>
    /// Reads both workbooks, each from the scenario's folder or else the shared one. A missing workbook is
    /// reported in <see cref="Inventory.MissingFiles"/>, not thrown: the report can still say what it
    /// could not check. The optimiser, which cannot place anything without stock, refuses instead.
    /// </summary>
    public static Inventory Load(string? dataDir, string? fallbackDir)
    {
        var warnings = new List<string>();
        var missing = new List<string>();
        var skipped = new List<SkippedRow>();

        List<BarSpec> Read(string fileName, bool mothers)
        {
            var path = InputFiles.Find(dataDir, fileName) ?? InputFiles.Find(fallbackDir, fileName);
            if (path is null)
            {
                missing.Add(fileName);
                return new List<BarSpec>();
            }
            return ReadFile(path, fileName, mothers, warnings, skipped);
        }

        // Mothers by length then capacity, palonniers by length: the order the optimiser takes stock in.
        var mothers = Read(MothersFileName, mothers: true)
            .OrderBy(b => b.Length).ThenByDescending(b => b.Capacity).ToList();
        var palonniers = Read(PalonniersFileName, mothers: false)
            .OrderBy(b => b.Length).ToList();

        // One piece, one weight: before anything reads these rows, so that the optimiser, the load table and
        // the study all weigh a piece the same way.
        ReconcileWeights(mothers, palonniers, warnings);

        return new Inventory
        {
            Mothers = mothers,
            Palonniers = palonniers,
            Warnings = warnings,
            Skipped = skipped,
            MissingFiles = missing,
            Catalog = AliantWebS.Reporting.TrussCatalog.FromInventory(mothers.Concat(palonniers), warnings),
        };
    }

    /// <summary>
    /// Rows of one model at one length - in one workbook, or one in each when a truss serves as mother and
    /// as palonnier - that give it different Poids propre. It is one piece, so it has one weight, and the
    /// heavier is the safe one: it is what the attachment points carry, and a heavier truss is allowed less
    /// when a value is computed past its longest row. Each lighter row is replaced by a copy carrying the
    /// heavier weight, so that the optimiser, which used each row's own, and the catalogue, which kept the
    /// first one read, now agree. A blank weight is unknown, not a lighter one, and is left as it is.
    /// </summary>
    private static void ReconcileWeights(List<BarSpec> mothers, List<BarSpec> palonniers, List<string> warnings)
    {
        var rows = mothers.Select((bar, i) => (List: mothers, Index: i, Bar: bar, File: MothersFileName))
            .Concat(palonniers.Select((bar, i) => (List: palonniers, Index: i, Bar: bar, File: PalonniersFileName)))
            .Where(r => r.Bar.WeightKg is > 0)
            .ToList();

        // Keyed as the catalogue keys a piece: the model without case, the length in whole centimetres.
        foreach (var piece in rows.GroupBy(r => (Type: r.Bar.Type.Trim().ToLowerInvariant(),
                                                 Cm: (long)Math.Round(r.Bar.Length * 100))))
        {
            var weights = piece.Select(r => r.Bar.WeightKg!.Value).Distinct().OrderBy(w => w).ToList();
            if (weights.Count < 2 || weights[^1] - weights[0] < 1e-9) continue;

            double heaviest = weights[^1];
            foreach (var row in piece.Where(r => r.Bar.WeightKg!.Value < heaviest))
                row.List[row.Index] = WithWeight(row.Bar, heaviest);

            var first = piece.First().Bar;
            var files = piece.Select(r => r.File).Distinct().ToList();
            string kg(double w) => w.ToString("0.##", Fr);
            warnings.Add($"« {first.Type.Trim()} » de {first.Length.ToString("0.##", Fr)} m a "
                         + (weights.Count == 2 ? "deux" : weights.Count.ToString(Fr)) + " poids propres dans "
                         + string.Join(" et ", files) + " ("
                         + string.Join(", ", weights.Take(weights.Count - 1).Select(kg)) + $" et {kg(heaviest)} kg) : "
                         + $"le plus lourd, {kg(heaviest)} kg, est retenu.");
        }
    }

    /// <summary>
    /// A copy of a row with another weight. BarSpec's properties are init-only, so every field is copied by
    /// hand, as BarSpec.Clone does; a field added there must be added here.
    /// </summary>
    private static BarSpec WithWeight(BarSpec bar, double weightKg) => new()
    {
        Type = bar.Type,
        Length = bar.Length,
        StockInit = bar.StockInit,
        Stock = bar.Stock,
        Capacity = bar.Capacity,
        WeightKg = weightKg,
        Material = bar.Material,
        Brand = bar.Brand,
        Usage = bar.Usage,
    };

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// The eight columns, in the order a file without a header row must put them in: that order is read
    /// straight off this array, so moving a line here moves a column in every headerless inventory file.
    /// </summary>
    private static readonly (string Key, string Heading, bool Required)[] Columns =
    {
        ("modele", "Modèle", true),
        ("longueur", "Longueur", true),
        ("nombre", "Nombre", true),
        ("poids propre", "Poids propre", false),
        ("charge ponctuelle au milieu", "Charge ponctuelle au milieu", true),
        ("materiau", "Matériau", false),
        ("marque", "Marque", false),
        ("usage", "Usage", false),
    };

    /// <summary>
    /// The references in one inventory workbook. A row whose length or allowed load cannot be read is
    /// skipped, named in <paramref name="warnings"/> and recorded in <paramref name="skipped"/>; a missing
    /// required column is an error, since then no row can be read at all - unless the file has no header
    /// row at all, which is read in the fixed order of <see cref="Columns"/> from its first row.
    /// </summary>
    public static List<BarSpec> ReadFile(string path, string fileName, bool mothers, List<string> warnings,
        List<SkippedRow>? skipped = null)
    {
        using var wb = ExcelLoader.OpenShared(path);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count == 0)
        {
            warnings.Add($"{fileName} est vide.");
            return new List<BarSpec>();
        }

        // Whether the first row names the columns, or is already the first reference: a file a person made
        // by copying a few lines out of another has no header. One known heading is enough to call it a
        // header - and then a required heading that is missing stays an error rather than being read as a
        // row, because a misspelt header read as data would enter the word « Modèle » as a truss model.
        var found = ExcelCells.Headings(rows[0]);
        bool hasHeader = Columns.Any(c => found.ContainsKey(c.Key));

        if (!hasHeader)
        {
            warnings.Add($"{fileName} : aucun en-tête reconnu ; les colonnes ont été lues dans l'ordre "
                         + "A=Modèle, B=Longueur, C=Nombre, D=Poids propre, E=Charge ponctuelle au milieu, "
                         + "F=Matériau, G=Marque, H=Usage, à partir de la première ligne.");
        }

        int Col(string key)
        {
            // No header: the file has to follow the fixed order, which is the order of Columns.
            if (!hasHeader) return Array.FindIndex(Columns, c => c.Key == key) + 1;
            if (found.TryGetValue(key, out var column)) return column;
            var (_, heading, required) = Columns.First(c => c.Key == key);
            if (required)
                throw new InvalidDataException(
                    $"{fileName} : la colonne « {heading} » est introuvable dans la ligne d'en-tête.");
            return 0;
        }

        int typeCol = Col("modele"), lengthCol = Col("longueur"), stockCol = Col("nombre"),
            weightCol = Col("poids propre"), capacityCol = Col("charge ponctuelle au milieu"),
            materialCol = Col("materiau"), brandCol = Col("marque"), usageCol = Col("usage");

        string role = mothers ? "mothers" : "palonniers";
        var seen = new HashSet<(string, long)>();
        var bars = new List<BarSpec>();

        foreach (var row in rows.Skip(hasHeader ? 1 : 0))
        {
            int line = row.RowNumber();
            var type = Text(row, typeCol);
            if (type.Length == 0) continue;

            var length = Number(row, lengthCol);
            if (length is not > 0)
            {
                warnings.Add($"{fileName}, ligne {line} (« {type} ») : longueur illisible, ligne ignorée.");
                skipped?.Add(new SkippedRow(fileName, mothers, type, null, line, "longueur illisible"));
                continue;
            }

            string metres = length.Value.ToString("0.##", Fr);
            var capacity = Number(row, capacityCol);
            if (capacity is not > 0)
            {
                warnings.Add($"{fileName}, ligne {line} (« {type} » de {metres} m) : charge ponctuelle au milieu "
                             + "illisible, ligne ignorée.");
                skipped?.Add(new SkippedRow(fileName, mothers, type, length.Value, line,
                    "charge ponctuelle au milieu illisible"));
                continue;
            }

            var usage = Text(row, usageCol);
            var usageKey = ExcelCells.Normalize(usage).Replace(" ", "");
            if (usageKey.Length > 0 && !usageKey.StartsWith(mothers ? "mother" : "palonnier"))
            {
                warnings.Add($"{fileName}, ligne {line} (« {type} ») : usage « {usage} » dans le fichier des "
                             + $"{role} ; la ligne est lue comme un de ces {role}.");
            }

            if (!seen.Add((type.ToLowerInvariant(), (long)Math.Round(length.Value * 100))))
                warnings.Add($"{fileName}, ligne {line} : « {type} » de {metres} m figure plusieurs fois. La plus faible "
                             + "charge ponctuelle au milieu est retenue ; les pièces de chaque ligne restent en stock.");

            var stock = Number(row, stockCol);
            if (stock is null)
                warnings.Add($"{fileName}, ligne {line} (« {type} » de {metres} m) : Nombre vide ; la ligne ne donne "
                             + "qu'une charge ponctuelle au milieu, aucune pièce n'est en stock.");
            else if (stock < 0)
                warnings.Add($"{fileName}, ligne {line} (« {type} » de {metres} m) : Nombre négatif, lu comme 0.");
            int count = stock is > 0 ? (int)Math.Round(stock.Value) : 0;
            var weight = Number(row, weightCol);

            bars.Add(new BarSpec
            {
                Type = type,
                Length = length.Value,
                StockInit = count,
                Stock = count,
                Capacity = capacity.Value,
                WeightKg = weight is > 0 ? weight : null,
                Material = NullIfEmpty(Text(row, materialCol)),
                Brand = NullIfEmpty(Text(row, brandCol)),
                Usage = NullIfEmpty(usage),
            });
        }

        return bars;
    }

    // Reading a cell is shared with ExcelLoader, in ExcelCells, so that the two readers accept the same
    // headings and the same way of writing a number. These two only turn a column number - 0 when an
    // optional column is absent from the file - into the cell that holds it.
    private static string Text(IXLRow row, int column) =>
        column <= 0 ? "" : ExcelCells.Text(row.Cell(column));

    private static double? Number(IXLRow row, int column) =>
        column <= 0 ? null : ExcelCells.Number(row.Cell(column));

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}
