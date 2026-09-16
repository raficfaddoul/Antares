// =============================================================
// Assembly.cs — Trusses assembled from stock pieces, and a stock that never lends a piece twice.
//
// A mother or a palonnier may be made of several pieces of ONE model joined end to end: 5,6 m + 2,8 m
// = 8,4 m. Where the joints fall does not matter. The pieces come from the inventory's stock rows
// (Nombre > 0); a row with Nombre = 0 gives a value to the load table and is never a piece.
//
// PieceStock hands each piece out once. Everything the optimiser uses it takes from here, so a layout
// can never use more pieces than MotherGrill.xlsx and Palonnier.xlsx hold.
// =============================================================

using Helpers;
using RiggingOptimizer.Models;

namespace RiggingOptimizer.Core;

/// <summary>Pieces of one model joined end to end.</summary>
public sealed class Assembly
{
    public Assembly(string type, IEnumerable<BarSpec> pieces)
    {
        Type = type;
        Pieces = pieces.OrderByDescending(p => p.Length).ToList();
    }

    public string Type { get; }

    /// <summary>The stock rows used, one entry per piece, longest first.</summary>
    public IReadOnlyList<BarSpec> Pieces { get; }

    public int Count => Pieces.Count;

    public double LengthM => Pieces.Sum(p => p.Length);

    /// <summary>The pieces' masses added up; null when any of them is unknown.</summary>
    public double? WeightKg =>
        Pieces.Count > 0 && Pieces.All(p => p.WeightKg is > 0) ? Pieces.Sum(p => p.WeightKg!.Value) : null;

    /// <summary>The stored form, "[560,280]" (Helpers.TrussComposition).</summary>
    public string? Composition => TrussComposition.Serialize(Pieces.Select(p => p.Length));

    /// <summary>"5,6 + 2,8 m".</summary>
    public string Describe() => TrussComposition.Describe(Pieces.Select(p => p.Length).ToList());

    /// <summary>The assembled truss as one bar: its model, total length and mass, and the given allowed load.</summary>
    public BarSpec AsBar(double allowedKg) => new()
    {
        Type = Type,
        Length = LengthM,
        StockInit = 0,
        Stock = 0,
        Capacity = allowedKg,
        WeightKg = WeightKg,
        Brand = Pieces.Select(p => p.Brand).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b)),
        Material = Pieces.Select(p => p.Material).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)),
        Usage = Pieces.Select(p => p.Usage).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
    };

    public override string ToString() => $"{Type} {Describe()}";
}

/// <summary>The pieces of one role in stock, each handed out once.</summary>
public sealed class PieceStock
{
    private readonly Dictionary<BarSpec, int> _left = new(ReferenceEqualityComparer.Instance);

    /// <param name="rows">The inventory rows of one role. Only those with a Nombre above zero are pieces.</param>
    public PieceStock(IEnumerable<BarSpec> rows)
    {
        Rows = rows.Where(r => r.StockInit > 0 && r.Length > 0 && !string.IsNullOrWhiteSpace(r.Type)).ToList();
        foreach (var r in Rows) _left[r] = r.StockInit;
    }

    /// <summary>The stock rows: those with a Nombre above zero.</summary>
    public IReadOnlyList<BarSpec> Rows { get; }

    public int Left(BarSpec row) => _left.TryGetValue(row, out var n) ? n : 0;

    public int Used(BarSpec row) => _left.TryGetValue(row, out var n) ? row.StockInit - n : 0;

    public IEnumerable<string> Models =>
        Rows.Select(r => r.Type.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The best way to reach <paramref name="minLengthM"/> with the pieces of one model still in stock:
    /// fewest pieces, then least extra length, then lightest. Null when they cannot reach it.
    /// </summary>
    public Assembly? Cheapest(string model, double minLengthM, int maxPieces = Assembler.MaxPieces)
    {
        var rows = Rows
            .Where(r => string.Equals(r.Type.Trim(), model.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(r => (Row: r, Available: Left(r)))
            .Where(t => t.Available > 0);

        var pieces = Assembler.Cheapest(rows, minLengthM, maxPieces);
        return pieces is null ? null : new Assembly(model.Trim(), pieces);
    }

    /// <summary>
    /// The best assembly of any model the caller accepts: fewest pieces, then least extra length, then
    /// lightest, then by name so the choice never depends on the order of the files.
    /// </summary>
    public Assembly? Best(double minLengthM, Func<string, bool> modelOk)
    {
        Assembly? best = null;
        foreach (var model in Models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            if (!modelOk(model)) continue;
            var candidate = Cheapest(model, minLengthM);
            if (candidate is null) continue;
            if (best is null || Compare(candidate, best) < 0) best = candidate;
        }
        return best;
    }

    private static int Compare(Assembly a, Assembly b)
    {
        if (a.Count != b.Count) return a.Count.CompareTo(b.Count);
        long la = (long)Math.Round(a.LengthM * 100), lb = (long)Math.Round(b.LengthM * 100);
        if (la != lb) return la.CompareTo(lb);
        double wa = a.WeightKg ?? double.MaxValue, wb = b.WeightKg ?? double.MaxValue;
        if (Math.Abs(wa - wb) > 1e-9) return wa.CompareTo(wb);
        return string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanTake(Assembly a) =>
        a.Pieces.GroupBy(p => p, ReferenceEqualityComparer.Instance).All(g => Left((BarSpec)g.Key!) >= g.Count());

    /// <summary>Takes the pieces out of stock. Throws rather than lend a piece that is not there.</summary>
    public void Take(Assembly a)
    {
        if (!CanTake(a))
            throw new InvalidOperationException($"Stock insuffisant pour {a}.");
        foreach (var p in a.Pieces) _left[p]--;
    }

    /// <summary>Puts the pieces back.</summary>
    public void Give(Assembly a)
    {
        foreach (var p in a.Pieces)
        {
            if (_left.TryGetValue(p, out var n)) _left[p] = Math.Min(p.StockInit, n + 1);
        }
    }

    /// <summary>The rows as they stand: StockInit what the inventory holds, Stock what is left.</summary>
    public List<BarSpec> Snapshot() => Rows.Select(r =>
    {
        var copy = r.Clone();
        copy.Stock = Left(r);
        return copy;
    }).ToList();
}

/// <summary>Chooses pieces.</summary>
public static class Assembler
{
    /// <summary>No element is assembled from more pieces than this.</summary>
    public const int MaxPieces = 16;

    /// <summary>
    /// The pieces, taken from <paramref name="rows"/> within what each has available, whose lengths add
    /// up to at least <paramref name="minLengthM"/>: fewest pieces, then least extra length, then
    /// lightest. Null when they cannot. A piece of unknown mass is treated as heavy, so a known one of
    /// the same length is preferred.
    /// </summary>
    public static List<BarSpec>? Cheapest(
        IEnumerable<(BarSpec Row, int Available)> rows, double minLengthM, int maxPieces = MaxPieces)
    {
        var items = rows
            .Where(t => t.Available > 0 && t.Row.Length > 0)
            .OrderByDescending(t => t.Row.Length)
            .ThenBy(t => t.Row.WeightKg ?? double.MaxValue)
            .ToList();
        if (items.Count == 0) return null;

        long target = Math.Max(1, (long)Math.Ceiling(minLengthM * 100 - 1e-6));   // centimetres
        long[] len = items.Select(t => Math.Max(1, (long)Math.Round(t.Row.Length * 100))).ToArray();
        double[] mass = items.Select(t => t.Row.WeightKg is > 0 ? t.Row.WeightKg.Value : 1e6).ToArray();

        var counts = new int[items.Count];
        int[]? best = null;
        (int Count, long Excess, double Mass) bestKey = (int.MaxValue, long.MaxValue, double.MaxValue);

        void Search(int i, int count, long sum, double weight)
        {
            if (sum >= target)
            {
                var key = (count, sum - target, weight);
                if (Better(key, bestKey))
                {
                    bestKey = key;
                    best = (int[])counts.Clone();
                }
                return;
            }

            if (i == items.Count || count >= maxPieces || count >= bestKey.Count) return;

            long missing = target - sum;
            int most = (int)Math.Min(items[i].Available,
                       Math.Min(maxPieces - count, (missing + len[i] - 1) / len[i]));

            for (int k = most; k >= 0; k--)
            {
                counts[i] = k;
                Search(i + 1, count + k, sum + k * len[i], weight + k * mass[i]);
            }
            counts[i] = 0;
        }

        Search(0, 0, 0, 0);
        if (best is null) return null;

        var pieces = new List<BarSpec>();
        for (int i = 0; i < items.Count; i++)
            for (int k = 0; k < best[i]; k++)
                pieces.Add(items[i].Row);
        return pieces;
    }

    private static bool Better((int Count, long Excess, double Mass) a, (int Count, long Excess, double Mass) b)
    {
        if (a.Count != b.Count) return a.Count < b.Count;
        if (a.Excess != b.Excess) return a.Excess < b.Excess;
        return a.Mass < b.Mass - 1e-9;
    }
}
