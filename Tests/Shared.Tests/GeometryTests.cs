using RiggingOptimizer.Core;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// The grid the optimiser plans on. These are the numbers every other rule is measured against, so they are
/// pinned here: a surveyed coordinate that changes must break a test rather than a study.
/// </summary>
public class GeometryTests
{
    [Fact]
    public void The_grid_is_nine_beams_and_nine_point_lines()
    {
        Assert.Equal(9, Geometry.ColXList.Count);
        Assert.Equal(9, Geometry.RowYList.Count);
        Assert.Equal(81, Geometry.Grid.Count);
    }

    [Fact]
    public void Beam_J_is_not_on_the_optimiser_s_grid()
    {
        // Deliberate: the six hand-written studies never hang anything on J. It allows 500 daN on one point
        // in two and nothing on the others, 2 500 daN in all, and it sits half a bay from I.
        Assert.DoesNotContain("J", Geometry.Cols);
        Assert.Equal("I", Geometry.ColXList[0].Col);
        Assert.Equal("A", Geometry.ColXList[^1].Col);
    }

    [Fact]
    public void The_beams_are_where_the_survey_puts_them()
    {
        // Relative to E5, which is Point0 for the Antarès.
        Assert.Equal(-29.306, Geometry.ColXList[0].X, 3);   // I
        Assert.Equal(0.0, Geometry.ColXList[4].X, 3);       // E
        Assert.Equal(29.294, Geometry.ColXList[^1].X, 3);   // A

        // The narrowest bay is E–D at 7,144 m and the widest H–G at 7,700 m: no inventory length covers
        // either without being assembled, which is why a mother line is several pieces.
        var gaps = Geometry.ColXList.Zip(Geometry.ColXList.Skip(1), (a, b) => b.X - a.X).ToList();
        Assert.Equal(7.144, gaps.Min(), 3);
        Assert.Equal(7.700, gaps.Max(), 3);
    }

    [Fact]
    public void The_point_lines_are_where_the_survey_puts_them()
    {
        var gaps = Geometry.RowYList.Zip(Geometry.RowYList.Skip(1), (a, b) => b.Y - a.Y).ToList();
        Assert.Equal(3.600, gaps.Min(), 3);
        Assert.Equal(4.101, gaps.Max(), 3);
        Assert.Equal(9, Geometry.Rows.Length);
    }

    [Fact]
    public void The_tolerances_are_the_ones_every_other_rule_is_derived_from()
    {
        Assert.Equal(0.08, Geometry.TOL, 6);
        Assert.Equal(0.20, Geometry.ColinearTol, 6);
        Assert.True(Geometry.CrossingTol > Geometry.TOL, "le croisement doit couvrir la tolérance de pose");
        Assert.True(Geometry.CrossingTol - Geometry.TOL < 1e-5, "et ne la dépasser que du micromètre d'arrondi");
    }

    [Fact]
    public void The_nearest_point_line_is_the_one_a_charge_hangs_from()
    {
        // Row 5 is y = 0; a charge 5 cm off it is on it, at 1 m it is not.
        Assert.Equal(5, Geometry.NearestRow(0.05).Row);
        Assert.True(Math.Abs(Geometry.NearestRow(0.05).Y - 0.0) < 1e-9);
        Assert.NotEqual(5, Geometry.NearestRow(3.60).Row);
    }
}
