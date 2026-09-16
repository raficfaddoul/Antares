// =============================================================
// ExcelLoader.cs  — Lecture des fichiers Excel d'entrée
//
// Two sheets a person fills in by hand: the charges sheet (the « Import Excel » of the Charges page, and
// of a palonnier's loads) and the element sheet (the MotherGrid page and the Palonnier component). Each
// may carry a header row naming its columns, or none at all, and the file itself decides which. The
// inventory workbooks are read by InventoryFiles; reading a cell and matching a heading are shared with
// it, in ExcelCells.
// Dépendance NuGet: ClosedXML
// =============================================================

using ClosedXML.Excel;
using Helpers;
using RiggingOptimizer.Models;

namespace RiggingOptimizer.IO;

public static class ExcelLoader
{
    /// <summary>
    /// Opens a workbook for reading while letting others keep it open. These input files are edited in
    /// Excel, which holds them open for writing; opening them the default way then fails with "being used
    /// by another process" for as long as the file stays open there - which stopped Calcul Charpente and
    /// the report whenever someone had an input workbook open. Reads a copy of what is saved on disk.
    /// </summary>
    public static XLWorkbook OpenShared(string path)
    {
        var copy = new MemoryStream();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            file.CopyTo(copy);
        copy.Position = 0;
        return new XLWorkbook(copy);
    }

    // ── Le plan de charges ────────────────────────────────────────
    // Format: col A=Nom, B=X, C=Y, D=Charge. X and Y in the studies' axes - X along the beams towards
    // point 9, Y across them towards J, from E5 - and returned in the grid's (StudyAxes). A header row
    // naming the columns is optional: ReadCharges decides per file.
    private const int ChargeNameColumn = 1, ChargeXColumn = 2, ChargeYColumn = 3, ChargeWeightColumn = 4;

    // The headings each column answers to, written as ExcelCells.Normalize leaves them: lower case,
    // without accents, without the unit in brackets. Several each, because these files are written by
    // different people and by other applications.
    private static readonly string[] ChargeNameHeadings = { "nom", "name", "moteur", "libelle" };
    private static readonly string[] ChargeXHeadings = { "x" };
    private static readonly string[] ChargeYHeadings = { "y" };
    private static readonly string[] ChargeWeightHeadings = { "charge", "poids", "weight" };

    /// <param name="warnings">
    /// What the reader noticed and read anyway, in French: a first row taken for a header and skipped,
    /// columns read in the fixed order. Optional, so that a caller with nowhere to show them keeps
    /// compiling; the pages pass a list. A dropped row that nobody is told about is the failure to avoid:
    /// a charge lost here lightens every attachment point downstream without a word.
    /// </param>
    public static List<Charge> LoadCharges(string path, List<string>? warnings = null)
    {
        using var wb = OpenShared(path);
        return ReadCharges(wb, warnings);
    }

    // Surcharge Stream — used by Pipeline.ImportChargesSceniquesFromStream
    //                            & Pipeline.ImportChargeMpsFromStream
    public static List<Charge> LoadCharges(Stream stream, List<string>? warnings = null)
    {
        using var wb = new XLWorkbook(stream);
        return ReadCharges(wb, warnings);
    }

    private static List<Charge> ReadCharges(XLWorkbook wb, List<string>? warnings)
    {
        var charges = new List<Charge>();
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count == 0) return charges;

        int nameCol = ChargeNameColumn, xCol = ChargeXColumn, yCol = ChargeYColumn,
            weightCol = ChargeWeightColumn;
        int firstDataRow = 0;

        // Header row or first load? The files this application is given have none - row 1 is already
        // "L1 | -7.45 | -2.99 | 154" - and a file exported from elsewhere has one, so only the file can
        // say. It is asked of the three cells a load must fill with numbers: a header writes words in
        // them. Asked of the headings instead, a headerless file would have to be wrong about them.
        if (!ExcelCells.AllNumbers(rows[0], ChargeXColumn, ChargeYColumn, ChargeWeightColumn))
        {
            firstDataRow = 1;
            warnings?.Add("La première ligne a été lue comme un en-tête et n'a pas été importée.");

            var headings = ExcelCells.Headings(rows[0]);
            int Column(string[] keys) => keys
                .Select(key => headings.TryGetValue(key, out var column) ? column : 0)
                .FirstOrDefault(column => column > 0);

            int named = Column(ChargeNameHeadings), abscissa = Column(ChargeXHeadings),
                ordinate = Column(ChargeYHeadings), load = Column(ChargeWeightHeadings);

            // All four or none. Half a mapping would read one column by its heading and the next by its
            // position, which is how a Y ends up read as a weight - and a wrong number is worse than an
            // unread file, because nothing downstream can notice it.
            if (named > 0 && abscissa > 0 && ordinate > 0 && load > 0)
                (nameCol, xCol, yCol, weightCol) = (named, abscissa, ordinate, load);
            else
                warnings?.Add("Aucun en-tête reconnu : les colonnes ont été lues dans l'ordre "
                              + "A=Nom, B=X, C=Y, D=Charge.");
        }

        // A charge is known by its name everywhere after this - the optimiser, the stored ChargeMp and the
        // study - so two under one name would make one of them vanish. Refused here, naming the rows, and
        // with every problem of the file in one message, because the page shows it as a single alert.
        var problems = new List<string>();
        var firstRow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Skip(firstDataRow))
        {
            int line = row.RowNumber();
            var name = ExcelCells.Text(row.Cell(nameCol));
            string rowLabel = name.Length > 0 ? $"ligne {line} (« {name} »)" : $"ligne {line}";

            if (name.Length == 0)
                problems.Add($"ligne {line} : charge sans nom");
            else if (firstRow.TryGetValue(name, out var first))
                problems.Add($"« {name} » : deux charges portent ce nom, aux lignes {first} et {line}");
            else
                firstRow[name] = line;

            // Read tolerantly and say which cell cannot be read, rather than letting the spreadsheet
            // library throw its own English InvalidCastException on the first word it meets.
            var studyX = ExcelCells.Number(row.Cell(xCol));
            var studyY = ExcelCells.Number(row.Cell(yCol));
            var weight = ExcelCells.Number(row.Cell(weightCol));

            var unreadable = new List<string>();
            if (studyX is null) unreadable.Add("X");
            if (studyY is null) unreadable.Add("Y");
            if (weight is null) unreadable.Add("charge");
            if (unreadable.Count > 0)
            {
                problems.Add($"{rowLabel} : {string.Join(", ", unreadable)} illisible"
                             + (unreadable.Count > 1 ? "s" : ""));
                continue;
            }

            var (x, y) = StudyAxes.ToGrid(studyX.Value, studyY.Value);
            charges.Add(new Charge
            {
                Name = name,
                X = x,
                Y = y,
                Weight = weight.Value,
            });
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException(
                "Le fichier des charges n'a pas pu être lu : "
                + string.Join(" ; ", problems.Take(10))
                + (problems.Count > 10 ? $" (+{problems.Count - 10})" : "") + ".");
        }

        return charges;
    }

    // Used by Pipeline.GenerateLayout. The stock only: the plan of charges is what the Charges page holds
    // for the scenario (the ChargesScenique rows), never a workbook, so that an edit made on that page
    // reaches the optimiser. Charges.xlsx is only one way of filling that page.
    /// <param name="dataDir">The scenario's own data folder; always searched first.</param>
    /// <param name="fallbackDir">
    /// Shared folder searched when the scenario has no copy of a file. Lets the inventory live in one
    /// place instead of being duplicated into every scenario folder.
    /// </param>
    public static (List<BarSpec> Pals, List<BarSpec> Mothers)
        LoadInventory(string dataDir, string? fallbackDir = null)
    {
        string P(string f)
        {
            // The scenario's copy first, then the shared one, whatever the case of the name: see
            // InputFiles.Find.
            var found = InputFiles.Find(dataDir, f) ?? InputFiles.Find(fallbackDir, f);
            if (found is not null) return found;

            var own = Path.Combine(dataDir, f);

            // Fail here rather than letting the spreadsheet library decide how a missing file surfaces,
            // and name the scenario folder, which is the one the user is actually looking at.
            throw new FileNotFoundException($"Fichier d'entrée introuvable : {f}", own);
        }

        // The stock, one row per truss reference: the palonniers in Palonnier.xlsx, the mothers in
        // MotherGrill.xlsx (InventoryFiles). The optimiser cannot place anything without them.
        P(InventoryFiles.PalonniersFileName);
        P(InventoryFiles.MothersFileName);
        var inventory = InventoryFiles.Load(dataDir, fallbackDir);
        foreach (var warning in inventory.Warnings) Console.Error.WriteLine("⚠️ " + warning);

        if (inventory.Palonniers.Count == 0)
            throw new InvalidDataException($"{InventoryFiles.PalonniersFileName} ne contient aucune ligne lisible.");
        if (inventory.Mothers.Count == 0)
            throw new InvalidDataException($"{InventoryFiles.MothersFileName} ne contient aucune ligne lisible.");

        return (inventory.Palonniers, inventory.Mothers);
    }

    // ── Import MotherGridPalonnier depuis un Stream ───────────────
    // Format: col A=Nom, B=ElementTypeName, C=X1, D=Y1, E=X2, F=Y2 in the studies' axes (StudyAxes),
    // optional G=the pieces, e.g. "5,6+2,8" (Helpers.TrussComposition.ParseText). A header row naming the
    // columns is optional, decided per file as for the charges sheet.
    // The ends are returned in the grid's axes.
    private const int ElementNameColumn = 1, ElementTypeColumn = 2, ElementX1Column = 3,
        ElementY1Column = 4, ElementX2Column = 5, ElementY2Column = 6, ElementPiecesColumn = 7;

    private static readonly string[] ElementNameHeadings = { "nom", "name", "libelle" };
    private static readonly string[] ElementTypeHeadings = { "type", "modele", "type d'element", "element type" };
    private static readonly string[] ElementX1Headings = { "x1", "x 1" };
    private static readonly string[] ElementY1Headings = { "y1", "y 1" };
    private static readonly string[] ElementX2Headings = { "x2", "x 2" };
    private static readonly string[] ElementY2Headings = { "y2", "y 2" };
    private static readonly string[] ElementPiecesHeadings = { "composition", "pieces", "elements" };

    /// <param name="warnings">
    /// What the reader noticed, in French: a first row taken for a header and skipped, columns read in the
    /// fixed order, a row whose coordinates could not be read and was therefore left out. Optional;
    /// Pipeline passes the list the page shows, so that nothing is dropped in silence.
    /// </param>
    public static List<(string Name, string ElementTypeName, double X1, double Y1, double X2, double Y2, string? Composition)>
        LoadMotherGridPalonniers(Stream stream, List<string>? warnings = null)
    {
        var result = new List<(string, string, double, double, double, double, string?)>();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count == 0) return result;

        int nameCol = ElementNameColumn, typeCol = ElementTypeColumn,
            x1Col = ElementX1Column, y1Col = ElementY1Column,
            x2Col = ElementX2Column, y2Col = ElementY2Column, piecesCol = ElementPiecesColumn;
        int firstDataRow = 0;

        // The same rule as the charges sheet, on the four coordinates an element must give as numbers.
        // Composition is left out of the test: it is optional, so a file without it would look as though
        // every one of its rows were a header.
        if (!ExcelCells.AllNumbers(rows[0], ElementX1Column, ElementY1Column, ElementX2Column, ElementY2Column))
        {
            firstDataRow = 1;
            warnings?.Add("La première ligne a été lue comme un en-tête et n'a pas été importée.");

            var headings = ExcelCells.Headings(rows[0]);
            int Column(string[] keys) => keys
                .Select(key => headings.TryGetValue(key, out var column) ? column : 0)
                .FirstOrDefault(column => column > 0);

            int named = Column(ElementNameHeadings), kind = Column(ElementTypeHeadings),
                a = Column(ElementX1Headings), b = Column(ElementY1Headings),
                c = Column(ElementX2Headings), d = Column(ElementY2Headings);

            // All six or none, for the same reason as the charges sheet: a half-read header would put one
            // end of a truss where the file never said it was.
            if (named > 0 && kind > 0 && a > 0 && b > 0 && c > 0 && d > 0)
            {
                (nameCol, typeCol, x1Col, y1Col, x2Col, y2Col) = (named, kind, a, b, c, d);
                piecesCol = Column(ElementPiecesHeadings);   // optional: 0 when the file has no such column
            }
            else
            {
                warnings?.Add("Aucun en-tête reconnu : les colonnes ont été lues dans l'ordre "
                              + "A=Nom, B=Type, C=X1, D=Y1, E=X2, F=Y2, G=Composition.");
            }
        }

        foreach (var row in rows.Skip(firstDataRow))
        {
            int line = row.RowNumber();
            var name = ExcelCells.Text(row.Cell(nameCol));
            string rowLabel = name.Length > 0 ? $"ligne {line} (« {name} »)" : $"ligne {line}";

            // A row whose ends cannot be read is left out and said in words, rather than throwing the
            // spreadsheet library's English InvalidCastException over the whole import.
            var studyX1 = ExcelCells.Number(row.Cell(x1Col));
            var studyY1 = ExcelCells.Number(row.Cell(y1Col));
            var studyX2 = ExcelCells.Number(row.Cell(x2Col));
            var studyY2 = ExcelCells.Number(row.Cell(y2Col));
            if (studyX1 is null || studyY1 is null || studyX2 is null || studyY2 is null)
            {
                warnings?.Add($"{rowLabel} : coordonnées illisibles, ligne ignorée.");
                continue;
            }

            var (x1, y1) = StudyAxes.ToGrid(studyX1.Value, studyY1.Value);
            var (x2, y2) = StudyAxes.ToGrid(studyX2.Value, studyY2.Value);
            var pieces = piecesCol > 0 ? ExcelCells.Text(row.Cell(piecesCol)) : "";
            result.Add((
                name,
                ExcelCells.Text(row.Cell(typeCol)),
                x1, y1, x2, y2,
                pieces.Length == 0 ? null : pieces
            ));
        }

        return result;
    }
}