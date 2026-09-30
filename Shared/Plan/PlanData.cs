// =============================================================
// PlanData.cs — What the plan draws and exports, brought into one frame before anything is drawn.
//
// The plan's inputs arrive in three frames:
// - element ends (mothers, palonniers), scenic loads and intersections: the grid's axes, metres from
//   Point0 - the STORED frame, as the database holds them;
// - sensors: their X/Y attributes, absolute millimetres in the grid's axes (no Point0);
// - charpente points: CharpenteCoordinates, absolute metres in the grid's axes.
// Plus the loads carried by a palonnier or a mother, given as a distance along the carrier.
//
// PlanData turns every one of them into the stored frame, once: the scene (PlanScene) takes stored positions
// to the screen (PlanView.StoredToSvg), the DXF (PlanDxfExporter) to the studies' axes (StudyAxes.ToStudy). One
// exception, kept so the DXF stays byte for byte what it was: the exporter reads the sensors' absolute
// millimetres and converts them with PlanView.StudyMetres, the same arithmetic as TrySensorPosition + ToStudy.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using AliantWebS.Models;

namespace AliantWebS.Plan;

/// <summary>The plan's inputs, and their positions in the stored frame: the grid's axes, metres from Point0.</summary>
public sealed class PlanData
{
    public double Point0X { get; init; }
    public double Point0Y { get; init; }

    public IReadOnlyList<MotherGridPalonnier> MotherGrids { get; init; } = Array.Empty<MotherGridPalonnier>();
    public IReadOnlyList<MotherGridPalonnier> Palonniers { get; init; } = Array.Empty<MotherGridPalonnier>();

    /// <summary>Stored frame. Keyed by the mother the points were found on.</summary>
    public IReadOnlyDictionary<Guid, List<(double X, double Y)>> IntersectionPoints { get; init; } =
        new Dictionary<Guid, List<(double X, double Y)>>();

    /// <summary>Charpente point name (e.g. "E5") → load in kilograms.</summary>
    public IReadOnlyDictionary<string, double> CharpenteLoads { get; init; } = new Dictionary<string, double>();

    /// <summary>
    /// The venue load book (TCharpente), in kilograms (= daN): a point's capacity under its name ("E5"), a beam's own total
    /// under row 10 ("E10"). CapacityOf gives no ratio for a 0: it is ambiguous - J2, J4, J6 and J8 are 0 because the studies
    /// allow nothing there, the row-10 totals are 0 because nobody has entered them, and B4 (NOT NULL) stores 0 when never
    /// filled. So a point recorded at 0 is drawn grey but flagged when anything hangs on it (PlanScene.CharpenteLoadStyle),
    /// while a beam total of 0 is simply unknown.
    /// </summary>
    public IReadOnlyDictionary<string, double> CharpenteCapacities { get; init; } = new Dictionary<string, double>();

    public IReadOnlyList<ChargesScenique> ChargesSceniques { get; init; } = Array.Empty<ChargesScenique>();

    /// <summary>Loads hung on a palonnier, or straight on a mother: ChargeMp.X is the distance along the carrier.</summary>
    public IReadOnlyList<ChargeMp> PalonnierLoads { get; init; } = Array.Empty<ChargeMp>();

    public IReadOnlyList<Element> Sensors { get; init; } = Array.Empty<Element>();

    /// <summary>Sensor ElementId → its highest charge ratio (0 to 1 and beyond).</summary>
    public IReadOnlyDictionary<Guid, double> SensorChargeRatios { get; init; } = new Dictionary<Guid, double>();

    // ── Element ends ──────────────────────────────────────────────────────────────────────────

    /// <summary>A mother's or a palonnier's ends, stored frame; false while any of the four is missing.</summary>
    public static bool TryEnds(MotherGridPalonnier element, out (double X1, double Y1, double X2, double Y2) ends)
    {
        if (element.X1 is { } x1 && element.Y1 is { } y1 && element.X2 is { } x2 && element.Y2 is { } y2)
        {
            ends = (x1, y1, x2, y2);
            return true;
        }
        ends = default;
        return false;
    }

    // ── Carried loads ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The element a ChargeMp hangs on: a palonnier, or a mother line it hangs from directly (a motor within 8 cm of
    /// a point line). The palonnier is looked up first.
    /// </summary>
    public MotherGridPalonnier? CarrierOf(ChargeMp charge) =>
        Palonniers.FirstOrDefault(p => p.MotherGridPalonnierId == charge.MotherGridPalonnierId)
        ?? MotherGrids.FirstOrDefault(m => m.MotherGridPalonnierId == charge.MotherGridPalonnierId);

    /// <summary>True when the carrier is a palonnier (it is in <see cref="Palonniers"/>), false for a mother.</summary>
    public bool IsPalonnier(MotherGridPalonnier carrier) => Palonniers.Contains(carrier);

    /// <summary>
    /// Where a carried load is, stored frame: ChargeMp.X metres along its carrier from (X1, Y1). A carrier of zero
    /// length puts it on (X1, Y1). False without a carrier, carrier ends or X.
    /// </summary>
    public bool TryCarriedPosition(ChargeMp charge, [NotNullWhen(true)] out MotherGridPalonnier? carrier, out double x, out double y)
    {
        x = y = 0;
        carrier = CarrierOf(charge);
        if (carrier is null || charge.X is not { } along || !TryEnds(carrier, out var e))
            return false;

        double length = Math.Sqrt(Math.Pow(e.X2 - e.X1, 2) + Math.Pow(e.Y2 - e.Y1, 2));
        double ratio = length > 0 ? along / length : 0;
        x = e.X1 + ratio * (e.X2 - e.X1);
        y = e.Y1 + ratio * (e.Y2 - e.Y1);
        return true;
    }

    // ── Sensors ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A sensor's "X" and "Y" attributes: absolute millimetres in the grid's axes, invariant culture. The one test of
    /// "this sensor has a position", shared by the plan, the DXF, the Surveillance page and the sensor list.
    ///
    /// Both must be finite numbers. The attribute is free text, and .NET reads "NaN", "Infinity" and "-Infinity" as
    /// numbers: such a sensor used to count as positioned, was drawn on Point0 (the browser ignores a NaN transform)
    /// and written into the DXF with NaN coordinates. Now it has no position, and the list offers to place it.
    /// </summary>
    public static bool TrySensorAbsoluteMm(Element sensor, out double xMm, out double yMm)
    {
        xMm = yMm = 0;
        var xAttr = sensor.AttributeElements?.FirstOrDefault(ae => ae.AttributeType?.Name == "X");
        var yAttr = sensor.AttributeElements?.FirstOrDefault(ae => ae.AttributeType?.Name == "Y");
        return xAttr != null && yAttr != null
            && double.TryParse(xAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out xMm)
            && double.TryParse(yAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out yMm)
            && double.IsFinite(xMm) && double.IsFinite(yMm);
    }

    /// <summary>A sensor's position, stored frame; false when its X/Y attributes are missing or unreadable.</summary>
    public bool TrySensorPosition(Element sensor, out double x, out double y)
    {
        x = y = 0;
        if (!TrySensorAbsoluteMm(sensor, out var xMm, out var yMm)) return false;
        (x, y) = AbsoluteMmToStored(xMm, yMm);
        return true;
    }

    /// <summary>A sensor's "Statut" attribute: Actif, Inactif, Panne, or null.</summary>
    public static string? StatutOf(Element sensor) =>
        sensor.AttributeElements?.FirstOrDefault(ae => ae.AttributeType?.Name == "Statut")?.Value;

    /// <summary>A sensor's charge ratio, when one is known.</summary>
    public double? RatioOf(Element sensor) =>
        SensorChargeRatios.TryGetValue(sensor.ElementId, out var ratio) ? ratio : null;

    // ── Charpente points ──────────────────────────────────────────────────────────────────────

    /// <summary>Every charpente point, stored frame, in CharpenteCoordinates' order.</summary>
    public IEnumerable<(string Key, double X, double Y)> CharpentePoints() =>
        CharpenteCoordinates.Points.Select(p => (p.Key, p.Value.X - Point0X, p.Value.Y - Point0Y));

    /// <summary>A charpente point by name (e.g. "E5"), stored frame.</summary>
    public bool TryCharpentePoint(string key, out double x, out double y)
    {
        x = y = 0;
        if (key is null || !CharpenteCoordinates.Points.TryGetValue(key, out var p)) return false;
        (x, y) = (p.X - Point0X, p.Y - Point0Y);
        return true;
    }

    /// <summary>A capacity on record (above 0), by its TCharpente name: a point ("E5") or a beam's total ("E10").</summary>
    public double? CapacityOf(string name) =>
        name is not null && CharpenteCapacities.TryGetValue(name, out var capacity) && capacity > 0 ? capacity : null;

    /// <summary>
    /// The capacities of the venue load book, by property name: A1 to J9 for the points, A10 to J10 for each beam's own
    /// total. Values left empty are left out. B4 is the one column declared as a plain double (NOT NULL), so both types
    /// are read, as the Charpente page reads them.
    /// </summary>
    public static Dictionary<string, double> CapacitiesOf(Tcharpente? loadBook)
    {
        var capacities = new Dictionary<string, double>();
        if (loadBook is null) return capacities;
        foreach (var property in typeof(Tcharpente).GetProperties())
        {
            if ((property.PropertyType == typeof(double?) || property.PropertyType == typeof(double))
                && System.Text.RegularExpressions.Regex.IsMatch(property.Name, "^[A-J](10|[1-9])$")
                && property.GetValue(loadBook) is double value)
                capacities[property.Name] = value;
        }
        return capacities;
    }

    // ── Frames ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Absolute millimetres in the grid's axes (sensors) → stored frame.</summary>
    public (double X, double Y) AbsoluteMmToStored(double xMm, double yMm) =>
        (xMm / PlanView.MmPerM - Point0X, yMm / PlanView.MmPerM - Point0Y);

    /// <summary>Stored frame → absolute millimetres in the grid's axes: the inverse of AbsoluteMmToStored. (A sensor placed on
    /// the plan is saved through PlanView.FromSvg, the same arithmetic.)</summary>
    public (double X, double Y) StoredToAbsoluteMm(double x, double y) =>
        ((x + Point0X) * PlanView.MmPerM, (y + Point0Y) * PlanView.MmPerM);
}
