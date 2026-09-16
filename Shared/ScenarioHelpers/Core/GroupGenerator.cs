// =============================================================
// GroupGenerator.cs  — Génération de tous les groupes candidats
// Équivalent Python: generate_all_groups()
// =============================================================

using RiggingOptimizer.Models;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.Core;

public static class GroupGenerator
{
    /// <summary>
    /// Every group of charges one palonnier could carry: charges sharing an x, hung from the point line
    /// below them to the one above (Y groups), and - unless <paramref name="allowBiais"/> is false -
    /// collinear charges on an inclined line.
    /// </summary>
    /// <param name="feasible">Whether the stock can assemble a palonnier of this span carrying this load.</param>
    public static List<CandidateGroup> Generate(
        List<Charge> charges, Func<double, double, bool> feasible, bool allowBiais = true)
    {
        var groups = new List<CandidateGroup>();
        var seen   = new HashSet<string>(); // clé = noms triés joints

        GenerateYGroups(charges, feasible, groups, seen);
        if (allowBiais) GenerateBiaisGroups(charges, feasible, groups, seen);

        return groups;
    }

    // ── Groupes Y (même x ± TOL) ─────────────────────────────────
    private static void GenerateYGroups(
        List<Charge> charges, Func<double, double, bool> feasible,
        List<CandidateGroup> groups, HashSet<string> seen)
    {
        // Regrouper les charges par x similaire
        var xBuckets = new Dictionary<double, List<int>>();
        for (int i = 0; i < charges.Count; i++)
        {
            double cx = charges[i].X;
            bool placed = false;
            foreach (double kx in xBuckets.Keys)
            {
                if (Math.Abs(cx - kx) <= TOL)
                { xBuckets[kx].Add(i); placed = true; break; }
            }
            if (!placed) xBuckets[cx] = new List<int> { i };
        }

        // Toutes les combinaisons de taille 1..n dans chaque bucket
        foreach (var (xKey, idxList) in xBuckets)
        {
            for (int size = 1; size <= idxList.Count; size++)
            {
                foreach (var combo in Combinations(idxList, size))
                {
                    var sub     = combo.Select(i => charges[i]).ToList();
                    double totW = sub.Sum(c => c.Weight);
                    double yMin = sub.Min(c => c.Y);
                    double yMax = sub.Max(c => c.Y);

                    var (below, above) = EnclosingRows(yMin, yMax);
                    double span = above.Y - below.Y;

                    if (!feasible(span, totW)) continue;

                    string key = MakeKey(sub);
                    if (!seen.Add(key)) continue;

                    groups.Add(new CandidateGroup
                    {
                        Type     = PalonnierDirection.Y,
                        Names    = sub.Select(c => c.Name).ToHashSet(),
                        Charges  = sub,
                        A        = (xKey, below.Y),
                        B        = (xKey, above.Y),
                        Span     = span,
                        Total    = totW,
                    });
                }
            }
        }
    }

    // ── Groupes biais (alignement non-X non-Y) ───────────────────
    private static void GenerateBiaisGroups(
        List<Charge> charges, Func<double, double, bool> feasible,
        List<CandidateGroup> groups, HashSet<string> seen)
    {
        int n = charges.Count;
        int maxSize = Math.Min(4, n);

        for (int size = 2; size <= maxSize; size++)
        {
            foreach (var combo in Combinations(Enumerable.Range(0, n).ToList(), size))
            {
                var sub = combo.Select(i => charges[i]).ToList();
                double dxTot = sub.Max(c => c.X) - sub.Min(c => c.X);
                double dyTot = sub.Max(c => c.Y) - sub.Min(c => c.Y);

                // Exclure alignements purs X ou Y
                if (dxTot < TOL) continue;   // même x → déjà traité en Y
                if (dyTot < TOL) continue;   // même y → ignoré

                // Direction de la barre: du point le plus bas au plus haut en Y
                var cLow  = sub.MinBy(c => c.Y)!;
                var cHigh = sub.MaxBy(c => c.Y)!;

                // Vérifier colinéarité pour size ≥ 3, contre cet axe-là
                if (size >= 3 && !WithinAxis(sub, cLow, cHigh)) continue;

                double totW = sub.Sum(c => c.Weight);
                double yMin = sub.Min(c => c.Y);
                double yMax = sub.Max(c => c.Y);

                var (below, above) = EnclosingRows(yMin, yMax);

                double dirX = cHigh.X - cLow.X;
                double dirY = cHigh.Y - cLow.Y;
                double norm = Math.Max(Math.Sqrt(dirX*dirX + dirY*dirY), 1e-9);
                dirX /= norm; dirY /= norm;

                // Calcul de xA (intersection droite avec y=below.Y) et xB (avec y=above.Y)
                double xA = Math.Abs(dirY) > 1e-6
                    ? cLow.X + (below.Y - cLow.Y) / dirY * dirX
                    : cLow.X;
                double xB = Math.Abs(dirY) > 1e-6
                    ? cLow.X + (above.Y - cLow.Y) / dirY * dirX
                    : cHigh.X;

                var A = (Math.Round(xA, 4), below.Y);
                var B = (Math.Round(xB, 4), above.Y);
                double spanAB = Math.Sqrt(Math.Pow(B.Item1-A.Item1,2) + Math.Pow(B.Item2-A.Item2,2));

                if (!feasible(spanAB, totW)) continue;

                string key = MakeKey(sub);
                if (!seen.Add(key)) continue;

                groups.Add(new CandidateGroup
                {
                    Type     = PalonnierDirection.Biais,
                    Names    = sub.Select(c => c.Name).ToHashSet(),
                    Charges  = sub,
                    A        = A,
                    B        = B,
                    Span     = spanAB,
                    Total    = totW,
                });
            }
        }
    }

    // ── Vérification de colinéarité ──────────────────────────────
    // Every charge within ColinearTol of the axis the palonnier is built on, from its lowest charge in y to
    // its highest: the axis its charges are projected onto and the study measures them from. Another line
    // through the charges (the two furthest apart in x, say) can pass near a charge that axis misses.
    private static bool WithinAxis(List<Charge> sub, Charge from, Charge to)
    {
        double ax = to.X - from.X, ay = to.Y - from.Y;
        double norm = Math.Sqrt(ax*ax + ay*ay);
        if (norm < 1e-9) return false;

        foreach (var c in sub)
        {
            double cross = Math.Abs(ax * (c.Y - from.Y) - ay * (c.X - from.X)) / norm;
            if (cross > ColinearTol) return false;
        }
        return true;
    }

    // ── Clé de déduplication ─────────────────────────────────────
    private static string MakeKey(IEnumerable<Charge> charges)
        => string.Join("|", charges.Select(c => c.Name).OrderBy(n => n));

    // ── Combinaisons (équivalent de itertools.combinations) ──────
    public static IEnumerable<List<T>> Combinations<T>(List<T> list, int k)
    {
        if (k == 0) { yield return new List<T>(); yield break; }
        for (int i = 0; i <= list.Count - k; i++)
        {
            var head = list[i];
            foreach (var tail in Combinations(list.GetRange(i + 1, list.Count - i - 1), k - 1))
            {
                var combo = new List<T> { head };
                combo.AddRange(tail);
                yield return combo;
            }
        }
    }
}
