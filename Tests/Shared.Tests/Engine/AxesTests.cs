using AliantWebS.Models;
using Helpers;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// The two sets of axes the application lives with, and the one boundary between them.
///
/// Everything a person reads or types follows the hand-written studies (Figure 2 of each): origin at E5,
/// X along the beams towards point 9, Y across them towards beam J. Every calculation - the optimiser's
/// grid, the load transmission, the stored coordinates - works in the grid's axes: X across the beams
/// towards beam A, Y along them towards point 1. The two differ by the reflection (x, y) → (-y, -x).
///
/// That reflection is its own inverse, and that is the whole danger: converting twice throws nothing,
/// looks like nothing, and simply puts the load somewhere else in the hall. So the conversion happens
/// exactly once, where coordinates meet a person or a file - the pages, Charges.xlsx, the plan, the study -
/// and never inside a calculation. These tests pin both directions and what a second conversion costs.
/// </summary>
public class AxesTests
{
    // ── The conversion itself ────────────────────────────────────

    [Fact]
    public void Converting_to_the_studies_axes_and_back_gives_the_same_point()
    {
        // The seed is fixed, so a failure names a point that can be tried again by hand.
        var rnd = new Random(7);

        for (int i = 0; i < 200; i++)
        {
            double x = rnd.NextDouble() * 60 - 30, y = rnd.NextDouble() * 30 - 15;

            var (sx, sy) = StudyAxes.ToStudy(x, y);
            var (gx, gy) = StudyAxes.ToGrid(sx, sy);

            Assert.True(Math.Abs(gx - x) < 1e-12 && Math.Abs(gy - y) < 1e-12,
                $"({x} ; {y}) revient en ({gx} ; {gy}) après un aller-retour dans les axes des études");
        }
    }

    [Fact]
    public void X_runs_along_the_beams_towards_point_9()
    {
        // E9, relative to E5, is 15,151 m along the beams and nowhere across them.
        var e9 = StudyAxes.ToStudy(0, -15.151);

        Assert.Equal(15.151, e9.X, 9);
        Assert.Equal(0.0, e9.Y, 9);
    }

    [Fact]
    public void Y_runs_across_the_beams_towards_beam_J()
    {
        // J is the far side of the hall, F is between E and it: both are positive in the studies' Y, and
        // J further out than F. Reading Y the other way round would mirror every study about beam E.
        var j5 = StudyAxes.ToStudy(-33.103, 0);
        var f5 = StudyAxes.ToStudy(-7.156, 0);

        Assert.Equal(33.103, j5.Y, 9);
        Assert.Equal(0.0, j5.X, 9);
        Assert.True(f5.Y > 0 && f5.Y < j5.Y,
            $"F5 doit être entre E et J en travers des poutres : F5 y = {f5.Y}, J5 y = {j5.Y}");
    }

    [Fact]
    public void The_one_way_helpers_say_exactly_what_the_pair_says()
    {
        // Four helpers exist so that a page can convert a single coordinate. They are the same map, and
        // must stay it: one of them drifting would convert an X against a Y for whoever uses that one.
        var (sx, sy) = StudyAxes.ToStudy(3.5, -2.25);
        var (gx, gy) = StudyAxes.ToGrid(3.5, -2.25);

        Assert.Equal(sx, StudyAxes.StudyX(-2.25), 12);
        Assert.Equal(sy, StudyAxes.StudyY(3.5), 12);
        Assert.Equal(gx, StudyAxes.GridX(-2.25), 12);
        Assert.Equal(gy, StudyAxes.GridY(3.5), 12);
    }

    // ── Where a second conversion must not happen ────────────────

    [Fact]
    public void A_position_converted_twice_is_back_in_the_studies_axes_and_hangs_from_nothing()
    {
        // L1 of the studies' Figure 2, (-7,45 ; -2,99). Converted once it lands 5 cm from point line 3,
        // between beams E and D - close enough to hang straight on the mother line there. Converted a
        // second time it is back where it started, 0,71 m from the nearest point line, and the placement
        // would give it a palonnier it never needed. Nothing throws: that is why this is a test.
        var once = StudyAxes.ToGrid(-7.45, -2.99);
        var twice = StudyAxes.ToGrid(once.X, once.Y);

        Assert.Equal(7.45, once.Y, 9);
        Assert.True(once.X > 0 && once.X < 7.144,
            $"L1 doit tomber entre les poutres E et D : x = {once.X}");
        Assert.Equal(3, Geometry.NearestRow(once.Y).Row);
        Assert.True(Math.Abs(once.Y - Geometry.NearestRow(once.Y).Y) <= Geometry.TOL,
            "converti une fois, L1 est sur la ligne de points 3");

        Assert.Equal(-7.45, twice.X, 9);
        Assert.Equal(-2.99, twice.Y, 9);
        Assert.True(Math.Abs(twice.Y - Geometry.NearestRow(twice.Y).Y) > Geometry.TOL,
            "converti deux fois, L1 n'est plus sur aucune ligne de points - et rien ne l'a signalé");
    }

    [Fact]
    public void The_charges_workbook_is_converted_once_on_the_way_in()
    {
        // Charges.xlsx is written in the studies' axes and everything after the reader works in the
        // grid's. 18 of the venue's 24 loads are meant to hang straight on a point line, which in the
        // grid's axes means their Y is within Geometry.TOL of a point line's Y. Converted twice - or not
        // at all - those 18 would be measured across the beams instead, and the placement would hang them
        // on palonniers nobody asked for.
        var charges = ExcelLoader.LoadCharges(Fixtures.Charges);
        int onAPointLine = charges.Count(c => Math.Abs(c.Y - Geometry.NearestRow(c.Y).Y) <= Geometry.TOL);

        Assert.Equal(24, charges.Count);
        Assert.True(onAPointLine == 18,
            $"{onAPointLine} charges sur une ligne de points au lieu de 18 : les coordonnées ont été "
            + "converties deux fois, ou pas du tout");
    }

    // ── What a page reads and types ──────────────────────────────

    [Fact]
    public void An_element_s_ends_are_read_in_the_studies_axes_and_typed_in_them()
    {
        // The stored X1..Y2 stay in the grid's axes; the Study* properties are the only thing a page shows
        // or receives. Writing one must move its own stored coordinate and leave the other alone, or an
        // element edited on a page walks sideways every time it is saved.
        var element = new MotherGridPalonnier { X1 = 1, Y1 = 2, X2 = 3, Y2 = 4 };

        Assert.Equal(-2.0, element.StudyX1!.Value, 9);
        Assert.Equal(-1.0, element.StudyY1!.Value, 9);
        Assert.Equal(-4.0, element.StudyX2!.Value, 9);
        Assert.Equal(-3.0, element.StudyY2!.Value, 9);

        element.StudyX1 = 5;
        element.StudyY2 = 6;

        Assert.Equal(-5.0, element.Y1!.Value, 9);
        Assert.Equal(-6.0, element.X2!.Value, 9);
        Assert.Equal(1.0, element.X1!.Value, 9);
        Assert.Equal(4.0, element.Y2!.Value, 9);
    }

    [Fact]
    public void A_scenic_load_is_read_in_the_studies_axes_and_typed_in_them()
    {
        var load = new ChargesScenique { X = 7, Y = -8 };

        Assert.Equal(8.0, load.StudyX!.Value, 9);
        Assert.Equal(-7.0, load.StudyY!.Value, 9);
    }

    [Fact]
    public void A_coordinate_left_empty_stays_empty()
    {
        // A load the Charges page has not positioned yet. Converting an absent coordinate into 0 would put
        // it at E5, in the middle of the grid, and the placement would carry it there.
        var load = new ChargesScenique { X = 7, Y = -8 };

        load.StudyX = null;

        Assert.Null(load.Y);
        Assert.Equal(7.0, load.X!.Value, 9);
    }
}
