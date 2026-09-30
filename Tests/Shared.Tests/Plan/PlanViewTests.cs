using System.Globalization;
using AliantWebS.Plan;
using Helpers;
using Xunit;

namespace Shared.Tests.Plan;

/// <summary>
/// The plan draws where it always drew.
///
/// PlanView moved from the web project to Shared on 2026-09-17 and gained a stored-frame form of its conversions:
/// StoredToSvg takes the grid's axes in metres from Point0, as element ends and loads are stored, and the old
/// ToSvg - absolute millimetres - is now written through it. A drawing that moved by a hair would put a load
/// beside its palonnier, or a snap beside its point, so the formulas the plan was drawn with before the move are
/// copied here as references and the new ones must agree with them everywhere, to 1e-9 SVG units. The picture's
/// calibration (PixelToGrid) moved unchanged, so the references use it as it is.
/// </summary>
public class PlanViewTests
{
    private const double Tolerance = 1e-9;

    // Point0 at E5, as at Antarès; none; and one that is neither.
    public static TheoryData<double, double> Point0s => new()
    {
        { 33.103, 0 },
        { 0, 0 },
        { 12.5, -3.25 },
    };

    // Absolute millimetres in the grid's axes: E5, J1, A9, and off the grid.
    private static readonly (double X, double Y)[] AbsoluteMm =
    {
        (33103, 0), (0, 15150), (62397, -15151), (-1234.5, 98765.4321), (25947.5, -3700),
    };

    // Stored frame, metres from Point0.
    private static readonly (double X, double Y)[] StoredM =
    {
        (0, 0), (-10.8, 7.4), (29.294, -15.151), (-33.103, 15.15), (0.001, -0.001), (1234.5678, -9876.54321),
    };

    // ── The references: PlanView as it was in Pages/CADPages before the move ─────────────────

    private const double OriginalSvgPerMm = 754.0 / 68397.0;

    private static (double X, double Y) OriginalToSvg(double gridXmm, double gridYmm, double point0Xm, double point0Ym)
    {
        var (studyX, studyY) = StudyAxes.ToStudy(gridXmm - point0Xm * 1000.0, gridYmm - point0Ym * 1000.0);
        return (-studyY * OriginalSvgPerMm, -studyX * OriginalSvgPerMm);
    }

    private static (double X, double Y) OriginalFromSvg(double svgX, double svgY, double point0Xm, double point0Ym)
    {
        var (gridX, gridY) = StudyAxes.ToGrid(-svgY / OriginalSvgPerMm, -svgX / OriginalSvgPerMm);
        return (gridX + point0Xm * 1000.0, gridY + point0Ym * 1000.0);
    }

    private static (double X, double Y) OriginalStudyMetres(double gridXmm, double gridYmm, double point0Xm, double point0Ym) =>
        StudyAxes.ToStudy(gridXmm / 1000.0 - point0Xm, gridYmm / 1000.0 - point0Ym);

    private static string OriginalImageTransform(double point0Xm, double point0Ym)
    {
        (double X, double Y) At(double u, double v)
        {
            var (gx, gy) = PlanView.PixelToGrid(u, v);
            return OriginalToSvg(gx, gy, point0Xm, point0Ym);
        }

        var o = At(0, 0);
        var alongU = At(1, 0);
        var alongV = At(0, 1);
        double a = alongU.X - o.X, b = alongU.Y - o.Y, c = alongV.X - o.X, d = alongV.Y - o.Y;
        return FormattableString.Invariant($"matrix({a} {b} {c} {d} {o.X} {o.Y})");
    }

    private static (double X, double Y, double Width, double Height) OriginalExtent(double point0Xm, double point0Ym)
    {
        var corners = new[]
            {
                (0.0, 0.0), ((double)PlanView.ImageWidthPx, 0.0), (0.0, (double)PlanView.ImageHeightPx),
                ((double)PlanView.ImageWidthPx, (double)PlanView.ImageHeightPx),
            }
            .Select(c =>
            {
                var (gx, gy) = PlanView.PixelToGrid(c.Item1, c.Item2);
                return OriginalToSvg(gx, gy, point0Xm, point0Ym);
            })
            .ToList();

        double x0 = corners.Min(c => c.X), x1 = corners.Max(c => c.X);
        double y0 = corners.Min(c => c.Y), y1 = corners.Max(c => c.Y);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    private static double[] MatrixNumbers(string transform)
    {
        Assert.StartsWith("matrix(", transform);
        return transform["matrix(".Length..^1]
            .Split(' ')
            .Select(n => double.Parse(n, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static void Near(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{what} : attendu {expected:R}, obtenu {actual:R}");

    // ── The stored frame ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void StoredToSvg_and_SvgToStored_give_back_the_position_they_were_given()
    {
        foreach (var (x, y) in StoredM)
        {
            var svg = PlanView.StoredToSvg(x, y);
            var back = PlanView.SvgToStored(svg.X, svg.Y);
            Near(x, back.X, $"X après aller-retour de ({x} ; {y})");
            Near(y, back.Y, $"Y après aller-retour de ({x} ; {y})");
        }
    }

    [Theory]
    [MemberData(nameof(Point0s))]
    public void StoredToSvg_draws_a_stored_position_where_the_original_ToSvg_drew_it(double point0X, double point0Y)
    {
        // The original took absolute millimetres: the plan's markup added Point0 back to a stored position first.
        foreach (var (x, y) in StoredM)
        {
            var expected = OriginalToSvg(x * 1000 + point0X * 1000, y * 1000 + point0Y * 1000, point0X, point0Y);
            var actual = PlanView.StoredToSvg(x, y);
            Near(expected.X, actual.X, $"x SVG de ({x} ; {y}) avec Point0 ({point0X} ; {point0Y})");
            Near(expected.Y, actual.Y, $"y SVG de ({x} ; {y}) avec Point0 ({point0X} ; {point0Y})");
        }
    }

    [Theory]
    [MemberData(nameof(Point0s))]
    public void ToSvg_FromSvg_and_StudyMetres_equal_the_original_PlanView(double point0X, double point0Y)
    {
        foreach (var (xMm, yMm) in AbsoluteMm)
        {
            var toSvg = PlanView.ToSvg(xMm, yMm, point0X, point0Y);
            var toSvgRef = OriginalToSvg(xMm, yMm, point0X, point0Y);
            Near(toSvgRef.X, toSvg.X, $"ToSvg x de ({xMm} ; {yMm})");
            Near(toSvgRef.Y, toSvg.Y, $"ToSvg y de ({xMm} ; {yMm})");

            var study = PlanView.StudyMetres(xMm, yMm, point0X, point0Y);
            var studyRef = OriginalStudyMetres(xMm, yMm, point0X, point0Y);
            Near(studyRef.X, study.X, $"StudyMetres X de ({xMm} ; {yMm})");
            Near(studyRef.Y, study.Y, $"StudyMetres Y de ({xMm} ; {yMm})");

            // FromSvg answers in millimetres, so its rounding is a thousand times larger: 1e-9 mm is still far below
            // anything drawn or saved.
            var fromSvg = PlanView.FromSvg(toSvgRef.X, toSvgRef.Y, point0X, point0Y);
            var fromSvgRef = OriginalFromSvg(toSvgRef.X, toSvgRef.Y, point0X, point0Y);
            Assert.True(Math.Abs(fromSvgRef.X - fromSvg.X) <= 1e-6 && Math.Abs(fromSvgRef.Y - fromSvg.Y) <= 1e-6,
                $"FromSvg de ({xMm} ; {yMm}) : attendu {fromSvgRef}, obtenu {fromSvg}");
            Assert.True(Math.Abs(fromSvg.X - xMm) <= 1e-6 && Math.Abs(fromSvg.Y - yMm) <= 1e-6,
                $"FromSvg(ToSvg) de ({xMm} ; {yMm}) ne revient pas au point : {fromSvg}");
        }
    }

    [Theory]
    [MemberData(nameof(Point0s))]
    public void Extent_and_ImageTransform_equal_the_original_PlanView(double point0X, double point0Y)
    {
        var extent = PlanView.Extent(point0X, point0Y);
        var extentRef = OriginalExtent(point0X, point0Y);
        Near(extentRef.X, extent.X, "Extent x");
        Near(extentRef.Y, extent.Y, "Extent y");
        Near(extentRef.Width, extent.Width, "Extent largeur");
        Near(extentRef.Height, extent.Height, "Extent hauteur");

        var matrix = MatrixNumbers(PlanView.ImageTransform(point0X, point0Y));
        var matrixRef = MatrixNumbers(OriginalImageTransform(point0X, point0Y));
        Assert.Equal(6, matrix.Length);
        for (int i = 0; i < 6; i++)
            Near(matrixRef[i], matrix[i], $"terme {i} de la matrice de l'image");
    }

    [Fact]
    public void SvgToStudyMatrix_maps_a_point_as_SvgToStored_then_the_studies_axes_do()
    {
        var m = PlanView.SvgToStudyMatrix();
        Assert.Equal(6, m.Length);

        foreach (var (svgX, svgY) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (-120.5, 37.25), (400, -250), (3.3, 3.3) })
        {
            var (gx, gy) = PlanView.SvgToStored(svgX, svgY);
            var expected = StudyAxes.ToStudy(gx, gy);
            double x = m[0] * svgX + m[2] * svgY + m[4];
            double y = m[1] * svgX + m[3] * svgY + m[5];
            Near(expected.X, x, $"X d'étude du point SVG ({svgX} ; {svgY})");
            Near(expected.Y, y, $"Y d'étude du point SVG ({svgX} ; {svgY})");
        }
    }

    [Fact]
    public void Up_the_screen_is_study_X_and_left_is_study_Y()
    {
        // The plan is horizontal, the axes are the studies': X towards point 9 goes up, Y towards J goes left.
        var m = PlanView.SvgToStudyMatrix();
        double oneMetre = PlanView.MetresToSvg(1);

        var up = (X: m[0] * 0 + m[2] * -oneMetre, Y: m[1] * 0 + m[3] * -oneMetre);
        var left = (X: m[0] * -oneMetre + m[2] * 0, Y: m[1] * -oneMetre + m[3] * 0);
        Near(1.0, up.X, "un mètre vers le haut en X d'étude");
        Near(0.0, up.Y, "un mètre vers le haut en Y d'étude");
        Near(0.0, left.X, "un mètre vers la gauche en X d'étude");
        Near(1.0, left.Y, "un mètre vers la gauche en Y d'étude");
    }

    [Theory]
    [MemberData(nameof(Point0s))]
    public void Point0_is_the_SVG_origin(double point0X, double point0Y)
    {
        var stored = PlanView.StoredToSvg(0, 0);
        Near(0, stored.X, "x SVG de Point0 (repère stocké)");
        Near(0, stored.Y, "y SVG de Point0 (repère stocké)");

        var absolute = PlanView.ToSvg(point0X * 1000, point0Y * 1000, point0X, point0Y);
        Near(0, absolute.X, "x SVG de Point0 (millimètres absolus)");
        Near(0, absolute.Y, "y SVG de Point0 (millimètres absolus)");
    }

    [Fact]
    public void MetresToSvg_and_SvgToMetres_are_one_scale()
    {
        foreach (var metres in new[] { 0.0, 1.0, 68.397, -3.7 })
            Near(metres, PlanView.SvgToMetres(PlanView.MetresToSvg(metres)), $"aller-retour de {metres} m");

        // The scale the plan has always had: 754 SVG units across 68,397 m.
        Near(754.0, PlanView.MetresToSvg(68.397), "68,397 m en unités SVG");
    }
}
