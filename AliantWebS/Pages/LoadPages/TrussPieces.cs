// =============================================================
// TrussPieces.cs — The "Pièces" column of the MotherGrid page: the pieces a mother or a palonnier is
// assembled from, what the stock can still make, and what does not add up.
//
// The pieces are stored on the element (MotherGridPalonnier.Composition) as Helpers.TrussComposition
// reads and writes them. Every piece is of the element's own model - its element type - and comes from
// the inventory of its role: MotherGrill.xlsx for mothers, Palonnier.xlsx for palonniers. A row whose
// Nombre is 0 gives a load value only, never a piece. The optimiser never uses more pieces than Nombre;
// neither does a proposal made here, which counts what the scenario's other elements of the same model
// already use.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AliantWebS.Models;
using Helpers;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;

namespace AliantWebS.Pages.LoadPages;

public static class TrussPieces
{
    /// <summary>
    /// How much shorter than its drawn segment an element's pieces may add up to: the 8 cm the optimiser
    /// lets a truss end fall off its support (Geometry.TOL).
    /// </summary>
    public const double ToleranceM = Geometry.TOL;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>A length in metres as the page prints it: "5,6", "9,12".</summary>
    public static string Metres(double m) => m.ToString("0.##", Fr);

    private static long Cm(double m) => (long)Math.Round(m * 100);

    /// <summary>The inventory workbook the pieces of a role come from.</summary>
    public static string FileOf(bool mothers) =>
        mothers ? InventoryFiles.MothersFileName : InventoryFiles.PalonniersFileName;

    /// <summary>The length of the segment drawn for the element, metres; null while an end is missing.</summary>
    public static double? DrawnLengthM(MotherGridPalonnier e) =>
        e.X1 is { } x1 && e.Y1 is { } y1 && e.X2 is { } x2 && e.Y2 is { } y2
            ? Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1))
            : null;

    /// <summary>"5,6 + 2,8 m"; "—" when none is recorded, or what is recorded cannot be read.</summary>
    public static string Display(MotherGridPalonnier e)
    {
        var pieces = TrussComposition.Parse(e.Composition, out _);
        return pieces is null ? "—" : TrussComposition.Describe(pieces);
    }

    /// <summary>
    /// The pieces as a person types them, "5,6+2,8"; empty when none is recorded. What is stored but cannot be
    /// read is shown as it is, to be corrected.
    /// </summary>
    public static string EditText(MotherGridPalonnier e)
    {
        var pieces = TrussComposition.Parse(e.Composition, out var readable);
        if (pieces is null) return readable ? "" : e.Composition ?? "";
        return string.Join("+", pieces.OrderByDescending(p => p).Select(Metres));
    }

    /// <summary>
    /// What a person typed, as it is stored: (the composition, null) - a null composition when the text is
    /// empty, which records no pieces - or (null, why it is refused).
    /// </summary>
    public static (string? Composition, string? Problem) FromText(string? text)
    {
        var typed = text?.Trim() ?? "";
        if (typed.Length == 0) return (null, null);

        var pieces = TrussComposition.ParseText(typed);
        if (pieces is null)
            return (null, $"« {typed} » n’est pas une liste de pièces lisible. Écrivez leurs longueurs en mètres, "
                          + "séparées par « + » : 5,6+2,8 ou 2x5,6+2,8.");
        if (pieces.Any(p => Cm(p) < 1))
            return (null, $"« {typed} » : une pièce fait moins d’un centimètre.");
        if (pieces.Count > Assembler.MaxPieces)
            return (null, $"« {typed} » : {pieces.Count} pièces, quand un élément en compte au plus {Assembler.MaxPieces}.");

        return (TrussComposition.Serialize(pieces), null);
    }

    // "S500 Carrée Acier L=8,4m #3": the one bar an element placed before pieces were recorded is made of.
    // The same reading as ReportDataBuilder.ReferenceLengthOf, without its fall-back to the drawn span,
    // which is no piece.
    private static readonly Regex LengthInName =
        new(@"L\s*=\s*(\d+(?:[.,]\d+)?)\s*m", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The pieces an element takes from the stock: those recorded; for an element placed before pieces were
    /// recorded, the one bar whose length its name gives; null when neither is known.
    /// </summary>
    public static List<double>? PiecesOf(MotherGridPalonnier e)
    {
        var recorded = TrussComposition.Parse(e.Composition, out var readable);
        if (recorded is not null) return recorded;
        if (!readable) return null;

        var match = LengthInName.Match(e.Name ?? "");
        if (match.Success
            && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out var length)
            && length > 0)
        {
            return new List<double> { length };
        }
        return null;
    }

    /// <summary>The inventory rows of one model that are pieces: those with a Nombre above zero.</summary>
    public static List<BarSpec> StockRows(IEnumerable<BarSpec> inventoryRows, string model) =>
        inventoryRows
            .Where(r => r.StockInit > 0 && r.Length > 0
                        && string.Equals(r.Type.Trim(), model.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Each stock row with what is left of it once the given elements' pieces are taken out, and how many of
    /// those elements have no known pieces, so could not be counted. Rows of one length share what is used of
    /// that length, in the order of the file.
    /// </summary>
    public static List<(BarSpec Row, int Available)> Remaining(
        IReadOnlyList<BarSpec> stockRows, IEnumerable<MotherGridPalonnier> elements, out int unknown)
    {
        var used = new Dictionary<long, int>();
        unknown = 0;
        foreach (var e in elements)
        {
            var pieces = PiecesOf(e);
            if (pieces is null)
            {
                unknown++;
                continue;
            }
            foreach (var p in pieces)
                used[Cm(p)] = used.GetValueOrDefault(Cm(p)) + 1;
        }

        var remaining = new List<(BarSpec Row, int Available)>();
        foreach (var row in stockRows)
        {
            long cm = Cm(row.Length);
            int taken = Math.Min(row.StockInit, used.GetValueOrDefault(cm));
            if (taken > 0) used[cm] -= taken;
            remaining.Add((row, row.StockInit - taken));
        }
        return remaining;
    }

    /// <summary>A proposal: the composition to store, or null with why there is none; a caveat either way.</summary>
    public sealed record Proposal(string? Composition, string? Message);

    /// <summary>
    /// The pieces the stock can still make for an element: RiggingOptimizer.Core.Assembler.Cheapest over the
    /// stock rows of its model - fewest pieces, then least extra length, then lightest - each with its Nombre
    /// less the pieces of that length the scenario's other elements of the model use, reaching at least the
    /// drawn length less <see cref="ToleranceM"/>.
    /// </summary>
    /// <param name="sameRole">The scenario's elements of the element's role: its mothers, or its palonniers.</param>
    public static Proposal Propose(
        MotherGridPalonnier element,
        Func<MotherGridPalonnier, string?> modelOf,
        IEnumerable<MotherGridPalonnier> sameRole,
        RiggingOptimizer.IO.Inventory? inventory,   // not "Inventory": Pages/Inventory is a namespace here
        string? inventoryError,
        bool mothers)
    {
        string file = FileOf(mothers);
        if (inventory is null)
            return new(null, "L’inventaire n’a pas pu être lu"
                             + (string.IsNullOrWhiteSpace(inventoryError) ? "." : $" : {inventoryError}"));
        if (inventory.MissingFiles.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
            return new(null, $"{file} est introuvable, ni dans les fichiers du scénario ni dans _common/data.");

        var model = modelOf(element)?.Trim();
        if (string.IsNullOrEmpty(model))
            return new(null, "L’élément n’a pas de modèle (type d’élément).");

        if (DrawnLengthM(element) is not { } length || length <= ToleranceM)
            return new(null, "L’élément n’a pas de longueur dessinée : renseignez X1, Y1, X2 et Y2.");

        var rows = StockRows(mothers ? inventory.Mothers : inventory.Palonniers, model);
        if (rows.Count == 0)
            return new(null, $"« {model} » n’a aucune pièce en stock dans {file} (modèle absent, ou Nombre à 0).");

        var others = sameRole.Where(o => !IsSame(o, element) && SameModel(modelOf(o), model)).ToList();
        var remaining = Remaining(rows, others, out int unknown);
        string caveat = unknown == 0
            ? ""
            : $" {unknown} autre(s) élément(s) de ce modèle n’ont pas de pièces connues et ne sont pas décomptés du stock.";

        double need = length - ToleranceM;
        var pieces = Assembler.Cheapest(remaining, need);
        if (pieces is null)
        {
            return new(null, $"Le stock de « {model} » ({file}) ne permet pas d’assembler au moins {Metres(need)} m "
                             + $"(élément dessiné de {Metres(length)} m) : {DescribeLeft(remaining)}.{caveat}");
        }

        var lengths = pieces.Select(p => p.Length).ToList();
        return new(TrussComposition.Serialize(lengths),
                   caveat.Length == 0 ? null : $"« {element.Name ?? model} » : {TrussComposition.Describe(lengths)} proposé.{caveat}");
    }

    /// <summary>
    /// What does not add up in an element's recorded pieces, in words; empty when nothing. The pieces are
    /// compared with the drawn segment and, when the inventory was read, with the stock of the model.
    /// </summary>
    /// <param name="sameRole">The scenario's elements of the element's role, the element among them.</param>
    public static List<string> Warnings(
        MotherGridPalonnier element,
        Func<MotherGridPalonnier, string?> modelOf,
        IEnumerable<MotherGridPalonnier> sameRole,
        RiggingOptimizer.IO.Inventory? inventory,   // not "Inventory": Pages/Inventory is a namespace here
        bool mothers)
    {
        var warnings = new List<string>();
        var pieces = TrussComposition.Parse(element.Composition, out var readable);
        if (!readable)
        {
            warnings.Add($"pièces enregistrées illisibles (« {element.Composition} »)");
            return warnings;
        }
        if (pieces is null) return warnings;

        double total = pieces.Sum();
        if (DrawnLengthM(element) is { } length && total < length - ToleranceM - 0.0005)
            warnings.Add($"les pièces ({Metres(total)} m) sont plus courtes que l’élément dessiné ({Metres(length)} m)");

        string file = FileOf(mothers);
        var model = modelOf(element)?.Trim();
        if (inventory is null || string.IsNullOrEmpty(model)
            || inventory.MissingFiles.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
        {
            return warnings;
        }

        var held = StockRows(mothers ? inventory.Mothers : inventory.Palonniers, model)
            .GroupBy(r => Cm(r.Length))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.StockInit));
        var ofModel = sameRole.Where(o => SameModel(modelOf(o), model)).ToList();

        foreach (var group in pieces.GroupBy(Cm).OrderByDescending(g => g.Key))
        {
            string piece = $"{Metres(group.Key / 100.0)} m";
            if (!held.TryGetValue(group.Key, out int inStock))
            {
                warnings.Add($"aucune pièce de {piece} de « {model} » en stock dans {file}");
                continue;
            }

            int used = ofModel.Sum(o => PiecesOf(o)?.Count(p => Cm(p) == group.Key) ?? 0);
            if (used > inStock)
                warnings.Add($"{used} pièces de {piece} de « {model} » dans le scénario, pour {inStock} en stock");
        }
        return warnings;
    }

    private static bool IsSame(MotherGridPalonnier a, MotherGridPalonnier b) =>
        ReferenceEquals(a, b)
        || (b.MotherGridPalonnierId != Guid.Empty && a.MotherGridPalonnierId == b.MotherGridPalonnierId);

    private static bool SameModel(string? a, string model) =>
        string.Equals(a?.Trim(), model, StringComparison.OrdinalIgnoreCase);

    private static string DescribeLeft(List<(BarSpec Row, int Available)> remaining)
    {
        var parts = remaining
            .Where(t => t.Available > 0)
            .GroupBy(t => Cm(t.Row.Length))
            .OrderByDescending(g => g.Key)
            .Select(g => $"{g.Sum(t => t.Available)} × {Metres(g.Key / 100.0)} m")
            .ToList();
        return parts.Count == 0 ? "il ne reste aucune pièce" : "il reste " + string.Join(", ", parts);
    }
}
