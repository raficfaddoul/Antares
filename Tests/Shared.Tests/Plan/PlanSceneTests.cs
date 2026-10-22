using System.Globalization;
using AliantWebS.Models;
using AliantWebS.Plan;
using Helpers;
using Xunit;

namespace Shared.Tests.Plan;

/// <summary>
/// The scene puts every item where the plan's markup used to put it, with the same words and colours.
///
/// Until 2026-09-17 SVGComponent's markup computed each position itself: an element end or a load stored in
/// metres from Point0 had Point0 added back, was scaled to absolute millimetres and went through PlanView.ToSvg; a
/// sensor went through ToSvg from its millimetres; a charpente load from CharpenteCoordinates × 1000. PlanScene now
/// does all of it before any markup. Those original formulas are copied here as references: every position must
/// agree with them to 1e-9 SVG units, every title and tooltip must read the same, word for word, and the same items
/// must be left out - a load without a position, an element without both ends, a sensor without readable
/// coordinates, a point name the survey does not know.
/// </summary>
public class PlanSceneTests
{
    private const double P0X = 33.103, P0Y = 0;   // E5
    private const double Tolerance = 1e-9;
    private const string Nnbsp = "\u202F";   // fr-FR's group separator, a narrow no-break space

    // ── The references: the component's markup before the move ───────────────────────────────

    private const double OriginalSvgPerMm = 754.0 / 68397.0;

    private static (double X, double Y) OriginalToSvg(double gridXmm, double gridYmm)
    {
        var (studyX, studyY) = StudyAxes.ToStudy(gridXmm - P0X * 1000.0, gridYmm - P0Y * 1000.0);
        return (-studyY * OriginalSvgPerMm, -studyX * OriginalSvgPerMm);
    }

    /// <summary>An element end, a scenic load, an intersection or a hanging point, as the markup placed them.</summary>
    private static (double X, double Y) OriginalStored(double x, double y) => OriginalToSvg(x * 1000 + P0X * 1000, y * 1000 + P0Y * 1000);

    /// <summary>A load on a palonnier or a mother, as the markup placed it.</summary>
    private static (double X, double Y) OriginalCarried(MotherGridPalonnier carrier, ChargeMp charge)
    {
        double x1 = (double)carrier.X1!, y1 = (double)carrier.Y1!, x2 = (double)carrier.X2!, y2 = (double)carrier.Y2!;
        double length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        double ratio = length > 0 ? (charge.X ?? 0) / length : 0;
        return OriginalStored(x1 + ratio * (x2 - x1), y1 + ratio * (y2 - y1));
    }

    /// <summary>GetSensorColor, for a sensor neither selected nor hovered.</summary>
    private static string OriginalSensorColor(double? ratioOrNone)
    {
        if (ratioOrNone is double ratio)
        {
            double t = Math.Clamp(ratio, 0.0, 1.0);
            int r, g;
            if (t < 0.5) { r = (int)(255 * (t / 0.5)); g = 255; }
            else { r = 255; g = (int)(255 * (1 - (t - 0.5) / 0.5)); }
            return $"rgb({r},{g},0)";
        }
        return "#4CAF50";
    }

    private static void At((double X, double Y) expected, double x, double y, string what) =>
        Assert.True(Math.Abs(expected.X - x) <= Tolerance && Math.Abs(expected.Y - y) <= Tolerance,
            $"{what} : attendu ({expected.X:R} ; {expected.Y:R}), obtenu ({x:R} ; {y:R})");

    private sealed class Culture : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        public Culture(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }

    // ── A plan like the venue's ──────────────────────────────────────────────────────────────

    private sealed class Venue
    {
        public MotherGridPalonnier M5 = El("M5", true, -14.406, 0, 0, 0);
        public MotherGridPalonnier Unnamed = El(null, true, 0, 3.7, 7.144, 3.7);
        public MotherGridPalonnier HalfMother = El("sans X2", true, 7.144, 7.4, null, 7.4);
        public MotherGridPalonnier P1 = El("P1", false, -10.8, 7.4, -10.8, 0);
        public MotherGridPalonnier UnnamedPalonnier = El(null, false, 3.5, 0.02, 3.5, -7.45);
        public MotherGridPalonnier HalfPalonnier = El("sans Y1", false, 2, null, 2, -3);

        public ChargesScenique T1 = Scenic("T1", -10.8, 4.4, 450);
        public ChargesScenique T2 = Scenic(null, 3.5, -3.3333, 501);
        public ChargesScenique T3 = Scenic("T3", 1.005, -1.005, 200);

        public ChargeMp L1, L2, L5, L6, L7, L8;
        public Element S1, S2;

        public PlanData Data;

        public Venue()
        {
            L1 = Carried("L1", P1, 3.0, 300);
            L2 = Carried("L2 sur mother", M5, 5.0, 600);
            L5 = Carried(null, P1, 1.0, null);
            L6 = Carried("L6", UnnamedPalonnier, 2.25, 500);
            L7 = Carried("L7 sans porteur", null, 1.0, 100);
            L8 = Carried("L8 sans X", P1, null, 100);

            S1 = Sensor("S1", ("X", "33103"), ("Y", "0"), ("Statut", "Actif"));
            S2 = Sensor("S2", ("X", "25947"), ("Y", "-3700"), ("Statut", "Panne"));

            Data = new PlanData
            {
                Point0X = P0X, Point0Y = P0Y,
                MotherGrids = new[] { M5, Unnamed, HalfMother },
                Palonniers = new[] { P1, UnnamedPalonnier, HalfPalonnier },
                IntersectionPoints = new Dictionary<Guid, List<(double X, double Y)>>
                {
                    [M5.MotherGridPalonnierId] = new() { (-10.8, 0) },
                    [Guid.NewGuid()] = new(),
                    [Unnamed.MotherGridPalonnierId] = new() { (3.5, 3.7), (-0.0, 3.7) },
                },
                CharpenteLoads = new Dictionary<string, double>
                {
                    ["E5"] = 1234.5, ["A1"] = 75, ["B2"] = 50, ["C3"] = 100.01, ["D4"] = 100, ["F6"] = 50.01, ["Z9"] = 999,
                },
                ChargesSceniques = new[]
                {
                    T1, T2, T3,
                    Scenic("sans X", null, 1, 100), Scenic("sans Y", 1, null, 100), Scenic("sans charge", 1, 1, null),
                },
                PalonnierLoads = new[] { L1, L2, L5, L6, L7, L8, Carried("sur porteur incomplet", HalfPalonnier, 1, 100) },
                Sensors = new[]
                {
                    S1, S2,
                    Sensor("virgule", ("X", "33103,5"), ("Y", "0")),
                    Sensor("sans Y", ("X", "33103")),
                    Sensor("illisible", ("X", "abc"), ("Y", "0")),
                },
                SensorChargeRatios = new Dictionary<Guid, double> { [S1.ElementId] = 0.75 },
            };
        }

        private static MotherGridPalonnier El(string? name, bool mother, double? x1, double? y1, double? x2, double? y2) => new()
        {
            MotherGridPalonnierId = Guid.NewGuid(), Famille = mother, Name = name, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
        };

        private static ChargesScenique Scenic(string? name, double? x, double? y, double? kg) => new()
        {
            ChargesSceniqueId = Guid.NewGuid(), Name = name, X = x, Y = y, Charge = kg,
        };

        private static ChargeMp Carried(string? name, MotherGridPalonnier? carrier, double? x, double? kg) => new()
        {
            ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = carrier?.MotherGridPalonnierId ?? Guid.NewGuid(),
            Name = name, X = x, Charge = kg,
        };

        private static Element Sensor(string name, params (string Type, string Value)[] attributes)
        {
            var sensor = new Element { ElementId = Guid.NewGuid(), Name = name };
            foreach (var (type, value) in attributes)
                sensor.AttributeElements.Add(new AttributeElement
                {
                    AttributeElementId = Guid.NewGuid(), Value = value,
                    AttributeType = new AttributeType { AttributeTypeId = Guid.NewGuid(), Name = type },
                });
            return sensor;
        }
    }

    // ── Positions ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mothers_and_palonniers_are_drawn_where_the_markup_drew_their_ends()
    {
        var venue = new Venue();
        var scene = PlanScene.Build(venue.Data);

        Assert.Equal(new[] { venue.P1, venue.UnnamedPalonnier }, scene.Palonniers.Select(s => s.Source));
        Assert.Equal(new[] { venue.M5, venue.Unnamed }, scene.MotherGrids.Select(s => s.Source));

        foreach (var segment in scene.Palonniers.Concat(scene.MotherGrids))
        {
            var e = segment.Source;
            At(OriginalStored((double)e.X1!, (double)e.Y1!), segment.X1, segment.Y1, $"première extrémité de {e.Name}");
            At(OriginalStored((double)e.X2!, (double)e.Y2!), segment.X2, segment.Y2, $"seconde extrémité de {e.Name}");
        }
    }

    [Fact]
    public void Loads_are_drawn_where_the_markup_drew_them()
    {
        var venue = new Venue();
        var scene = PlanScene.Build(venue.Data);

        Assert.Equal(new[] { venue.T1, venue.T2, venue.T3 }, scene.ScenicLoads.Select(l => l.Source));
        foreach (var load in scene.ScenicLoads)
        {
            var c = (ChargesScenique)load.Source;
            At(OriginalStored(c.X!.Value, c.Y!.Value), load.X, load.Y, $"charge scénique {c.Name}");
        }

        Assert.Equal(new[] { venue.L1, venue.L2, venue.L5, venue.L6 }, scene.CarriedLoads.Select(l => l.Source));
        var carriers = new[] { venue.P1, venue.M5, venue.P1, venue.UnnamedPalonnier };
        for (int i = 0; i < carriers.Length; i++)
        {
            var load = scene.CarriedLoads[i];
            At(OriginalCarried(carriers[i], (ChargeMp)load.Source), load.X, load.Y, $"charge portée {((ChargeMp)load.Source).Name}");
        }

        Assert.Equal(new[] { "E5", "A1", "B2", "C3", "D4", "F6" }, scene.CharpenteLoads.Select(l => (string)l.Source));
        foreach (var load in scene.CharpenteLoads)
        {
            var coords = CharpenteCoordinates.GetCoordinates((string)load.Source)!.Value;
            At(OriginalToSvg(coords.X * 1000, coords.Y * 1000), load.X, load.Y, $"charge charpente {load.Source}");
        }
    }

    [Fact]
    public void Sensors_intersections_nodes_and_labels_are_drawn_where_the_markup_drew_them()
    {
        var venue = new Venue();
        var scene = PlanScene.Build(venue.Data);

        Assert.Equal(new[] { venue.S1, venue.S2 }, scene.Sensors.Select(s => s.Source));
        At(OriginalToSvg(33103, 0), scene.Sensors[0].X, scene.Sensors[0].Y, "capteur S1");
        At(OriginalToSvg(25947, -3700), scene.Sensors[1].X, scene.Sensors[1].Y, "capteur S2");

        var points = venue.Data.IntersectionPoints.SelectMany(kvp => kvp.Value).ToList();
        Assert.Equal(points.Count, scene.Intersections.Count);
        for (int i = 0; i < points.Count; i++)
            At(OriginalStored(points[i].X, points[i].Y), scene.Intersections[i].X, scene.Intersections[i].Y, $"intersection {i}");

        Assert.Equal(CharpenteCoordinates.Points.Keys, scene.Nodes.Select(n => n.Key));
        foreach (var node in scene.Nodes)
        {
            var p = CharpenteCoordinates.Points[node.Key];
            At(OriginalToSvg(p.X * 1000, p.Y * 1000), node.X, node.Y, $"point de charpente {node.Key}");
        }

        Assert.Equal(PlanView.GridLabels.Select(l => l.Text), scene.GridLabels.Select(l => l.Text));
        for (int i = 0; i < PlanView.GridLabels.Count; i++)
        {
            var label = PlanView.GridLabels[i];
            At(OriginalToSvg(label.GridXmm, label.GridYmm), scene.GridLabels[i].X, scene.GridLabels[i].Y, $"repère {label.Text}");
        }

        Assert.Equal(PlanView.Extent(P0X, P0Y), scene.Extent);
        Assert.Equal(PlanView.ImageTransform(P0X, P0Y), scene.ImageTransform);
        Assert.Equal(PlanView.SvgToStudyMatrix(), scene.SvgToStudy);
        Assert.True(Math.Abs(scene.SnapDistance - 1000 * OriginalSvgPerMm) <= Tolerance,
            "l'aimantation porte à 1 m, comme les 1 000 mm du composant");
    }

    [Fact]
    public void Hanging_marks_are_drawn_where_the_markup_drew_them_with_their_titles()
    {
        var venue = new Venue();
        var marks = new[]
        {
            new PlanHangingMark(-10.8, 7.4, true, "Accroché à M3 — accroché par défaut"),
            new PlanHangingMark(-10.8, 0, false, "Non accroché à M5 — décroché à la main"),
        };

        var scene = PlanScene.Build(venue.Data, marks);

        Assert.Equal(2, scene.HangingMarks.Count);
        for (int i = 0; i < marks.Length; i++)
        {
            At(OriginalStored(marks[i].X, marks[i].Y), scene.HangingMarks[i].X, scene.HangingMarks[i].Y, $"marque {i}");
            Assert.Equal(marks[i].Hooked, scene.HangingMarks[i].Hooked);
            Assert.Equal(marks[i].Title, scene.HangingMarks[i].Title);
        }

        Assert.Empty(PlanScene.Build(venue.Data).HangingMarks);
    }

    // ── Words ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Elements_and_intersections_are_titled_with_their_positions_in_the_studies_axes()
    {
        var scene = PlanScene.Build(new Venue().Data);

        Assert.Equal(
            new[] { "P1 — (-7,40 ; 10,80) → (0,00 ; 10,80) m", "Palonnier — (-0,02 ; -3,50) → (7,45 ; -3,50) m" },
            scene.Palonniers.Select(s => s.Title));
        Assert.Equal(
            new[] { "M5 — (0,00 ; 14,41) → (0,00 ; 0,00) m", "MotherGrid — (-3,70 ; 0,00) → (-3,70 ; -7,14) m" },
            scene.MotherGrids.Select(s => s.Title));
        // Since 2026-09-17 (round 2) the dots are named for what they are: where the calculation put load into a mother - a
        // palonnier's hanging point, or a motor hung straight on the mother - no longer "Intersection".
        Assert.Equal(
            new[]
            {
                "Charge transmise à la mother : (0,00 ; 10,80) m",
                "Charge transmise à la mother : (-3,70 ; -3,50) m",
                "Charge transmise à la mother : (-3,70 ; 0,00) m",
            },
            scene.Intersections.Select(d => d.Title));
        Assert.DoesNotContain(scene.Intersections, d => d.Title.Contains("Intersection"));
    }

    [Fact]
    public void Loads_carry_the_tooltips_the_markup_gave_them()
    {
        using var culture = new Culture("");   // the invariant culture, for the value printed beside the symbol
        var scene = PlanScene.Build(new Venue().Data);

        // Since 2026-09-17 a charpente point's tooltip also gives its capacity and its beam's; this venue has no load book.
        var e5 = scene.CharpenteLoads[0];
        Assert.Equal(("Point E5",
                $"Charge : 1{Nnbsp}234,50 kg\nCapacité du point : non renseignée\nPoutre E : 1{Nnbsp}234,50 kg, capacité non renseignée",
                "1234.5"),
            (e5.TipTitle, e5.Tip, e5.Value));

        var t1 = scene.ScenicLoads[0];
        Assert.Equal(("T1", "Charge : 450,00 kg\nPosition : (-4,40 ; 10,80) m", "450.0"), (t1.TipTitle, t1.Tip, t1.Value));
        Assert.Equal("Charge", scene.ScenicLoads[1].TipTitle);
        Assert.Equal("Charge : 200,00 kg\nPosition : (1,01 ; -1,01) m", scene.ScenicLoads[2].Tip);

        var l1 = scene.CarriedLoads[0];
        Assert.Equal("L1", l1.TipTitle);
        Assert.Equal("Charge : 300,00 kg\nPalonnier : P1\nÀ 3,00 m de l'extrémité (-7,40 ; 10,80)\nPosition : (-4,40 ; 10,80) m", l1.Tip);

        var l2 = scene.CarriedLoads[1];
        Assert.Equal("Charge : 600,00 kg\nMother : M5\nÀ 5,00 m de l'extrémité (0,00 ; 14,41)\nPosition : (0,00 ; 9,41) m", l2.Tip);

        var l5 = scene.CarriedLoads[2];
        Assert.Equal(("Charge", "0.0"), (l5.TipTitle, l5.Value));
        Assert.StartsWith("Charge : 0,00 kg\nPalonnier : P1\nÀ 1,00 m", l5.Tip);

        Assert.Contains("\nPalonnier : —\n", scene.CarriedLoads[3].Tip);
    }

    [Fact]
    public void The_value_beside_a_load_follows_the_current_culture_as_it_always_did()
    {
        using (new Culture("fr-FR"))
            Assert.Equal("1234,5", PlanScene.Build(new Venue().Data).CharpenteLoads[0].Value);
        using (new Culture("en-US"))
            Assert.Equal("450.0", PlanScene.Build(new Venue().Data).ScenicLoads[0].Value);
    }

    [Fact]
    public void Sensors_are_titled_with_their_ratio_when_known_and_keep_their_status()
    {
        var scene = PlanScene.Build(new Venue().Data);

        Assert.Equal("S1 — Charge: 75%", scene.Sensors[0].Title);
        Assert.Equal(("Actif", 0.75), (scene.Sensors[0].Statut, scene.Sensors[0].Ratio!.Value));
        Assert.Equal("S2", scene.Sensors[1].Title);
        Assert.Equal("Panne", scene.Sensors[1].Statut);
        Assert.Null(scene.Sensors[1].Ratio);
        Assert.Equal("S2", scene.Sensors[1].Name);
    }

    [Fact]
    public void A_study_position_never_prints_minus_zero_and_rounds_half_away_from_zero()
    {
        // StudyPair takes the stored frame and prints the studies' axes, (x, y) -> (-y, -x): a study value v is grid -v.
        static string Study(double x, double y) => PlanScene.StudyPair(-y, -x);

        Assert.Equal("(0,00 ; 0,00)", PlanScene.StudyPair(0, 0));                // ToStudy makes -0
        Assert.Equal("(0,00 ; 0,00)", PlanScene.StudyPair(-0.0, -0.0));
        Assert.Equal("(0,00 ; 0,00)", Study(-0.004, 0.004));
        Assert.Equal("(1,01 ; -0,01)", Study(1.005, -0.005));
        Assert.Equal("(-1,00 ; 12,35)", Study(-1.004, 12.345));
    }

    [Fact]
    public void A_weight_is_printed_in_French_with_a_narrow_space_between_thousands()
    {
        Assert.Equal($"Charge : 1{Nnbsp}234,50 kg", PlanScene.ChargeLine(1234.5));
        Assert.Equal($"Charge : 12{Nnbsp}345{Nnbsp}678,00 kg", PlanScene.ChargeLine(12345678));
        Assert.Equal("Charge : 0,05 kg", PlanScene.ChargeLine(0.05));
    }

    // ── Colours ──────────────────────────────────────────────────────────────────────────────

    // Until 2026-09-17 a charpente load was green up to 50 kg, orange up to 100 kg and red beyond, whatever the point could
    // carry. It is now coloured by its load over the point's capacity in the venue load book (TCharpente).

    /// <summary>
    /// TauxColor.ToColor (AliantWebS/CharpenteFEHelper.cs) - the scale of the 3D model and the "Taux de travail" bars - without
    /// its rounding to twentieths. Copied here as the reference: the plan must stay on the same scale.
    /// </summary>
    private static string TauxColorUnquantised(double taux)
    {
        double t = Math.Clamp(taux, 0.0, 1.0);
        int r, g;
        if (t < 0.5) { r = (int)(255 * (t / 0.5)); g = 255; }
        else { r = 255; g = (int)(255 * (1 - (t - 0.5) / 0.5)); }
        return $"rgb({r},{g},0)";
    }

    private static PlanData LoadBook(IDictionary<string, double> loads, IDictionary<string, double> capacities) => new()
    {
        Point0X = P0X, Point0Y = P0Y,
        CharpenteLoads = new Dictionary<string, double>(loads),
        CharpenteCapacities = new Dictionary<string, double>(capacities),
    };

    [Fact]
    public void A_charpente_load_is_coloured_by_its_load_over_the_point_s_capacity_on_the_3D_model_s_scale()
    {
        // (point, load, capacity, ratio, colour): every ratio exact in binary but 1,2 and 0,33, which are the literals' own doubles.
        var cases = new (string Point, double Kg, double Capacity, double Ratio, string Colour)[]
        {
            ("A1", 0, 1000, 0, "rgb(0,255,0)"),
            ("B2", 250, 1000, 0.25, "rgb(127,255,0)"),
            ("C3", 750, 1500, 0.5, "rgb(255,255,0)"),
            ("D4", 750, 1000, 0.75, "rgb(255,127,0)"),
            ("E5", 2500, 2500, 1, "rgb(255,0,0)"),
            ("F6", 1200, 1000, 1.2, "rgb(255,0,0)"),
            ("G7", 330, 1000, 0.33, "rgb(168,255,0)"),   // TauxColor's twentieths would give 0,35: rgb(178,255,0)
        };
        var data = LoadBook(cases.ToDictionary(c => c.Point, c => c.Kg), cases.ToDictionary(c => c.Point, c => c.Capacity));

        var scene = PlanScene.Build(data);

        Assert.Equal(cases.Select(c => c.Point), scene.CharpenteLoads.Select(l => (string)l.Source));
        foreach (var c in cases)
        {
            var load = scene.CharpenteLoads.Single(l => (string)l.Source == c.Point);
            Assert.Equal(c.Ratio, c.Kg / c.Capacity);
            Assert.Equal(c.Colour, TauxColorUnquantised(c.Ratio));
            Assert.Equal(c.Colour, PlanScene.RatioColor(c.Ratio));
            Assert.True(c.Colour == load.Color, $"{c.Point} à {c.Ratio:P0} : attendu {c.Colour}, obtenu {load.Color}");
        }
        Assert.NotEqual("rgb(168,255,0)", TauxColorQuantised(0.33));   // the reference really is unquantised

        static string TauxColorQuantised(double taux) => TauxColorUnquantised(Math.Round(Math.Clamp(taux, 0.0, 1.0) * 20) / 20.0);
    }

    [Fact]
    public void A_ratio_s_colour_follows_TauxColor_unquantised_everywhere_on_the_scale()
    {
        for (int i = -20; i <= 300; i++)
        {
            double ratio = i / 200.0;
            Assert.Equal(TauxColorUnquantised(ratio), PlanScene.RatioColor(ratio));
        }
        Assert.Equal("rgb(0,255,0)", PlanScene.RatioColor(-0.5));
        Assert.Equal("rgb(255,0,0)", PlanScene.RatioColor(12));
    }

    [Fact]
    public void A_charpente_load_without_a_capacity_on_record_is_grey_whatever_its_weight()
    {
        // Missing, 0 and negative are all grey (the Charpente page reads 0 as none), never "lightly loaded". Since round 2
        // a capacity recorded as exactly 0 is flagged when something hangs there (J2, J4, J6, J8 take nothing in the
        // studies); a negative one is no capacity at all.
        var data = LoadBook(
            new Dictionary<string, double> { ["A1"] = 5, ["B2"] = 50_000, ["C3"] = 0, ["D4"] = 120, ["E5"] = 999 },
            new Dictionary<string, double> { ["B2"] = 0, ["C3"] = -100, ["D4"] = -1, ["E10"] = 4000, ["A2"] = 1000 });

        var scene = PlanScene.Build(data);

        Assert.Equal(5, scene.CharpenteLoads.Count);
        Assert.All(scene.CharpenteLoads, l => Assert.Equal("#9e9e9e", l.Color));
        Assert.Equal(new[] { "B2" }, scene.CharpenteLoads.Where(l => l.Overloaded).Select(l => (string)l.Source));
        Assert.Equal("#9e9e9e", PlanScene.NoCapacityColor);

        // The venue of the other tests has no load book: every load is grey, from 50 kg to 1 234,5 kg.
        var venue = PlanScene.Build(new Venue().Data);
        Assert.All(venue.CharpenteLoads, l => Assert.Equal("#9e9e9e", l.Color));
        Assert.DoesNotContain(venue.CharpenteLoads, l => l.Overloaded);
    }

    [Fact]
    public void A_charpente_point_is_overloaded_only_beyond_its_capacity()
    {
        var data = LoadBook(
            new Dictionary<string, double> { ["A1"] = 1000, ["B2"] = 1000.001, ["C3"] = 1200, ["D4"] = 999.999, ["E5"] = 0 },
            new Dictionary<string, double> { ["A1"] = 1000, ["B2"] = 1000, ["C3"] = 1000, ["D4"] = 1000, ["E5"] = 1000 });

        var overloaded = PlanScene.Build(data).CharpenteLoads.ToDictionary(l => (string)l.Source, l => l.Overloaded);

        Assert.False(overloaded["A1"], "exactement à sa capacité (100 %), un point n'est pas en surcharge");
        Assert.True(overloaded["B2"], "au-delà de sa capacité, même d'un gramme, un point est en surcharge");
        Assert.True(overloaded["C3"]);
        Assert.False(overloaded["D4"]);
        Assert.False(overloaded["E5"]);
    }

    // ── A charpente point's tooltip ──────────────────────────────────────────────────────────

    [Fact]
    public void A_charpente_point_s_tooltip_gives_its_load_its_capacity_and_its_beam_s_in_French()
    {
        var data = LoadBook(
            new Dictionary<string, double> { ["E5"] = 1234.5, ["E6"] = 765.5, ["D5"] = 300, ["C3"] = 2600 },
            new Dictionary<string, double> { ["E5"] = 2000, ["E6"] = 0, ["E10"] = 4000, ["C3"] = 2000, ["C10"] = 2500, ["D10"] = 0 });

        var tips = PlanScene.Build(data).CharpenteLoads.ToDictionary(l => (string)l.Source, l => (l.TipTitle, l.Tip));

        Assert.Equal(("Point E5",
                $"Charge : 1{Nnbsp}234,50 kg\n"
                + $"Capacité du point : 2{Nnbsp}000,00 kg — 62 %\n"                               // 1 234,5 / 2 000 = 61,7 %
                + $"Poutre E : 2{Nnbsp}000,00 kg sur 4{Nnbsp}000,00 kg — 50 %"),
            tips["E5"]);
        Assert.Equal(("Point E6",
                "Charge : 765,50 kg\n"
                + "Capacité du point : 0 kg — aucune charge admise, ou non renseignée\n"             // a capacity of 0 on record
                + $"Poutre E : 2{Nnbsp}000,00 kg sur 4{Nnbsp}000,00 kg — 50 %"),
            tips["E6"]);
        Assert.Equal(("Point D5",
                "Charge : 300,00 kg\n"
                + "Capacité du point : non renseignée\n"
                + "Poutre D : 300,00 kg, capacité non renseignée"),                                   // D10 is 0
            tips["D5"]);
        Assert.Equal(("Point C3",
                $"Charge : 2{Nnbsp}600,00 kg\n"
                + $"Capacité du point : 2{Nnbsp}000,00 kg — 130 %\n"
                + $"Poutre C : 2{Nnbsp}600,00 kg sur 2{Nnbsp}500,00 kg — 104 %"),
            tips["C3"]);

        Assert.Equal(tips["E5"].Tip, PlanScene.CharpenteLoadTip(data, "E5", 1234.5));
    }

    [Fact]
    public void A_beam_s_load_is_the_sum_of_its_own_points_rows_1_to_9_only()
    {
        var loads = new Dictionary<string, double>
        {
            ["E1"] = 100, ["E5"] = 200, ["E9"] = 300,        // beam E: 600
            ["E10"] = 10_000,                                 // row 10 is the beam's total, not a point
            ["E0"] = 20_000, ["E11"] = 30_000, ["EX"] = 40_000, ["E"] = 50_000, [""] = 55_000,
            ["e5"] = 60_000,                                  // names are case-sensitive
            ["D5"] = 70_000, ["F5"] = 80_000, ["Z9"] = 90_000,
        };
        var data = LoadBook(loads, new Dictionary<string, double> { ["E10"] = 1200 });

        var scene = PlanScene.Build(data);

        Assert.Equal(new[] { "E1", "E5", "E9", "D5", "F5" }, scene.CharpenteLoads.Select(l => (string)l.Source));
        var e5 = scene.CharpenteLoads.Single(l => (string)l.Source == "E5");
        Assert.Equal($"Poutre E : 600,00 kg sur 1{Nnbsp}200,00 kg — 50 %", e5.Tip.Split('\n')[2]);
        Assert.Equal(e5.Tip.Split('\n')[2], scene.CharpenteLoads.Single(l => (string)l.Source == "E9").Tip.Split('\n')[2]);
        Assert.Equal($"Poutre D : 70{Nnbsp}000,00 kg, capacité non renseignée",
            scene.CharpenteLoads.Single(l => (string)l.Source == "D5").Tip.Split('\n')[2]);
    }

    // ── A charpente point's colour and ring (CharpenteLoadStyle, 2026-09-17 round 2) ──────────

    // The style of a point whose load is the one recorded in the data, as the scene asks for it.
    private static (string Color, bool Flagged) Style(PlanData data, string point) =>
        PlanScene.CharpenteLoadStyle(data, point, data.CharpenteLoads[point]);

    // 2026-09-18: a darker blue (was #1e88e5, the grid labels' rings), the one the report's plan (PlanDrawer) gives a point
    // being lifted.
    private const string Grey = "#9e9e9e", Blue = "#1565c0";

    [Fact]
    public void A_lifted_charpente_point_is_blue_and_flagged_whatever_its_capacity_and_its_beam()
    {
        Assert.Equal(Blue, PlanScene.UpliftColor);

        foreach (var kg in new[] { -1, -0.001, -5000, -1e-9 })
        {
            // A1 has a capacity, B2 a capacity of 0, C3 none, D4 a negative one; beam E is far from its total.
            var data = LoadBook(
                new Dictionary<string, double> { ["A1"] = kg, ["B2"] = kg, ["C3"] = kg, ["D4"] = kg, ["E5"] = kg },
                new Dictionary<string, double> { ["A1"] = 1000, ["B2"] = 0, ["D4"] = -50, ["E5"] = 10, ["E10"] = 100_000 });

            foreach (var point in new[] { "A1", "B2", "C3", "D4", "E5" })
                Assert.True((Blue, true) == Style(data, point), $"{point} à {kg} kg : attendu bleu et cerclé, obtenu {Style(data, point)}");
        }
    }

    [Fact]
    public void A_point_carrying_nothing_is_not_lifted()
    {
        // 0 kg, and -0,0, press nothing and lift nothing: the ratio's colour (0 %), no ring.
        foreach (var kg in new[] { 0.0, -0.0 })
        {
            var data = LoadBook(new Dictionary<string, double> { ["A1"] = kg, ["C3"] = kg },
                new Dictionary<string, double> { ["A1"] = 1000 });
            Assert.Equal(("rgb(0,255,0)", false), Style(data, "A1"));
            Assert.Equal(PlanScene.RatioColor(0), Style(data, "A1").Color);
            Assert.Equal((Grey, false), Style(data, "C3"));
            Assert.Equal(3, PlanScene.CharpenteLoadTip(data, "A1", kg).Split('\n').Length);
        }
    }

    [Fact]
    public void A_point_past_its_capacity_is_flagged_and_keeps_the_ratio_s_colour()
    {
        var data = LoadBook(
            new Dictionary<string, double> { ["A1"] = 1300, ["B1"] = 1000, ["C1"] = 1000.001, ["D1"] = 450 },
            new Dictionary<string, double> { ["A1"] = 1000, ["B1"] = 1000, ["C1"] = 1000, ["D1"] = 1000 });

        Assert.Equal(("rgb(255,0,0)", true), Style(data, "A1"));                     // 130 %
        Assert.Equal((PlanScene.RatioColor(1.3), true), Style(data, "A1"));
        Assert.Equal(("rgb(255,0,0)", false), Style(data, "B1"));                    // exactly 100 %: not past it
        Assert.Equal((PlanScene.RatioColor(1000.001 / 1000), true), Style(data, "C1"));
        Assert.Equal(("rgb(229,255,0)", false), Style(data, "D1"));                  // 45 %
    }

    [Fact]
    public void A_beam_past_its_own_total_flags_every_point_on_it_even_a_light_one_or_one_without_a_capacity()
    {
        // Beam E: 100 + 950 + 0 = 1 050 kg on a total of 1 000 kg, though E1 is at 10 % and E2 has no capacity.
        var loads = new Dictionary<string, double>
        {
            ["E1"] = 100, ["E2"] = 950, ["E3"] = 0,   // E1 10 %, E2 no capacity, E3 a capacity of 0 and nothing on it
            ["D1"] = 100, ["D2"] = 2000,               // beam D: 2 100 kg on a total of 5 000 kg
            ["F1"] = 500,                              // beam F: no total on record
        };
        var capacities = new Dictionary<string, double>
        {
            ["E1"] = 1000, ["E3"] = 0, ["E10"] = 1000, ["D1"] = 1000, ["D10"] = 5000,
        };

        var over = LoadBook(loads, capacities);
        Assert.Equal((PlanScene.RatioColor(0.1), true), Style(over, "E1"));     // the colour stays the point's own ratio
        Assert.Equal(("rgb(51,255,0)", true), Style(over, "E1"));
        Assert.Equal((Grey, true), Style(over, "E2"));
        Assert.Equal((Grey, true), Style(over, "E3"));
        Assert.Equal((PlanScene.RatioColor(0.1), false), Style(over, "D1"));    // other beams are not concerned
        Assert.Equal((Grey, false), Style(over, "D2"));
        Assert.Equal((Grey, false), Style(over, "F1"));

        // The scene rings the same points.
        Assert.Equal(new[] { "E1", "E2", "E3" },
            PlanScene.Build(over).CharpenteLoads.Where(l => l.Overloaded).Select(l => (string)l.Source).OrderBy(k => k));

        // Exactly at its total a beam is not past it; a gram under, it is.
        var atTotal = LoadBook(loads, new Dictionary<string, double>(capacities) { ["E10"] = 1050 });
        Assert.Equal(new[] { false, false, false }, new[] { "E1", "E2", "E3" }.Select(p => Style(atTotal, p).Flagged));
        var justUnder = LoadBook(loads, new Dictionary<string, double>(capacities) { ["E10"] = 1049.999 });
        Assert.Equal(new[] { true, true, true }, new[] { "E1", "E2", "E3" }.Select(p => Style(justUnder, p).Flagged));

        // Row 10 is the total, not a load: a load recorded under E10 does not count in the beam's sum.
        var withRow10 = LoadBook(new Dictionary<string, double>(loads) { ["E1"] = 10, ["E10"] = 50_000 }, capacities);
        Assert.Equal(new[] { false, false, false }, new[] { "E1", "E2", "E3" }.Select(p => Style(withRow10, p).Flagged));
    }

    [Fact]
    public void A_beam_total_of_zero_negative_or_missing_never_flags()
    {
        var loads = new Dictionary<string, double> { ["E1"] = 100, ["E2"] = 50_000, ["E3"] = 0 };
        foreach (var total in new double?[] { 0, -5, double.NaN, null })
        {
            var capacities = new Dictionary<string, double> { ["E1"] = 1000 };
            if (total is { } t) capacities["E10"] = t;
            var data = LoadBook(loads, capacities);

            Assert.Equal((PlanScene.RatioColor(0.1), false), Style(data, "E1"));
            Assert.Equal((Grey, false), Style(data, "E2"));
            Assert.Equal((Grey, false), Style(data, "E3"));
            Assert.Empty(PlanScene.Build(data).CharpenteLoads.Where(l => l.Overloaded));
            Assert.Equal($"Poutre E : 50{Nnbsp}100,00 kg, capacité non renseignée", PlanScene.CharpenteLoadTip(data, "E2", 50_000).Split('\n')[2]);
        }
    }

    [Fact]
    public void A_capacity_of_zero_on_record_is_grey_and_flagged_only_when_something_hangs_there()
    {
        foreach (var (kg, flagged) in new[] { (0.0, false), (0.001, true), (250.0, true), (1e-9, true) })
        {
            var data = LoadBook(new Dictionary<string, double> { ["J4"] = kg }, new Dictionary<string, double> { ["J4"] = 0 });
            Assert.True((Grey, flagged) == Style(data, "J4"), $"J4 (0 kg admis) à {kg} kg : attendu gris, cerclé {flagged}, obtenu {Style(data, "J4")}");
            Assert.Equal("Capacité du point : 0 kg — aucune charge admise, ou non renseignée",
                PlanScene.CharpenteLoadTip(data, "J4", kg).Split('\n')[1]);
        }

        // Lifted, it is blue like any lifted point.
        var lifted = LoadBook(new Dictionary<string, double> { ["J4"] = -3 }, new Dictionary<string, double> { ["J4"] = 0 });
        Assert.Equal((Blue, true), Style(lifted, "J4"));
    }

    [Fact]
    public void A_negative_or_unreadable_capacity_on_record_is_no_capacity()
    {
        var data = LoadBook(
            new Dictionary<string, double> { ["C3"] = 50, ["D4"] = 50, ["E5"] = 50 },
            new Dictionary<string, double> { ["C3"] = -100, ["D4"] = double.NaN, ["E5"] = -0.0 });

        Assert.Equal((Grey, false), Style(data, "C3"));
        Assert.Equal("Capacité du point : non renseignée", PlanScene.CharpenteLoadTip(data, "C3", 50).Split('\n')[1]);
        Assert.Equal((Grey, false), Style(data, "D4"));
        Assert.Equal("Capacité du point : non renseignée", PlanScene.CharpenteLoadTip(data, "D4", 50).Split('\n')[1]);

        // -0,0 is a zero: recorded, nothing admitted.
        Assert.Equal((Grey, true), Style(data, "E5"));
        Assert.Equal("Capacité du point : 0 kg — aucune charge admise, ou non renseignée", PlanScene.CharpenteLoadTip(data, "E5", 50).Split('\n')[1]);
    }

    [Fact]
    public void A_lifted_point_s_tooltip_ends_with_a_line_saying_it_is_pulled_up()
    {
        const string Uplift = "Soulèvement : ce point est tiré vers le haut";
        var data = LoadBook(
            new Dictionary<string, double> { ["A1"] = -100, ["J4"] = -2.5, ["C3"] = -1234.5, ["A2"] = 300, ["B2"] = 0 },
            new Dictionary<string, double> { ["A1"] = 1000, ["J4"] = 0 });

        var tips = PlanScene.Build(data).CharpenteLoads.ToDictionary(l => (string)l.Source, l => l.Tip);

        Assert.Equal(
            "Charge : -100,00 kg\n"
            + $"Capacité du point : 1{Nnbsp}000,00 kg\n"                                // no share of it: the point is lifted
            + "Poutre A : 200,00 kg, capacité non renseignée\n"                       // -100 + 300
            + Uplift,
            tips["A1"]);
        Assert.Equal(
            "Charge : -2,50 kg\n"
            + "Capacité du point : 0 kg — aucune charge admise, ou non renseignée\n"
            + "Poutre J : -2,50 kg, capacité non renseignée\n"
            + Uplift,
            tips["J4"]);
        Assert.Equal(
            $"Charge : -1{Nnbsp}234,50 kg\nCapacité du point : non renseignée\nPoutre C : -1{Nnbsp}234,50 kg, capacité non renseignée\n{Uplift}",
            tips["C3"]);

        // Pressing down, or carrying nothing: three lines, no uplift.
        Assert.Equal(3, tips["A2"].Split('\n').Length);
        Assert.Equal(3, tips["B2"].Split('\n').Length);
        Assert.DoesNotContain("Soulèvement", tips["A2"] + tips["B2"]);
        Assert.Equal(tips["A1"], PlanScene.CharpenteLoadTip(data, "A1", -100));
    }

    [Fact]
    public void A_lifted_point_s_capacity_line_gives_the_capacity_without_a_share_of_it()
    {
        // 2026-09-18: « Capacité du point : 1 000,00 kg », no percentage, however little or much it is lifted.
        foreach (var kg in new[] { -1e-9, -0.001, -100, -1000, -5000 })
        {
            var data = LoadBook(new Dictionary<string, double> { ["A1"] = kg }, new Dictionary<string, double> { ["A1"] = 1000 });
            var lines = PlanScene.CharpenteLoadTip(data, "A1", kg).Split('\n');
            Assert.Equal($"Capacité du point : 1{Nnbsp}000,00 kg", lines[1]);
            Assert.DoesNotContain("%", lines[1]);
            Assert.Equal("Soulèvement : ce point est tiré vers le haut", lines[^1]);
            Assert.Equal(PlanScene.CharpenteLoadTip(data, "A1", kg), PlanScene.Build(data).CharpenteLoads.Single().Tip);
        }

        // Pressing down, or carrying nothing, the share is still there.
        Assert.Equal($"Capacité du point : 1{Nnbsp}000,00 kg — 0 %",
            PlanScene.CharpenteLoadTip(LoadBook(new Dictionary<string, double> { ["A1"] = 0 }, new Dictionary<string, double> { ["A1"] = 1000 }), "A1", 0).Split('\n')[1]);
        Assert.Equal($"Capacité du point : 1{Nnbsp}000,00 kg — 25 %",
            PlanScene.CharpenteLoadTip(LoadBook(new Dictionary<string, double> { ["A1"] = 250 }, new Dictionary<string, double> { ["A1"] = 1000 }), "A1", 250).Split('\n')[1]);

        // A capacity of 0 or none reads as before when lifted.
        var others = LoadBook(new Dictionary<string, double> { ["J4"] = -3, ["C3"] = -3 }, new Dictionary<string, double> { ["J4"] = 0 });
        Assert.Equal("Capacité du point : 0 kg — aucune charge admise, ou non renseignée", PlanScene.CharpenteLoadTip(others, "J4", -3).Split('\n')[1]);
        Assert.Equal("Capacité du point : non renseignée", PlanScene.CharpenteLoadTip(others, "C3", -3).Split('\n')[1]);
    }

    [Fact]
    public void The_uplift_blue_is_the_report_plan_s_and_neither_the_palonnier_lines_nor_the_grid_rings()
    {
        // The report's plan (PlanDrawer) draws a point being lifted in its own UpliftColor: one blue for one meaning.
        var drawerField = typeof(RiggingOptimizer.IO.PlanDrawer).GetField("UpliftColor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(drawerField);
        Assert.Equal(SkiaSharp.SKColor.Parse(PlanScene.UpliftColor), (SkiaSharp.SKColor)drawerField!.GetValue(null)!);

        // SVGComponent's palonnier lines (#4488ff) and grid labels' rings (#1e88e5).
        Assert.NotEqual("#4488ff", PlanScene.UpliftColor, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("#1e88e5", PlanScene.UpliftColor, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_scene_colours_and_rings_every_charpente_load_as_CharpenteLoadStyle_says()
    {
        var data = LoadBook(
            new Dictionary<string, double>
            {
                ["A1"] = 1300, ["A2"] = 200, ["B4"] = 2000, ["J4"] = 250, ["J6"] = 0, ["E4"] = 585, ["E6"] = 310, ["F4"] = -40,
                ["C3"] = 70, ["Z9"] = 5,
            },
            new Dictionary<string, double>
            {
                ["A1"] = 1000, ["A2"] = 1000, ["B4"] = 2500, ["J4"] = 0, ["J6"] = 0, ["E4"] = 1000, ["E10"] = 800, ["F4"] = 500,
            });

        var scene = PlanScene.Build(data);

        Assert.Equal(new[] { "A1", "A2", "B4", "J4", "J6", "E4", "E6", "F4", "C3" }, scene.CharpenteLoads.Select(l => (string)l.Source));
        foreach (var load in scene.CharpenteLoads)
        {
            var (color, flagged) = PlanScene.CharpenteLoadStyle(data, (string)load.Source, data.CharpenteLoads[(string)load.Source]);
            Assert.Equal((color, flagged), (load.Color, load.Overloaded));
        }
        var byPoint = scene.CharpenteLoads.ToDictionary(l => (string)l.Source, l => (l.Color, l.Overloaded));
        Assert.Equal(("rgb(255,0,0)", true), byPoint["A1"]);
        Assert.Equal(("rgb(102,255,0)", false), byPoint["A2"]);
        Assert.Equal(("rgb(255,101,0)", false), byPoint["B4"]);   // 80 %
        Assert.Equal((Grey, true), byPoint["J4"]);
        Assert.Equal((Grey, false), byPoint["J6"]);
        Assert.Equal((PlanScene.RatioColor(0.585), true), byPoint["E4"]);   // beam E: 895 kg on 800 kg
        Assert.Equal((Grey, true), byPoint["E6"]);
        Assert.Equal((Blue, true), byPoint["F4"]);
        Assert.Equal((Grey, false), byPoint["C3"]);
    }

    [Fact]
    public void Scenic_and_carried_loads_turn_dark_orange_above_200_kg_and_dark_red_above_500_kg()
    {
        Assert.Equal("darkred", PlanScene.LoadColor(500.01));
        Assert.Equal("darkorange", PlanScene.LoadColor(500));
        Assert.Equal("darkorange", PlanScene.LoadColor(200.01));
        Assert.Equal("darkgreen", PlanScene.LoadColor(200));
        Assert.Equal("darkgreen", PlanScene.LoadColor(0));

        var scene = PlanScene.Build(new Venue().Data);
        Assert.Equal(new[] { "darkorange", "darkred", "darkgreen" }, scene.ScenicLoads.Select(l => l.Color));
        Assert.Equal(new[] { "darkorange", "darkred", "darkgreen", "darkorange" }, scene.CarriedLoads.Select(l => l.Color));   // L6 is 500 kg
    }

    [Fact]
    public void A_sensor_s_colour_runs_from_green_through_yellow_to_red_with_its_ratio()
    {
        var expected = new Dictionary<double, string>
        {
            [0] = "rgb(0,255,0)", [0.25] = "rgb(127,255,0)", [0.5] = "rgb(255,255,0)",
            [0.75] = "rgb(255,127,0)", [1] = "rgb(255,0,0)", [1.5] = "rgb(255,0,0)",
        };

        foreach (var (ratio, colour) in expected)
        {
            Assert.Equal(colour, PlanScene.SensorFill(ratio));
            Assert.Equal(OriginalSensorColor(ratio), PlanScene.SensorFill(ratio));
            Assert.Equal(colour, PlanScene.SensorStroke(ratio));
        }

        // Without a ratio: grey since 2026-10-22, no longer the green of ratio 0 (see the test below).
        Assert.Equal(PlanScene.NoCapacityColor, PlanScene.SensorFill(null));
        Assert.Equal(PlanScene.NoCapacityColor, PlanScene.SensorStroke(null));
    }

    [Fact]
    public void A_sensor_without_a_ratio_is_grey_like_a_point_without_capacity()
    {
        // Decision 5 of the dashboard redesign (2026-10-22): "no ratio known" is not "lightly loaded". The old green
        // #4CAF50 was nearly RatioColor(0); every plan built on PlanScene (dashboard and Surveillance) now draws it grey.
        var scene = PlanScene.Build(new Venue().Data);
        var unrated = scene.Sensors.Where(s => s.Ratio is null).ToList();
        Assert.NotEmpty(unrated);

        foreach (var sensor in unrated)
        {
            Assert.Equal("#9e9e9e", PlanScene.SensorFill(sensor.Ratio));
            Assert.Equal("#9e9e9e", PlanScene.SensorStroke(sensor.Ratio));
        }
        Assert.Equal("#9e9e9e", PlanScene.NoCapacityColor);
        Assert.NotEqual(PlanScene.RatioColor(0), PlanScene.SensorFill(null));
    }

    [Fact]
    public void A_sensor_s_colour_is_unchanged_now_it_shares_the_charpente_loads_scale()
    {
        // SensorFill became RatioColor on 2026-09-17: whatever the ratio, even an odd one, a sensor keeps the colour it had.
        var ratios = Enumerable.Range(-40, 361).Select(i => i / 200.0)
            .Concat(new[] { -0.0, 1e-12, 0.4999999, 0.5000001, 0.9999999, 1e9, double.MaxValue, double.Epsilon,
                            double.PositiveInfinity, double.NegativeInfinity, double.NaN });
        foreach (var ratio in ratios)
        {
            Assert.Equal(OriginalSensorColor(ratio), PlanScene.SensorFill(ratio));
            Assert.Equal(OriginalSensorColor(ratio), PlanScene.SensorStroke(ratio));
            Assert.Equal(PlanScene.RatioColor(ratio), PlanScene.SensorFill(ratio));
        }
    }

    // ── What is left out ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Items_without_a_position_are_left_out_as_the_markup_left_them_out()
    {
        var venue = new Venue();
        var scene = PlanScene.Build(venue.Data);

        Assert.DoesNotContain(scene.ScenicLoads, l => ((ChargesScenique)l.Source).Name?.StartsWith("sans") == true);
        Assert.DoesNotContain(scene.CarriedLoads, l => l.Source == venue.L7 || l.Source == venue.L8);
        Assert.DoesNotContain(scene.CarriedLoads, l => ((ChargeMp)l.Source).Name == "sur porteur incomplet");
        Assert.DoesNotContain(scene.MotherGrids, s => s.Source == venue.HalfMother);
        Assert.DoesNotContain(scene.Palonniers, s => s.Source == venue.HalfPalonnier);
        Assert.DoesNotContain(scene.Sensors, s => s.Name is "virgule" or "sans Y" or "illisible");
        Assert.DoesNotContain(scene.CharpenteLoads, l => (string)l.Source == "Z9");

        Assert.Equal(3, scene.ScenicLoads.Count);
        Assert.Equal(4, scene.CarriedLoads.Count);
        Assert.Equal(2, scene.Sensors.Count);
        Assert.Equal(6, scene.CharpenteLoads.Count);
    }

    [Fact]
    public void A_sensor_whose_coordinates_are_not_finite_numbers_is_not_drawn()
    {
        // .NET reads "NaN" and "Infinity" as numbers; such a sensor used to be drawn on Point0 (the browser ignores a NaN
        // translate). Now it has no position, like a sensor whose coordinates cannot be read.
        static Element NotFinite(string name, string x, string y)
        {
            var sensor = new Element { ElementId = Guid.NewGuid(), Name = name };
            foreach (var (type, value) in new[] { ("X", x), ("Y", y), ("Statut", "Actif") })
                sensor.AttributeElements.Add(new AttributeElement
                {
                    AttributeElementId = Guid.NewGuid(), Value = value,
                    AttributeType = new AttributeType { AttributeTypeId = Guid.NewGuid(), Name = type },
                });
            return sensor;
        }

        var venue = new Venue();
        var odd = new[]
        {
            NotFinite("x NaN", "NaN", "0"), NotFinite("y Infinity", "33103", "Infinity"), NotFinite("x -Infinity", "-Infinity", "0"),
            NotFinite("les deux", "NaN", "-Infinity"), NotFinite("dépassement", "1e309", "0"),
        };
        var data = new PlanData
        {
            Point0X = P0X, Point0Y = P0Y,
            Sensors = venue.Data.Sensors.Concat(odd).ToList(),
            SensorChargeRatios = new Dictionary<Guid, double>(venue.Data.SensorChargeRatios) { [odd[0].ElementId] = 0.5 },
        };

        var scene = PlanScene.Build(data);

        Assert.Equal(new[] { venue.S1, venue.S2 }, scene.Sensors.Select(s => s.Source));
        Assert.All(scene.Sensors, s => Assert.True(double.IsFinite(s.X) && double.IsFinite(s.Y)));
    }

    [Fact]
    public void An_empty_plan_draws_only_the_grid()
    {
        var scene = PlanScene.Build(new PlanData());

        Assert.Empty(scene.Palonniers);
        Assert.Empty(scene.MotherGrids);
        Assert.Empty(scene.ScenicLoads);
        Assert.Empty(scene.CarriedLoads);
        Assert.Empty(scene.CharpenteLoads);
        Assert.Empty(scene.Sensors);
        Assert.Empty(scene.Intersections);
        Assert.Equal(CharpenteCoordinates.Points.Count, scene.Nodes.Count);
        Assert.Equal(PlanView.GridLabels.Count, scene.GridLabels.Count);
    }
}
