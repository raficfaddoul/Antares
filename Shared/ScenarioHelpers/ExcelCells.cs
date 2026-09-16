// =============================================================
// ExcelCells.cs — Reading one cell, and telling a header row from a data row.
//
// The input workbooks are written by hand, in Excel, by different people: the same column is a real
// number in one file and text with a comma in another, and the first row names the columns in one file
// and is already the first load in the next. Both readers - ExcelLoader, which reads the charges sheet
// and the element sheet, and InventoryFiles, which reads MotherGrill.xlsx and Palonnier.xlsx - have to
// answer the same two questions, and they must answer them the same way: a heading recognised in one
// file has to be recognised in the other, or a person who fixes one file breaks the other.
// =============================================================

using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace RiggingOptimizer.IO;

public static class ExcelCells
{
    /// <summary>
    /// A heading as it is matched: lower case, without accents, single spaces, and without the unit that a
    /// heading often carries in brackets, so that "Longueur (m)" and "Charge (daN)" name the same columns
    /// as "Longueur" and "Charge". Here rather than in one reader because both match headings, and a
    /// heading either reader accepts must be accepted by the other.
    /// </summary>
    public static string Normalize(string? text)
    {
        var decomposed = (text ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) kept.Append(ch);
        var heading = string.Join(' ', kept.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // The unit is written for the reader, not for us. Dropped only when a heading is left in front of
        // it, so that a column headed just "(m)" keeps the only text it has and is not read as unnamed.
        if (heading.EndsWith(')'))
        {
            int open = heading.LastIndexOf('(');
            if (open > 0)
            {
                var withoutUnit = heading[..open].TrimEnd();
                if (withoutUnit.Length > 0) heading = withoutUnit;
            }
        }

        return heading;
    }

    /// <summary>
    /// A number, whether typed as one, typed as text with either decimal separator, or the result of a
    /// formula such as =2*B3. Null when there is none - which is also how a cell reads that ClosedXML
    /// refuses, so a header cell asked for a number answers "not a number" instead of throwing.
    /// </summary>
    public static double? Number(IXLCell? cell)
    {
        if (cell is null) return null;
        try
        {
            var value = cell.Value;
            if (value.IsNumber) return value.GetNumber();
            if (value.IsText)
            {
                // The thousands separator a person types: a plain space, a no-break space, or the narrow
                // one Excel itself inserts in fr-FR.
                var text = value.GetText().Trim()
                    .Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }
            return null;
        }
        catch (Exception)
        {
            return null;   // a formula the spreadsheet library cannot evaluate
        }
    }

    /// <summary>
    /// The text of a cell, trimmed. A cell nobody filled reads as an empty string, never as null, so that
    /// every caller may test its length.
    /// </summary>
    public static string Text(IXLCell? cell) => cell is null ? "" : cell.GetString().Trim();

    /// <summary>
    /// Where each heading of a row is, by its normalized text: the map a reader needs to accept the columns
    /// in another order. The first column of a repeated heading wins, so a duplicated heading cannot make
    /// the reader jump to a later column the person did not mean.
    /// </summary>
    public static Dictionary<string, int> Headings(IXLRow row)
    {
        var found = new Dictionary<string, int>();
        foreach (var cell in row.CellsUsed())
        {
            var key = Normalize(cell.GetString());
            if (key.Length > 0 && !found.ContainsKey(key)) found[key] = cell.Address.ColumnNumber;
        }
        return found;
    }

    /// <summary>
    /// Whether every one of these columns of a row reads as a number. This is how a positional reader tells
    /// its first row apart: the cells a data row must have as numbers - a coordinate, a weight - are text
    /// in a header row, whatever the headings say. Asked of the cells that MUST be numeric, never of an
    /// optional one, since a blank cell would then make a data row look like a header.
    /// </summary>
    public static bool AllNumbers(IXLRow row, params int[] columns)
    {
        foreach (var column in columns)
            if (column <= 0 || Number(row.Cell(column)) is null) return false;
        return columns.Length > 0;
    }
}
