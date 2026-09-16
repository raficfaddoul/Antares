using RiggingOptimizer.Core;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// The floor no layout of a plan can go below, and what a study is allowed to say about it.
///
/// A bound answers one question and one only: could this have been done with fewer palonniers, fewer
/// pieces, less mother line? It never proves that a better layout exists, and it is not a target. So the
/// one property that matters is the direction it is allowed to be wrong in: BEING TOO LOW IS ONLY
/// UNHELPFUL, BEING TOO HIGH IS WRONG. A floor above the layout the optimiser actually produced would tell
/// a study it is further from optimal than it is - or, worse, that the answer in front of the reader is
/// impossible. The first two tests are the ones that would catch that, on the venue's own plan and on a
/// sweep of seeded plans; the others check that each bound assumes nothing wherever the geometry leaves a
/// choice, which is what keeps it a floor.
/// </summary>
[Collection(EngineCollection.Name)]
public class BoundsTests
{
    private readonly VenuePlan _venue;

    public BoundsTests(VenuePlan venue) => _venue = venue;

    // ══════════════════════════════════════════════════════════
    // Un plancher est un plancher
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Every_bound_is_a_floor_under_the_venue_s_own_layout()
    {
        var result = _venue.Placed;

        Assert.True(result.Bound is not null,
            "un run avec OptimizerOptions.WithBounds doit porter son plancher (RiggingResult.Bound)");

        // A floor of nothing is no floor at all: the venue's plan hangs charges on its point lines, so
        // there are metres of mother line no layout of it can avoid drawing.
        Assert.True(result.Bound!.MotherMetres > 0,
            "le plancher de mètres de mother ne peut pas être nul sur un plan qui charge ses lignes de points : "
            + result.Bound.Describe());

        AssertFloorHolds("le plan du site", result);
    }

    [Fact]
    public void Every_bound_is_a_floor_under_a_sweep_of_seeded_plans()
    {
        // One plan is one plan. The property has to hold wherever the charges fall, so the same three
        // floors are computed against twenty plans drawn from seeds - reproducible by their number alone -
        // each of them placed from the venue's real stock.
        int compared = 0;
        var couldNotBePlaced = new List<string>();

        for (int seed = 1; seed <= 20; seed++)
        {
            var result = Place(SeededPlan(seed, count: 6));

            // A plan the stock cannot carry is not a layout: it hangs fewer palonniers than a real one
            // would need, so a floor compared against it would fail for the wrong reason.
            if (result.Uncovered.Count > 0)
            {
                couldNotBePlaced.Add($"plan {seed} ({result.Uncovered.Count} charge(s) non portée(s))");
                continue;
            }

            AssertFloorHolds($"plan {seed}", result);
            compared++;
        }

        Assert.True(compared > 0,
            "aucun des plans tirés n'a pu être posé avec le stock du site, donc rien n'a été vérifié : "
            + string.Join(", ", couldNotBePlaced));
    }

    // ══════════════════════════════════════════════════════════
    // Ce que chaque plancher compte, et ce qu'il refuse de compter
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_mother_metre_bound_counts_only_a_line_a_charge_forces()
    {
        // A charge sitting on a point line hangs from that line wherever the layout puts its palonniers:
        // those metres are forced. A charge between two point lines hangs from a palonnier whose ends the
        // layout may put on either line, and anywhere along them - it forces no line and no bay of its
        // own, so it adds nothing here. Assuming that it would is the one mistake a floor must not make.
        var onThePointLine = new Charge { Name = "D1", X = -10.5, Y = Geometry.RowYList[4].Y, Weight = 300 };
        var betweenTwoLines = new Charge
        {
            Name = "H1",
            X = 12.3,
            Y = (Geometry.RowYList[6].Y + Geometry.RowYList[7].Y) / 2,
            Weight = 300,
        };

        double forcedAlone = Bounds.MotherMetresBound(new[] { onThePointLine });
        double withTheFreeOneToo = Bounds.MotherMetresBound(new[] { onThePointLine, betweenTwoLines });

        Assert.True(forcedAlone > 0, "une charge posée sur une ligne de points force la travée qui la porte");
        Assert.Equal(forcedAlone, withTheFreeOneToo, 9);

        // And on its own, a charge no point line claims forces nothing at all - even though the layout will
        // certainly draw mother lines to hang its palonnier from.
        Assert.Equal(0.0, Bounds.MotherMetresBound(new[] { betweenTwoLines }), 9);
    }

    [Fact]
    public void The_palonnier_bound_ignores_the_stock_so_it_can_never_over_estimate()
    {
        // Three charges no point line can carry, each at its own x. Along the beams a palonnier gathers the
        // charges that share an x, so three charges at three x need three palonniers - whatever they weigh,
        // and whatever the inventory holds.
        double betweenTwoLines = (Geometry.RowYList[4].Y + Geometry.RowYList[5].Y) / 2;
        var orthogonal = new OptimizerOptions { OrthogonalOnly = true };

        var light = new[]
        {
            new Charge { Name = "H1", X = -10.5, Y = betweenTwoLines, Weight = 100 },
            new Charge { Name = "H2", X = -3.3, Y = betweenTwoLines, Weight = 100 },
            new Charge { Name = "H3", X = 4.7, Y = betweenTwoLines, Weight = 100 },
        };
        var crushing = light.Select(c => new Charge { Name = c.Name, X = c.X, Y = c.Y, Weight = 50_000 }).ToList();

        Assert.Equal(3, Bounds.PalonnierCountBound(light, orthogonal));

        // The same geometry with fifty tonnes on every point: no truss in the venue carries that, so the
        // real layout would need far more palonniers - or would place nothing at all. The floor does not
        // move, which is exactly why it can only ever be too low. It is handed no stock to read, either.
        Assert.Equal(3, Bounds.PalonnierCountBound(crushing, orthogonal));

        // Two of the three sharing an x: one palonnier takes both, so the floor drops to two.
        var shared = new[]
        {
            light[0],
            new Charge { Name = "H2", X = light[0].X, Y = betweenTwoLines, Weight = 100 },
            light[2],
        };
        Assert.Equal(2, Bounds.PalonnierCountBound(shared, orthogonal));

        // Allowing inclined palonniers can only let one palonnier gather more charges, so the floor can
        // only go down. A setting that raised a floor would have the study claim a layout is worse than it is.
        Assert.True(Bounds.PalonnierCountBound(light, OptimizerOptions.Default)
                    <= Bounds.PalonnierCountBound(light, orthogonal),
            "autoriser les palonniers inclinés ne peut que baisser le plancher");
    }

    // ══════════════════════════════════════════════════════════
    // L'écart au plancher
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_gap_is_the_difference_between_the_layout_and_the_floor()
    {
        var result = _venue.Placed;
        var bound = result.Bound!;
        var gap = bound.Gap(result.Cost);

        Assert.Equal(result.Cost.Palonniers - bound.Palonniers, gap.Palonniers);
        Assert.Equal(result.Cost.Pieces - bound.Pieces, gap.Pieces);

        // The metres are read with care: the floor is on the mother lines alone, while the layout's drawn
        // length is the whole of it, palonniers included. So that figure is an upper limit on what could be
        // saved and never a target - most of it is palonnier length that had to be drawn.
        Assert.Equal(result.Cost.DrawnMetres - bound.MotherMetres, gap.Metres, 9);
    }

    [Fact]
    public void No_gap_is_ever_negative()
    {
        // A negative gap is the floor sitting above the layout, said the other way round: the study would
        // be telling its reader that the implantation in front of them cannot exist.
        AssertTheGapIsNotNegative("le plan du site", _venue.Placed);

        for (int seed = 1; seed <= 8; seed++)
        {
            var result = Place(SeededPlan(seed, count: 6));
            if (result.Uncovered.Count == 0) AssertTheGapIsNotNegative($"plan {seed}", result);
        }
    }

    // ── Ce qui sert aux essais ───────────────────────────────────

    /// <summary>A plan placed on the venue's own stock, at the level every generation runs at.</summary>
    private RiggingResult Place(List<Charge> charges) =>
        Optimizer.Optimize(charges, _venue.Palonniers, _venue.Mothers, verbose: false);

    /// <summary>
    /// A plan drawn from a seed: the same seed always gives the same charges, so a failure is reproduced by
    /// its number alone. They fall where the venue's own do - some on a point line, to be hung straight
    /// from it, some between two, needing a palonnier - at x taken off the beams so each hangs inside a
    /// bay, and repeated, so that charges share an x and one palonnier can gather them.
    /// </summary>
    private static List<Charge> SeededPlan(int seed, int count)
    {
        var rnd = new Random(seed);
        double[] offBeam = { -2.4, -1.2, 1.2, 2.4 };

        var plan = new List<Charge>();
        for (int i = 1; i <= count; i++)
        {
            int row = rnd.Next(1, Geometry.RowYList.Count - 1);
            bool onThePointLine = rnd.Next(2) == 0;

            plan.Add(new Charge
            {
                Name = $"C{i}",
                X = Geometry.ColXList[rnd.Next(1, Geometry.ColXList.Count - 1)].X + offBeam[rnd.Next(offBeam.Length)],
                Y = onThePointLine
                    ? Geometry.RowYList[row].Y
                    : (Geometry.RowYList[row].Y + Geometry.RowYList[row + 1].Y) / 2,
                Weight = 100 + 50 * rnd.Next(9),
            });
        }
        return plan;
    }

    /// <summary>
    /// Every floor of one run, against what that run actually produced. The metres are compared to the
    /// whole drawn length, which is the reading <see cref="LayoutBound.Gap"/> itself uses: no honest floor
    /// exists for a palonnier's span, so the mother-metre floor is never held against the lines alone.
    /// </summary>
    private static void AssertFloorHolds(string plan, RiggingResult result)
    {
        var bound = result.Bound!;
        var cost = result.Cost;

        Assert.True(bound.Palonniers <= cost.Palonniers,
            $"{plan} : plancher de {bound.Palonniers} palonnier(s) pour {cost.Palonniers} posé(s) — "
            + "un plancher au-dessus de l'implantation réelle est un plancher faux. " + bound.Describe());

        Assert.True(bound.Pieces <= cost.Pieces,
            $"{plan} : plancher de {bound.Pieces} pièce(s) pour {cost.Pieces} assemblée(s). " + bound.Describe());

        // The micrometre is floating-point noise: metres summed in two different orders differ in the last
        // place, and that is not a broken bound.
        Assert.True(bound.MotherMetres <= cost.DrawnMetres + 1e-6,
            $"{plan} : plancher de {bound.MotherMetres:0.##} m de ligne de mother pour {cost.DrawnMetres:0.##} m "
            + "tracés en tout. " + bound.Describe());
    }

    private static void AssertTheGapIsNotNegative(string plan, RiggingResult result)
    {
        var gap = result.Bound!.Gap(result.Cost);

        Assert.True(gap.Palonniers >= 0 && gap.Pieces >= 0 && gap.Metres >= -1e-6,
            $"{plan} : écart négatif — {gap.Describe()} ({result.Bound.Describe()})");
    }
}
