// =============================================================
// TrussComposition.cs — The pieces a truss is assembled from, as the database stores them.
//
// A mother or palonnier may be several pieces of its model joined end to end (5,6 m + 2,8 m). The
// pieces are stored on the element's row (MotherGridPalonnier.Composition) as JSON whole centimetres,
// "[560,280]": culture-free, and free of the floating-point noise spreadsheet lengths carry. Every
// piece is of the element's own model. Null means an element placed before pieces were recorded,
// whose length is then read from its name.
// =============================================================

using System.Globalization;
using System.Text.Json;

namespace Helpers;

public static class TrussComposition
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>The stored form of these piece lengths (metres), longest first; null when there is none.</summary>
    public static string? Serialize(IEnumerable<double> lengthsM)
    {
        var cm = lengthsM
            .Select(l => (int)Math.Round(l * 100))
            .Where(c => c > 0)
            .OrderByDescending(c => c)
            .ToList();
        return cm.Count == 0 ? null : "[" + string.Join(",", cm) + "]";
    }

    /// <summary>
    /// The piece lengths (metres) of a stored composition. Null when nothing is stored; null with
    /// <paramref name="readable"/> false when something is stored that cannot be read.
    /// </summary>
    public static List<double>? Parse(string? stored, out bool readable)
    {
        readable = true;
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            var cm = JsonSerializer.Deserialize<List<int>>(stored);
            if (cm is null || cm.Count == 0 || cm.Any(c => c <= 0))
            {
                readable = false;
                return null;
            }
            return cm.Select(c => c / 100.0).ToList();
        }
        catch (Exception)
        {
            readable = false;
            return null;
        }
    }

    /// <summary>
    /// Piece lengths typed by a person, as in the element import or on the MotherGrid page:
    /// "5,6+2,8", "5.6 + 2.8", "2x5,6+2,8" or "2*5.6 + 2.8". Null when the text is not that.
    /// </summary>
    public static List<double>? ParseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var pieces = new List<double>();
        foreach (var raw in text.Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim().TrimEnd('m', 'M');
            int count = 1;
            var split = part.Split(new[] { 'x', 'X', '*', '×' }, 2);
            if (split.Length == 2)
            {
                if (!int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count <= 0)
                    return null;
                part = split[1];
            }

            if (!double.TryParse(part.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var length)
                || length <= 0)
            {
                return null;
            }

            for (int i = 0; i < count; i++) pieces.Add(length);
        }

        return pieces.Count == 0 ? null : pieces;
    }

    /// <summary>How the pieces are printed: "5,6 + 2,8 m", or "3 × 5,6 + 2,8 m" when pieces repeat.</summary>
    public static string Describe(IReadOnlyList<double> lengthsM)
    {
        if (lengthsM.Count == 0) return "";

        var parts = lengthsM
            .GroupBy(l => Math.Round(l, 2))
            .OrderByDescending(g => g.Key)
            .Select(g => g.Count() == 1
                ? g.Key.ToString("0.##", Fr)
                : $"{g.Count()} × {g.Key.ToString("0.##", Fr)}");
        return string.Join(" + ", parts) + " m";
    }
}
