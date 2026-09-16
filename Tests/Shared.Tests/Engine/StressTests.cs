using Helpers;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// Hundreds of plans nobody wrote, put through the whole engine. This is the suite's best net.
///
/// The venue's own plan exercises one path through the optimiser. Random plans exercise the rest: loads
/// off every point line and between them, stocks too small to reach across a bay, palonniers that cross
/// three lines, plans the stock simply cannot carry. Two runs, each with its own question.
///
/// On small stocks - every Nombre redrawn between 0 and 3 - the question is whether the engine ever
/// cheats: a piece used twice, a bay taken past its value, a charge dropped without a word, an exception.
/// On the venue's full stock, the question is the round trip: does the load calculation, reading only the
/// stored rows, find on each attachment point exactly what the optimiser put there.
///
/// The seeds are fixed and written down, so a failure is replayed rather than hunted: the failing plan is
/// the one this seed produces at that iteration, every time, on every machine.
/// </summary>
[Collection(EngineCollection.Name)]
public class StressTests : IClassFixture<StressTests.Runs>
{
    private readonly Runs _runs;

    public StressTests(Runs runs) => _runs = runs;

    // ── Small stocks: the engine never cheats ────────────────────

    [Fact]
    public void No_plan_on_a_small_stock_uses_a_piece_it_does_not_have_overloads_a_bay_or_crashes()
    {
        Assert.True(_runs.SmallStockFailure is null, _runs.SmallStockFailure);
    }

    [Fact]
    public void The_small_stocks_did_run_short_so_the_check_had_something_to_catch()
    {
        // If every plan were placed in full, the run would prove only that a generous stock is generous.
        // What is wanted is the edge: stocks that cannot do it, refusing cleanly.
        Assert.True(_runs.PlansShortOfSmallStock > 0,
            $"aucun des {Runs.SmallStockPlans} plans n'a manqué de stock : le contrôle n'a rien vérifié");
    }

    // ── The full stock: the stored layout is the layout ──────────

    [Fact]
    public void On_every_random_plan_the_calculation_finds_what_the_optimiser_placed()
    {
        // Each attachment point, and each palonnier hung from exactly the lines the optimiser hung it
        // from. A disagreement here is a study verifying loads nobody is hanging.
        Assert.True(_runs.FullStockFailure is null, _runs.FullStockFailure);
    }

    [Fact]
    public void Enough_random_plans_were_placed_in_full_for_that_to_mean_something()
    {
        // The round trip is only run on plans that were placed in full - a partial layout is never saved.
        // Too few of them and the comparison above is vacuous.
        Assert.True(_runs.PlansPlacedInFull >= 50,
            $"seulement {_runs.PlansPlacedInFull} plans placés en entier sur {Runs.FullStockPlans}");
    }

    /// <summary>
    /// The two runs, done once for the whole class - they are the slow part of the suite, about a second
    /// together, and each question below reads its own answer out of them.
    /// </summary>
    public sealed class Runs
    {
        /// <summary>Redrawing the stock and the plan from this seed reproduces the small-stock run exactly.</summary>
        public const int SmallStockSeed = 20260911;

        /// <summary>And this one the full-stock run.</summary>
        public const int FullStockSeed = 911;

        public const int SmallStockPlans = 300;
        public const int FullStockPlans = 150;

        public Runs()
        {
            var (palonniers, mothers) = ExcelLoader.LoadInventory(Fixtures.Dir, null);

            SmallStockFailure = RunOnSmallStocks(palonniers, mothers, out int shortOfStock);
            PlansShortOfSmallStock = shortOfStock;

            FullStockFailure = RunOnTheFullStock(palonniers, mothers, out int placedInFull);
            PlansPlacedInFull = placedInFull;
        }

        /// <summary>The first plan whose layout cheated, named by its seed; null when none did.</summary>
        public string? SmallStockFailure { get; }

        /// <summary>How many of the small-stock plans left a charge uncovered for lack of pieces.</summary>
        public int PlansShortOfSmallStock { get; }

        /// <summary>The first plan where the stored layout and the calculation disagreed; null when none did.</summary>
        public string? FullStockFailure { get; }

        /// <summary>How many of the full-stock plans were placed in full, and so went through the round trip.</summary>
        public int PlansPlacedInFull { get; }

        private static string? RunOnSmallStocks(
            List<BarSpec> palonniers, List<BarSpec> mothers, out int shortOfStock)
        {
            shortOfStock = 0;
            string? firstFailure = null;
            var gen = new Random(SmallStockSeed);

            for (int seed = 0; seed < SmallStockPlans; seed++)
            {
                // Every Nombre redrawn between 0 and 3: most plans then have to be refused in part, which
                // is exactly the case a layout is most likely to cheat in.
                List<BarSpec> Scale(List<BarSpec> rows) => rows.Select(b =>
                {
                    int n = gen.Next(0, 4);
                    return new BarSpec
                    {
                        Type = b.Type, Length = b.Length, StockInit = n, Stock = n,
                        Capacity = b.Capacity, WeightKg = b.WeightKg,
                    };
                }).ToList();

                var smallPalonniers = Scale(palonniers);
                var smallMothers = Scale(mothers);

                int count = gen.Next(3, 22);
                var plan = Enumerable.Range(0, count).Select(i =>
                {
                    // Half on a point line, half anywhere, written in the studies' axes as Charges.xlsx is.
                    double studyX = gen.Next(2) == 0
                        ? StudyAxes.StudyX(Geometry.RowYList[gen.Next(1, Geometry.RowYList.Count - 1)].Y)
                          + (gen.NextDouble() - 0.5) * 0.1
                        : gen.NextDouble() * 26 - 13;
                    double studyY = gen.NextDouble() * 30 - 12;
                    var (x, y) = StudyAxes.ToGrid(studyX, studyY);

                    return new Charge { Name = $"R{seed}_{i}", X = x, Y = y, Weight = 50 + gen.Next(0, 850) };
                }).ToList();

                try
                {
                    var result = Optimizer.Optimize(plan, smallPalonniers, smallMothers, verbose: false,
                        options: new OptimizerOptions { OrthogonalOnly = seed % 2 == 0 });

                    var wrong = LayoutChecks.WhatIsWrong(result, smallPalonniers, smallMothers);
                    if (wrong is not null)
                        firstFailure ??= $"graine {SmallStockSeed}, plan {seed} : {wrong}";

                    if (result.Uncovered.Count > 0) shortOfStock++;
                }
                catch (Exception ex)
                {
                    firstFailure ??= $"graine {SmallStockSeed}, plan {seed} : {ex.GetType().Name} — {ex.Message}";
                }
            }

            return firstFailure;
        }

        private static string? RunOnTheFullStock(
            List<BarSpec> palonniers, List<BarSpec> mothers, out int placedInFull)
        {
            placedInFull = 0;
            string? firstFailure = null;
            var gen = new Random(FullStockSeed);

            for (int seed = 0; seed < FullStockPlans; seed++)
            {
                var plan = RandomPlan(gen, "F", seed);

                RiggingResult result;
                try
                {
                    result = Optimizer.Optimize(plan, palonniers, mothers, verbose: false,
                        options: new OptimizerOptions { OrthogonalOnly = seed % 3 == 0 });
                }
                catch (Exception ex)
                {
                    firstFailure ??= $"graine {FullStockSeed}, plan {seed} : {ex.GetType().Name} — {ex.Message}";
                    continue;
                }

                var wrong = LayoutChecks.StockOveruse(result, palonniers, mothers)
                            ?? LayoutChecks.ValuesRespected(result);
                if (wrong is not null)
                {
                    firstFailure ??= $"graine {FullStockSeed}, plan {seed} : {wrong}";
                    continue;
                }

                // A layout that leaves a charge uncovered is never saved, so there is nothing to store and
                // nothing to read back: those plans are counted and passed over.
                if (result.Uncovered.Count > 0) continue;
                placedInFull++;

                var stored = new StoredLayout(result);
                var disagreement = stored.PointLoadDisagreement() ?? stored.HangingDisagreement();
                if (disagreement is not null)
                    firstFailure ??= $"graine {FullStockSeed}, plan {seed} : {disagreement}";
            }

            return firstFailure;
        }

        /// <summary>
        /// A plan as the Charges page holds one: three to twenty-five loads, half of them within 5 cm of a
        /// point line - where the placement hangs them straight on their mother line - and half anywhere,
        /// written in the studies' axes and converted once, exactly as the workbook reader does it.
        /// </summary>
        private static List<Charge> RandomPlan(Random gen, string prefix, int seed)
        {
            int count = gen.Next(3, 26);

            return Enumerable.Range(0, count).Select(i =>
            {
                double studyX = gen.Next(2) == 0
                    ? StudyAxes.StudyX(Geometry.RowYList[gen.Next(1, Geometry.RowYList.Count - 1)].Y)
                      + (gen.NextDouble() - 0.5) * 0.1
                    : gen.NextDouble() * 26 - 13;
                double studyY = gen.NextDouble() * 30 - 12;
                var (x, y) = StudyAxes.ToGrid(studyX, studyY);

                return new Charge { Name = $"{prefix}{seed}_{i}", X = x, Y = y, Weight = 50 + gen.Next(0, 850) };
            }).ToList();
        }
    }
}
