// =============================================================
// LoadTable.cs — What a truss may carry over a span: the one reading of the inventory's
// "Charge ponctuelle au milieu", shared by the optimiser and the study.
//
// The owner's rule (2026-09-11): the loads between two neighbouring hanging points, wherever they sit
// between them, may add up to the charge ponctuelle au milieu of the same model, read at the length
// immediately above the span - conservative, since a longer truss allows less. Every row of the model
// counts, in stock or not: a row with Nombre = 0 is there for its value alone. Two rows at the same
// length: the lower value.
//
// Past the longest row the value is computed at equal moment from that row. A centred point load P on
// a span L bends a truss by P·L/4, and its own weight, p per metre, by p·L²/8, so
//     M = P0·L0/4 + p·L0²/8     and     P(L) = 4·(M - p·L²/8) / L.
// Only up to twice that row's length, and only when its weight is known: at long spans deflection
// governs, and the formula would allow more than the truss carries - checked against a manufacturer
// table, it stays within 6 % on the safe side up to three times the base length and is 22 % over at
// three and a half. The study says when a value was computed rather than read.
// =============================================================

using System.Globalization;
using RiggingOptimizer.Models;

namespace RiggingOptimizer.IO;

/// <summary>Where an allowed load comes from.</summary>
public enum AllowedSource
{
    /// <summary>No value: the model is not in the inventory, the span is too long, or a weight is missing.</summary>
    None = 0,

    /// <summary>Read from a row of the inventory.</summary>
    Table = 1,

    /// <summary>Computed at equal moment from the model's longest row.</summary>
    Computed = 2,
}

/// <summary>The load a truss may carry over one span, and where that figure comes from.</summary>
/// <param name="Kg">The allowed sum of the loads on the span; null when there is no value.</param>
/// <param name="SpanM">The span it was asked for.</param>
/// <param name="RowLengthM">The row read, or for a computed value the row it was computed from.</param>
/// <param name="RowKg">That row's charge ponctuelle au milieu.</param>
/// <param name="KgPerM">The weight per metre used by the computation.</param>
/// <param name="Reason">Why there is no value, in French, for the study.</param>
public sealed record AllowedLoad(
    double? Kg,
    AllowedSource Source,
    double SpanM,
    double? RowLengthM = null,
    double? RowKg = null,
    double? KgPerM = null,
    string? Reason = null)
{
    public bool Known => Kg is > 0;

    /// <summary>Whether a load of <paramref name="kg"/> is within this value.</summary>
    public bool Carries(double kg) => Kg is { } allowed && allowed > 0 && kg <= allowed + 1e-6;

    public static AllowedLoad None(double spanM, string reason) => new(null, AllowedSource.None, spanM, Reason: reason);
}

/// <summary>The load table of one role - the mothers of MotherGrill.xlsx, or the palonniers of Palonnier.xlsx.</summary>
public sealed class LoadTable
{
    /// <summary>A row this much shorter than the span still covers it: spreadsheet lengths carry floating-point noise.</summary>
    public const double LengthToleranceM = 0.01;

    /// <summary>How far past its longest row a model's value is computed, as a multiple of that row's length.</summary>
    public const double ExtrapolationLimit = 2.0;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly Dictionary<string, List<BarSpec>> _byModel;

    private LoadTable(Dictionary<string, List<BarSpec>> byModel) => _byModel = byModel;

    public static LoadTable Empty { get; } = new(new Dictionary<string, List<BarSpec>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The table of these rows: every row with a length and a value, in stock or not.</summary>
    public static LoadTable From(IEnumerable<BarSpec> rows)
    {
        var byModel = new Dictionary<string, List<BarSpec>>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in rows
                     .Where(r => !string.IsNullOrWhiteSpace(r.Type) && r.Length > 0 && r.Capacity > 0)
                     .GroupBy(r => r.Type.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            byModel[model.Key] = model
                .GroupBy(r => (long)Math.Round(r.Length * 100))
                .Select(sameLength => sameLength.OrderBy(r => r.Capacity).First())   // the lower value
                .OrderBy(r => r.Length)
                .ToList();
        }

        return new LoadTable(byModel);
    }

    public IReadOnlyCollection<string> Models => _byModel.Keys;

    public bool Has(string? model) => model is not null && _byModel.ContainsKey(model.Trim());

    /// <summary>The model's rows, shortest first, one per length.</summary>
    public IReadOnlyList<BarSpec> RowsOf(string? model) =>
        model is not null && _byModel.TryGetValue(model.Trim(), out var rows) ? rows : Array.Empty<BarSpec>();

    /// <summary>What the loads on a span of <paramref name="spanM"/> may add up to, for this model.</summary>
    public AllowedLoad At(string? model, double spanM)
    {
        var rows = RowsOf(model);
        if (rows.Count == 0)
            return AllowedLoad.None(spanM, $"« {model} » ne figure pas dans l'inventaire");

        // The length immediately above the span. A span shorter than every row reads the shortest.
        var row = rows.FirstOrDefault(r => r.Length >= spanM - LengthToleranceM);
        if (row is not null)
            return new AllowedLoad(row.Capacity, AllowedSource.Table, spanM, row.Length, row.Capacity);

        var longest = rows[^1];
        double l0 = longest.Length, p0 = longest.Capacity;

        if (spanM > ExtrapolationLimit * l0 + LengthToleranceM)
        {
            return AllowedLoad.None(spanM,
                $"portée de plus de {ExtrapolationLimit.ToString("0.#", Fr)} fois la plus grande longueur de l'inventaire "
                + $"({l0.ToString("0.##", Fr)} m)");
        }

        if (longest.WeightKg is not > 0)
        {
            return AllowedLoad.None(spanM,
                $"poids propre inconnu pour la longueur de {l0.ToString("0.##", Fr)} m, dont la valeur serait calculée");
        }

        double p = longest.WeightKg.Value / l0;
        double moment = p0 * l0 / 4 + p * l0 * l0 / 8;
        double allowed = 4 * (moment - p * spanM * spanM / 8) / spanM;

        if (allowed <= 0)
            return AllowedLoad.None(spanM, "le poids propre seul atteint le moment admissible");

        return new AllowedLoad(allowed, AllowedSource.Computed, spanM, l0, p0, p);
    }
}
