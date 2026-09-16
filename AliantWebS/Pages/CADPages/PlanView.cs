// =============================================================
// PlanView.cs — Where SVGComponent draws things, and how the background picture sits under them.
//
// The plan is drawn HORIZONTAL, the way the hall's own plan is drawn: the beams J..A as vertical lines,
// J at the left and A at the right, point line 9 at the top and 1 at the bottom. The hall is twice as
// long across the beams as along them, so that is the view that fills a screen.
//
// The AXES are the studies' (Helpers.StudyAxes), and they belong to the beams, not to the screen: X runs
// ALONG the beams towards point 9, Y ACROSS them towards J, from the event's Point0 (E5 at Antarès). In
// this view that puts X pointing UP and Y pointing LEFT - a quarter turn from the studies' Figure 2, which
// lays the beams horizontal instead. The drawing is turned; the axes are not re-labelled: a charge at
// X = 5 m is 5 m towards point 9 on either.
//
// It was drawn in Figure 2's orientation for a while (2026-09-11 to 09-12): beams horizontal, the whole
// plan standing on end. That was asked for the axes, never for the view, and it is undone here - in the
// one place the screen orientation lives, ToSvg and FromSvg.
//
// SVGComponent keeps its "real" coordinates as they always were: the grid's axes, in absolute
// millimetres, as CharpenteCoordinates and the sensors' X/Y attributes are. One SVG unit is the same
// length in every direction, so what is measured on the screen - a drawn line, the distance to a mother -
// is true, and hand drawing, hit tests and what is saved do not change.
//
// The background picture is drawn this way already, so it is laid on the plan with a scale and a shift
// only - neither turned nor mirrored - placed by the positions of its own grid lines, measured on it, so
// they fall on the charpente points.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AliantWebS.Models;
using Helpers;

namespace AliantWebS.Pages.CADPages;

public static class PlanView
{
    public const double MmPerM = 1000.0;

    /// <summary>
    /// SVG user units per millimetre - the scale the plan has always been drawn at: 754 units across the
    /// 68,4 m the old view spanned. Every marker, stroke and text on the plan is sized in SVG units, so
    /// keeping the scale keeps them the size people know.
    /// </summary>
    public const double SvgPerMm = 754.0 / 68397.0;

    /// <summary>
    /// A length on the plan, SVG user units, in metres. One SVG unit is the same length in every direction, so this
    /// holds along any line: the pick tolerance of a click, set in screen pixels, is turned into metres by it.
    /// </summary>
    public static double SvgToMetres(double svgUnits) => svgUnits / SvgPerMm / MmPerM;

    // ── Grid <-> screen ─────────────────────────────────────────────────────────────────────────

    /// <summary>A position in the grid's axes, absolute millimetres, on the plan (SVG user units).</summary>
    public static (double X, double Y) ToSvg(double gridXmm, double gridYmm, double point0Xm, double point0Ym)
    {
        var (studyX, studyY) = StudyAxes.ToStudy(gridXmm - point0Xm * MmPerM, gridYmm - point0Ym * MmPerM);

        // Horizontal. Study X, along the beams towards point 9, goes UP the screen - and SVG's y points
        // down, hence the minus. Study Y, across the beams towards J, goes LEFT - hence the other one.
        // A right-handed pair either way: the plan is turned, never mirrored.
        return (-studyY * SvgPerMm, -studyX * SvgPerMm);
    }

    /// <summary>A point of the plan (SVG user units), in the grid's axes, absolute millimetres.</summary>
    public static (double X, double Y) FromSvg(double svgX, double svgY, double point0Xm, double point0Ym)
    {
        // The inverse of ToSvg, term for term: up is study X, left is study Y.
        var (gridX, gridY) = StudyAxes.ToGrid(-svgY / SvgPerMm, -svgX / SvgPerMm);
        return (gridX + point0Xm * MmPerM, gridY + point0Ym * MmPerM);
    }

    /// <summary>A position in the grid's axes, absolute millimetres, in the studies' axes: metres from Point0.</summary>
    public static (double X, double Y) StudyMetres(double gridXmm, double gridYmm, double point0Xm, double point0Ym) =>
        StudyAxes.ToStudy(gridXmm / MmPerM - point0Xm, gridYmm / MmPerM - point0Ym);

    // ── The background picture ──────────────────────────────────────────────────────────────────

    public const string BackgroundImage = "PLAN ANTARES-ALIANT.jpg";
    public const int ImageWidthPx = 7017;
    public const int ImageHeightPx = 3720;

    // Measured on the picture on 2026-09-11: the centre, in pixels, of each of its green grid lines - the
    // beams are columns (u, to the right), the point lines rows (v, down). Checked against its black
    // charpente dots: the fitted intersections fall within 2 px (2 cm) of the dots, except on lines 2, 3
    // and 7, where the picture draws the dots up to 6 px from the surveyed position. E1's dot is drawn
    // 30 cm right of beam E; the survey puts E1 on the beam, and so does the plan.
    private static readonly (char Beam, double U)[] BeamLinesPx =
    {
        ('J', 306.98), ('I', 695.97), ('H', 1433.69), ('G', 2223.52), ('F', 2967.45),
        ('E', 3700.55), ('D', 4434.99), ('C', 5177.53), ('B', 5964.85), ('A', 6703.73),
    };

    private static readonly (int Line, double V)[] PointLinesPx =
    {
        (9, 304.98), (8, 725.99), (7, 1099.99), (6, 1478.99), (5, 1858.98),
        (4, 2237.53), (3, 2622.50), (2, 2991.54), (1, 3412.65),
    };

    // The picture's own labels, rings of 43,5 px radius: the beam letters on one row above line 9 (v), the
    // point numbers on one column left of beam J (u).
    private const double BeamLabelsV = 171.5;
    private const double PointLabelsU = 152.5;

    // u = slope * grid X (mm) + offset and v = slope * grid Y (mm) + offset, least squares over the lines
    // above against CharpenteCoordinates. Both slopes are 0,10253 px per mm to within 0,02 %: the picture is
    // to scale. Declared after the measurements, which static initialisation reads in order.
    private static readonly (double Slope, double Offset) PixelU =
        Fit(BeamLinesPx.Select(b => (BeamXmm(b.Beam), b.U)));

    private static readonly (double Slope, double Offset) PixelV =
        Fit(PointLinesPx.Select(l => (LineYmm(l.Line), l.V)));

    private static double BeamXmm(char beam) => CharpenteCoordinates.Points[$"{beam}5"].X * MmPerM;

    private static double LineYmm(int line) => CharpenteCoordinates.Points[$"E{line}"].Y * MmPerM;

    private static (double Slope, double Offset) Fit(IEnumerable<(double Mm, double Px)> points)
    {
        var p = points.ToList();
        double meanMm = p.Average(t => t.Mm), meanPx = p.Average(t => t.Px);
        double sxy = p.Sum(t => (t.Mm - meanMm) * (t.Px - meanPx));
        double sxx = p.Sum(t => (t.Mm - meanMm) * (t.Mm - meanMm));
        double slope = sxy / sxx;
        return (slope, meanPx - slope * meanMm);
    }

    /// <summary>A pixel of the picture (u to the right, v down), in the grid's axes, absolute millimetres.</summary>
    public static (double X, double Y) PixelToGrid(double u, double v) =>
        ((u - PixelU.Offset) / PixelU.Slope, (v - PixelV.Offset) / PixelV.Slope);

    /// <summary>
    /// The SVG transform that lays the picture - an image element at 0,0 of its own pixel size - on the plan:
    /// a scale and a shift. The picture draws the hall horizontal, as the plan does, so nothing turns it; the
    /// matrix is still computed from the calibration rather than written out, so it stays right if the view
    /// is ever turned again.
    /// </summary>
    public static string ImageTransform(double point0Xm, double point0Ym)
    {
        // Both steps are affine, so three points give the whole map: x' = a·u + c·v + e, y' = b·u + d·v + f.
        (double X, double Y) At(double u, double v)
        {
            var (gx, gy) = PixelToGrid(u, v);
            return ToSvg(gx, gy, point0Xm, point0Ym);
        }

        var o = At(0, 0);
        var alongU = At(1, 0);
        var alongV = At(0, 1);
        double a = alongU.X - o.X, b = alongU.Y - o.Y, c = alongV.X - o.X, d = alongV.Y - o.Y;
        return FormattableString.Invariant($"matrix({a} {b} {c} {d} {o.X} {o.Y})");
    }

    /// <summary>
    /// What the plan shows when fitted to the view: the picture's footprint, which frames the whole charpente
    /// and its labels. (x, y, width, height) in SVG user units, as a viewBox takes them.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Extent(double point0Xm, double point0Ym)
    {
        var corners = new[]
            {
                (0.0, 0.0), ((double)ImageWidthPx, 0.0), (0.0, (double)ImageHeightPx),
                ((double)ImageWidthPx, (double)ImageHeightPx),
            }
            .Select(c =>
            {
                var (gx, gy) = PixelToGrid(c.Item1, c.Item2);
                return ToSvg(gx, gy, point0Xm, point0Ym);
            })
            .ToList();

        double x0 = corners.Min(c => c.X), x1 = corners.Max(c => c.X);
        double y0 = corners.Min(c => c.Y), y1 = corners.Max(c => c.Y);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// The picture in the studies' axes, metres from Point0, for the DXF: where the corner that was its lower
    /// left lands, and its width and height in metres.
    ///
    /// The DXF is written in the studies' axes themselves - X to its right, Y up - which is Figure 2's
    /// orientation, not the screen's. So in the DXF the picture IS turned (<see cref="ImageRotationDeg"/>):
    /// its width runs down from that corner, towards A, and its height to the right, towards point 9. That is
    /// a property of the file's coordinates, and it did not change when the screen went back to horizontal.
    /// </summary>
    public static (double X, double Y, double WidthM, double HeightM) ImageInStudyMetres(double point0Xm, double point0Ym)
    {
        var (gx, gy) = PixelToGrid(0, ImageHeightPx);
        var corner = StudyMetres(gx, gy, point0Xm, point0Ym);
        return (corner.X, corner.Y, ImageWidthPx / PixelU.Slope / MmPerM, ImageHeightPx / PixelV.Slope / MmPerM);
    }

    /// <summary>
    /// The picture's rotation IN THE DXF, degrees counter-clockwise: a quarter turn clockwise, because the DXF
    /// is laid out in the studies' axes. Its width (u) runs towards beam A, which is -Y there; its height,
    /// upwards in the picture towards line 9, is +X. The screen does not turn the picture at all.
    /// </summary>
    public const double ImageRotationDeg = -90;

    // ── Grid labels ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A grid label - a beam letter or a point number - at a position in the grid's axes, absolute mm.</summary>
    public sealed record GridLabel(string Text, double GridXmm, double GridYmm);

    /// <summary>
    /// Radius of a label's white disc, SVG units: 0,49 m, enough to cover one of the picture's own rings
    /// (0,42 m) with room for the 3 cm they sit off the survey.
    /// </summary>
    public const double LabelRadius = 5.4;

    /// <summary>
    /// The grid's labels: each point number left of beam J, over the picture's own number; each beam letter
    /// above line 9, over the picture's own letter, and again below line 1 so a letter is in reach from
    /// either end of a long beam. Positions are in the grid's axes, so they follow the view wherever it is
    /// turned. Declared after the calibration it is computed from.
    /// </summary>
    public static IReadOnlyList<GridLabel> GridLabels { get; } = BuildLabels();

    private static List<GridLabel> BuildLabels()
    {
        double numbersX = PixelToGrid(PointLabelsU, 0).X;                 // beyond beam J: left of it
        double lettersRightY = PixelToGrid(0, BeamLabelsV).Y;             // beyond line 9: above it
        double lettersLeftY = LineYmm(1) + (LineYmm(9) - lettersRightY);  // as far beyond line 1: below it

        var labels = new List<GridLabel>();
        foreach (var (line, _) in PointLinesPx)
            labels.Add(new GridLabel(line.ToString(CultureInfo.InvariantCulture), numbersX, LineYmm(line)));
        foreach (var (beam, _) in BeamLinesPx)
        {
            labels.Add(new GridLabel(beam.ToString(), BeamXmm(beam), lettersLeftY));
            labels.Add(new GridLabel(beam.ToString(), BeamXmm(beam), lettersRightY));
        }
        return labels;
    }
}
