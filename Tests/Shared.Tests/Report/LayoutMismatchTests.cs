using AliantWebS.Models;
using AliantWebS.Reporting;
using Helpers;
using Xunit;

namespace Shared.Tests.Report;

/// <summary>
/// The layout against the plan of charges: is every charge hung where the production says it is?
///
/// The study checks a pre-rig that was placed from the Charges page. If someone has moved a charge, changed
/// its weight or added one since, the implantation no longer carries the plan the study is about - and every
/// attachment-point load in it is a load nobody is actually hanging. So each of these is a verification gap
/// that stops the study concluding, and each one ends by asking for the placement to be re-run, because that
/// is the only thing that can fix it.
///
/// The tolerances are the optimiser's own: a charge is projected onto its palonnier's axis, and a charge on a
/// mother line is moved at most Geometry.TOL onto it. Everything is built in code - no database, no workbook.
/// </summary>
public class LayoutMismatchTests
{
    /// <summary>How every gap ends when the reader has nothing to correct first.</summary>
    private const string Rerun = "Relancez le placement (Générer l’implantation) puis le calcul charpente.";

    /// <summary>And how it ends when they do.</summary>
    private const string RerunAfter = "puis relancez le placement (Générer l’implantation) et le calcul charpente.";

    // The venue's axes, relative to Point0 = E5.
    private const double P0X = 33.103;
    private const double G = 18.697 - P0X, F = 25.947 - P0X, E = 0, D = 40.247 - P0X;
    private const double Row1 = 15.15, Row3 = 7.4, Row5 = 0, Row6 = -3.7;

    // ═══════════════════════════════════════════════════════════════
    // A layout that still carries the plan
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_layout_within_tolerance_of_the_plan_is_not_a_gap()
    {
        var scene = new Scene();

        // Placing rounds coordinates onto the truss it hangs them from, so the two sources never agree to the
        // millimetre. Names are compared without case after trimming - " t1 " is T1 - and a few hundred grams
        // of rounding is not a different charge.
        var gaps = ReportDataBuilder.LayoutMismatches(scene.PlacedSet, scene.Elements, scene.PlanSet);

        Assert.Empty(gaps);
    }

    [Fact]
    public void A_charge_moved_from_where_the_plan_puts_it_is_a_gap_word_for_word()
    {
        var scene = new Scene();
        // T2 is 0,6 m from the plan: too far to be rounding, and far enough to be a different point line.
        var movedPlan = scene.PlanSet.Select(p => p.Name == "T2" ? Scene.PlanLoad("T2", -10.8, 3.0, 400) : p).ToList();

        var gap = Assert.Single(ReportDataBuilder.LayoutMismatches(scene.PlacedSet, scene.Elements, movedPlan));

        Assert.Equal(
            $"La charge « T2 » est placée en {StudyAt(-10.8, 2.4)} sur « P300 L=12,0m #1 » mais en "
            + $"{StudyAt(-10.8, 3.0)} dans le plan de charges : l’implantation ne correspond plus aux charges. {Rerun}",
            gap);
    }

    [Fact]
    public void A_gap_prints_its_coordinates_in_the_studies_axes()
    {
        var scene = new Scene();
        var movedPlan = scene.PlanSet.Select(p => p.Name == "T2" ? Scene.PlanLoad("T2", -10.8, 3.0, 400) : p).ToList();

        var gap = Assert.Single(ReportDataBuilder.LayoutMismatches(scene.PlacedSet, scene.Elements, movedPlan));

        // The database keeps the grid's axes; anything a person reads is in the studies'. Grid (-10,8 ; 2,4)
        // is (-2,40 ; 10,80) on the page, and printing the grid's numbers would send the reader to the wrong
        // side of the hall.
        Assert.Contains("(-2,40 ; 10,80)", gap);
    }

    // ═══════════════════════════════════════════════════════════════
    // How far is too far
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void On_a_palonnier_along_the_beams_the_tolerance_is_15_cm_along_it_and_21_cm_across_it()
    {
        var scene = new Scene();

        bool FlagsAt(double dx, double dy) => ReportDataBuilder.LayoutMismatches(
            new List<ChargeMp> { Scene.PlacedLoad("T1", scene.Palonnier, 3.0, 500) }, scene.Elements,
            new List<ChargesScenique> { Scene.PlanLoad("T1", -10.8 + dx, 4.4 + dy, 500) }).Count > 0;

        // Along it (Y here) the optimiser moves nothing but a charge past an end, so 15 cm is the whole
        // budget. Across it (X here) a charge may legitimately lie a colinearity tolerance off the axis.
        Assert.False(FlagsAt(0.08, 0.08), "8 cm dans les deux sens : pose normale");
        Assert.False(FlagsAt(0, 0.149), "14,9 cm le long du palonnier : dans la tolérance");
        Assert.True(FlagsAt(0, 0.151), "15,1 cm le long du palonnier : trop loin");
        Assert.False(FlagsAt(0.11, 0.11), "11 cm en biais : dans la tolérance");
        Assert.False(FlagsAt(0.20, 0), "20 cm en travers d'un palonnier : dans la tolérance de colinéarité");
        Assert.True(FlagsAt(0.22, 0), "22 cm en travers d'un palonnier : trop loin");
    }

    [Fact]
    public void On_a_mother_line_the_tolerance_is_15_cm_along_it_and_9_cm_across_it()
    {
        var scene = new Scene();

        bool FlagsAt(double dx, double dy) => ReportDataBuilder.LayoutMismatches(
            new List<ChargeMp> { Scene.PlacedLoad("D1", scene.Mother, 20.0, 250) }, scene.Elements,
            new List<ChargesScenique> { Scene.PlanLoad("D1", G + 20.0 + dx, Row3 + dy, 250) }).Count > 0;

        // A charge is hung on a mother line only within Geometry.TOL of its point line: across one, the
        // tolerance is tighter than on a palonnier, because past it the charge is on another line entirely.
        Assert.False(FlagsAt(0.14, 0.08), "14 cm le long, 8 cm en travers : pose normale");
        Assert.True(FlagsAt(0.16, 0), "16 cm le long du mother : trop loin");
        Assert.True(FlagsAt(0, 0.10), "10 cm en travers d'un mother : trop loin");
    }

    [Fact]
    public void On_an_inclined_palonnier_the_tolerance_follows_its_own_axis()
    {
        var scene = new Scene();
        // Neither along the beams nor across them: the distances are measured on the truss, not on the grid.
        var inclined = El("P300 L=11,5m #3", scene.P300Id, false, 0.0, Row3, 3.0, Row6, "[400,400,400]");

        bool FlagsAt(double along, double across)
        {
            double dx = 3.0, dy = Row6 - Row3, length = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length, uy = dy / length;
            double ax = 5.0 * ux, ay = Row3 + 5.0 * uy;
            return ReportDataBuilder.LayoutMismatches(
                new List<ChargeMp> { Scene.PlacedLoad("B1", inclined, 5.0, 300) },
                new List<MotherGridPalonnier> { inclined },
                new List<ChargesScenique>
                {
                    Scene.PlanLoad("B1", ax + along * ux - across * uy, ay + along * uy + across * ux, 300),
                }).Count > 0;
        }

        Assert.False(FlagsAt(0, 0.20), "20 cm en travers de son axe : dans la tolérance");
        Assert.False(FlagsAt(0.14, -0.20), "14 cm le long, 20 cm en travers de l'autre côté : dans la tolérance");
        Assert.True(FlagsAt(0, 0.22), "22 cm en travers de son axe : trop loin");
        Assert.True(FlagsAt(0.16, 0), "16 cm le long de son axe : trop loin");
    }

    [Fact]
    public void Many_misplaced_charges_name_the_five_furthest_and_count_the_rest()
    {
        var scene = new Scene();
        // Seven charges, each further from the plan than the last. One gap per charge would bury the section;
        // a bare count would name none of them.
        var placed = Enumerable.Range(1, 7).Select(i => Scene.PlacedLoad($"M{i}", scene.Palonnier, i, 100)).ToList();
        var plan = Enumerable.Range(1, 7).Select(i => Scene.PlanLoad($"M{i}", -10.8 + 0.2 + 0.1 * i, 7.4 - i, 100)).ToList();

        var gap = Assert.Single(ReportDataBuilder.LayoutMismatches(placed, scene.Elements, plan));

        Assert.StartsWith("7 charges sont placées ailleurs que dans le plan de charges : « M7 »", gap);
        Assert.Contains("(+2)", gap);
        Assert.DoesNotContain("« M1 »", gap);
        Assert.EndsWith(Rerun, gap);
    }

    // ═══════════════════════════════════════════════════════════════
    // The kinds of mismatch
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Each_kind_of_mismatch_is_one_gap_of_its_own()
    {
        var scene = new Scene();

        // Six kinds in one layout: placed but absent from the plan, in the plan but placed nowhere, another
        // weight, no weight anywhere, no position, and a name given twice or not at all.
        Assert.Equal(6, scene.MixedGaps().Count);
    }

    [Fact]
    public void Every_gap_ends_by_asking_for_the_placement_to_be_re_run()
    {
        var scene = new Scene();
        var movedPlan = scene.PlanSet.Select(p => p.Name == "T2" ? Scene.PlanLoad("T2", -10.8, 3.0, 400) : p).ToList();
        var moved = ReportDataBuilder.LayoutMismatches(scene.PlacedSet, scene.Elements, movedPlan);

        // The placement reads the Charges page itself, so there is no second source to reconcile first: the
        // fix is always to re-run it, the other way round when the reader has something to correct first.
        Assert.All(scene.MixedGaps().Concat(moved),
            g => Assert.True(g.EndsWith(Rerun) || g.EndsWith(RerunAfter), $"réserve sans marche à suivre : {g}"));
    }

    [Fact]
    public void No_gap_still_sends_the_reader_to_the_workbook_the_placement_no_longer_reads()
    {
        var scene = new Scene();

        Assert.All(scene.MixedGaps(), g => Assert.DoesNotContain("Charges.xlsx", g));
    }

    [Fact]
    public void A_name_given_twice_or_not_at_all_is_a_gap()
    {
        var scene = new Scene();

        // Both leave the charge nothing to be compared with, which is not the same as being in its place.
        Assert.Contains(scene.MixedGaps(), g => g.Contains("« T7 » 2 fois dans le plan de charges"));
        Assert.Contains(scene.MixedGaps(), g => g.Contains("1 charge(s) sans nom sur l’implantation"));
    }

    [Fact]
    public void A_charge_of_the_plan_that_is_placed_nowhere_is_a_gap()
    {
        var scene = new Scene();

        Assert.Contains(scene.MixedGaps(), g => g.Contains(
            "La charge « T5 » du plan de charges n’est placée sur aucun élément de l’implantation : "
            + $"l’implantation ne correspond plus aux charges. {Rerun}"));
    }

    [Fact]
    public void A_placed_charge_that_is_in_no_plan_is_a_gap()
    {
        var scene = new Scene();

        Assert.Contains(scene.MixedGaps(), g => g.Contains(
            "La charge « T9 » est placée sur « P300 L=12,0m #1 » mais ne figure pas dans le plan de charges : "
            + $"l’implantation ne correspond plus aux charges. {Rerun}"));
    }

    [Fact]
    public void A_charge_of_another_weight_than_the_plan_is_a_gap()
    {
        var scene = new Scene();

        // The study would check 200 daN on a point the production hangs 250 daN from.
        Assert.Contains(scene.MixedGaps(), g => g.Contains(
            "La charge « T4 » n’a pas le même poids sur l’implantation et dans le plan de charges "
            + "(200 daN sur l’implantation, 250 daN dans le plan de charges)"));
    }

    [Fact]
    public void A_charge_with_no_weight_anywhere_does_not_pass()
    {
        var scene = new Scene();

        Assert.Contains(scene.MixedGaps(), g => g.Contains(
            "La charge « T8 » n’a de poids ni sur l’implantation ni dans le plan de charges"));
    }

    [Fact]
    public void A_placed_charge_with_no_position_cannot_be_compared_and_says_so()
    {
        var scene = new Scene();

        Assert.Contains(scene.MixedGaps(), g => g.Contains(
            "La position de la charge « T6 » (sans position sur « P300 L=12,0m #1 ») ne peut pas être comparée "
            + "au plan de charges"));
    }

    [Fact]
    public void A_plan_charge_without_coordinates_cannot_be_compared_either()
    {
        var scene = new Scene();

        var gap = Assert.Single(ReportDataBuilder.LayoutMismatches(
            new List<ChargeMp> { Scene.PlacedLoad("T1", scene.Palonnier, 3.0, 500) }, scene.Elements,
            new List<ChargesScenique> { Scene.PlanLoad("T1", null, 4.4, 500) }));

        Assert.Contains("« T1 » (sans coordonnées dans le plan de charges)", gap);
    }

    [Fact]
    public void A_carrier_without_both_its_ends_cannot_place_the_charges_hung_on_it()
    {
        var scene = new Scene();
        // An element half drawn: its charges have a distance along it but no line to measure that along.
        var brokenCarrier = new MotherGridPalonnier
        {
            MotherGridPalonnierId = Guid.NewGuid(), ElementTypeId = scene.P300Id, Famille = false,
            Name = "P300 L=4,0m #9", X1 = 1.0, Y1 = 1.0, X2 = null, Y2 = 2.0,
        };

        var gap = Assert.Single(ReportDataBuilder.LayoutMismatches(
            new List<ChargeMp> { Scene.PlacedLoad("T1", brokenCarrier, 1.0, 500) },
            scene.Elements.Append(brokenCarrier),
            new List<ChargesScenique> { Scene.PlanLoad("T1", 1.0, 2.0, 500) }));

        Assert.Contains("dont les coordonnées sont incomplètes", gap);
    }

    [Fact]
    public void A_charge_is_found_on_its_element_by_id_without_its_navigation_property()
    {
        var scene = new Scene();
        // As it arrives from a query that did not include the element: the id is enough to place it, and a
        // charge that cannot be placed would be reported as a gap that is not one.
        var byIdOnly = new ChargeMp
        {
            ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = scene.Palonnier.MotherGridPalonnierId,
            Name = "T1", X = 3.0, Charge = 500,
        };

        Assert.Empty(ReportDataBuilder.LayoutMismatches(
            new List<ChargeMp> { byIdOnly }, scene.Elements,
            new List<ChargesScenique> { Scene.PlanLoad("T1", -10.8, 4.4, 500) }));
    }

    // ── The studies' axes, as the gaps print them ────────────────

    /// <summary>A grid position as a gap prints it: converted to the studies' axes, to the centimetre.</summary>
    private static string StudyAt(double gridX, double gridY)
    {
        var (x, y) = StudyAxes.ToStudy(gridX, gridY);
        return $"({M2(x)} ; {M2(y)})";
    }

    private static string M2(double v) => (Math.Abs(v) < 0.005 ? 0.0 : v).ToString("0.00", ReportDataBuilder.Fr);

    // ── The scenario ─────────────────────────────────────────────

    private static MotherGridPalonnier El(
        string name, Guid type, bool mother, double x1, double y1, double x2, double y2, string? composition) => new()
    {
        MotherGridPalonnierId = Guid.NewGuid(), ElementTypeId = type, Famille = mother, Name = name,
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Composition = composition,
    };

    /// <summary>
    /// A pre-rig and the plan it was placed from. « P300 L=12,0m #1 » runs from (-10,8 ; 7,4) towards
    /// (-10,8 ; -3,7), so X metres along it is the grid position (-10,8 ; 7,4 - X). A fresh one per test.
    /// </summary>
    private sealed class Scene
    {
        public readonly Guid S500Id = Guid.NewGuid();
        public readonly Guid P300Id = Guid.NewGuid();

        public readonly MotherGridPalonnier Mother, Palonnier;
        public readonly List<MotherGridPalonnier> Elements;

        public readonly List<ChargeMp> PlacedSet;
        public readonly List<ChargesScenique> PlanSet;

        public Scene()
        {
            Mother = El("S500 L=22,4m #1", S500Id, true, G, Row3, D, Row3, "[560,560,560,560]");
            Palonnier = El("P300 L=12,0m #1", P300Id, false, -10.8, Row3, -10.8, Row6, "[400,400,400]");
            var longPalonnier = El("P300 L=16,0m #2", P300Id, false, -5.0, Row1, -5.0, Row5, "[400,400,400,400]");
            var oldMother = El("S500 L=8,4m #9", S500Id, true, F, -11.05, E, -11.05, null);
            var unreadable = El("S500 L=2,8m #7", S500Id, true, -1.0, -15.151, 1.0, -15.151, "oops");
            var partial = El("S500 L=15,6m #5", S500Id, true, D, 3.7, D + 15.0, 3.7, "[999,560]");
            Elements = new List<MotherGridPalonnier> { Mother, Palonnier, longPalonnier, oldMother, unreadable, partial };

            PlacedSet = new List<ChargeMp>
            {
                PlacedLoad("T1", Palonnier, 3.0, 500),
                PlacedLoad("T2", Palonnier, 5.0, 400),
                PlacedLoad("T3", Palonnier, 9.0, 300),
                PlacedLoad("D1", Mother, 20.0, 250),        // hung straight on the mother line, 20 m from G
            };
            PlanSet = new List<ChargesScenique>
            {
                PlanLoad(" t1 ", -10.8 + 0.08, 4.4 + 0.08, 500),    // 0,113 m off, another case, spaces
                PlanLoad("T2", -10.8, 2.4, 400),
                PlanLoad("T3", -10.8 - 0.08, -1.6, 300.4),          // 8 cm across, 0,4 kg of rounding
                PlanLoad("D1", G + 20.0 + 0.08, Row3 - 0.08, 250),  // 8 cm along onto the line and 8 cm across
            };
        }

        /// <summary>One layout holding every kind of mismatch at once, and the plan it is held against.</summary>
        public List<string> MixedGaps()
        {
            var placed = new List<ChargeMp>
            {
                PlacedLoad("T1", Palonnier, 3.0, 500),
                PlacedLoad("T9", Palonnier, 4.0, 100),     // not in the plan
                PlacedLoad("T4", Palonnier, 6.0, 200),     // 250 in the plan
                PlacedLoad("T6", Palonnier, null, 100),    // no position
                PlacedLoad("T7", Palonnier, 7.0, 100),     // twice in the plan
                PlacedLoad("T8", Palonnier, 8.0, null),    // no weight anywhere
                PlacedLoad("  ", Palonnier, 1.0, 50),      // no name
            };
            var plan = new List<ChargesScenique>
            {
                PlanLoad("T1", -10.8, 4.4, 500),
                PlanLoad("T5", 0, 0, 300),                 // not placed
                PlanLoad("T4", -10.8, 1.4, 250),
                PlanLoad("T6", -10.8, 1.0, 100),
                PlanLoad("T7", -10.8, 0.4, 100), PlanLoad("t7 ", -10.8, 0.4, 100),
                PlanLoad("T8", -10.8, -0.6, null),
            };

            return ReportDataBuilder.LayoutMismatches(placed, Elements, plan);
        }

        public static ChargeMp PlacedLoad(string? name, MotherGridPalonnier on, double? x, double? kg) => new()
        {
            ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = on.MotherGridPalonnierId,
            MotherGridPalonnier = on, Name = name, X = x, Charge = kg,
        };

        public static ChargesScenique PlanLoad(string? name, double? x, double? y, double? kg) => new()
        {
            ChargesSceniqueId = Guid.NewGuid(), Name = name, X = x, Y = y, Charge = kg,
        };
    }
}
