// =============================================================
// ReportFigureDrawer.cs — Lays out the report's plan figures, and renders them to PNG.
//
// The existing PlanDrawer targets a screen: dark background, saturated colours. A document printed in
// black and white needs the opposite, so this draws on white with dark line work and reserves colour
// for distinguishing truss families.
//
// Drawn as the studies draw their Figure 2, in the studies' axes (Helpers.StudyAxes): the beams
// horizontal, J at the top and A at the bottom; the points 1 to 9 from left to right; beam letters down
// the left edge and point numbers along the top; the X arrow to the right, towards point 9, and the Y
// arrow up, towards J, from the origin at the event's Point0 (E5). The hall is about twice as long
// across the beams as along them, so the plan is portrait: the canvas takes the proportions of what it
// frames instead of shrinking it into a landscape box.
//
// Every figure is framed on the content rather than on the whole 62 m hall, otherwise a production
// occupying the middle third would be illegible at page width.
//
// LAYOUT, THEN MEDIUM. This used to paint straight onto a Skia canvas, which is why a figure could only
// ever be a picture. Every decision is still made here - the framing, the grid, the trusses, the
// labels' collision avoidance, the legend, the scale bar - but it is now written into a FigureScene:
// lines, discs, boxes and labels on a page of millimetres. The PNG below is one reading of that scene;
// the Word builder makes real shapes out of the same one.
//
// MILLIMETRES, AND WHY EIGHT. The figure's own page is 200 mm wide and its PNG is 1600 px, so one
// millimetre is exactly eight pixels. The scale is a power of two deliberately: multiplying or dividing
// a float by eight only shifts its exponent, so a length that used to be computed in pixels and is now
// computed in millimetres comes back out as the very same pixel. That is what lets this become a scene
// without the PNGs - which the scenario keeps on disk, and one of which is the plan the user downloads
// - moving by so much as a rounding.
// =============================================================

using AliantWebS.Models;
using SkiaSharp;

namespace AliantWebS.Reporting;

/// <summary>One truss to draw: a segment, a family and an optional colour.</summary>
/// <param name="X1">
/// The ends are in the studies' axes (Helpers.StudyAxes) - X along the beams towards point 9, Y across
/// them towards J - in metres from Point0. ReportGenerator converts them from the stored grid coordinates.
/// </param>
/// <param name="Label">What the legend shows for the truss's family; null shows the family itself.</param>
public sealed record FigureTruss(
    double X1, double Y1, double X2, double Y2,
    string Family, string? ColorHex, bool IsMother, string? Label = null);

public static class ReportFigureDrawer
{
    // ── The page, in millimetres ──────────────────────────────────────────────────────────────
    // The width is fixed and the height follows the content's proportions, within limits.
    private const float PageWidth = 200f;
    private const float MarginLeft = 15f;       // room for the beam letters
    private const float MarginRight = 7.5f;
    private const float MarginTop = 18.75f;     // the caption, then the point numbers
    private const float MinPlotHeight = 87.5f;
    private const float MaxPlotHeight = 287.5f;
    private const float LegendLine = 4.75f;
    private const float Swatch = 2.75f;

    // ── The trusses, drawn as trusses ─────────────────────────────────────────────────────────
    // A mother line is the heavier of the two and reads as the wider band, as the studies draw them.
    // The numbers are millimetres on the figure's page, not metres in the hall: a 500 mm truss seen
    // from above would be a third of a millimetre wide at the scale a whole hall is drawn at, which is
    // a hairline nobody could read. The band is a symbol of a truss, sized to be read.
    private const float MotherBand = 2f;
    private const float PalonnierBand = 1.375f;

    /// <summary>How much wider than the body the darker segment under it is: the truss's edge.</summary>
    private const float BandEdge = 0.25f;

    /// <summary>The share of the band each chord keeps, along one edge. The web runs between the two.</summary>
    private const float ChordShare = 0.2f;

    /// <summary>The web's stroke, as a share of the band. Thin enough that the lattice stays open.</summary>
    private const float WebShare = 0.105f;

    /// <summary>
    /// How long one panel of web is, as a share of the band's width. Around one keeps the crossings near
    /// square, which is what reads as a lattice; much less and the web closes up into a hatch, much more
    /// and it stretches into a row of lozenges.
    /// </summary>
    private const float PanelShare = 1.15f;

    /// <summary>The band drawn in the legend's key.</summary>
    private const float LegendBand = 1.75f;

    /// <summary>
    /// How long a truss's key is. Longer than the square the charges keep, because a key that cannot
    /// hold two panels of web shows a plain bar - which is the one thing the reader must not take a
    /// truss for.
    /// </summary>
    private const float TrussKey = 6f;

    /// <summary>The key's width for one legend entry: a length of truss, or the charges' square.</summary>
    private static float KeyWidth(bool truss) => truss ? TrussKey : Swatch;

    // Text sizes, in millimetres.
    private const float CaptionText = 4f;
    private const float GridText = 3.25f;       // the beam letters, the point numbers and the axes
    private const float LegendText = 3f;
    private const float ScaleText = 3f;

    private const uint White = 0xFFFFFFFFu;
    private const uint Background = White;
    private const uint GridLine = 0xFFD6D6D6u;
    private const uint GridPoint = 0xFF7FAF4Fu;
    private const uint GridPointEdge = 0xFF4F7A2Fu;
    private const uint LabelBox = 0xFFF2C14Eu;
    private const uint Ink = 0xFF1A1A1Au;
    private const uint MotherInk = 0xFF3E8E41u;
    private const uint PalonnierInk = 0xFF1F6FB8u;
    private const uint LoadInk = 0xFFC62828u;
    private const uint LeaderInk = 0xFF8A8A8Au;

    // Fallback palette, used when a truss family has no catalogued colour.
    private static readonly uint[] Palette =
    {
        0xFF3E8E41u, 0xFF1F6FB8u, 0xFFB3529Eu,
        0xFFD98324u, 0xFF00A0A0u, 0xFF7A5AC7u,
    };

    /// <summary>Figure 5: the pre-rig on its own.</summary>
    /// <param name="point0">The event's Point0, absolute metres: the origin the coordinates are measured
    /// from, which places the charpente grid under them. Null takes E5.</param>
    public static bool DrawPreRig(
        IReadOnlyList<FigureTruss> trusses, string outPath, (double X, double Y)? point0 = null) =>
        Render(SceneOfPreRig(trusses, point0), outPath);

    /// <summary>Figure 6: the pre-rig with the production's loads placed on it.</summary>
    public static bool DrawImplantation(
        IReadOnlyList<FigureTruss> trusses, IReadOnlyList<ProductionLoad> loads, string outPath,
        (double X, double Y)? point0 = null) =>
        Render(SceneOfImplantation(trusses, loads, point0), outPath);

    /// <summary>
    /// Figure 4 stand-in: the production's loads plotted from their coordinates. Used when no plan
    /// image has been uploaded, so the reader still sees where the loads are.
    /// </summary>
    public static bool DrawLoadPlan(
        IReadOnlyList<ProductionLoad> loads, string outPath, (double X, double Y)? point0 = null) =>
        Render(SceneOfLoadPlan(loads, point0), outPath);

    /// <summary>Figure 5 as a scene, for a renderer that draws shapes rather than pixels.</summary>
    /// <returns>Null when there is nothing to draw, exactly as the PNG is not written in that case.</returns>
    public static FigureScene? SceneOfPreRig(
        IReadOnlyList<FigureTruss> trusses, (double X, double Y)? point0 = null) =>
        BuildScene(trusses, Array.Empty<ProductionLoad>(), "Mother + pré rigg", point0);

    /// <summary>Figure 6 as a scene.</summary>
    /// <returns>Null when there is nothing to draw.</returns>
    public static FigureScene? SceneOfImplantation(
        IReadOnlyList<FigureTruss> trusses, IReadOnlyList<ProductionLoad> loads,
        (double X, double Y)? point0 = null) =>
        BuildScene(trusses, loads, "Implantation de la production", point0);

    /// <summary>Figure 4's stand-in as a scene.</summary>
    /// <returns>Null when there is nothing to draw.</returns>
    public static FigureScene? SceneOfLoadPlan(
        IReadOnlyList<ProductionLoad> loads, (double X, double Y)? point0 = null) =>
        BuildScene(Array.Empty<FigureTruss>(), loads, "Plan de charges", point0);

    private static FigureScene? BuildScene(
        IReadOnlyList<FigureTruss> trusses,
        IReadOnlyList<ProductionLoad> loads,
        string caption,
        (double X, double Y)? point0)
    {
        // A load the plan gives no position has no place on a drawing.
        var located = loads.Where(l => l.Located).ToList();
        if (trusses.Count == 0 && located.Count == 0) return null;

        var grid = StudyGrid.Of(point0);
        var bounds = ComputeBounds(trusses, located, grid);
        if (bounds is null) return null;

        var (xMin, xMax, yMin, yMax) = bounds.Value;

        // Preserve the aspect ratio so a plan is never stretched; a distorted plan reads as a
        // different rig. The plot takes the content's proportions, clamped so that a narrow strip of
        // the hall is neither a ribbon nor a tower.
        var plotW = PageWidth - MarginLeft - MarginRight;
        var plotH = (float)Math.Clamp(plotW * (yMax - yMin) / (xMax - xMin), MinPlotHeight, MaxPlotHeight);
        var scale = (float)Math.Min(plotW / (xMax - xMin), plotH / (yMax - yMin));
        var offsetX = MarginLeft + (plotW - (float)(xMax - xMin) * scale) / 2f;
        var offsetY = MarginTop + (plotH - (float)(yMax - yMin) * scale) / 2f;

        // Metres in the studies' axes to millimetres on the page. The scene's page grows downwards, so
        // this is where - and the only place where - Y is turned over.
        float Px(double x) => offsetX + (float)(x - xMin) * scale;
        float Py(double y) => offsetY + (float)(yMax - y) * scale;   // Y grows upward on the plan, towards J

        using var legendFont = Font(LegendText, bold: false);
        var legend = LayoutLegend(LegendEntries(trusses, located), s => Measure(legendFont, s), plotW);

        // Below the plot: the scale bar, then the legend, so neither can cover the grid's labels.
        var plotBottom = MarginTop + plotH;
        var scaleBarY = plotBottom + 7.5f;
        var legendTop = plotBottom + 13.125f;
        var height = legendTop + legend.Rows * LegendLine + 3.75f;

        // In paint order, back to front: the hall, then what is hung in it, then what names it.
        var parts = new List<FigurePrimitive>();

        BuildVenueGrid(parts, grid, Px, Py, xMin, xMax, yMin, yMax);
        BuildTrusses(parts, trusses, Px, Py);
        BuildLoads(parts, located, Px, Py, top: MarginTop - 1.25f, bottom: plotBottom + 5f);
        BuildOrigin(parts, Px, Py, xMin, xMax, yMin, yMax);
        BuildCaption(parts, caption);
        BuildScaleBar(parts, scale, Px(xMin), scaleBarY);
        BuildLegend(parts, legend.Items, legendTop);

        return new FigureScene(PageWidth, height, Background, parts);
    }

    /// <summary>
    /// The venue's charpente in the studies' axes, relative to Point0: the beams (constant Y, J the
    /// highest), the point lines (constant X, 1 the leftmost) and every attachment point.
    /// </summary>
    private sealed class StudyGrid
    {
        public List<(string Beam, double Y)> Beams { get; } = new();
        public List<(int Point, double X)> Points { get; } = new();
        public List<(double X, double Y)> Nodes { get; } = new();

        public static StudyGrid Of((double X, double Y)? point0)
        {
            // CharpenteCoordinates is absolute; the element and load coordinates are relative to Point0.
            var origin = point0 ?? CharpenteCoordinates.Points["E5"];
            var grid = new StudyGrid();

            foreach (var beam in ReportDataBuilder.Beams)
            {
                // A beam runs along the plan: its study Y is its grid X, reflected.
                var ys = ReportDataBuilder.Points
                    .Select(p => CharpenteCoordinates.GetCoordinates($"{beam}{p}"))
                    .Where(c => c is not null)
                    .Select(c => global::Helpers.StudyAxes.StudyY(c!.Value.X - origin.X))
                    .ToList();
                if (ys.Count > 0) grid.Beams.Add((beam, ys.Average()));
            }

            foreach (var point in ReportDataBuilder.Points)
            {
                // A point line crosses the beams: its study X is its grid Y, reflected.
                var xs = ReportDataBuilder.Beams
                    .Select(b => CharpenteCoordinates.GetCoordinates($"{b}{point}"))
                    .Where(c => c is not null)
                    .Select(c => global::Helpers.StudyAxes.StudyX(c!.Value.Y - origin.Y))
                    .ToList();
                if (xs.Count > 0) grid.Points.Add((point, xs.Average()));
            }

            foreach (var node in CharpenteCoordinates.Points.Values)
                grid.Nodes.Add(global::Helpers.StudyAxes.ToStudy(node.X - origin.X, node.Y - origin.Y));

            return grid;
        }
    }

    private static (double XMin, double XMax, double YMin, double YMax)? ComputeBounds(
        IReadOnlyList<FigureTruss> trusses, IReadOnlyList<ProductionLoad> loads, StudyGrid grid)
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        void Include(double x, double y)
        {
            xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x);
            yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y);
        }

        foreach (var t in trusses) { Include(t.X1, t.Y1); Include(t.X2, t.Y2); }
        foreach (var l in loads) Include(l.X, l.Y);

        if (xMin > xMax || yMin > yMax) return null;

        // Widen to the grid lines just beyond the content on each side, so the drawing carries enough
        // of the hall to be locatable. Compared against the CONTENT bounds, not the bounds being
        // written: testing against a value that grows inside the loop walks outwards grid line by grid
        // line and ends up framing the whole 62 m hall.
        double contentXMin = xMin, contentXMax = xMax, contentYMin = yMin, contentYMax = yMax;
        const double reach = 12.0;

        foreach (var (_, px) in grid.Points)
        {
            if (px <= contentXMin && px > contentXMin - reach) xMin = Math.Min(xMin, px);
            if (px >= contentXMax && px < contentXMax + reach) xMax = Math.Max(xMax, px);
        }
        foreach (var (_, by) in grid.Beams)
        {
            if (by <= contentYMin && by > contentYMin - reach) yMin = Math.Min(yMin, by);
            if (by >= contentYMax && by < contentYMax + reach) yMax = Math.Max(yMax, by);
        }

        var padX = Math.Max(2.0, (xMax - xMin) * 0.10);
        var padY = Math.Max(2.0, (yMax - yMin) * 0.10);

        // A single point, or a perfectly straight line of them, would give a zero-sized box.
        if (xMax - xMin < 1) { xMin -= 3; xMax += 3; }
        if (yMax - yMin < 1) { yMin -= 3; yMax += 3; }

        return (xMin - padX, xMax + padX, yMin - padY, yMax + padY);
    }

    private static void BuildVenueGrid(
        List<FigurePrimitive> into, StudyGrid grid, Func<double, float> Px, Func<double, float> Py,
        double xMin, double xMax, double yMin, double yMax)
    {
        const float hairline = 0.1875f;
        using var font = Font(GridText, bold: true);

        // The beams run along the plan, from point 1 to point 9: horizontal, J at the top.
        foreach (var (_, by) in grid.Beams)
        {
            if (by < yMin || by > yMax) continue;
            into.Add(new FigureLine(Px(xMin), Py(by), Px(xMax), Py(by), hairline, GridLine));
        }

        // The point lines cross them: vertical, 1 on the left.
        foreach (var (_, px) in grid.Points)
        {
            if (px < xMin || px > xMax) continue;
            into.Add(new FigureLine(Px(px), Py(yMin), Px(px), Py(yMax), hairline, GridLine));
        }

        foreach (var (nx, ny) in grid.Nodes)
        {
            if (nx < xMin || nx > xMax || ny < yMin || ny > yMax) continue;
            into.Add(new FigureCircle(Px(nx), Py(ny), 0.875f, GridPoint, GridPointEdge, hairline));
        }

        // Beam letters down the left edge, in a tinted box as the venue nomenclature figure shows them.
        foreach (var (beam, by) in grid.Beams)
        {
            if (by < yMin || by > yMax) continue;
            var w = Measure(font, beam);
            var bx = Px(xMin) - w - 2.5f - 1.75f;
            var top = Py(by) - 2.125f;
            into.Add(new FigureRect(bx, top, w + 2.5f, 4.25f, LabelBox, CornerRadius: 0.625f));
            into.Add(new FigureLabel(
                beam, bx + 1.25f, top + 3.125f,
                FigureHAlign.Left, FigureVAlign.Baseline, GridText, Bold: true, Ink));
        }

        // Point numbers along the top, centred on their line: the renderer measures the digits, as the
        // layout used to, and the scene says what the number is centred on rather than where it starts.
        foreach (var (point, px) in grid.Points)
        {
            if (px < xMin || px > xMax) continue;
            into.Add(new FigureLabel(
                point.ToString(), Px(px), Py(yMax) - 1.75f,
                FigureHAlign.Center, FigureVAlign.Baseline, GridText, Bold: true, Ink));
        }
    }

    private static void BuildTrusses(
        List<FigurePrimitive> into, IReadOnlyList<FigureTruss> trusses,
        Func<double, float> Px, Func<double, float> Py)
    {
        var families = Families(trusses);

        foreach (var t in trusses)
        {
            var colour = ColourFor(t, families);
            float band = t.IsMother ? MotherBand : PalonnierBand;

            // The darker segment underneath is a hair wider than the body: that is the truss's edge.
            into.Add(new FigureLine(
                Px(t.X1), Py(t.Y1), Px(t.X2), Py(t.Y2),
                band + BandEdge, Darken(colour), RoundCap: true));
            into.Add(new FigureLine(
                Px(t.X1), Py(t.Y1), Px(t.X2), Py(t.Y2),
                band, colour, RoundCap: true));

            // ...and the web inside it, which is what makes a bar read as a truss.
            BuildWeb(into, Px(t.X1), Py(t.Y1), Px(t.X2), Py(t.Y2), band);
        }

        // Endpoints last so they sit above every truss.
        foreach (var t in trusses)
        {
            foreach (var (x, y) in new[] { (t.X1, t.Y1), (t.X2, t.Y2) })
                into.Add(new FigureCircle(Px(x), Py(y), 0.75f, White, Ink, 0.3125f));
        }
    }

    /// <summary>
    /// The truss's web: the criss-cross between the two chords that the studies' plans draw, and that
    /// tells a bar from a truss at a glance.
    ///
    /// It is drawn the way a truss is actually made rather than as a decorative hatch. The chords are
    /// what is left of the coloured band along its two edges; the web is cut out of it in the colour of
    /// the page, as two zig-zags in antiphase - one from chord to chord, one the mirror of it - which
    /// cross in the middle of every panel and meet the chords at the same nodes. A panel is about as
    /// long as the band is wide, so the crossings stay near square whatever the truss's length: that
    /// proportion is what the eye reads as a lattice rather than as a texture.
    ///
    /// WHOLE PANELS, NEVER A CUT ONE. The pitch is stretched so a whole number of panels fits between
    /// the two ends: a pattern clipped in the middle of a panel is the thing that makes a drawn truss
    /// look like a texture instead of a structure. Both ends keep half a band of solid colour - the
    /// round cap of the bar underneath - which reads as the end plate it is.
    ///
    /// TWO SHAPES, NOT FORTY. Each zig-zag is one polyline. A mother line across the hall holds some
    /// forty panels; written as separate segments that would be a hundred and sixty shapes for one
    /// truss, in a document that carries thirteen of them.
    /// </summary>
    private static void BuildWeb(
        List<FigurePrimitive> into, float x1, float y1, float x2, float y2, float band)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return;

        // The chords are the band's own edges, so the web runs between what is left in the middle.
        double half = band / 2.0 - band * ChordShare;
        if (half <= 0.05) return;

        // Both ends keep the round cap solid, and what is left has to hold at least one whole panel.
        double inset = band / 2.0;
        double run = length - 2 * inset;
        double panel = band * PanelShare;
        if (run < panel) return;

        int panels = Math.Max(1, (int)Math.Round(run / panel));
        double pitch = run / panels;

        double ux = dx / length, uy = dy / length;
        double nx = -uy, ny = ux;

        (double X, double Y) Node(int index, int side)
        {
            double along = inset + index * pitch;
            double across = side * half;
            return (x1 + ux * along + nx * across, y1 + uy * along + ny * across);
        }

        var down = new List<(double X, double Y)>(panels + 1);
        var up = new List<(double X, double Y)>(panels + 1);

        for (var i = 0; i <= panels; i++)
        {
            int side = i % 2 == 0 ? 1 : -1;
            down.Add(Node(i, side));
            up.Add(Node(i, -side));
        }

        into.Add(new FigurePolyline(down, band * WebShare, Background, RoundCap: true));
        into.Add(new FigurePolyline(up, band * WebShare, Background, RoundCap: true));
    }

    private static void BuildLoads(
        List<FigurePrimitive> into, IReadOnlyList<ProductionLoad> loads,
        Func<double, float> Px, Func<double, float> Py, float top, float bottom)
    {
        if (loads.Count == 0) return;

        // Loads cluster on the same lines, so labels are placed with collision avoidance rather than
        // at a fixed offset. Overlapping text is worse than no text in a document someone rigs from.
        var size = loads.Count > 24 ? 2.125f : 2.5f;
        using var font = Font(size, bold: true);

        foreach (var l in loads)
        {
            into.Add(new FigureCircle(Px(l.X), Py(l.Y), 1.5f, White));
            into.Add(new FigureCircle(Px(l.X), Py(l.Y), 1.125f, LoadInk));
        }

        // SKRect is used here as a plain rectangle of MILLIMETRES, for the overlap test alone; nothing
        // in this search is a pixel any more.
        var placed = new List<SKRect>();
        var lineHeight = size + 0.75f;

        // Tried in order: right then left, each stepping further from the point.
        var offsets = new List<(float Dx, float Dy)>();
        for (var ring = 0; ring < 4; ring++)
        {
            var dy = -1.25f - ring * lineHeight;
            offsets.Add((1.875f, dy));
            offsets.Add((1.875f, -dy + 0.75f));
        }

        foreach (var l in loads.OrderBy(l => l.Y).ThenBy(l => l.X))
        {
            float cx = Px(l.X), cy = Py(l.Y);
            // A charge nobody weighed is not a charge of 0 daN: the figure says so, as Tableau 1 does.
            var label = l.Weighed ? $"{l.Name}  {l.WeightKg:0} daN" : $"{l.Name}  — daN";
            var width = Measure(font, label);

            SKRect? chosen = null;
            foreach (var (dx, dy) in offsets)
            {
                foreach (var side in new[] { 1, -1 })
                {
                    var x = side > 0 ? cx + dx : cx - dx - width;
                    var candidate = new SKRect(x - 0.25f, cy + dy - size, x + width + 0.25f, cy + dy + 0.5f);

                    if (candidate.Left < 1f || candidate.Right > PageWidth - 1f) continue;
                    if (candidate.Top < top || candidate.Bottom > bottom) continue;
                    if (placed.Any(r => r.IntersectsWith(candidate))) continue;

                    chosen = candidate;
                    break;
                }
                if (chosen is not null) break;
            }

            // Nowhere free: the dot still marks the load, and the table carries the number.
            if (chosen is not { } box) continue;

            placed.Add(box);
            into.Add(new FigureLine(
                cx, cy, box.MidX < cx ? box.Right : box.Left, box.Bottom - 0.5f, 0.15f, LeaderInk));
            into.Add(new FigureLabel(
                label, box.Left + 0.25f, box.Bottom - 0.5f,
                FigureHAlign.Left, FigureVAlign.Baseline, size, Bold: true, Ink,
                Halo: White, HaloWidth: 0.5f));
        }
    }

    /// <summary>
    /// The studies' axes at their origin, Point0: X to the right, towards point 9, and Y up, towards J.
    /// Drawn only when the origin is inside the frame.
    /// </summary>
    private static void BuildOrigin(
        List<FigurePrimitive> into, Func<double, float> Px, Func<double, float> Py,
        double xMin, double xMax, double yMin, double yMax)
    {
        if (0 < xMin || 0 > xMax || 0 < yMin || 0 > yMax) return;

        const float length = 10f, head = 2f, shaft = 0.375f;

        float ox = Px(0), oy = Py(0);

        // The shafts stop where the heads begin, so no line shows through a solid arrow.
        into.Add(new FigureLine(ox, oy, ox + length - head, oy, shaft, Ink));
        into.Add(new FigureLine(ox, oy, ox, oy - length + head, shaft, Ink));

        into.Add(new FigurePolygon(
            new (double X, double Y)[]
            {
                (ox + length, oy),
                (ox + length - head, oy - head / 2f),
                (ox + length - head, oy + head / 2f),
            },
            Ink));

        into.Add(new FigurePolygon(
            new (double X, double Y)[]
            {
                (ox, oy - length),
                (ox - head / 2f, oy - length + head),
                (ox + head / 2f, oy - length + head),
            },
            Ink));

        into.Add(new FigureLabel(
            "X", ox + length + 1f, oy + 1.125f,
            FigureHAlign.Left, FigureVAlign.Baseline, GridText, Bold: true, Ink));
        into.Add(new FigureLabel(
            "Y", ox - 1.125f, oy - length - 1.25f,
            FigureHAlign.Left, FigureVAlign.Baseline, GridText, Bold: true, Ink));
        into.Add(new FigureCircle(ox, oy, 0.625f, Ink));
    }

    private static void BuildCaption(List<FigurePrimitive> into, string caption) =>
        into.Add(new FigureLabel(
            caption, MarginLeft, 7.75f,
            FigureHAlign.Left, FigureVAlign.Baseline, CaptionText, Bold: true, Ink));

    private static void BuildScaleBar(List<FigurePrimitive> into, float scale, float x0, float y0)
    {
        // Pick a round number of metres that lands near a quarter of the page's width.
        var candidates = new[] { 1, 2, 5, 10, 20, 50 };
        var metres = candidates.LastOrDefault(m => m * scale <= 32.5f);
        if (metres == 0) metres = 1;

        float x1 = x0 + metres * scale;
        const float bar = 0.5f, tick = 1.125f;

        into.Add(new FigureLine(x0, y0, x1, y0, bar, Ink));
        into.Add(new FigureLine(x0, y0 - tick, x0, y0 + tick, bar, Ink));
        into.Add(new FigureLine(x1, y0 - tick, x1, y0 + tick, bar, Ink));
        into.Add(new FigureLabel(
            $"{metres} m", x1 + 1.75f, y0 + tick,
            FigureHAlign.Left, FigureVAlign.Baseline, ScaleText, Bold: false, Ink));
    }

    private static List<string> Families(IReadOnlyList<FigureTruss> trusses) =>
        trusses.Select(t => t.Family)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<(string Label, uint Colour, bool Truss)> LegendEntries(
        IReadOnlyList<FigureTruss> trusses, IReadOnlyList<ProductionLoad> loads)
    {
        var families = Families(trusses);
        var entries = new List<(string Label, uint Colour, bool Truss)>();

        foreach (var family in families)
        {
            var sample = trusses.First(t => string.Equals(t.Family, family, StringComparison.OrdinalIgnoreCase));
            entries.Add((sample.Label ?? family, ColourFor(sample, families), true));
        }

        // The charges are not a truss family, so their key stays the plain square it has always been.
        if (loads.Count > 0) entries.Add(($"{loads.Count} charge(s)", LoadInk, false));
        return entries;
    }

    /// <summary>
    /// The legend laid out in rows under the plot, left to right, wrapping at the plot's width. It used to
    /// be a panel over the top right of the plot, which the point numbers now occupy.
    /// </summary>
    /// <param name="measure">
    /// How wide a label is, in millimetres. Wrapping cannot be decided without it, and only a text
    /// engine knows; the scene itself never measures anything.
    /// </param>
    private static (List<(string Label, uint Colour, bool Truss, float X, int Row)> Items, int Rows) LayoutLegend(
        List<(string Label, uint Colour, bool Truss)> entries, Func<string, float> measure, float available)
    {
        const float gap = 1.75f, spacing = 5.5f;

        var items = new List<(string Label, uint Colour, bool Truss, float X, int Row)>();
        float x = 0;
        int row = 0;

        foreach (var (label, colour, truss) in entries)
        {
            var w = KeyWidth(truss) + gap + measure(label);
            if (x > 0 && x + w > available) { row++; x = 0; }
            items.Add((label, colour, truss, x, row));
            x += w + spacing;
        }

        return (items, entries.Count == 0 ? 0 : row + 1);
    }

    private static void BuildLegend(
        List<FigurePrimitive> into, List<(string Label, uint Colour, bool Truss, float X, int Row)> items, float top)
    {
        const float gap = 1.75f;

        foreach (var (label, colour, truss, x, row) in items)
        {
            var left = MarginLeft + x;
            var rowTop = top + row * LegendLine;

            if (truss)
            {
                // The key is a short length of the truss itself, web and all, rather than a square of
                // its colour: the reader matches what is drawn in the plan, not a shade of it.
                float centre = rowTop + Swatch / 2f;
                into.Add(new FigureLine(left, centre, left + TrussKey, centre,
                    LegendBand + BandEdge, Darken(colour), RoundCap: true));
                into.Add(new FigureLine(left, centre, left + TrussKey, centre,
                    LegendBand, colour, RoundCap: true));
                BuildWeb(into, left, centre, left + TrussKey, centre, LegendBand);
            }
            else
            {
                into.Add(new FigureRect(left, rowTop, Swatch, Swatch, colour));
            }

            into.Add(new FigureLabel(
                label, left + KeyWidth(truss) + gap, rowTop + Swatch - 0.375f,
                FigureHAlign.Left, FigureVAlign.Baseline, LegendText, Bold: false, Ink));
        }
    }

    private static uint ColourFor(FigureTruss t, List<string> families)
    {
        if (!string.IsNullOrWhiteSpace(t.ColorHex) &&
            SKColor.TryParse("#" + t.ColorHex!.TrimStart('#'), out var parsed))
        {
            // Catalogue colours are pale table shades; darken them so a line stays visible.
            return Darken((uint)parsed);
        }

        var index = families.FindIndex(f => string.Equals(f, t.Family, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) return Palette[index % Palette.Length];

        return t.IsMother ? MotherInk : PalonnierInk;
    }

    private static uint Darken(uint c)
    {
        byte Channel(int shift) => (byte)(((c >> shift) & 0xFFu) * 0.72);
        return (c & 0xFF000000u) | ((uint)Channel(16) << 16) | ((uint)Channel(8) << 8) | Channel(0);
    }

    // ── Text, measured ────────────────────────────────────────────────────────────────────────
    // The layout must know how wide a label is before it can place it, and only a text engine knows
    // that. So measurement stays Skia's, and it is done with the very paint the PNG renderer draws
    // with, which is why a label the layout fitted is a label that fits.

    // The two faces, resolved once. SKTypeface.FromFamilyName falls back to whatever the machine has
    // when the family is missing, so this never comes back empty - and what it comes back with is what
    // the layout measures, which is the point: measuring and drawing must be the same face.
    private static readonly SKTypeface RegularFace =
        SKTypeface.FromFamilyName(FigureFonts.Family, SKFontStyle.Normal) ?? SKTypeface.Default;

    private static readonly SKTypeface BoldFace =
        SKTypeface.FromFamilyName(FigureFonts.Family, SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>
    /// The paint a label is measured and drawn with. Naming the typeface is not cosmetic: with none
    /// named, Skia letters in the machine's default family - Segoe UI on a Windows developer's machine -
    /// while the Word document asks for Arial, so every width the layout computed would be a width of a
    /// different font than the one the reader sees.
    /// </summary>
    private static SKPaint Font(float size, bool bold)
    {
        var face = bold ? BoldFace : RegularFace;

        return new SKPaint
        {
            TextSize = size * PngScale,
            IsAntialias = true,
            Typeface = face,

            // Only where the family has no bold of its own: synthesising it over a real bold face
            // would draw heavier than it measures.
            FakeBoldText = bold && !face.IsBold,
        };
    }

    private static float Measure(SKPaint font, string text) => font.MeasureText(text) / PngScale;

    // ── The scene, read as pixels ─────────────────────────────────────────────────────────────
    // One walk over the primitives, in order. The PNGs still go into the scenario's output folder and
    // the plan the reader downloads is one of them, so this must keep producing what it always did:
    // 1600 px wide, eight pixels to the millimetre.

    private const float PngScale = 8f;

    private static float ToPx(double mm) => (float)(mm * PngScale);

    private static SKColor Colour(uint argb) => new(argb);

    /// <summary>
    /// Writes a scene that has already been laid out. The three Draw* methods above lay out and render
    /// in one call, which is what a caller wanting only a file needs; a caller that also hands the scene
    /// to the document uses this one instead, so the picture on disk and the shapes in the study come
    /// out of ONE layout rather than two that merely happen to agree.
    /// </summary>
    /// <returns>False for a scene with nothing in it, exactly as the Draw* methods do.</returns>
    public static bool Write(FigureScene? scene, string outPath) => Render(scene, outPath);

    private static bool Render(FigureScene? scene, string outPath)
    {
        if (scene is null) return false;

        var width = (int)Math.Ceiling(scene.Width * PngScale);
        var height = (int)Math.Ceiling(scene.Height * PngScale);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(Colour(scene.Background));

        foreach (var part in scene.Primitives) RenderPart(canvas, part);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);
        return true;
    }

    private static void RenderPart(SKCanvas canvas, FigurePrimitive part)
    {
        switch (part)
        {
            case FigureLine l:
            {
                using var dash = l.Dashed
                    ? SKPathEffect.CreateDash(new[] { ToPx(1.5), ToPx(1.0) }, 0f)
                    : null;
                using var paint = new SKPaint
                {
                    Color = Colour(l.Color),
                    StrokeWidth = ToPx(l.Width),
                    IsAntialias = true,
                    IsStroke = true,
                    StrokeCap = l.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt,
                    PathEffect = dash,
                };
                canvas.DrawLine(ToPx(l.X1), ToPx(l.Y1), ToPx(l.X2), ToPx(l.Y2), paint);
                break;
            }

            case FigureCircle c:
            {
                if (c.Fill is { } fill)
                {
                    using var paint = new SKPaint { Color = Colour(fill), IsAntialias = true };
                    canvas.DrawCircle(ToPx(c.Cx), ToPx(c.Cy), ToPx(c.Radius), paint);
                }

                if (c.Outline is { } outline)
                {
                    using var paint = new SKPaint
                    {
                        Color = Colour(outline),
                        IsAntialias = true,
                        IsStroke = true,
                        StrokeWidth = ToPx(c.OutlineWidth),
                    };
                    canvas.DrawCircle(ToPx(c.Cx), ToPx(c.Cy), ToPx(c.Radius), paint);
                }

                break;
            }

            case FigureRect r:
            {
                // Built from a corner and a size, in that order, because that is how the canvas itself
                // works out a rectangle's far edges - the same additions, in the same arithmetic.
                var box = SKRect.Create(ToPx(r.X), ToPx(r.Y), ToPx(r.Width), ToPx(r.Height));
                var radius = ToPx(r.CornerRadius);

                if (r.Fill is { } fill)
                {
                    using var paint = new SKPaint { Color = Colour(fill), IsAntialias = true };
                    if (radius > 0) canvas.DrawRoundRect(box, radius, radius, paint);
                    else canvas.DrawRect(box, paint);
                }

                if (r.Outline is { } outline)
                {
                    using var paint = new SKPaint
                    {
                        Color = Colour(outline),
                        IsAntialias = true,
                        IsStroke = true,
                        StrokeWidth = ToPx(r.OutlineWidth),
                    };
                    if (radius > 0) canvas.DrawRoundRect(box, radius, radius, paint);
                    else canvas.DrawRect(box, paint);
                }

                break;
            }

            case FigurePolyline p:
            {
                if (p.Points.Count < 2) break;

                using var path = new SKPath();
                path.MoveTo(ToPx(p.Points[0].X), ToPx(p.Points[0].Y));
                for (var i = 1; i < p.Points.Count; i++)
                    path.LineTo(ToPx(p.Points[i].X), ToPx(p.Points[i].Y));

                using var paint = new SKPaint
                {
                    Color = Colour(p.Color),
                    StrokeWidth = ToPx(p.Width),
                    IsAntialias = true,
                    IsStroke = true,
                    StrokeCap = p.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt,
                    StrokeJoin = SKStrokeJoin.Round,
                };
                canvas.DrawPath(path, paint);
                break;
            }

            case FigurePolygon p:
            {
                if (p.Points.Count == 0) break;

                using var path = new SKPath();
                path.MoveTo(ToPx(p.Points[0].X), ToPx(p.Points[0].Y));
                for (var i = 1; i < p.Points.Count; i++)
                    path.LineTo(ToPx(p.Points[i].X), ToPx(p.Points[i].Y));
                path.Close();

                using var paint = new SKPaint { Color = Colour(p.Fill), IsAntialias = true };
                canvas.DrawPath(path, paint);
                break;
            }

            case FigureLabel t:
            {
                using var font = Font((float)t.Size, t.Bold);
                var metrics = font.FontMetrics;

                var x = ToPx(t.X) + t.HAlign switch
                {
                    FigureHAlign.Center => -font.MeasureText(t.Text) / 2f,
                    FigureHAlign.Right => -font.MeasureText(t.Text),
                    _ => 0f,
                };

                // Skia draws from the baseline; the other anchors are that baseline, moved by the
                // font's own metrics rather than by the size, so a tall glyph is not clipped.
                var y = ToPx(t.Y) + t.VAlign switch
                {
                    FigureVAlign.Top => -metrics.Ascent,
                    FigureVAlign.Middle => -(metrics.Ascent + metrics.Descent) / 2f,
                    FigureVAlign.Bottom => -metrics.Descent,
                    _ => 0f,
                };

                // The halo first: it is an outline of the same glyphs, and the text is drawn over it.
                if (t.Halo is { } halo)
                {
                    using var outline = Font((float)t.Size, t.Bold);
                    outline.Color = Colour(halo);
                    outline.IsStroke = true;
                    outline.StrokeWidth = ToPx(t.HaloWidth);
                    canvas.DrawText(t.Text, x, y, outline);
                }

                font.Color = Colour(t.Color);
                canvas.DrawText(t.Text, x, y, font);
                break;
            }
        }
    }
}
