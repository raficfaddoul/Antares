// =============================================================
// MotherLines.cs — Where the mother lines go, and what each is made of.
//
// As in the hand-written studies: one continuous mother line above a point line, across the beams,
// over the run of bays that holds something to carry - a palonnier's end, or a charge hung straight on
// it - hung at every beam it crosses. A bay with nothing in it ends the line; the next load further along
// the same point line starts another. A load beyond the outermost beam is reached in cantilever.
//
// Each line is assembled from pieces of one model of MotherGrill.xlsx, taken from a PieceStock, and its
// model is the one whose value at each bay's span carries that bay's loads (LoadTable).
// =============================================================

using System.Globalization;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.Core;

public static class MotherLines
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// The lines these demands need, not yet sized: on each point line, one line per run of consecutive
    /// bays holding a demand, from beam to beam. A demand within Geometry.TOL of a beam needs only that
    /// beam: it joins a bay already needed on either side, or else the bay towards the middle of the grid.
    /// </summary>
    /// <param name="demands">Each point line (row and its y) and the x of something to hang there.</param>
    public static List<Mother> Plan(IEnumerable<(int Row, double Y, double X)> demands)
    {
        var cols = ColXList;
        int n = cols.Count;
        var lines = new List<Mother>();
        if (n < 2) return lines;

        foreach (var row in demands.GroupBy(d => d.Row).OrderBy(g => g.Key))
        {
            double y = row.First().Y;
            var needed = new bool[n - 1];
            double overLeft = 0, overRight = 0;
            var onBeams = new List<int>();

            foreach (var (_, _, x) in row)
            {
                if (x < cols[0].X - TOL)
                {
                    needed[0] = true;
                    overLeft = Math.Max(overLeft, cols[0].X - x);
                    continue;
                }
                if (x > cols[n - 1].X + TOL)
                {
                    needed[n - 2] = true;
                    overRight = Math.Max(overRight, x - cols[n - 1].X);
                    continue;
                }

                int beam = -1;
                for (int c = 0; c < n; c++)
                    if (Math.Abs(x - cols[c].X) <= TOL) { beam = c; break; }
                if (beam >= 0)
                {
                    onBeams.Add(beam);
                    continue;
                }

                for (int k = 0; k < n - 1; k++)
                    if (x > cols[k].X && x < cols[k + 1].X) { needed[k] = true; break; }
            }

            foreach (int c in onBeams.Distinct().OrderBy(c => c))
            {
                bool covered = (c > 0 && needed[c - 1]) || (c < n - 1 && needed[c]);
                if (covered) continue;
                int bay = c == 0 ? 0 : c == n - 1 ? n - 2 : cols[c].X > 0 ? c - 1 : c;
                needed[bay] = true;
            }

            for (int k0 = 0; k0 < n - 1;)
            {
                if (!needed[k0]) { k0++; continue; }
                int k1 = k0;
                while (k1 + 1 < n - 1 && needed[k1 + 1]) k1++;

                lines.Add(new Mother
                {
                    Row = row.Key,
                    Y = y,
                    Columns = Enumerable.Range(k0, k1 - k0 + 2).Select(i => (cols[i].Col, cols[i].X)).ToList(),
                    XL = cols[k0].X - (k0 == 0 ? overLeft : 0),
                    XR = cols[k1 + 1].X + (k1 + 1 == n - 1 ? overRight : 0),
                });
                k0 = k1 + 1;
            }
        }

        return lines;
    }

    /// <summary>The line on this point line that reaches <paramref name="x"/>, within <paramref name="tolerance"/>.</summary>
    public static Mother? LineAt(IEnumerable<Mother> lines, int row, double x, double tolerance = CrossingTol) =>
        lines.Where(l => l.Row == row && x >= l.XL - tolerance && x <= l.XR + tolerance)
             .OrderBy(l => Math.Max(0, Math.Max(l.XL - x, x - l.XR)))
             .FirstOrDefault();

    /// <summary>
    /// Chooses the line's model and takes its pieces from stock: among the models whose value at each
    /// loaded bay's span carries that bay, the assembly with the fewest pieces, then the least extra
    /// length, then the lightest. False, with the reason, when none can.
    /// </summary>
    public static bool Size(
        Mother line, IReadOnlyCollection<SuspensionPoint> points, PieceStock stock, LoadTable table, out string? problem)
    {
        problem = null;
        var loads = line.BayLoadsOf(points);

        bool Carries(string model)
        {
            for (int k = 0; k < line.BayCount; k++)
                if (loads[k] > 1e-9 && !table.At(model, line.BayLength(k)).Carries(loads[k]))
                    return false;
            return true;
        }

        double needed = Math.Max(0.01, line.Length - TOL);
        var assembly = stock.Best(needed, Carries);
        if (assembly is null)
        {
            problem = Explain(line, loads, stock, table, needed, Carries);
            return false;
        }

        stock.Take(assembly);
        line.Assembly = assembly;
        line.BayAllowed.Clear();
        for (int k = 0; k < line.BayCount; k++)
            line.BayAllowed.Add(table.At(assembly.Type, line.BayLength(k)));
        line.Bar = assembly.AsBar(line.BayAllowed.Where(a => a.Known).Select(a => a.Kg!.Value).DefaultIfEmpty(0).Min());
        return true;
    }

    private static string Explain(
        Mother line, double[] loads, PieceStock stock, LoadTable table, double needed, Func<string, bool> carries)
    {
        string where = $"Ligne de mother {line.ColL}{line.Row}–{line.ColR}{line.Row}";
        string metres = needed.ToString("0.##", Fr);

        if (!stock.Models.Any())
            return $"{where} : aucune pièce de {InventoryFiles.MothersFileName} n'est en stock.";

        if (stock.Models.Any(carries))
            return $"{where} : les pièces restant en stock ne permettent pas d'assembler {metres} m d'un modèle qui la porte.";

        int worst = Enumerable.Range(0, loads.Length).OrderByDescending(k => loads[k]).FirstOrDefault();
        if (loads.Length == 0)
            return $"{where} : aucun modèle de MotherGrill.xlsx ne convient.";

        double span = line.BayLength(worst);
        double best = stock.Models.Select(m => table.At(m, span).Kg ?? 0).DefaultIfEmpty(0).Max();
        return $"{where} : {Math.Round(loads[worst]).ToString("N0", Fr)} kg sur la travée "
             + $"{line.Columns[worst].Col}{line.Row}–{line.Columns[worst + 1].Col}{line.Row} "
             + $"({span.ToString("0.##", Fr)} m), alors que le modèle le plus fort en stock y admet "
             + $"{Math.Round(best).ToString("N0", Fr)} kg.";
    }
}
