// =============================================================
// TrussCatalog.cs — Mass, stock and printed name of each truss piece, from the inventory.
//
// The database records which trusses a scenario uses and the pieces they are assembled from, but not
// what a piece weighs. That is inventory data: the "Poids propre" column of MotherGrill.xlsx and
// Palonnier.xlsx (RiggingOptimizer.IO.InventoryFiles), the same rows that give each piece its stock and
// its load value. A piece whose mass is left blank is catalogued with an unknown mass - the report prints
// an em dash and raises a reserve rather than a made-up figure - and a mass of zero or less counts as
// unknown too: typed as a placeholder, it would otherwise make the truss weightless in every load it feeds.
//
// A row with Nombre = 0 is a load value only (RiggingOptimizer.IO.LoadTable): no piece is ever taken
// from it, so it never gives a mass. Weighing an element with it would describe a truss the stock cannot
// supply; the element's mass stays unknown instead, and the study says so.
// =============================================================

using RiggingOptimizer.Models;

namespace AliantWebS.Reporting;

/// <summary>One catalogued truss piece: a model at one length.</summary>
/// <param name="UnitWeightKg">
/// The mass of one piece. Null when unknown, and when no row of this model and length has a piece in
/// stock: those rows are load values only.
/// </param>
/// <param name="Stock">
/// How many pieces the inventory holds: the Nombre of every row of this model and length, added up. Zero
/// when the length is listed for its load value only.
/// </param>
public sealed record TrussSpec(
    string Type,
    double LengthM,
    double? UnitWeightKg,
    string? ColorHex,
    string? Label,
    int Stock = 0);

public sealed class TrussCatalog
{
    // Lengths arrive as 8.399999999999999 from spreadsheet formulas, so the key is the length in whole
    // centimetres. Comparing the doubles directly would miss every such row.
    private readonly Dictionary<(string Type, long Cm), TrussSpec> _byTypeAndLength = new();

    public static TrussCatalog Empty { get; } = new();

    public bool IsEmpty => _byTypeAndLength.Count == 0;

    private static long Key(double lengthM) => (long)Math.Round(lengthM * 100.0);

    /// <summary>
    /// The catalogue of an inventory: one entry per model and length. A model used both as a mother and
    /// as a palonnier is one truss, so it is one entry, whose stock is the Nombre of both rows added up;
    /// should two rows give it different masses, the heavier is kept - it is what the attachment points
    /// carry - and the disagreement named in <paramref name="warnings"/>. Only a row with pieces in stock
    /// gives a mass. InventoryFiles.Load already gives such rows the heavier weight, so the optimiser uses it
    /// too; this is the same rule for rows that come from anywhere else.
    /// </summary>
    public static TrussCatalog FromInventory(IEnumerable<BarSpec> bars, List<string>? warnings = null)
    {
        var catalog = new TrussCatalog();

        foreach (var bar in bars)
        {
            var type = bar.Type.Trim();
            if (type.Length == 0) continue;

            int stock = Math.Max(0, bar.StockInit);

            // A load value only: whatever its Poids propre, no piece of it is ever placed.
            double? weight = stock > 0 && bar.WeightKg is > 0 ? bar.WeightKg : null;
            var label = string.IsNullOrWhiteSpace(bar.Brand) ? null : $"{bar.Brand!.Trim()} {DefaultLabel(type, bar.Length)}";
            var key = (type.ToLowerInvariant(), Key(bar.Length));

            if (catalog._byTypeAndLength.TryGetValue(key, out var existing))
            {
                var merged = existing with
                {
                    Stock = existing.Stock + stock,
                    Label = existing.Label ?? label,
                };

                if (existing.UnitWeightKg is null && weight is not null)
                    merged = merged with { UnitWeightKg = weight };
                else if (weight is not null && existing.UnitWeightKg is { } kept && Math.Abs(kept - weight.Value) > 1e-9)
                {
                    // The heavier, whichever was read first: a mass taken too low makes every point it feeds read low.
                    double lighter = Math.Min(kept, weight.Value), heavier = Math.Max(kept, weight.Value);
                    merged = merged with { UnitWeightKg = heavier };
                    warnings?.Add($"« {type} » de {bar.Length.ToString("0.##", Fr)} m a deux poids propres dans "
                                  + $"l'inventaire ({lighter.ToString("0.##", Fr)} et {heavier.ToString("0.##", Fr)} kg) : "
                                  + $"le plus lourd, {heavier.ToString("0.##", Fr)} kg, est retenu.");
                }

                catalog._byTypeAndLength[key] = merged;
                continue;
            }

            catalog._byTypeAndLength[key] = new TrussSpec(type, bar.Length, weight, null, label, stock);
        }

        return catalog;
    }

    /// <summary>The catalogued piece for a type at a length, or null when it is not listed.</summary>
    public TrussSpec? Find(string? type, double lengthM)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        return _byTypeAndLength.TryGetValue((type!.Trim().ToLowerInvariant(), Key(lengthM)), out var spec)
            ? spec
            : null;
    }

    /// <summary>How many pieces of a model at a length the inventory holds: 0 when none, or when it is not listed.</summary>
    public int StockOf(string? type, double lengthM) => Find(type, lengthM)?.Stock ?? 0;

    /// <summary>
    /// Label to print when the inventory names no brand, in the studies' style: "lg:5,6m". To the
    /// centimetre, the resolution pieces are keyed at: one decimal printed a 1,45 m piece as 1,5 m.
    /// </summary>
    public static string DefaultLabel(string? type, double lengthM) =>
        $"{type} lg:{lengthM.ToString("0.##", Fr)}m";

    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
}
