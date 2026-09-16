// =============================================================
// PlanDrawer.cs  — Dessin du plan de rigging (PNG)
// Équivalent Python: plot_rigging()
// Dépendance NuGet: SkiaSharp
//
// Drawn as the studies' Figure 2: the beams horizontal, J at the top and A at the bottom, the point lines
// 1 to 9 from left to right; X along the beams towards point 9 to the right, Y across them towards J
// upwards, from E5 (Helpers.StudyAxes). The engine gives grid coordinates - the grid points, the mother
// lines' ends, the palonniers' ends and hanging points, the charges - and each is converted here, once,
// as it is drawn. One scale for both axes: a stretched plan reads as a different rig.
//
// A mother line runs along its point line, across the beams, so it is vertical here, with a support
// marked at every beam it rests on. A palonnier along the beams is horizontal; an inclined one is not.
// =============================================================

using System.Globalization;
using Helpers;
using RiggingOptimizer.Models;
using SkiaSharp;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.IO;

public static class PlanDrawer
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ── Palette de couleurs ───────────────────────────────────────
    // On white, as the studies' figures are, with the venue figure's own colours for the charpente: green
    // points, amber beam letters, red point numbers.
    private static SKColor C(string hex) => SKColor.Parse(hex);
    private static readonly SKColor BgColor         = SKColors.White;
    private static readonly SKColor Ink             = C("#1a1a1a");
    private static readonly SKColor SubInk          = C("#4a4a4a");
    private static readonly SKColor BeamColor       = C("#b4b4b4");
    private static readonly SKColor PointLineColor  = C("#c5e6bb");
    private static readonly SKColor GridPtColor     = C("#7faf4f");
    private static readonly SKColor GridPtEdge      = C("#4f7a2f");
    private static readonly SKColor BeamBoxColor    = C("#f2c14e");
    private static readonly SKColor PointBoxColor   = C("#d32f2f");
    private static readonly SKColor MotherColor     = C("#8e24aa");
    private static readonly SKColor SupportColor    = C("#4a148c");
    private static readonly SKColor AlongColor      = C("#00897b");
    private static readonly SKColor InclinedColor   = C("#ef6c00");
    private static readonly SKColor PalChargeColor  = C("#ffb300");
    private static readonly SKColor LineChargeColor = C("#c2185b");
    private static readonly SKColor UncoveredColor  = C("#757575");
    private static readonly SKColor LoadPtColor     = C("#e53935");
    private static readonly SKColor LoadTextColor   = C("#b71c1c");
    private static readonly SKColor UpliftColor     = C("#1565c0");
    private static readonly SKColor LeaderColor     = C("#9e9e9e");

    // ── Mise en page ──────────────────────────────────────────────
    private const float  PxPerMetre = 34f;     // at most; less when the content reaches far out
    private const float  MaxPlotPx  = 3000f;   // so a stray coordinate cannot ask for a giant image
    private const double PadM       = 2.0;     // metres of hall shown around the charpente and the content
    private const float  EdgeL = 100f, EdgeR = 100f;                 // beam letters, both sides
    private const float  TitleH = 150f, TopBandH = 60f, BottomBandH = 60f, FooterH = 70f;
    private const float  Gap = 24f, LegendW = 470f, RightMargin = 24f, LegendStep = 36f;

    public static void Draw(RiggingResult result, string outPath)
    {
        var venue  = Venue.InStudyAxes();
        var legend = LegendEntries(result);
        var f      = Layout(venue, result, LegendHeight(legend.Count));

        using var surface = SKSurface.Create(new SKImageInfo(f.Width, f.Height));
        var canvas = surface.Canvas;
        canvas.Clear(BgColor);

        // Everything drawn after the charpente is kept clear of the labels: the axes, the trusses, the loads
        // and the charges.
        DrawVenue(canvas, f, venue);
        var marks = DrawOrigin(canvas, f);
        marks.AddRange(DrawMothers   (canvas, f, result.Mothers));
        marks.AddRange(DrawPalonniers(canvas, f, result.Palonniers));
        marks.AddRange(DrawGridLoads (canvas, f, result.GridLoads));
        marks.AddRange(DrawCharges   (canvas, f, result));

        // Labels over everything, most important first, never over anything drawn above; one that finds no
        // room is left out and counted, and the Excel report has its value.
        using var labels = new LabelPlacer(f.Plot);
        foreach (var r in marks) labels.Reserve(r);
        LabelMothers   (canvas, f, labels, result.Mothers);
        LabelPalonniers(canvas, f, labels, result.Palonniers);
        LabelGridLoads (canvas, f, labels, result.GridLoads);
        LabelCharges   (canvas, f, labels, result);

        DrawTitle (canvas, result);
        DrawLegend(canvas, f, legend);
        DrawFooter(canvas, f, labels.Skipped);

        // Sauvegarder en PNG. File.Create empties the file first; File.OpenWrite left the tail of a larger
        // previous plan behind.
        using var image = surface.Snapshot();
        using var data  = image.Encode(SKEncodedImageFormat.Png, 90);
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);

        Console.WriteLine($"✅ Plan: {outPath}");
    }

    // ── La charpente dans les axes des études ─────────────────────
    /// <summary>Every beam (J to A), every point line (1 to 9) and every point, in the studies' axes.</summary>
    private sealed record Venue(
        List<(string Beam, double Y)> Beams,
        List<(int Number, double X)> PointLines,
        List<(double X, double Y)> Points)
    {
        public static Venue InStudyAxes()
        {
            // Relative to E5, the origin the engine's grid and the studies share (Geometry, StudyAxes). Beam J is
            // not in the optimiser's grid but is in the charpente, and in the studies' figure.
            var points = AliantWebS.Models.CharpenteCoordinates.Points;
            var (refX, refY) = points["E5"];

            var beams = new Dictionary<string, List<double>>();
            var lines = new Dictionary<int, List<double>>();
            var all = new List<(double X, double Y)>();

            foreach (var (name, (x, y)) in points)
            {
                int split = 0;
                while (split < name.Length && char.IsLetter(name[split])) split++;
                if (split == 0 || !int.TryParse(name[split..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    continue;

                var study = StudyAxes.ToStudy(x - refX, y - refY);
                all.Add(study);

                string beam = name[..split];
                if (!beams.TryGetValue(beam, out var ys)) beams[beam] = ys = new List<double>();
                ys.Add(study.Y);
                if (!lines.TryGetValue(number, out var xs)) lines[number] = xs = new List<double>();
                xs.Add(study.X);
            }

            return new Venue(
                beams.Select(kv => (kv.Key, kv.Value.Average())).OrderByDescending(b => b.Item2).ToList(),
                lines.Select(kv => (kv.Key, kv.Value.Average())).OrderBy(l => l.Item2).ToList(),
                all);
        }
    }

    // ── Cadre et échelle ──────────────────────────────────────────
    private sealed class Frame
    {
        public double XMin, XMax, YMin, YMax;           // study axes, metres
        public float Scale, PlotLeft, PlotTop, PlotW, PlotH;
        public int Width, Height;

        public float Px(double x) => PlotLeft + (float)((x - XMin) * Scale);
        public float Py(double y) => PlotTop + (float)((YMax - y) * Scale);   // Y grows upwards
        public SKPoint At((double X, double Y) study) => new(Px(study.X), Py(study.Y));

        public float Right => PlotLeft + PlotW;
        public float Bottom => PlotTop + PlotH;
        public SKRect Plot => new(PlotLeft, PlotTop, Right, Bottom);
        public float LegendLeft => Right + EdgeR + Gap;
    }

    /// <summary>The whole charpente, and whatever the layout places beyond it.</summary>
    private static Frame Layout(Venue venue, RiggingResult result, float legendHeight)
    {
        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        void Include((double X, double Y) s)
        {
            if (!double.IsFinite(s.X) || !double.IsFinite(s.Y)) return;
            xMin = Math.Min(xMin, s.X); xMax = Math.Max(xMax, s.X);
            yMin = Math.Min(yMin, s.Y); yMax = Math.Max(yMax, s.Y);
        }

        foreach (var p in venue.Points) Include(p);
        foreach (var m in result.Mothers)
        {
            Include((StudyAxes.StudyX(m.Y), StudyAxes.StudyY(m.XL)));
            Include((StudyAxes.StudyX(m.Y), StudyAxes.StudyY(m.XR)));
        }
        foreach (var p in result.Palonniers)
        {
            Include(StudyAxes.ToStudy(p.A.X, p.A.Y));
            Include(StudyAxes.ToStudy(p.B.X, p.B.Y));
        }
        foreach (var c in result.Charges) Include(StudyAxes.ToStudy(c.X, c.Y));

        if (xMin > xMax || yMin > yMax) (xMin, xMax, yMin, yMax) = (-1, 1, -1, 1);   // nothing at all to frame
        xMin -= PadM; xMax += PadM; yMin -= PadM; yMax += PadM;

        float scale = (float)Math.Min(PxPerMetre, Math.Min(MaxPlotPx / (xMax - xMin), MaxPlotPx / (yMax - yMin)));
        var f = new Frame
        {
            XMin = xMin, XMax = xMax, YMin = yMin, YMax = yMax,
            Scale = scale,
            PlotLeft = EdgeL,
            PlotTop = TitleH + TopBandH,
            PlotW = (float)((xMax - xMin) * scale),
            PlotH = (float)((yMax - yMin) * scale),
        };
        f.Width = (int)Math.Ceiling(f.Right + EdgeR + Gap + LegendW + RightMargin);
        f.Height = (int)Math.Ceiling(Math.Max(f.Bottom + BottomBandH + FooterH, f.PlotTop + legendHeight + FooterH));
        return f;
    }

    // ── Charpente : poutres, lignes de points, points ─────────────
    private static void DrawVenue(SKCanvas canvas, Frame f, Venue venue)
    {
        using var beamPaint = new SKPaint { Color = BeamColor, StrokeWidth = 4f, IsAntialias = true, IsStroke = true };
        using var linePaint = new SKPaint { Color = PointLineColor, StrokeWidth = 2f, IsAntialias = true, IsStroke = true };
        using var dot       = new SKPaint { Color = GridPtColor, IsAntialias = true };
        using var dotEdge   = new SKPaint { Color = GridPtEdge, IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };

        // The point lines, which the mother lines follow, then the beams across them.
        foreach (var (_, x) in venue.PointLines)
            canvas.DrawLine(f.Px(x), f.PlotTop, f.Px(x), f.Bottom, linePaint);
        foreach (var (_, y) in venue.Beams)
            canvas.DrawLine(f.PlotLeft, f.Py(y), f.Right, f.Py(y), beamPaint);
        foreach (var p in venue.Points)
        {
            var at = f.At(p);
            canvas.DrawCircle(at, 5f, dot);
            canvas.DrawCircle(at, 5f, dotEdge);
        }

        // Named as the venue figure names them: beam letters in amber boxes on both sides, point numbers in
        // red boxes above and below.
        using var beamText  = TextPaint(Ink, 22f, bold: true);
        using var pointText = TextPaint(SKColors.White, 20f, bold: true);
        foreach (var (beam, y) in venue.Beams)
        {
            Tag(canvas, f.PlotLeft - EdgeL / 2, f.Py(y), beam, BeamBoxColor, beamText);
            Tag(canvas, f.Right + EdgeR / 2, f.Py(y), beam, BeamBoxColor, beamText);
        }
        foreach (var (number, x) in venue.PointLines)
        {
            var text = number.ToString(CultureInfo.InvariantCulture);
            Tag(canvas, f.Px(x), f.PlotTop - TopBandH / 2, text, PointBoxColor, pointText);
            Tag(canvas, f.Px(x), f.Bottom + BottomBandH / 2, text, PointBoxColor, pointText);
        }
    }

    private static void Tag(SKCanvas canvas, float cx, float cy, string text, SKColor fill, SKPaint textPaint)
    {
        float tw = textPaint.MeasureText(text);
        float w = Math.Max(34f, tw + 16f), h = 32f;
        var rect = new SKRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
        using var box  = new SKPaint { Color = fill, IsAntialias = true };
        using var edge = new SKPaint { Color = Darken(fill), IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        canvas.DrawRoundRect(rect, 5f, 5f, box);
        canvas.DrawRoundRect(rect, 5f, 5f, edge);
        canvas.DrawText(text, cx - tw / 2, cy + textPaint.TextSize * 0.36f, textPaint);
    }

    // ── Origine E5 et axes ────────────────────────────────────────
    private static List<SKRect> DrawOrigin(SKCanvas canvas, Frame f)
    {
        // Short, and under the trusses, so that nothing placed is hidden by it: the X letter stays short of
        // point line 6. The legend repeats the axes in full.
        var o = f.At((0, 0));
        const float len = 56f;
        using var axis = new SKPaint { Color = Ink, StrokeWidth = 2.5f, IsAntialias = true, IsStroke = true, StrokeCap = SKStrokeCap.Round };
        using var text = TextPaint(Ink, 20f, bold: true);
        Arrow(canvas, o, new SKPoint(o.X + len, o.Y), axis);
        Arrow(canvas, o, new SKPoint(o.X, o.Y - len), axis);
        float xw = text.MeasureText("X"), yw = text.MeasureText("Y");
        canvas.DrawText("X", o.X + len + 6f, o.Y + 7f, text);
        canvas.DrawText("Y", o.X - yw / 2, o.Y - len - 8f, text);
        return new List<SKRect>
        {
            new(o.X, o.Y - 8f, o.X + len + 8f + xw, o.Y + 8f),      // the X arrow and its letter
            new(o.X - 8f, o.Y - len - 30f, o.X + 8f, o.Y),          // the Y arrow and its letter
        };
    }

    private static void Arrow(SKCanvas canvas, SKPoint from, SKPoint to, SKPaint paint)
    {
        canvas.DrawLine(from, to, paint);
        float dx = to.X - from.X, dy = to.Y - from.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        float ux = dx / len, uy = dy / len;
        const float head = 13f;
        canvas.DrawLine(to.X, to.Y, to.X - head * ux - 0.5f * head * uy, to.Y - head * uy + 0.5f * head * ux, paint);
        canvas.DrawLine(to.X, to.Y, to.X - head * ux + 0.5f * head * uy, to.Y - head * uy - 0.5f * head * ux, paint);
    }

    // ── Lignes de mother ──────────────────────────────────────────
    private static List<SKRect> DrawMothers(SKCanvas canvas, Frame f, List<Mother> mothers)
    {
        var bodies = new List<SKRect>();
        using var edge = new SKPaint { Color = Darken(MotherColor), StrokeWidth = 12f, IsAntialias = true, IsStroke = true, StrokeCap = SKStrokeCap.Butt };
        using var body = new SKPaint { Color = MotherColor, StrokeWidth = 9f, IsAntialias = true, IsStroke = true, StrokeCap = SKStrokeCap.Butt };

        // One continuous segment per line, at its point line, from its end by the first beam (XL, towards J)
        // to its end by the last (XR, towards A), overhang included. Butt ends: the drawn length is its length.
        foreach (var m in mothers)
        {
            float x = f.Px(StudyAxes.StudyX(m.Y));
            float y1 = f.Py(StudyAxes.StudyY(m.XL)), y2 = f.Py(StudyAxes.StudyY(m.XR));
            canvas.DrawLine(x, y1, x, y2, edge);
            canvas.DrawLine(x, y1, x, y2, body);
            bodies.Add(new SKRect(x - 6f, Math.Min(y1, y2), x + 6f, Math.Max(y1, y2)));
        }

        // Its supports: every beam it rests on.
        foreach (var m in mothers)
        {
            float x = f.Px(StudyAxes.StudyX(m.Y));
            foreach (var (_, beamX) in m.Columns)
                Triangle(canvas, new SKPoint(x, f.Py(StudyAxes.StudyY(beamX))), 9f, up: true, SupportColor);
        }

        return bodies;
    }

    // ── Palonniers ────────────────────────────────────────────────
    private static SKColor ColourOf(Palonnier p) =>
        p.Direction == PalonnierDirection.Biais ? InclinedColor : AlongColor;

    private static List<SKRect> DrawPalonniers(SKCanvas canvas, Frame f, List<Palonnier> pals)
    {
        var bodies = new List<SKRect>();
        using var dash = new SKPaint
        {
            Color = LeaderColor, StrokeWidth = 1.2f, IsAntialias = true, IsStroke = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 5f, 4f }, 0),
        };

        foreach (var p in pals)
        {
            var colour = ColourOf(p);
            var a = f.At(StudyAxes.ToStudy(p.A.X, p.A.Y));
            var b = f.At(StudyAxes.ToStudy(p.B.X, p.B.Y));
            using var edge = new SKPaint { Color = Darken(colour), StrokeWidth = 7f, IsAntialias = true, IsStroke = true, StrokeCap = SKStrokeCap.Round };
            using var body = new SKPaint { Color = colour, StrokeWidth = 5f, IsAntialias = true, IsStroke = true, StrokeCap = SKStrokeCap.Round };
            canvas.DrawLine(a, b, edge);
            canvas.DrawLine(a, b, body);
            bodies.AddRange(AlongSegment(a, b, 4f));

            // Each charge, to where it bears on the palonnier.
            if (p.Span > 1e-9)
            {
                foreach (var c in p.Charges)
                {
                    double s = ProjOnAxis(c.X, c.Y, p.A.X, p.A.Y, p.B.X, p.B.Y) / p.Span;
                    var on = StudyAxes.ToStudy(p.A.X + s * (p.B.X - p.A.X), p.A.Y + s * (p.B.Y - p.A.Y));
                    canvas.DrawLine(f.At(StudyAxes.ToStudy(c.X, c.Y)), f.At(on), dash);
                }
            }
        }

        // Where each hangs: its two ends and every line it is hooked to in between.
        foreach (var p in pals)
        {
            var hooks = p.Supports.Count > 0
                ? p.Supports.Select(s => (s.X, s.Y)).ToList()
                : new List<(double X, double Y)> { p.A, p.B };
            foreach (var (hx, hy) in hooks)
                Triangle(canvas, f.At(StudyAxes.ToStudy(hx, hy)), 7f, up: false, ColourOf(p));
        }

        return bodies;
    }

    /// <summary>Small boxes along a segment, so that a label can be kept off it whatever its angle.</summary>
    private static IEnumerable<SKRect> AlongSegment(SKPoint a, SKPoint b, float half)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        int steps = Math.Max(1, (int)Math.Ceiling(MathF.Sqrt(dx * dx + dy * dy) / half));
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float cx = a.X + t * dx, cy = a.Y + t * dy;
            yield return new SKRect(cx - half, cy - half, cx + half, cy + half);
        }
    }

    // ── Points d'accroche chargés ─────────────────────────────────
    private static float LoadRadius(double kg) => Math.Clamp(11f + (float)Math.Abs(kg) * 0.005f, 11f, 22f);

    private static List<SKRect> DrawGridLoads(SKCanvas canvas, Frame f, Dictionary<(string Col, int Row), double> loads)
    {
        // After the lines, as a ring around the support it marks: a load reaches a point only through a line.
        var marks = new List<SKRect>();
        foreach (var ((col, row), kg) in loads)
        {
            if (Math.Abs(kg) <= 0.5 || !Grid.TryGetValue((col, row), out var g)) continue;
            var at = f.At(StudyAxes.ToStudy(g.X, g.Y));
            float r = LoadRadius(kg);
            var colour = kg < 0 ? UpliftColor : LoadPtColor;
            using var fill = new SKPaint { Color = colour.WithAlpha(55), IsAntialias = true };
            using var ring = new SKPaint { Color = colour, IsAntialias = true, IsStroke = true, StrokeWidth = 3f };
            canvas.DrawCircle(at, r, fill);
            canvas.DrawCircle(at, r, ring);
            marks.Add(Box(at, r));
        }
        return marks;
    }

    // ── Charges ───────────────────────────────────────────────────
    private static float ChargeRadius(double kg) => Math.Clamp(4f + (float)kg * 0.01f, 5f, 12f);

    private static List<SKRect> DrawCharges(SKCanvas canvas, Frame f, RiggingResult result)
    {
        var marks = new List<SKRect>();
        using var edge = new SKPaint { Color = Ink, IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };

        // On a palonnier: a diamond.
        using var palFill = new SKPaint { Color = PalChargeColor, IsAntialias = true };
        foreach (var c in result.Palonniers.SelectMany(p => p.Charges))
        {
            var at = f.At(StudyAxes.ToStudy(c.X, c.Y));
            float r = ChargeRadius(c.Weight);
            using var path = Diamond(at, r);
            canvas.DrawPath(path, palFill);
            canvas.DrawPath(path, edge);
            marks.Add(Box(at, r));
        }

        // Hung straight on a mother line: a ringed disc.
        using var lineFill = new SKPaint { Color = LineChargeColor, IsAntialias = true };
        using var ring = new SKPaint { Color = SKColors.White, IsAntialias = true, IsStroke = true, StrokeWidth = 2f };
        foreach (var c in result.Mothers.SelectMany(m => m.DirectCharges))
        {
            var at = f.At(StudyAxes.ToStudy(c.X, c.Y));
            float r = ChargeRadius(c.Weight);
            canvas.DrawCircle(at, r, lineFill);
            canvas.DrawCircle(at, Math.Max(1f, r - 3f), ring);
            canvas.DrawCircle(at, r, edge);
            marks.Add(Box(at, r));
        }

        // Carried by nothing: a cross.
        using var cross = new SKPaint { Color = UncoveredColor, IsAntialias = true, IsStroke = true, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
        foreach (var c in result.Uncovered)
        {
            var at = f.At(StudyAxes.ToStudy(c.X, c.Y));
            const float r = 8f;
            canvas.DrawLine(at.X - r, at.Y - r, at.X + r, at.Y + r, cross);
            canvas.DrawLine(at.X - r, at.Y + r, at.X + r, at.Y - r, cross);
            marks.Add(Box(at, r));
        }

        return marks;
    }

    // ── Étiquettes ────────────────────────────────────────────────
    private static void LabelMothers(SKCanvas canvas, Frame f, LabelPlacer labels, List<Mother> mothers)
    {
        using var text = TextPaint(Darken(MotherColor), 17f, bold: true);
        foreach (var m in mothers)
        {
            float x = f.Px(StudyAxes.StudyX(m.Y));
            float top = f.Py(StudyAxes.StudyY(m.XL)), bottom = f.Py(StudyAxes.StudyY(m.XR));
            string name = $"M{m.Id}";
            float w = text.MeasureText(name), h = text.TextSize;

            // Beside the line, from its middle outwards: its ends are on beams, where the loads are ringed.
            float mid = (top + bottom) / 2 + h * 0.35f, half = Math.Abs(bottom - top) / 2;
            var candidates = new List<(float, float)>();
            for (float d = 0; d <= half + h; d += h + 6f)
            {
                candidates.Add((x + 10f, mid - d));
                candidates.Add((x - 10f - w, mid - d));
                if (d > 0)
                {
                    candidates.Add((x + 10f, mid + d));
                    candidates.Add((x - 10f - w, mid + d));
                }
            }
            candidates.Add((x - w / 2, top - 30f));
            candidates.Add((x - w / 2, bottom + h + 28f));
            labels.Place(canvas, name, text, candidates);
        }
    }

    private static void LabelPalonniers(SKCanvas canvas, Frame f, LabelPlacer labels, List<Palonnier> pals)
    {
        foreach (var p in pals)
        {
            using var text = TextPaint(Darken(ColourOf(p)), 15f, bold: true);
            var a = f.At(StudyAxes.ToStudy(p.A.X, p.A.Y));
            var b = f.At(StudyAxes.ToStudy(p.B.X, p.B.Y));
            var mid = new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            string name = $"P{p.Id}";
            float w = text.MeasureText(name), h = text.TextSize;

            // Above or below the bar first, rather than over it.
            var candidates = new List<(float, float)> { (mid.X - w / 2, mid.Y - 10f), (mid.X - w / 2, mid.Y + h + 6f) };
            candidates.AddRange(Around(mid, 5f, w, h));
            labels.Place(canvas, name, text, candidates);
        }
    }

    private static void LabelGridLoads(SKCanvas canvas, Frame f, LabelPlacer labels, Dictionary<(string Col, int Row), double> loads)
    {
        using var down = TextPaint(LoadTextColor, 15f, bold: true);
        using var up   = TextPaint(UpliftColor, 15f, bold: true);
        foreach (var ((col, row), kg) in loads.OrderByDescending(kv => Math.Abs(kv.Value)))
        {
            if (Math.Abs(kg) <= 0.5 || !Grid.TryGetValue((col, row), out var g)) continue;
            var at = f.At(StudyAxes.ToStudy(g.X, g.Y));
            var paint = kg < 0 ? up : down;
            string label = KgText(kg);
            labels.Place(canvas, label, paint, Around(at, LoadRadius(kg), paint.MeasureText(label), paint.TextSize), at);
        }
    }

    private static void LabelCharges(SKCanvas canvas, Frame f, LabelPlacer labels, RiggingResult result)
    {
        using var onPalonnier = TextPaint(Ink, 13f);
        using var onLine      = TextPaint(Darken(LineChargeColor), 13f, bold: true);
        using var carriedBy0  = TextPaint(UncoveredColor, 13f, bold: true);

        void One(Charge c, SKPaint paint, string suffix = "")
        {
            var at = f.At(StudyAxes.ToStudy(c.X, c.Y));
            string label = $"{c.Name} · {KgText(c.Weight)} kg{suffix}";
            labels.Place(canvas, label, paint, Around(at, ChargeRadius(c.Weight), paint.MeasureText(label), paint.TextSize), at);
        }

        foreach (var c in result.Mothers.SelectMany(m => m.DirectCharges)) One(c, onLine);
        foreach (var c in result.Palonniers.SelectMany(p => p.Charges)) One(c, onPalonnier);
        foreach (var c in result.Uncovered) One(c, carriedBy0, " (non portée)");
    }

    /// <summary>
    /// Where a label may go around a mark of radius <paramref name="r"/>: diagonally above or below it first,
    /// clear of the beam, line or palonnier through it; then beside it; then further out.
    /// </summary>
    private static IEnumerable<(float Left, float Baseline)> Around(SKPoint a, float r, float w, float h)
    {
        float middle = a.Y + h * 0.35f;
        for (int ring = 0; ring < 6; ring++)
        {
            float d = ring * (h + 4f);
            yield return (a.X + r + 2f, a.Y - r - 2f - d);              // above right
            yield return (a.X - r - 2f - w, a.Y - r - 2f - d);          // above left
            yield return (a.X + r + 2f, a.Y + r + h + 2f + d);          // below right
            yield return (a.X - r - 2f - w, a.Y + r + h + 2f + d);      // below left
            if (ring == 0)
            {
                yield return (a.X + r + 4f, middle);                    // beside
                yield return (a.X - r - 4f - w, middle);
            }
            yield return (a.X - w / 2f, a.Y - r - 4f - d);              // centred above
            yield return (a.X - w / 2f, a.Y + r + h + 4f + d);          // centred below
        }
    }

    /// <summary>Places labels where they overlap nothing already placed, within the plot.</summary>
    private sealed class LabelPlacer : IDisposable
    {
        private readonly List<SKRect> _taken = new();
        private readonly SKRect _area;
        private readonly SKPaint _halo = new()
        {
            Color = SKColors.White, IsAntialias = true, IsStroke = true, StrokeWidth = 4f, StrokeJoin = SKStrokeJoin.Round,
        };
        private readonly SKPaint _leader = new() { Color = LeaderColor, IsAntialias = true, IsStroke = true, StrokeWidth = 1.2f };

        public LabelPlacer(SKRect area) => _area = area;

        public int Skipped { get; private set; }

        public void Reserve(SKRect r) => _taken.Add(r);

        /// <summary>
        /// Draws <paramref name="text"/> at the first candidate (left, baseline) where it fits; with a leader
        /// back to <paramref name="anchor"/> when it had to go further than beside it.
        /// </summary>
        public bool Place(SKCanvas canvas, string text, SKPaint paint,
                          IEnumerable<(float Left, float Baseline)> candidates, SKPoint? anchor = null)
        {
            float w = paint.MeasureText(text), h = paint.TextSize;
            foreach (var (left, baseline) in candidates)
            {
                var box = new SKRect(left - 2f, baseline - h, left + w + 2f, baseline + h * 0.28f);
                if (!_area.Contains(box) || _taken.Any(t => t.IntersectsWith(box))) continue;

                _taken.Add(box);
                if (anchor is { } a)
                {
                    float nx = Math.Clamp(a.X, box.Left, box.Right), ny = Math.Clamp(a.Y, box.Top, box.Bottom);
                    float dx = nx - a.X, dy = ny - a.Y;
                    if (MathF.Sqrt(dx * dx + dy * dy) > h * 1.6f) canvas.DrawLine(a.X, a.Y, nx, ny, _leader);
                }
                _halo.TextSize = paint.TextSize;
                _halo.FakeBoldText = paint.FakeBoldText;
                canvas.DrawText(text, left, baseline, _halo);
                canvas.DrawText(text, left, baseline, paint);
                return true;
            }

            Skipped++;
            return false;
        }

        public void Dispose()
        {
            _halo.Dispose();
            _leader.Dispose();
        }
    }

    // ── Titre ─────────────────────────────────────────────────────
    private sealed record Counts(
        int Lines, int Along, int Inclined, int OnPalonniers, int OnLines, int Uncovered, int LoadedPoints, bool Uplift)
    {
        public static Counts Of(RiggingResult r) => new(
            r.Mothers.Count,
            r.Palonniers.Count(p => p.Direction == PalonnierDirection.Y),
            r.Palonniers.Count(p => p.Direction == PalonnierDirection.Biais),
            r.Palonniers.Sum(p => p.Charges.Count),
            r.Mothers.Sum(m => m.DirectCharges.Count),
            r.Uncovered.Count,
            r.GridLoads.Count(kv => kv.Value > 0.5),
            r.GridLoads.Any(kv => kv.Value < -0.5));
    }

    private static void DrawTitle(SKCanvas canvas, RiggingResult r)
    {
        using var title = TextPaint(Ink, 30f, bold: true);
        using var sub   = TextPaint(SubInk, 18f);
        var c = Counts.Of(r);
        const float x = 24f;

        canvas.DrawText("PLAN DE RIGGING — ANTARÈS", x, 46f, title);
        canvas.DrawText(
            $"{c.Lines} ligne(s) de mother  ·  {c.Along} palonnier(s) le long des poutres  ·  "
            + $"{c.Inclined} palonnier(s) incliné(s)  ·  {c.OnLines} charge(s) sur les lignes de mother",
            x, 82f, sub);

        // A result with no charge at all has nothing to balance: said as such, not as an imbalance.
        string balance = r.Charges.Count == 0 && r.GridLoads.Count == 0 ? "aucune charge"
                       : r.BalanceOk ? "équilibre vérifié" : "déséquilibre";
        var second = $"{r.Charges.Count} charge(s), {KgText(r.TotalCharges)} kg  ·  total aux points d'accroche "
                     + $"{KgText(r.TotalGrid)} kg  ·  {balance}";
        if (r.Uncovered.Count > 0) second += $"  ·  {r.Uncovered.Count} charge(s) non portée(s)";
        if (r.Options.OrthogonalOnly) second += "  ·  orientations orthogonales uniquement";
        canvas.DrawText(second, x, 110f, sub);

        canvas.DrawText("Axes des études : origine E5, X le long des poutres vers le point 9, Y en travers vers la poutre J ; "
                        + "coordonnées en m.", x, 136f, sub);
    }

    // ── Légende ───────────────────────────────────────────────────
    private sealed record LegendEntry(Action<SKCanvas, SKPoint> Symbol, string Text);

    private static List<LegendEntry> LegendEntries(RiggingResult r)
    {
        var c = Counts.Of(r);
        var list = new List<LegendEntry>
        {
            new(SampleLine(MotherColor, 9f, 0f, 13f), $"Lignes de mother ({c.Lines})"),
            new((cv, p) => Triangle(cv, p, 9f, up: true, SupportColor), "Appui d'une ligne sur une poutre"),
            new(SampleLine(AlongColor, 5f, 18f, 0f), $"Palonniers le long des poutres ({c.Along})"),
            new(SampleLine(InclinedColor, 5f, 16f, -10f), $"Palonniers inclinés ({c.Inclined})"),
            new((cv, p) => Triangle(cv, p, 7f, up: false, AlongColor), "Accroche d'un palonnier à une ligne"),
            new(SampleDiamond, $"Charges sur palonnier ({c.OnPalonniers})"),
            new(SampleDisc, $"Charges sur une ligne de mother ({c.OnLines})"),
        };
        if (c.Uncovered > 0) list.Add(new(SampleCross, $"Charges non portées ({c.Uncovered})"));
        list.Add(new(SampleLoad(LoadPtColor), $"Points chargés ({c.LoadedPoints}) : charge scénique en kg"));
        if (c.Uplift) list.Add(new(SampleLoad(UpliftColor), "Point soulevé : charge négative"));
        list.Add(new(SampleGridPoint, "Point d'accroche de la charpente"));
        return list;
    }

    private static float LegendHeight(int entries) => 64f + entries * LegendStep + 250f;

    private static void DrawLegend(SKCanvas canvas, Frame f, List<LegendEntry> entries)
    {
        float left = f.LegendLeft, top = f.PlotTop;
        var panel = new SKRect(left, top, left + LegendW, top + LegendHeight(entries.Count));
        using var bg     = new SKPaint { Color = C("#fafafa"), IsAntialias = true };
        using var border = new SKPaint { Color = C("#d0d0d0"), IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        canvas.DrawRoundRect(panel, 8f, 8f, bg);
        canvas.DrawRoundRect(panel, 8f, 8f, border);

        using var heading = TextPaint(Ink, 22f, bold: true);
        using var text    = TextPaint(Ink, 17f);
        canvas.DrawText("Légende", left + 18f, top + 36f, heading);

        float y = top + 64f + LegendStep / 2;
        foreach (var e in entries)
        {
            e.Symbol(canvas, new SKPoint(left + 40f, y));
            canvas.DrawText(e.Text, left + 76f, y + 6f, text);
            y += LegendStep;
        }

        // The axes, as the studies draw them.
        y += 24f;
        canvas.DrawText("Axes des études", left + 18f, y, heading);
        var o = new SKPoint(left + 44f, y + 126f);
        using var axis = new SKPaint { Color = Ink, StrokeWidth = 3f, IsAntialias = true, IsStroke = true, StrokeCap = SKStrokeCap.Round };
        using var bold = TextPaint(Ink, 20f, bold: true);
        Arrow(canvas, o, new SKPoint(o.X + 80f, o.Y), axis);
        Arrow(canvas, o, new SKPoint(o.X, o.Y - 80f), axis);
        canvas.DrawText("X", o.X + 88f, o.Y + 7f, bold);
        canvas.DrawText("Y", o.X - 6f, o.Y - 90f, bold);
        canvas.DrawText("origine E5", o.X - 16f, o.Y + 26f, text);
        canvas.DrawText("X : le long des poutres, vers le point 9", o.X + 120f, o.Y - 52f, text);
        canvas.DrawText("Y : en travers, vers la poutre J", o.X + 120f, o.Y - 26f, text);

        // The scale: a round number of metres near 200 px.
        int metres = new[] { 1, 2, 5, 10, 20, 50, 100 }.LastOrDefault(m => m * f.Scale <= 260f);
        if (metres == 0) metres = 1;
        float sy = o.Y + 66f, x0 = left + 24f, x1 = x0 + metres * f.Scale;
        using var bar = new SKPaint { Color = Ink, StrokeWidth = 4f, IsAntialias = true, IsStroke = true };
        canvas.DrawLine(x0, sy, x1, sy, bar);
        canvas.DrawLine(x0, sy - 8f, x0, sy + 8f, bar);
        canvas.DrawLine(x1, sy - 8f, x1, sy + 8f, bar);
        canvas.DrawText($"{metres} m", x1 + 12f, sy + 7f, text);
    }

    private static Action<SKCanvas, SKPoint> SampleLine(SKColor colour, float width, float dx, float dy) => (canvas, p) =>
    {
        using var edge = new SKPaint { Color = Darken(colour), StrokeWidth = width + 3f, IsAntialias = true, IsStroke = true };
        using var body = new SKPaint { Color = colour, StrokeWidth = width, IsAntialias = true, IsStroke = true };
        canvas.DrawLine(p.X - dx, p.Y - dy, p.X + dx, p.Y + dy, edge);
        canvas.DrawLine(p.X - dx, p.Y - dy, p.X + dx, p.Y + dy, body);
    };

    private static void SampleDiamond(SKCanvas canvas, SKPoint p)
    {
        using var fill = new SKPaint { Color = PalChargeColor, IsAntialias = true };
        using var edge = new SKPaint { Color = Ink, IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        using var path = Diamond(p, 9f);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, edge);
    }

    private static void SampleDisc(SKCanvas canvas, SKPoint p)
    {
        using var fill = new SKPaint { Color = LineChargeColor, IsAntialias = true };
        using var ring = new SKPaint { Color = SKColors.White, IsAntialias = true, IsStroke = true, StrokeWidth = 2f };
        using var edge = new SKPaint { Color = Ink, IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        canvas.DrawCircle(p, 9f, fill);
        canvas.DrawCircle(p, 6f, ring);
        canvas.DrawCircle(p, 9f, edge);
    }

    private static void SampleCross(SKCanvas canvas, SKPoint p)
    {
        using var cross = new SKPaint { Color = UncoveredColor, IsAntialias = true, IsStroke = true, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(p.X - 8f, p.Y - 8f, p.X + 8f, p.Y + 8f, cross);
        canvas.DrawLine(p.X - 8f, p.Y + 8f, p.X + 8f, p.Y - 8f, cross);
    }

    private static Action<SKCanvas, SKPoint> SampleLoad(SKColor colour) => (canvas, p) =>
    {
        using var fill = new SKPaint { Color = colour.WithAlpha(55), IsAntialias = true };
        using var ring = new SKPaint { Color = colour, IsAntialias = true, IsStroke = true, StrokeWidth = 3f };
        canvas.DrawCircle(p, 12f, fill);
        canvas.DrawCircle(p, 12f, ring);
    };

    private static void SampleGridPoint(SKCanvas canvas, SKPoint p)
    {
        using var dot  = new SKPaint { Color = GridPtColor, IsAntialias = true };
        using var edge = new SKPaint { Color = GridPtEdge, IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        canvas.DrawCircle(p, 5f, dot);
        canvas.DrawCircle(p, 5f, edge);
    }

    // ── Pied de page ──────────────────────────────────────────────
    private static void DrawFooter(SKCanvas canvas, Frame f, int skipped)
    {
        using var text = TextPaint(SubInk, 16f);
        float x = 24f, y = f.Bottom + BottomBandH + 28f;
        canvas.DrawText("Charges en kg. Charges aux points : charges scéniques seules, sans le poids propre des palonniers "
                        + "et des mothers.", x, y, text);
        if (skipped > 0)
            canvas.DrawText($"{skipped} étiquette(s) non affichée(s) faute de place : les valeurs sont dans le rapport Excel.",
                            x, y + 24f, text);
    }

    // ── Formes et outils ──────────────────────────────────────────
    private static void Triangle(SKCanvas canvas, SKPoint c, float r, bool up, SKColor colour)
    {
        float s = up ? 1f : -1f;
        using var path = new SKPath();
        path.MoveTo(c.X, c.Y - s * r);
        path.LineTo(c.X + r, c.Y + s * r * 0.8f);
        path.LineTo(c.X - r, c.Y + s * r * 0.8f);
        path.Close();
        using var fill = new SKPaint { Color = colour, IsAntialias = true };
        using var edge = new SKPaint { Color = SKColors.White, IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, edge);
    }

    private static SKPath Diamond(SKPoint c, float r)
    {
        var path = new SKPath();
        path.MoveTo(c.X, c.Y - r);
        path.LineTo(c.X + r, c.Y);
        path.LineTo(c.X, c.Y + r);
        path.LineTo(c.X - r, c.Y);
        path.Close();
        return path;
    }

    private static SKRect Box(SKPoint c, float r) => new(c.X - r, c.Y - r, c.X + r, c.Y + r);

    private static SKPaint TextPaint(SKColor colour, float size, bool bold = false) =>
        new() { Color = colour, TextSize = size, IsAntialias = true, FakeBoldText = bold };

    private static SKColor Darken(SKColor c) =>
        new((byte)(c.Red * 0.7), (byte)(c.Green * 0.7), (byte)(c.Blue * 0.7), c.Alpha);

    // Rounded as the Excel report shows kilograms: half away from zero.
    private static string KgText(double kg) =>
        (Math.Round(kg, MidpointRounding.AwayFromZero) + 0.0).ToString("N0", Fr);
}
