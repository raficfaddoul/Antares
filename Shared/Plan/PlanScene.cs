// =============================================================
// PlanScene.cs — Everything the plan draws, placed on the screen and labelled, before any markup.
//
// Built from PlanData, whose inputs are all in the stored frame (the grid's axes, metres from Point0):
// each is taken to SVG user units by PlanView.StoredToSvg, and given the text a person reads - tooltips,
// titles, the load's value - in the studies' axes. The component's markup only lays these out; the
// browser module (wwwroot/js/svg-plan.js) reads positions back from the drawn elements to hit-test and
// snap, so what is drawn, what is picked and what is snapped to are the same numbers.
//
// Markers and text are sized in CSS pixels by the markup (a scale set by the browser at every zoom);
// the picture and the grid labels are sized in SVG units and grow with the plan.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AliantWebS.Models;
using Helpers;

namespace AliantWebS.Plan;

/// <summary>A mother or a palonnier on the screen (SVG user units), with its tooltip.</summary>
public sealed record PlanSegment(MotherGridPalonnier Source, double X1, double Y1, double X2, double Y2, string Title);

/// <summary>A charpente point drawing can snap to, by its name (e.g. "E5").</summary>
public sealed record PlanNode(string Key, double X, double Y);

/// <summary>A beam letter or point number.</summary>
public sealed record PlanGridLabel(string Text, double X, double Y);

/// <summary>A point with a tooltip: an intersection.</summary>
public sealed record PlanDot(double X, double Y, string Title);

/// <summary>
/// A load: its source row (a charpente point name, a ChargesScenique or a ChargeMp), where it is, its colour, the
/// value printed beside it, and its tooltip (a title, then one fact per line). Overloaded: a charpente point that needs
/// attention - past its capacity, on a beam past its own total, loaded where nothing is admitted, or lifted
/// (PlanScene.CharpenteLoadStyle).
/// </summary>
public sealed record PlanLoad(object Source, double X, double Y, string Color, string Value, string TipTitle, string Tip,
    bool Overloaded = false);

/// <summary>A sensor: where it is, its charge ratio if known, its status and its tooltip.</summary>
public sealed record PlanSensor(Element Source, string? Name, double X, double Y, double? Ratio, string? Statut, string Title);

/// <summary>
/// Where the selected palonnier hangs, as a page hands it to the plan: stored frame (the grid's axes, metres from Point0),
/// hooked or not, and its tooltip. Antarès computes them with PlanHangingMarks.For.
/// </summary>
public sealed record PlanHangingMark(double X, double Y, bool Hooked, string Title);

/// <summary>A hanging mark of the selected palonnier, on the screen.</summary>
public sealed record PlanMark(double X, double Y, bool Hooked, string Title);

public sealed class PlanScene
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Drawing snaps an end onto a charpente point, a mother end or a palonnier end within this distance (1 m).</summary>
    public const double SnapDistanceM = 1.0;

    /// <summary>The picture's footprint, as a viewBox: what "zoom to extent" shows.</summary>
    public (double X, double Y, double Width, double Height) Extent { get; private init; }

    /// <summary>The SVG transform laying the background picture on the plan.</summary>
    public string ImageTransform { get; private init; } = "";

    /// <summary>SVG user units → the studies' axes, metres from Point0 (a b c d e f), for the mouse position readout.</summary>
    public IReadOnlyList<double> SvgToStudy { get; private init; } = Array.Empty<double>();

    /// <summary><see cref="SnapDistanceM"/> in SVG user units.</summary>
    public double SnapDistance { get; private init; }

    public IReadOnlyList<PlanGridLabel> GridLabels { get; private init; } = Array.Empty<PlanGridLabel>();
    public IReadOnlyList<PlanNode> Nodes { get; private init; } = Array.Empty<PlanNode>();
    public IReadOnlyList<PlanSegment> Palonniers { get; private init; } = Array.Empty<PlanSegment>();
    public IReadOnlyList<PlanSegment> MotherGrids { get; private init; } = Array.Empty<PlanSegment>();
    public IReadOnlyList<PlanMark> HangingMarks { get; private init; } = Array.Empty<PlanMark>();
    public IReadOnlyList<PlanDot> Intersections { get; private init; } = Array.Empty<PlanDot>();
    public IReadOnlyList<PlanLoad> CharpenteLoads { get; private init; } = Array.Empty<PlanLoad>();
    public IReadOnlyList<PlanLoad> ScenicLoads { get; private init; } = Array.Empty<PlanLoad>();
    public IReadOnlyList<PlanLoad> CarriedLoads { get; private init; } = Array.Empty<PlanLoad>();
    public IReadOnlyList<PlanSensor> Sensors { get; private init; } = Array.Empty<PlanSensor>();

    public static PlanScene Build(PlanData data, IEnumerable<PlanHangingMark>? hangingMarks = null)
    {
        (double X, double Y) Svg(double x, double y) => PlanView.StoredToSvg(x, y);

        return new PlanScene
        {
            Extent = PlanView.Extent(data.Point0X, data.Point0Y),
            ImageTransform = PlanView.ImageTransform(data.Point0X, data.Point0Y),
            SvgToStudy = PlanView.SvgToStudyMatrix(),
            SnapDistance = PlanView.MetresToSvg(SnapDistanceM),

            GridLabels = PlanView.GridLabels
                .Select(l =>
                {
                    var (x, y) = data.AbsoluteMmToStored(l.GridXmm, l.GridYmm);
                    var at = Svg(x, y);
                    return new PlanGridLabel(l.Text, at.X, at.Y);
                })
                .ToList(),

            Nodes = data.CharpentePoints()
                .Select(p =>
                {
                    var at = Svg(p.X, p.Y);
                    return new PlanNode(p.Key, at.X, at.Y);
                })
                .ToList(),

            Palonniers = Segments(data.Palonniers, "Palonnier"),
            MotherGrids = Segments(data.MotherGrids, "MotherGrid"),

            HangingMarks = (hangingMarks ?? Enumerable.Empty<PlanHangingMark>())
                .Select(m =>
                {
                    var at = Svg(m.X, m.Y);
                    return new PlanMark(at.X, at.Y, m.Hooked, m.Title);
                })
                .ToList(),

            Intersections = data.IntersectionPoints
                .SelectMany(kvp => kvp.Value)
                .Select(p =>
                {
                    // Where the calculation put load into a mother: a palonnier's hanging point, or a motor hung
                    // straight on the mother.
                    var at = Svg(p.X, p.Y);
                    return new PlanDot(at.X, at.Y, $"Charge transmise à la mother : {StudyPair(p.X, p.Y)} m");
                })
                .ToList(),

            CharpenteLoads = data.CharpenteLoads
                .Select(load =>
                {
                    if (!data.TryCharpentePoint(load.Key, out var x, out var y))
                        return null;
                    var at = Svg(x, y);
                    var (color, flagged) = CharpenteLoadStyle(data, load.Key, load.Value);
                    return new PlanLoad(load.Key, at.X, at.Y, color, load.Value.ToString("F1"), $"Point {load.Key}",
                        CharpenteLoadTip(data, load.Key, load.Value), Overloaded: flagged);
                })
                .OfType<PlanLoad>()
                .ToList(),

            ScenicLoads = data.ChargesSceniques
                .Where(load => load.X is not null && load.Y is not null && load.Charge is not null)
                .Select(load =>
                {
                    double x = load.X!.Value, y = load.Y!.Value, kg = load.Charge!.Value;
                    var at = Svg(x, y);
                    return new PlanLoad(load, at.X, at.Y, LoadColor(kg), kg.ToString("F1"),
                        load.Name ?? "Charge", $"{ChargeLine(kg)}\nPosition : {StudyPair(x, y)} m");
                })
                .ToList(),

            CarriedLoads = data.PalonnierLoads
                .Select(charge =>
                {
                    if (!data.TryCarriedPosition(charge, out var carrier, out var x, out var y))
                        return null;
                    var kg = charge.Charge ?? 0;
                    var at = Svg(x, y);
                    return new PlanLoad(charge, at.X, at.Y, LoadColor(kg), kg.ToString("F1"),
                        charge.Name ?? "Charge", CarriedChargeTip(data, kg, carrier, charge.X ?? 0, x, y));
                })
                .OfType<PlanLoad>()
                .ToList(),

            Sensors = data.Sensors
                .Select(sensor =>
                {
                    if (!data.TrySensorPosition(sensor, out var x, out var y))
                        return null;
                    var at = Svg(x, y);
                    var ratio = data.RatioOf(sensor);
                    var ratioText = ratio is { } r ? $" — Charge: {r * 100:F0}%" : "";
                    return new PlanSensor(sensor, sensor.Name, at.X, at.Y, ratio, PlanData.StatutOf(sensor),
                        $"{sensor.Name}{ratioText}");
                })
                .OfType<PlanSensor>()
                .ToList(),
        };

        List<PlanSegment> Segments(IEnumerable<MotherGridPalonnier> elements, string defaultName) =>
            elements
                .Select(e =>
                {
                    if (!PlanData.TryEnds(e, out var ends)) return null;
                    var a = Svg(ends.X1, ends.Y1);
                    var b = Svg(ends.X2, ends.Y2);
                    return new PlanSegment(e, a.X, a.Y, b.X, b.Y, $"{e.Name ?? defaultName} — {Ends(ends)}");
                })
                .OfType<PlanSegment>()
                .ToList();
    }

    // ── Colours ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A scenic or carried load's colour, by its weight in kilograms.</summary>
    public static string LoadColor(double kg) => kg > 500 ? "darkred" : kg > 200 ? "darkorange" : "darkgreen";

    /// <summary>
    /// A load ratio's colour: green (0 %) → yellow (50 %) → red (100 % and beyond). The scale of the 3D model and the
    /// "Taux de travail" bars (TauxColor), unquantised.
    /// </summary>
    public static string RatioColor(double ratio)
    {
        double t = Math.Clamp(ratio, 0.0, 1.0);
        int r, g;
        if (t < 0.5)
        {
            r = (int)(255 * (t / 0.5));
            g = 255;
        }
        else
        {
            r = 255;
            g = (int)(255 * (1 - (t - 0.5) / 0.5));
        }
        return $"rgb({r},{g},0)";
    }

    /// <summary>
    /// A point with no capacity on record, or a sensor with no charge ratio: grey, because "unknown" is not "lightly
    /// loaded" (as on the Charpente page).
    /// </summary>
    public const string NoCapacityColor = "#9e9e9e";

    /// <summary>A sensor's fill by its charge ratio, on <see cref="RatioColor"/>'s scale; grey without one.</summary>
    public static string SensorFill(double? ratio) => ratio is { } value ? RatioColor(value) : NoCapacityColor;

    /// <summary>A sensor's ring: the same colour as its fill.</summary>
    public static string SensorStroke(double? ratio) => SensorFill(ratio);

    // ── Text ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A position in the stored frame printed in the studies' axes: "(12,35 ; -4,00)". ToStudy negates, so a point on
    /// beam E or line 5 comes out as -0, which prints "-0,00", as does -0,004: the minus of a zero is dropped.
    /// (Rounding the double first instead would print 1,005 m as "1,00".)
    /// </summary>
    public static string StudyPair(double gridXm, double gridYm)
    {
        var (x, y) = StudyAxes.ToStudy(gridXm, gridYm);

        static string M(double v)
        {
            var text = v.ToString("0.00", Fr);
            var zero = 0.0.ToString("0.00", Fr);
            return text == Fr.NumberFormat.NegativeSign + zero ? zero : text;
        }
        return $"({M(x)} ; {M(y)})";
    }

    /// <summary>An element's ends in the studies' axes: "(x ; y) → (x ; y) m".</summary>
    public static string Ends((double X1, double Y1, double X2, double Y2) ends) =>
        $"{StudyPair(ends.X1, ends.Y1)} → {StudyPair(ends.X2, ends.Y2)} m";

    /// <summary>A load's weight as its tooltip prints it: "Charge : 1 234,50 kg".</summary>
    public static string ChargeLine(double kg) => $"Charge : {Kg(kg)}";

    private static string Kg(double kg) => $"{kg.ToString("#,##0.00", Fr)} kg";

    /// <summary>
    /// A charpente point being lifted (a negative reaction): blue, off the green-to-red scale of loads pressing down, and
    /// darker than the palonnier lines (#4488ff) and the grid labels' rings (#1e88e5).
    /// </summary>
    public const string UpliftColor = "#1565c0";

    /// <summary>
    /// A charpente point's colour and whether it is flagged (the thick dark ring):
    /// - lifted (a negative load): blue, flagged - a hanging point cannot hold a point being pulled up;
    /// - a capacity on record: the load ratio on RatioColor's scale, flagged past 100 %;
    /// - a capacity of 0 on record (J2, J4, J6, J8 take nothing in the studies): grey, flagged when anything hangs there;
    /// - no capacity: grey.
    /// A point is also flagged when its beam's sum passes the beam's own total (row 10), which is lower than the sum of
    /// the point capacities and usually the limit that binds.
    /// </summary>
    public static (string Color, bool Flagged) CharpenteLoadStyle(PlanData data, string point, double kg)
    {
        bool beamOver = BeamRatio(data, point) > 1;
        if (kg < 0)
            return (UpliftColor, true);
        if (data.CapacityOf(point) is { } capacity)
            return (RatioColor(kg / capacity), kg / capacity > 1 || beamOver);
        if (data.CharpenteCapacities.TryGetValue(point, out var recorded) && recorded == 0)
            return (NoCapacityColor, kg > 0 || beamOver);
        return (NoCapacityColor, beamOver);
    }

    /// <summary>
    /// A charpente point's tooltip: its load, its capacity and how much of it is used, then its beam's - the sum of the
    /// beam's points against the beam's own total from row 10 of the load book (as the Charpente page's Total row).
    /// </summary>
    public static string CharpenteLoadTip(PlanData data, string point, double kg)
    {
        string pointLine;
        if (data.CapacityOf(point) is { } capacity)
            pointLine = kg < 0
                ? $"Capacité du point : {Kg(capacity)}"   // a share of the capacity means nothing for a point being lifted
                : $"Capacité du point : {Kg(capacity)} — {(kg / capacity).ToString("P0", Fr)}";
        else if (data.CharpenteCapacities.TryGetValue(point, out var recorded) && recorded == 0)
            pointLine = "Capacité du point : 0 kg — aucune charge admise, ou non renseignée";
        else
            pointLine = "Capacité du point : non renseignée";

        var beam = point[..1];
        double beamKg = BeamLoad(data, beam);
        var beamLine = data.CapacityOf(beam + "10") is { } beamCapacity
            ? $"Poutre {beam} : {Kg(beamKg)} sur {Kg(beamCapacity)} — {(beamKg / beamCapacity).ToString("P0", Fr)}"
            : $"Poutre {beam} : {Kg(beamKg)}, capacité non renseignée";

        var tip = $"{ChargeLine(kg)}\n{pointLine}\n{beamLine}";
        return kg < 0 ? tip + "\nSoulèvement : ce point est tiré vers le haut" : tip;
    }

    // The sum of a beam's loads over its points 1 to 9 (row 10 is the beam's total, not a point).
    private static double BeamLoad(PlanData data, string beam) =>
        data.CharpenteLoads
            .Where(l => l.Key.Length >= 2 && l.Key[..1] == beam && int.TryParse(l.Key[1..], out var row) && row is >= 1 and <= 9)
            .Sum(l => l.Value);

    // The beam's sum over its own total, when that total is on record.
    private static double? BeamRatio(PlanData data, string point)
    {
        var beam = point[..1];
        return data.CapacityOf(beam + "10") is { } capacity ? BeamLoad(data, beam) / capacity : null;
    }

    /// <summary>
    /// A load on a palonnier, or on a mother it hangs from directly: what carries it, how far along from the carrier's
    /// first end (ChargeMp.X), and where it is.
    /// </summary>
    public static string CarriedChargeTip(PlanData data, double kg, MotherGridPalonnier carrier, double alongM, double gridXm, double gridYm)
    {
        var kind = data.IsPalonnier(carrier) ? "Palonnier" : "Mother";
        var from = carrier.X1 is { } x1 && carrier.Y1 is { } y1 ? $" de l'extrémité {StudyPair(x1, y1)}" : "";
        return $"{ChargeLine(kg)}\n{kind} : {carrier.Name ?? "—"}\nÀ {alongM.ToString("0.00", Fr)} m{from}\n"
             + $"Position : {StudyPair(gridXm, gridYm)} m";
    }
}
