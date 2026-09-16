using AliantWebS.Reporting;
using Xunit;

namespace Shared.Tests.Report;

/// <summary>
/// How a truss is drawn, as opposed to where it is drawn.
///
/// The studies' plans do not draw a truss as a bar: they draw the band with its two chords and the web
/// crossing between them, which is how a reader tells a truss from a hanging point's leader line at a
/// glance. The figures draw it the same way, and these are the rules that keep it a truss - the web
/// inside its band, whole panels, nothing spilling past the ends - and keep it affordable, because a
/// lattice written segment by segment would be hundreds of shapes in the Word file.
///
/// Everything here reads the SCENE. What becomes of it in the document is FigureTests' subject.
/// </summary>
public class TrussFigureTests
{
    // The same pre-rig the figure tests use: two mothers along beams E and F, two palonniers across
    // them, in the studies' axes, metres from Point0 = E5.
    private const double BeamF = 7.156, BeamE = 0, BeamD = -7.144;
    private const double Point3 = -7.4, Point4 = -3.7, Point6 = 3.7, Point7 = 7.45;

    private static readonly List<FigureTruss> PreRig = new()
    {
        new(Point3, BeamE, Point7, BeamE, "S500", null, IsMother: true, Label: "S500"),
        new(Point3, BeamF, Point7, BeamF, "S500", null, IsMother: true, Label: "S500"),
        new(Point4, BeamD, Point4, BeamF, "P300", null, IsMother: false, Label: "P300"),
        new(Point6, BeamD, Point6, BeamF, "P300", null, IsMother: false, Label: "P300"),
    };

    private static FigureScene Scene(IReadOnlyList<FigureTruss> trusses) =>
        ReportFigureDrawer.SceneOfPreRig(trusses)!;

    // ═══════════════════════════════════════════════════════════════
    // A truss, not a bar
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Every_truss_carries_a_web()
    {
        // Two zig-zags per truss - one the mirror of the other - which is what crosses them into a
        // lattice. A truss drawn without them is a coloured bar, and a bar is what a leader line is.
        var webs = Scene(PreRig).Primitives.OfType<FigurePolyline>().ToList();

        Assert.True(webs.Count >= 2 * PreRig.Count,
            $"{webs.Count} polyligne(s) pour {PreRig.Count} treillis : il en faut deux par treillis");
    }

    [Fact]
    public void The_two_zigzags_of_a_truss_are_mirrors_of_one_another()
    {
        // Antiphase is the whole trick: where one runs to the near chord the other runs to the far one,
        // so every panel is crossed. Two zig-zags in phase would draw a row of chevrons, not a truss.
        foreach (var (down, up) in Pairs(Scene(PreRig)))
        {
            Assert.True(down.Points.Count == up.Points.Count,
                $"les deux zigzags n'ont pas le même nombre de nœuds : {down.Points.Count} et {up.Points.Count}");

            for (var i = 0; i < down.Points.Count; i++)
            {
                Assert.True(Math.Abs(down.Points[i].X - up.Points[i].X) < 1e-6
                            || Math.Abs(down.Points[i].Y - up.Points[i].Y) < 1e-6,
                    $"le nœud {i} des deux zigzags n'est pas sur la même section du treillis");
                Assert.True(
                    Math.Abs(down.Points[i].X - up.Points[i].X) > 1e-9
                    || Math.Abs(down.Points[i].Y - up.Points[i].Y) > 1e-9,
                    $"le nœud {i} des deux zigzags est au même endroit : ils ne se croisent pas");
            }
        }
    }

    [Fact]
    public void The_web_never_leaves_the_band_it_is_cut_out_of()
    {
        // The web is drawn in the colour of the page. A point of it outside the coloured band is a white
        // mark on the plan, over the grid or over whatever the truss crosses.
        var scene = Scene(PreRig);

        foreach (var web in scene.Primitives.OfType<FigurePolyline>())
        {
            var band = BandOf(scene, web);
            Assert.True(band is not null, "une polyligne ne longe aucun treillis");

            var (x1, y1, x2, y2, width) = band!.Value;
            double half = width / 2;

            foreach (var (x, y) in web.Points)
            {
                double across = Math.Abs(Across(x1, y1, x2, y2, x, y));
                Assert.True(across <= half + 1e-6,
                    $"un nœud du treillis est à {across:0.###} mm de l'axe, hors d'une bande de {width:0.###} mm");

                double along = Along(x1, y1, x2, y2, x, y);
                Assert.True(along >= -1e-6 && along <= Length(x1, y1, x2, y2) + 1e-6,
                    $"un nœud du treillis dépasse l'extrémité de la poutre de {along:0.###} mm");
            }
        }
    }

    [Fact]
    public void The_web_stops_short_of_both_ends()
    {
        // Half a band of solid colour at each end, which is the round cap of the bar underneath: it
        // reads as the end plate a truss really has, and it keeps the web off the hanging point drawn
        // on top of it.
        var scene = Scene(PreRig);

        foreach (var web in scene.Primitives.OfType<FigurePolyline>())
        {
            var (x1, y1, x2, y2, width) = BandOf(scene, web)!.Value;
            double length = Length(x1, y1, x2, y2);

            double first = Along(x1, y1, x2, y2, web.Points[0].X, web.Points[0].Y);
            double last = Along(x1, y1, x2, y2, web.Points[^1].X, web.Points[^1].Y);

            Assert.True(Math.Abs(first - width / 2) < 1e-3,
                $"le treillis commence à {first:0.###} mm de l'extrémité au lieu de {width / 2:0.###}");
            Assert.True(Math.Abs((length - last) - width / 2) < 1e-3,
                $"le treillis s'arrête à {length - last:0.###} mm de l'autre extrémité au lieu de {width / 2:0.###}");
        }
    }

    [Fact]
    public void Every_panel_is_a_whole_one()
    {
        // The pitch is stretched to fit rather than clipped. A pattern cut in the middle of a panel is
        // what makes a drawn truss look like a texture laid over a bar instead of a structure.
        var scene = Scene(PreRig);

        foreach (var web in scene.Primitives.OfType<FigurePolyline>())
        {
            var (x1, y1, x2, y2, _) = BandOf(scene, web)!.Value;

            var pitches = web.Points
                .Zip(web.Points.Skip(1), (a, b) =>
                    Along(x1, y1, x2, y2, b.X, b.Y) - Along(x1, y1, x2, y2, a.X, a.Y))
                .ToList();

            Assert.True(pitches.Count > 0, "un treillis sans panneau");
            Assert.True(pitches.Max() - pitches.Min() < 1e-6,
                $"les panneaux ne font pas tous la même longueur : de {pitches.Min():0.###} à {pitches.Max():0.###} mm");
        }
    }

    [Fact]
    public void A_panel_is_about_as_long_as_the_band_is_wide()
    {
        // The proportion is what reads as a lattice. Much shorter and the web closes into a hatch; much
        // longer and it stretches into a row of lozenges.
        var scene = Scene(PreRig);

        foreach (var web in scene.Primitives.OfType<FigurePolyline>())
        {
            var (x1, y1, x2, y2, width) = BandOf(scene, web)!.Value;

            double pitch = Along(x1, y1, x2, y2, web.Points[1].X, web.Points[1].Y)
                         - Along(x1, y1, x2, y2, web.Points[0].X, web.Points[0].Y);

            Assert.True(pitch > 0.6 * width && pitch < 2.0 * width,
                $"un panneau de {pitch:0.###} mm pour une bande de {width:0.###} mm");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // What is too short to be drawn as a truss
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_truss_too_short_for_one_panel_is_drawn_as_a_plain_bar()
    {
        // A palonnier of a few centimetres, or a long one on a figure framed on the whole hall, comes
        // out shorter on paper than a single panel. Half a panel drawn anyway would spill past the ends
        // - white marks on either side of a bar - so nothing is drawn.
        var withStub = new List<FigureTruss>(PreRig)
        {
            new(Point4, BeamE, Point4 + 0.12, BeamE, "P300", null, IsMother: false, Label: "P300"),
        };

        var before = Scene(PreRig).Primitives.OfType<FigurePolyline>().Count();
        var after = Scene(withStub).Primitives.OfType<FigurePolyline>().Count();

        Assert.True(after == before,
            $"{after - before} polyligne(s) de plus : un tronçon trop court a été treillissé quand même");
    }

    // ═══════════════════════════════════════════════════════════════
    // The legend
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void The_legend_shows_a_length_of_truss_for_every_family()
    {
        // The key has to be what is drawn in the plan, web and all: a plain coloured square would leave
        // the reader matching a shade instead of a truss, and a key too short for two panels would show
        // the one thing a truss must not be taken for - a bar.
        var scene = Scene(PreRig);

        // Two families here, two keys, so four zig-zags beyond the trusses' own eight.
        int families = PreRig.Select(t => t.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int webs = scene.Primitives.OfType<FigurePolyline>().Count();

        Assert.True(webs == 2 * PreRig.Count + 2 * families,
            $"{webs} polyligne(s) : attendu {2 * PreRig.Count} pour les treillis et {2 * families} pour la légende");
    }

    // ═══════════════════════════════════════════════════════════════
    // The cost
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_lattice_of_forty_panels_is_still_two_shapes()
    {
        // The reason the web is a polyline at all. A mother line across the hall carries some forty
        // panels; segment by segment that is a hundred and sixty shapes for one truss, in a document
        // that holds a dozen of them and has to open on a laptop.
        var scene = Scene(PreRig);
        var longest = scene.Primitives.OfType<FigurePolyline>()
            .OrderByDescending(w => w.Points.Count)
            .First();

        Assert.True(longest.Points.Count >= 8,
            $"le plus long treillis n'a que {longest.Points.Count} nœuds : l'échantillon ne prouve rien");

        // One shape per zig-zag, whatever the number of panels: two per band drawn - the trusses and
        // the legend's keys - and the count does not follow the number of nodes.
        var webs = scene.Primitives.OfType<FigurePolyline>().ToList();
        int bands = PreRig.Count
                  + PreRig.Select(t => t.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        Assert.Equal(2 * bands, webs.Count);
        Assert.True(webs.Sum(w => w.Points.Count) > 4 * webs.Count,
            "les polylignes n'ont presque pas de nœuds : la règle ne prouve rien");
    }

    // ── Reading the scene ─────────────────────────────────────────

    /// <summary>The zig-zags two by two, in the order they were drawn: one truss, one pair.</summary>
    private static IEnumerable<(FigurePolyline Down, FigurePolyline Up)> Pairs(FigureScene scene)
    {
        var webs = scene.Primitives.OfType<FigurePolyline>().ToList();
        for (var i = 0; i + 1 < webs.Count; i += 2) yield return (webs[i], webs[i + 1]);
    }

    /// <summary>
    /// The coloured band a web belongs to: the line of the scene whose axis every one of the web's nodes
    /// sits on. Found rather than assumed, so the rules do not depend on the order things are drawn in.
    /// </summary>
    private static (double X1, double Y1, double X2, double Y2, double Width)? BandOf(
        FigureScene scene, FigurePolyline web)
    {
        (double X1, double Y1, double X2, double Y2, double Width)? best = null;

        foreach (var line in scene.Primitives.OfType<FigureLine>())
        {
            double length = Length(line.X1, line.Y1, line.X2, line.Y2);
            if (length <= 0) continue;

            bool holds = web.Points.All(p =>
            {
                double across = Math.Abs(Across(line.X1, line.Y1, line.X2, line.Y2, p.X, p.Y));
                double along = Along(line.X1, line.Y1, line.X2, line.Y2, p.X, p.Y);
                return across <= line.Width / 2 + 1e-6 && along >= -1e-6 && along <= length + 1e-6;
            });

            // The narrowest line that holds the whole web is its body: the darker edge under it is wider
            // and would let a web that spills a little pass.
            if (holds && (best is null || line.Width < best.Value.Width))
                best = (line.X1, line.Y1, line.X2, line.Y2, line.Width);
        }

        return best;
    }

    private static double Length(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    /// <summary>How far along the segment a point falls, in millimetres from its first end.</summary>
    private static double Along(double x1, double y1, double x2, double y2, double x, double y)
    {
        double length = Length(x1, y1, x2, y2);
        return ((x - x1) * (x2 - x1) + (y - y1) * (y2 - y1)) / length;
    }

    /// <summary>How far off the segment's axis a point falls, signed, in millimetres.</summary>
    private static double Across(double x1, double y1, double x2, double y2, double x, double y)
    {
        double length = Length(x1, y1, x2, y2);
        return ((x - x1) * -(y2 - y1) + (y - y1) * (x2 - x1)) / length;
    }
}
