using System.Globalization;
using System.Text;
using AliantWebS.Models;
using AliantWebS.Plan;
using Helpers;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Units;
using Xunit;

namespace Shared.Tests.Plan;

/// <summary>
/// The DXF is the plan in the studies' axes, in metres from Point0, one layer per kind of thing.
///
/// CAD users measure on this file and hand it on, so its numbers are the studies' own: X along the beams towards
/// point 9, Y across them towards J, a stored position (x, y) written as (-y, -x), a sensor's absolute millimetres
/// taken from Point0 first. Each layer the dialog unticks must be empty, the picture must land on the charpente at
/// its true size, turned a quarter as the file's axes require, and the drawing data must say which origin was used
/// and when the file was made. Every test reads the file back with netDxf, as CAD would read it, rather than
/// looking for strings in the text.
/// </summary>
public class PlanDxfExporterTests
{
    private const double P0X = 33.103, P0Y = 0;   // E5
    private const double Tolerance = 1e-9;
    private static readonly DateTime Exported = new(2026, 9, 17, 8, 5, 42);

    private static readonly string[] Layers =
        { "BACKGROUND", "MOTHERGRID", "PALONNIERS", "CHARGES", "PAL_LOADS", "INTERSECTIONS", "SENSORS", "ORIGIN", "DRAWING_DATA" };

    private const string AxesTail = ", X le long des poutres vers le point 9, Y en travers des poutres vers la poutre J";

    // ── A small plan at Antarès ──────────────────────────────────────────────────────────────

    private sealed class Venue
    {
        // Stored frame: the grid's axes, metres from E5.
        public readonly MotherGridPalonnier Mother = El("S500 L=14,4m #1", true, -14.406, 7.4, 0, 7.4);
        public readonly MotherGridPalonnier Palonnier = El("P300 L=8,0m #1", false, -10.8, 7.4, -10.8, 0);
        public readonly MotherGridPalonnier HalfDrawn = El("sans X2", true, 7.144, 3.7, null, 3.7);

        public PlanData Data(double point0X = P0X, double point0Y = P0Y) => new()
        {
            Point0X = point0X, Point0Y = point0Y,
            MotherGrids = new[] { Mother, HalfDrawn },
            Palonniers = new[] { Palonnier },
            IntersectionPoints = new Dictionary<Guid, List<(double X, double Y)>> { [Mother.MotherGridPalonnierId] = new() { (-10.8, 7.4) } },
            ChargesSceniques = new[]
            {
                new ChargesScenique { ChargesSceniqueId = Guid.NewGuid(), Name = "T1", X = -10.8, Y = 4.4, Charge = 450 },
                new ChargesScenique { ChargesSceniqueId = Guid.NewGuid(), Name = "sans charge", X = 1, Y = 1, Charge = null },
            },
            PalonnierLoads = new[]
            {
                Load("L1", Palonnier, 2.5, 300),
                Load("L2 sur mother", Mother, 5.0, 250),
                Load("L3 poids inconnu", Palonnier, 6.0, null),
                new ChargeMp { ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = Guid.NewGuid(), Name = "sans porteur", X = 1, Charge = 100 },
            },
            Sensors = new[]
            {
                Sensor("Manille_F6", "25947", "-3700"),
                Sensor("virgule", "33103,5", "0"),
            },
        };

        private static MotherGridPalonnier El(string name, bool mother, double? x1, double? y1, double? x2, double? y2) => new()
        {
            MotherGridPalonnierId = Guid.NewGuid(), Famille = mother, Name = name, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
        };

        private static ChargeMp Load(string name, MotherGridPalonnier carrier, double x, double? kg) => new()
        {
            ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = carrier.MotherGridPalonnierId, Name = name, X = x, Charge = kg,
        };

        private static Element Sensor(string name, string x, string y)
        {
            var sensor = new Element { ElementId = Guid.NewGuid(), Name = name };
            sensor.AttributeElements.Add(new AttributeElement { AttributeType = new AttributeType { Name = "X" }, Value = x });
            sensor.AttributeElements.Add(new AttributeElement { AttributeType = new AttributeType { Name = "Y" }, Value = y });
            return sensor;
        }
    }

    private static readonly PlanDxfInfo Info = new()
    {
        ModuleName = "MotherGrid", StructureName = "Antarès", EventName = "Concert\ndu soir",
        EventId = Guid.Parse("7e2f0c8a-1b2c-4d5e-8f90-a1b2c3d4e5f6"), ScenarioName = "Scénario 1", ScenarioId = Guid.Empty,
        ExportedAt = Exported,
    };

    private static DxfDocument Export(PlanData data, PlanDxfOptions? options = null, PlanDxfInfo? info = null)
    {
        using var culture = new Culture("");   // the loads' values are printed in the current culture
        var text = PlanDxfExporter.Write(data, options ?? new PlanDxfOptions(), info ?? Info);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfDocument.Load(stream);
    }

    private static IEnumerable<EntityObject> On(DxfDocument doc, string layer) =>
        doc.Entities.All.Where(e => e.Layer.Name == layer);

    private static List<string> DrawingData(DxfDocument doc) =>
        doc.Entities.Texts.Where(t => t.Layer.Name == "DRAWING_DATA").Select(t => t.Value).ToList();

    private static void Near(double expected, double actual, string what, double tolerance = Tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what} : attendu {expected:R}, obtenu {actual:R}");

    private static void Near((double X, double Y) expected, Vector3 actual, string what)
    {
        Near(expected.X, actual.X, what + " (X)");
        Near(expected.Y, actual.Y, what + " (Y)");
    }

    private sealed class Culture : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        public Culture(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }

    // ── The document ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_file_is_AutoCAD_2000_in_metres()
    {
        var text = PlanDxfExporter.Write(new Venue().Data(), new PlanDxfOptions(), Info);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var doc = DxfDocument.Load(stream);

        Assert.Contains("AC1015", text);
        Assert.Equal(DxfVersion.AutoCad2000, doc.DrawingVariables.AcadVer);
        Assert.Equal(DrawingUnits.Meters, doc.DrawingVariables.InsUnits);
    }

    [Fact]
    public void The_nine_layers_are_there_in_order_with_their_colours_whatever_is_exported()
    {
        foreach (var options in new[] { new PlanDxfOptions(), NoLayers })
        {
            var doc = Export(new Venue().Data(), options);
            var colours = new short[] { 8, 1, 5, 3, 6, 1, 4, 7, 2 };

            Assert.Equal(Layers, doc.Layers.Select(l => l.Name).Where(n => n != "0"));
            for (int i = 0; i < Layers.Length; i++)
                Assert.True(doc.Layers[Layers[i]].Color.Index == colours[i],
                    $"le calque {Layers[i]} doit avoir la couleur ACI {colours[i]}, il a {doc.Layers[Layers[i]].Color.Index}");
        }
    }

    // ── Geometry, in the studies' axes ───────────────────────────────────────────────────────

    [Fact]
    public void A_mother_and_a_palonnier_are_lines_in_the_studies_axes_named_at_their_middle()
    {
        var doc = Export(new Venue().Data());

        // Stored (x, y) is (-y, -x) in the studies' axes: the mother on line 3 from G to E runs along X = -7,4.
        var mother = Assert.Single(doc.Entities.Lines, l => l.Layer.Name == "MOTHERGRID");
        Near((-7.4, 14.406), mother.StartPoint, "début de la mother");
        Near((-7.4, 0), mother.EndPoint, "fin de la mother");
        var motherName = Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "MOTHERGRID");
        Assert.Equal("S500 L=14,4m #1", motherName.Value);
        Near((-7.4, 7.203), motherName.Position, "nom de la mother");
        Near(0.15, motherName.Height, "hauteur du nom");

        // The palonnier along beam x = -10,8, from line 3 to line 5.
        var palonnier = Assert.Single(doc.Entities.Lines, l => l.Layer.Name == "PALONNIERS");
        Near((-7.4, 10.8), palonnier.StartPoint, "début du palonnier");
        Near((0, 10.8), palonnier.EndPoint, "fin du palonnier");
        Near((-3.7, 10.8), Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "PALONNIERS").Position, "nom du palonnier");
    }

    [Fact]
    public void A_scenic_load_is_a_circle_with_its_name_and_weight_beside_it()
    {
        var doc = Export(new Venue().Data());

        var circle = Assert.Single(doc.Entities.Circles, c => c.Layer.Name == "CHARGES");
        Near((-4.4, 10.8), circle.Center, "charge T1");
        Near(0.1, circle.Radius, "rayon de la charge");

        var label = Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "CHARGES");
        Assert.Equal("T1: 450.0", label.Value);
        Near((-4.25, 10.8), label.Position, "étiquette de T1");
        Near(0.12, label.Height, "hauteur de l'étiquette");
    }

    [Fact]
    public void A_palonnier_load_is_at_its_distance_along_the_palonnier()
    {
        var doc = Export(new Venue().Data());

        // 2,5 m from (-10,8 ; 7,4) towards (-10,8 ; 0) is stored (-10,8 ; 4,9): (-4,9 ; 10,8) in the studies' axes.
        var circles = doc.Entities.Circles.Where(c => c.Layer.Name == "PAL_LOADS").ToList();
        var texts = doc.Entities.Texts.Where(t => t.Layer.Name == "PAL_LOADS").ToList();
        Assert.Equal(3, circles.Count);
        Near((-4.9, 10.8), circles[0].Center, "charge L1");
        Near(0.08, circles[0].Radius, "rayon de la charge portée");
        Assert.Equal("L1: 300.0", texts[0].Value);
        Near((-4.78, 10.8), texts[0].Position, "étiquette de L1");
        Near(0.1, texts[0].Height, "hauteur de l'étiquette");
    }

    [Fact]
    public void A_load_hung_straight_on_a_mother_is_along_the_mother()
    {
        var doc = Export(new Venue().Data());

        // 5 m from (-14,406 ; 7,4) towards (0 ; 7,4) is stored (-9,406 ; 7,4): (-7,4 ; 9,406).
        var circles = doc.Entities.Circles.Where(c => c.Layer.Name == "PAL_LOADS").ToList();
        Near((-7.4, 9.406), circles[1].Center, "charge L2 sur la mother");
        Assert.Equal("L2 sur mother: 250.0", doc.Entities.Texts.Where(t => t.Layer.Name == "PAL_LOADS").ElementAt(1).Value);
    }

    [Fact]
    public void A_load_of_unknown_weight_is_placed_with_nothing_after_its_name()
    {
        var doc = Export(new Venue().Data());

        var texts = doc.Entities.Texts.Where(t => t.Layer.Name == "PAL_LOADS").Select(t => t.Value).ToList();
        Assert.Equal("L3 poids inconnu:", texts[2].TrimEnd());
    }

    [Fact]
    public void The_weights_are_printed_in_the_current_culture_as_the_page_printed_them()
    {
        string French;
        using (new Culture("fr-FR"))
            French = PlanDxfExporter.Write(new Venue().Data(), new PlanDxfOptions(), Info);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(French));
        var doc = DxfDocument.Load(stream);

        Assert.Equal("T1: 450,0", Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "CHARGES").Value);
    }

    [Fact]
    public void A_sensor_is_placed_from_its_absolute_millimetres_with_Point0_at_E5()
    {
        var doc = Export(new Venue().Data());

        // F6 is (25 947 ; -3 700) mm: stored (-7,156 ; -3,7) from E5, (3,7 ; 7,156) in the studies' axes.
        var circle = Assert.Single(doc.Entities.Circles, c => c.Layer.Name == "SENSORS");
        Near((3.7, 7.156), circle.Center, "capteur F6");
        Near(0.1, circle.Radius, "rayon du capteur");
        var label = Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "SENSORS");
        Assert.Equal("Manille_F6", label.Value);
        Near((3.85, 7.156), label.Position, "nom du capteur");
    }

    [Fact]
    public void A_sensor_moves_with_Point0_while_stored_positions_do_not()
    {
        var atE5 = Export(new Venue().Data(P0X, P0Y));
        var atZero = Export(new Venue().Data(0, 0));

        Near((3.7, -25.947), Assert.Single(atZero.Entities.Circles, c => c.Layer.Name == "SENSORS").Center, "capteur sans Point0");
        Assert.Equal(
            atE5.Entities.Lines.Where(l => l.Layer.Name == "MOTHERGRID").Select(l => (l.StartPoint, l.EndPoint)),
            atZero.Entities.Lines.Where(l => l.Layer.Name == "MOTHERGRID").Select(l => (l.StartPoint, l.EndPoint)));
    }

    [Fact]
    public void An_intersection_is_a_circle_in_the_studies_axes()
    {
        var doc = Export(new Venue().Data());

        var circle = Assert.Single(doc.Entities.Circles, c => c.Layer.Name == "INTERSECTIONS");
        Near((-7.4, 10.8), circle.Center, "intersection");
        Near(0.12, circle.Radius, "rayon de l'intersection");
    }

    [Fact]
    public void What_has_no_position_is_left_out()
    {
        var doc = Export(new Venue().Data());

        Assert.Single(doc.Entities.Lines, l => l.Layer.Name == "MOTHERGRID");            // not the half-drawn one
        Assert.Single(doc.Entities.Circles, c => c.Layer.Name == "CHARGES");             // not the one without a weight
        Assert.Equal(3, doc.Entities.Circles.Count(c => c.Layer.Name == "PAL_LOADS"));   // not the one without a carrier
        Assert.Single(doc.Entities.Circles, c => c.Layer.Name == "SENSORS");             // not the one with a comma
    }

    [Fact]
    public void A_sensor_whose_coordinates_are_not_finite_numbers_writes_nothing()
    {
        // Such a sensor used to be written with NaN or infinite coordinates. Now it is left out, and the file is otherwise
        // exactly the file of the same plan without it - netDxf's four time stamps aside.
        static Element Sensor(string name, string x, string y)
        {
            var sensor = new Element { ElementId = Guid.NewGuid(), Name = name };
            sensor.AttributeElements.Add(new AttributeElement { AttributeType = new AttributeType { Name = "X" }, Value = x });
            sensor.AttributeElements.Add(new AttributeElement { AttributeType = new AttributeType { Name = "Y" }, Value = y });
            return sensor;
        }

        var venue = new Venue();
        var clean = venue.Data();
        var odd = new[]
        {
            Sensor("Sonde X", "NaN", "0"), Sensor("Sonde Y", "25947", "Infinity"), Sensor("Sonde XY", "-Infinity", "NaN"),
            Sensor("Sonde depassement", "1e309", "-3700"),
        };
        var withOdd = new PlanData
        {
            Point0X = clean.Point0X, Point0Y = clean.Point0Y, MotherGrids = clean.MotherGrids, Palonniers = clean.Palonniers,
            IntersectionPoints = clean.IntersectionPoints, ChargesSceniques = clean.ChargesSceniques, PalonnierLoads = clean.PalonnierLoads,
            Sensors = clean.Sensors.Take(1).Concat(odd).Concat(clean.Sensors.Skip(1)).ToList(),
        };

        string Write(PlanData data)
        {
            using var culture = new Culture("");
            return PlanDxfExporter.Write(data, new PlanDxfOptions(), Info);
        }
        var expected = Write(clean);
        var actual = Write(withOdd);

        Assert.Equal(WithoutTimeStamps(expected, out var stampsExpected), WithoutTimeStamps(actual, out var stampsActual));
        Assert.Equal(4, stampsExpected);
        Assert.Equal(4, stampsActual);
        Assert.DoesNotContain("Sonde", actual);
        Assert.DoesNotContain("NaN", actual);
        Assert.DoesNotContain("∞", actual);
        Assert.DoesNotContain("Infinity", actual);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(actual));
        var doc = DxfDocument.Load(stream);
        Assert.Equal("Manille_F6", Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "SENSORS").Value);
        Assert.Single(doc.Entities.Circles, c => c.Layer.Name == "SENSORS");
    }

    /// <summary>The file with the values of netDxf's $TDCREATE, $TDUCREATE, $TDUPDATE and $TDUUPDATE blanked: the time it was written.</summary>
    private static string WithoutTimeStamps(string dxf, out int found)
    {
        var lines = dxf.Replace("\r\n", "\n").Split('\n');
        found = 0;
        for (int i = 0; i + 2 < lines.Length; i++)
            if (lines[i].Trim() is "$TDCREATE" or "$TDUCREATE" or "$TDUPDATE" or "$TDUUPDATE")
            {
                lines[i + 2] = "<heure>";
                found++;
            }
        return string.Join("\n", lines);
    }

    [Fact]
    public void The_origin_is_two_metre_axes_at_Point0()
    {
        var doc = Export(new Venue().Data());

        var axes = doc.Entities.Lines.Where(l => l.Layer.Name == "ORIGIN").ToList();
        Assert.Equal(2, axes.Count);
        Near((0, 0), axes[0].StartPoint, "origine de l'axe X");
        Near((2, 0), axes[0].EndPoint, "bout de l'axe X");
        Near((0, 2), axes[1].EndPoint, "bout de l'axe Y");
        Assert.Equal(new[] { "X", "Y", "(0,0)" }, doc.Entities.Texts.Where(t => t.Layer.Name == "ORIGIN").Select(t => t.Value));
    }

    // ── Options ──────────────────────────────────────────────────────────────────────────────

    private static readonly PlanDxfOptions NoLayers = new()
    {
        MotherGrids = false, Palonniers = false, Charges = false, PalonnierLoads = false,
        Intersections = false, Sensors = false, Origin = false, BackgroundImage = false,
    };

    [Theory]
    [InlineData("MOTHERGRID", "mothergrids")]
    [InlineData("PALONNIERS", "palonniers")]
    [InlineData("CHARGES", "charges scéniques")]
    [InlineData("PAL_LOADS", "charges palonniers")]
    [InlineData("INTERSECTIONS", "intersections")]
    [InlineData("SENSORS", "capteurs")]
    [InlineData("ORIGIN", "origine")]
    public void A_layer_switched_off_is_empty_and_not_listed_while_the_others_stay(string layer, string listed)
    {
        var options = new PlanDxfOptions
        {
            MotherGrids = layer != "MOTHERGRID", Palonniers = layer != "PALONNIERS", Charges = layer != "CHARGES",
            PalonnierLoads = layer != "PAL_LOADS", Intersections = layer != "INTERSECTIONS", Sensors = layer != "SENSORS",
            Origin = layer != "ORIGIN",
        };

        var all = Export(new Venue().Data());
        var doc = Export(new Venue().Data(), options);

        Assert.NotEmpty(On(all, layer));
        Assert.Empty(On(doc, layer));
        foreach (var other in Layers.Where(l => l != layer && l != "BACKGROUND"))
            Assert.True(On(doc, other).Count() == On(all, other).Count(), $"le calque {other} ne doit pas changer quand {layer} est décoché");

        var exported = Assert.Single(DrawingData(all), l => l.StartsWith("Calques exportés : "));
        Assert.Contains(listed, exported.Split(" : ")[1].Split(", "));
        var remaining = Assert.Single(DrawingData(doc), l => l.StartsWith("Calques exportés : "));
        Assert.DoesNotContain(listed, remaining.Split(" : ")[1].Split(", "));
    }

    [Fact]
    public void With_every_layer_off_only_the_drawing_data_is_written()
    {
        var doc = Export(new Venue().Data(), NoLayers);

        Assert.All(doc.Entities.All, e => Assert.Equal("DRAWING_DATA", e.Layer.Name));
        Assert.DoesNotContain(DrawingData(doc), l => l.StartsWith("Calques exportés"));
        Assert.Empty(doc.ImageDefinitions);
    }

    [Fact]
    public void The_picture_is_left_out_unless_asked_for()
    {
        var doc = Export(new Venue().Data());

        Assert.Empty(doc.Entities.Images);
        Assert.Empty(doc.ImageDefinitions);
        Assert.Empty(On(doc, "BACKGROUND"));
        Assert.DoesNotContain("fond de plan", Assert.Single(DrawingData(doc), l => l.StartsWith("Calques exportés")));
    }

    [Fact]
    public void The_picture_lies_on_the_charpente_at_its_true_size_turned_a_quarter()
    {
        var doc = Export(new Venue().Data(), new PlanDxfOptions { BackgroundImage = true });
        var (x, y, widthM, heightM) = PlanView.ImageInStudyMetres(P0X, P0Y);

        var image = Assert.Single(doc.Entities.Images);
        Assert.Equal("BACKGROUND", image.Layer.Name);
        Assert.Equal(PlanView.BackgroundImage, image.Definition.File);
        Assert.Equal(PlanView.ImageWidthPx, image.Definition.Width);
        Assert.Equal(PlanView.ImageHeightPx, image.Definition.Height);
        Near(x, image.Position.X, "coin de l'image (X)", 1e-6);
        Near(y, image.Position.Y, "coin de l'image (Y)", 1e-6);
        Near(widthM, image.Width, "largeur de l'image", 1e-6);
        Near(heightM, image.Height, "hauteur de l'image", 1e-6);
        Near(270, ((image.Rotation % 360) + 360) % 360, "rotation de l'image (un quart de tour horaire)", 1e-6);
        Assert.Contains("fond de plan", Assert.Single(DrawingData(doc), l => l.StartsWith("Calques exportés")));

        // Independently of PlanView: the picture is 0,10253 px per mm, so about 68,4 m by 36,3 m, and the corner that
        // was its lower left lies beyond line 1 (X < -15,15) and beyond beam J (Y > 33,103).
        Near(68.44, image.Width, "largeur mesurée de la salle", 0.05);
        Near(36.28, image.Height, "hauteur mesurée de la salle", 0.05);
        Near(-18.15, image.Position.X, "coin au-delà de la ligne 1", 0.05);
        Near(36.10, image.Position.Y, "coin au-delà de la poutre J", 0.05);
    }

    // ── Drawing data ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_drawing_data_names_Point0_when_it_is_set()
    {
        var lines = DrawingData(Export(new Venue().Data()));

        Assert.Equal(
            new[]
            {
                "DONNÉES DU DESSIN",
                "Module : MotherGrid",
                "Structure : Antarès",
                "Événement : Concert du soir",
                "ID événement : 7e2f0c8a-1b2c-4d5e-8f90-a1b2c3d4e5f6",
                "Scénario : Scénario 1",
                "Date d'export : 17/09/2026 08:05",
                "Unités : mètres",
                "Axes : ceux des études — origine au Point0 (33,103 ; 0,000)" + AxesTail,
                "Calques exportés : mothergrids, palonniers, charges scéniques, charges palonniers, intersections, capteurs, origine",
            },
            lines);
    }

    [Fact]
    public void The_drawing_data_says_when_no_Point0_is_set()
    {
        var lines = DrawingData(Export(new Venue().Data(0, 0), info: new PlanDxfInfo { ExportedAt = Exported }));

        Assert.Contains("Axes : ceux des études — aucun Point0 défini, origine au (0,000 ; 0,000) du plan" + AxesTail, lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Module") || l.StartsWith("Structure") || l.StartsWith("Événement")
                                          || l.StartsWith("ID ") || l.StartsWith("Scénario"));
    }

    [Fact]
    public void The_export_date_printed_is_the_one_given_to_the_minute_whatever_the_culture()
    {
        string text;
        using (new Culture("de-DE"))   // whose date separator is a dot
            text = PlanDxfExporter.Write(new Venue().Data(), NoLayers,
                new PlanDxfInfo { ExportedAt = new DateTime(1999, 12, 31, 23, 59, 59, 999) });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        Assert.Contains("Date d'export : 31/12/1999 23:59", DrawingData(DxfDocument.Load(stream)));
    }

    [Fact]
    public void Empty_names_and_empty_ids_are_not_printed()
    {
        var lines = DrawingData(Export(new Venue().Data(), NoLayers, new PlanDxfInfo
        {
            ModuleName = " ", StructureName = "", EventName = "\t", EventId = Guid.Empty, ScenarioName = null, ScenarioId = null,
            ExportedAt = Exported,
        }));

        Assert.Equal(new[] { "DONNÉES DU DESSIN", "Date d'export : 17/09/2026 08:05", "Unités : mètres" }, lines.Take(3));
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void The_drawing_data_sits_below_the_picture_s_footprint()
    {
        var doc = Export(new Venue().Data(), NoLayers);
        var (x, y, widthM, _) = PlanView.ImageInStudyMetres(P0X, P0Y);

        var texts = doc.Entities.Texts.Where(t => t.Layer.Name == "DRAWING_DATA").ToList();
        double top = y - widthM - 1.2;
        Near((x, top), texts[0].Position, "titre des données");
        Near(0.6, texts[0].Height, "hauteur du titre");
        for (int i = 1; i < texts.Count; i++)
        {
            Near((x, top - 0.55 * (i + 1)), texts[i].Position, $"ligne {i} des données");
            Near(0.35, texts[i].Height, $"hauteur de la ligne {i}");
        }

        var rule = Assert.Single(doc.Entities.Lines, l => l.Layer.Name == "DRAWING_DATA");
        Near((x, top - 0.55 + 0.15), rule.StartPoint, "début du trait");
        Near((x + 25, top - 0.55 + 0.15), rule.EndPoint, "fin du trait");
    }

    [Fact]
    public void Newlines_in_names_become_spaces()
    {
        var venue = new Venue();
        venue.Mother.Name = "Mother\r\nsur deux lignes";

        var doc = Export(venue.Data());

        Assert.Equal("Mother  sur deux lignes", Assert.Single(doc.Entities.Texts, t => t.Layer.Name == "MOTHERGRID").Value);
        Assert.Contains("Événement : Concert du soir", DrawingData(doc));
    }
}
