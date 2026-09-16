// =============================================================
// Pipeline.Messages.cs — What the user reads when the placement refuses.
//
// Kept together, and kept out of the orchestration: these sentences are the whole of what a user has to go
// on when a generation produces nothing, so they are written to be acted on - what was not placed, why the
// stock cannot do it, and what to change.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using Helpers;
using Microsoft.EntityFrameworkCore;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;

namespace RiggingOptimizer;

public static partial class Pipeline
{
    /// <summary>
    /// Why a layout was not saved: the loads nothing carries and, when the stock cannot reach across the
    /// grid at all, which lengths are missing. Public because it is a pure function worth checking.
    /// </summary>
    public static string UncoveredMessage(
        RiggingOptimizer.Models.RiggingResult result,
        IReadOnlyCollection<RiggingOptimizer.Models.BarSpec> pals,
        IReadOnlyCollection<RiggingOptimizer.Models.BarSpec> mothers)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        string M(double metres) => metres.ToString("0.##", fr);

        var names = result.Uncovered.Select(c => $"{c.Name} ({c.Weight.ToString("N0", fr)} kg)").ToList();
        var listed = names.Count <= 8
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(8)) + $" (+{names.Count - 8})";
        var text = new System.Text.StringBuilder(
            $"{result.Uncovered.Count} charge(s) sur {result.Charges.Count} ne sont portées ni par un palonnier "
            + $"ni par un mother : {listed}.");

        // The longest truss each file could assemble from its stock, whatever the load: every piece of one
        // model end to end, up to Assembler.MaxPieces. A palonnier spans at least one gap between point
        // lines, and a mother line at least one bay between beams.
        static double LongestAssembly(IEnumerable<RiggingOptimizer.Models.BarSpec> rows) => rows
            .Where(r => r.StockInit > 0 && r.Length > 0)
            .GroupBy(r => r.Type.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.SelectMany(r => Enumerable.Repeat(r.Length, r.StockInit))
                          .OrderByDescending(l => l)
                          .Take(Assembler.MaxPieces)
                          .Sum())
            .DefaultIfEmpty(0)
            .Max();

        var rows = Geometry.RowYList.Select(r => r.Y).ToList();
        var cols = Geometry.ColXList.Select(c => c.X).ToList();
        var rowGaps = rows.Zip(rows.Skip(1), (a, b) => b - a).ToList();
        var colGaps = cols.Zip(cols.Skip(1), (a, b) => b - a).ToList();
        double longestPal = LongestAssembly(pals);
        double longestMother = LongestAssembly(mothers);

        if (longestPal <= 0)
            text.Append($" {InventoryFiles.PalonniersFileName} n'a aucune pièce en stock.");
        else if (rowGaps.Count > 0 && longestPal < rowGaps.Min() - Geometry.TOL)
            text.Append($" Même assemblé de toutes ses pièces, le plus long palonnier de {InventoryFiles.PalonniersFileName} "
                      + $"mesure {M(longestPal)} m, alors que deux lignes de points sont espacées d'au moins {M(rowGaps.Min())} m.");

        if (longestMother <= 0)
            text.Append($" {InventoryFiles.MothersFileName} n'a aucune pièce en stock.");
        else if (colGaps.Count > 0 && longestMother < colGaps.Min() - Geometry.TOL)
            text.Append($" Même assemblé de toutes ses pièces, le plus long mother de {InventoryFiles.MothersFileName} "
                      + $"mesure {M(longestMother)} m, alors que les poutres sont espacées de {M(colGaps.Min())} à {M(colGaps.Max())} m.");

        // What the optimiser found wrong with the lines it could not build - said once above when the stock is empty.
        if (longestMother > 0)
        {
            foreach (var problem in result.Errors.Where(e => e.StartsWith("Ligne de mother", StringComparison.Ordinal)).Take(3))
                text.Append(' ').Append(problem);
        }

        if (result.Options.OrthogonalOnly)
            text.Append(" Le réglage « orientations orthogonales uniquement » est activé : aucun palonnier incliné n'a été proposé.");

        text.Append(" L'implantation existante n'a pas été modifiée.");
        return text.ToString();
    }

    /// <summary>
    /// The rows named in a warning, quoted, and not endlessly: the pages show these as a single alert.
    /// </summary>
    internal static string NamedRows(IReadOnlyCollection<string> names) =>
        string.Join(", ", names.Take(10).Select(n => $"« {n} »"))
        + (names.Count > 10 ? $" (+{names.Count - 10})" : "");
}
