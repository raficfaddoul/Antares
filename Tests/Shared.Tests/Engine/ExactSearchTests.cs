using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// The exact search: every way of grouping the charges that need a palonnier, and the best of them by the
/// ranking.
///
/// THE ONE GUARANTEE IS WHAT MAKES THE SETTING SAFE TO TURN ON. The search starts from the greedy layout
/// and can only replace it with something the ranking prefers: a candidate is built, measured and compared
/// before it is kept, and anything that goes wrong - a branch that cannot be built, the budget running out
/// - leaves the greedy layout standing. So a user who turns the setting on cannot get a worse plan than
/// the one they had. That is the first three tests, on the venue's own plan, on the files the application
/// publishes as examples, and on a sweep of seeded plans.
///
/// The rest is what a study has to be able to say afterwards: how many layouts were really walked, whether
/// anything was PROVEN (never claimed lightly - the budget running out, a branch that failed and a plan
/// with no complete cover all leave it false), and that the inventory is left holding exactly the pieces
/// the returned layout hangs. That last one is the search's own hazard: it takes pieces out of the stock
/// in every branch it opens, and a piece left behind by a branch it abandoned is a truss the crew would go
/// looking for on the truck.
/// </summary>
[Collection(EngineCollection.Name)]
public class ExactSearchTests
{
    private readonly VenuePlan _venue;

    public ExactSearchTests(VenuePlan venue) => _venue = venue;

    /// <summary>The setting turned on, with the budget a page actually generates under.</summary>
    private static OptimizerOptions Searching => new() { Search = SearchLevel.Exact };

    // ══════════════════════════════════════════════════════════
    // LA GARANTIE
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_exact_search_never_returns_a_layout_the_ranking_prefers_less_on_the_venue_s_plan()
    {
        // The plan the whole tool was written for. It hangs charges on palonniers, so there is something to
        // search: on a plan with nothing to group the guarantee would hold for want of anything happening.
        Assert.True(_venue.Placed.Palonniers.Count > 0,
            "le plan du site doit demander des palonniers, sans quoi cette garantie ne vérifie rien");

        AssertNeverWorse("le plan du site", _venue.Placed, Search(_venue.Charges));
    }

    [Fact]
    public void The_exact_search_never_returns_a_layout_the_ranking_prefers_less_on_the_published_examples()
    {
        // The example workbooks are a new user's first run, and the setting is the first thing a curious
        // user turns on. Charges and stock both come from wwwroot/exemples, as they are handed out.
        var (pals, mothers) = ExcelLoader.LoadInventory(Fixtures.ExamplesDir, null);
        var charges = ExcelLoader.LoadCharges(Path.Combine(Fixtures.ExamplesDir, "Charges.xlsx"));

        var greedy = Optimizer.Optimize(charges, pals, mothers, verbose: false);
        var searched = Optimizer.Optimize(charges, pals, mothers, verbose: false, options: Searching);

        AssertNeverWorse("les fichiers d'exemple", greedy, searched);
    }

    [Fact]
    public void The_exact_search_never_returns_a_layout_the_ranking_prefers_less_on_a_sweep_of_seeded_plans()
    {
        // One plan proves nothing about the next, so the guarantee is checked against plans drawn from
        // seeds - reproducible by their number alone - each placed twice from the venue's real stock. The
        // budget is cut short on purpose: the guarantee has to hold whether the walk finished or gave up
        // half way, and giving up is what happens on a plan with many charges.
        var shortBudget = new OptimizerOptions
        {
            Search = SearchLevel.Exact,
            SearchBudgetMs = 750,
            SearchBudgetNodes = 3_000,
        };

        for (int seed = 1; seed <= 8; seed++)
        {
            var charges = SeededPlan(seed, count: 5);
            var greedy = Optimizer.Optimize(charges, _venue.Palonniers, _venue.Mothers, verbose: false);
            var searched = Optimizer.Optimize(charges, _venue.Palonniers, _venue.Mothers, verbose: false,
                                              options: shortBudget);

            AssertNeverWorse($"plan {seed}", greedy, searched);

            // Whatever the walk tried on the way, what is missing from the stock is what is hanging.
            AssertStockIsWhatTheLayoutHangs($"plan {seed}", searched);
        }
    }

    // ══════════════════════════════════════════════════════════
    // CE QUE LE RUN EN DIT
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_search_says_how_much_it_walked_and_proves_the_optimum_on_a_plan_it_can_walk_whole()
    {
        // Three charges to group is a space the walk covers entirely in milliseconds, so this is where
        // "proven" has to appear. It means: among every way of covering these charges with the candidate
        // groups, none gives a layout this ranking prefers to the one returned. It does not say that no
        // better layout exists in the world - another run of mother lines, another choice of pieces, a
        // group no generator proposed are all outside what was walked.
        var result = Search(SmallPlan());

        Assert.True(result.Uncovered.Count == 0,
            "le petit plan doit se poser entièrement : " + string.Join(", ", result.Uncovered.Select(c => c.Name)));

        Assert.Equal(SearchLevel.Exact, result.Search.Level);
        Assert.True(result.Search.Proven,
            "un plan de trois charges à grouper se parcourt entièrement : l'optimalité doit être prouvée. "
            + result.Search.Describe());

        // The greedy layout, plus every complete cover the walk built and measured.
        Assert.True(result.Search.LayoutsExamined >= 2,
            "la recherche doit avoir construit et mesuré au moins une implantation de plus que la gloutonne : "
            + result.Search.Describe());

        // And the study can say which objective they were all weighed under - keys, not labels.
        Assert.Equal(Objective.Format(Objective.DefaultRanking), result.Search.Ranking);
    }

    [Fact]
    public void A_greedy_run_claims_no_optimality_and_says_it_examined_one_layout()
    {
        // The level every generation has run at until now: one layout, built by taking the best group in
        // hand and never looking back. Anything it said about being optimal would be an invention, and a
        // study that cannot tell the two levels apart is not traceable.
        var greedy = _venue.Placed.Search;

        Assert.Equal(SearchLevel.Bounds, greedy.Level);
        Assert.False(greedy.Proven, "le placement glouton ne prouve rien : " + greedy.Describe());
        Assert.Equal(1, greedy.LayoutsExamined);
        Assert.Contains("aucune optimalité n'est prouvée", greedy.Describe());
    }

    [Fact]
    public void A_budget_of_almost_nothing_still_returns_a_usable_layout_and_proves_nothing()
    {
        // A page that generates a layout on a click has to come back. The budget is checked at every node
        // so that no branch can miss it, and what comes back when it bites is the greedy layout - a whole,
        // usable implantation - with Proven false. Both halves of the stop are cut to almost nothing here:
        // the clock, and the node count that keeps two machines returning the same layout for one plan.
        var starved = Optimizer.Optimize(_venue.Charges, _venue.Palonniers, _venue.Mothers, verbose: false,
            options: new OptimizerOptions
            {
                Search = SearchLevel.Exact,
                SearchBudgetMs = 1,
                SearchBudgetNodes = 1,
            });

        Assert.False(starved.Search.Proven,
            "une recherche coupée par son budget n'a rien prouvé : " + starved.Search.Describe());

        // Usable: every charge carried, and a whole layout - not a half-walked candidate.
        Assert.True(starved.Uncovered.Count == 0,
            "charge(s) laissée(s) en l'air par une recherche interrompue : "
            + string.Join(", ", starved.Uncovered.Select(c => c.Name)));
        Assert.True(starved.Palonniers.Count > 0, "l'implantation rendue doit être complète, palonniers compris");

        Assert.True(Objective.Compare(starved.Cost, _venue.Placed.Cost) <= 0,
            "un budget épuisé doit rendre l'implantation gloutonne, jamais moins bien : "
            + starved.Cost.Describe() + " contre " + _venue.Placed.Cost.Describe());
    }

    [Fact]
    public void A_plan_hung_entirely_on_the_point_lines_needs_no_palonnier_and_is_proven_at_once()
    {
        // Every charge sits on a point line, so each hangs straight from that line's mother: there is
        // nothing to group, one layout exists, and the greedy path has already built it. Walking that space
        // whole is walking nothing at all - which is how the search is instant here rather than merely quick.
        var onThePointLines = _venue.Charges
            .Where(c => Math.Abs(c.Y - Geometry.NearestRow(c.Y).Y) <= Geometry.TOL)
            .Take(8)
            .ToList();

        Assert.True(onThePointLines.Count > 0,
            "le plan du site doit porter des charges posées sur ses lignes de points");

        var result = Search(onThePointLines);

        Assert.True(result.Palonniers.Count == 0,
            $"{result.Palonniers.Count} palonnier(s) posé(s) pour des charges qui pendent toutes d'une ligne de points");
        Assert.True(result.Uncovered.Count == 0,
            "charge(s) non portée(s) : " + string.Join(", ", result.Uncovered.Select(c => c.Name)));

        Assert.True(result.Search.Proven,
            "il n'existe qu'une implantation : elle est optimale. " + result.Search.Describe());

        // The greedy layout and nothing else: not one cover was built, because there was none to build.
        Assert.Equal(1, result.Search.LayoutsExamined);
    }

    // ══════════════════════════════════════════════════════════
    // LE STOCK, REMIS COMME IL A ÉTÉ TROUVÉ
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_stock_holds_back_exactly_the_pieces_the_returned_layout_hangs()
    {
        // The search opens a branch, takes the pieces that branch needs out of the inventory, and rewinds
        // them when it abandons it (StockMark). A rewind missed would leave the run reporting a stock short
        // of pieces nothing is hanging from: the crew would look for a truss that is on the shelf, and the
        // next line to be sized would be refused a model it could have had.
        var result = Search(_venue.Charges);

        AssertStockIsWhatTheLayoutHangs("le plan du site", result);

        // And no row handed out more often than the venue owns it, whatever the search tried on the way.
        Assert.All(result.PalsStock.Concat(result.MomsStock), row =>
            Assert.True(row.Stock >= 0 && row.Stock <= row.StockInit,
                $"{row.Type} {row.Length:0.##} m : {row.Stock} restante(s) sur {row.StockInit} en inventaire"));

        // The count the study quotes is the stock's own count: one measurement, one figure.
        int hung = result.Palonniers.Sum(p => p.Assembly!.Count) + result.Mothers.Sum(m => m.Assembly!.Count);
        Assert.Equal(result.Cost.Pieces, hung);
    }

    // ── Ce qui sert aux essais ───────────────────────────────────

    /// <summary>A plan placed on the venue's own stock with the exact search turned on.</summary>
    private RiggingResult Search(List<Charge> charges) =>
        Optimizer.Optimize(charges, _venue.Palonniers, _venue.Mothers, verbose: false, options: Searching);

    /// <summary>
    /// The guarantee, on one plan: the layout the search returns is the greedy one, or one the ranking
    /// prefers to it - never one it prefers less.
    /// <para>
    /// The one exception runs the other way: a spotless layout replaces a greedy one that left a charge in
    /// the air or took a truss past its allowed load, whatever the ranking makes of the two, because those
    /// are hard constraints - a layout that breaks one is not a worse layout, it is not a layout
    /// (Objective.HardConstraints).
    /// </para>
    /// </summary>
    private static void AssertNeverWorse(string plan, RiggingResult greedy, RiggingResult searched)
    {
        Assert.Equal(SearchLevel.Exact, searched.Search.Level);

        bool greedyIsSound = greedy.Uncovered.Count == 0 && greedy.Errors.Count == 0;
        bool searchedIsSound = searched.Uncovered.Count == 0 && searched.Errors.Count == 0;
        int verdict = Objective.Compare(searched.Cost, greedy.Cost, Objective.DefaultRanking);

        string said = $"{plan} — gloutonne : {greedy.Cost.Describe()} ; recherche exacte : {searched.Cost.Describe()}";

        if (greedyIsSound)
        {
            Assert.True(verdict <= 0,
                "la recherche exacte a rendu une implantation que le classement préfère MOINS que la gloutonne, "
                + "alors qu'elle ne peut que la garder ou faire mieux : " + said);
            return;
        }

        Assert.True(searchedIsSound || verdict <= 0,
            "l'implantation gloutonne laissait une charge en l'air ou dépassait une charge admise : la "
            + "recherche ne peut la remplacer que par une implantation sans reproche : " + said);
    }

    /// <summary>
    /// The pieces missing from both stocks are the pieces the returned layout hangs, and no others. What a
    /// branch took, that branch gave back.
    /// </summary>
    private static void AssertStockIsWhatTheLayoutHangs(string plan, RiggingResult result)
    {
        Assert.All(result.Palonniers, p =>
            Assert.True(p.Assembly is not null, $"{plan} — {p} : aucune pièce d'inventaire n'est attachée à ce palonnier"));
        Assert.All(result.Mothers, m =>
            Assert.True(m.Assembly is not null, $"{plan} — {m} : aucune pièce d'inventaire n'est attachée à cette ligne"));

        AssertStockMatchesLayout($"{plan} — {InventoryFiles.PalonniersFileName}", result.PalsStock,
            result.Palonniers.SelectMany(p => p.Assembly!.Pieces));
        AssertStockMatchesLayout($"{plan} — {InventoryFiles.MothersFileName}", result.MomsStock,
            result.Mothers.SelectMany(m => m.Assembly!.Pieces));
    }

    /// <summary>
    /// What one workbook's stock says was taken, against what the returned layout actually hangs. Counted
    /// by model and length, so two inventory lines of the same reference make one parcel on either side.
    /// </summary>
    private static void AssertStockMatchesLayout(
        string file, IReadOnlyList<BarSpec> stock, IEnumerable<BarSpec> hung)
    {
        var taken = stock
            .Where(r => r.StockInit > r.Stock)
            .GroupBy(Reference)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.StockInit - r.Stock));

        var placed = hung
            .GroupBy(Reference)
            .ToDictionary(g => g.Key, g => g.Count());

        var disagree = taken.Keys.Union(placed.Keys)
            .Where(k => taken.GetValueOrDefault(k) != placed.GetValueOrDefault(k))
            .Select(k => $"{k} : {taken.GetValueOrDefault(k)} sortie(s) du stock pour {placed.GetValueOrDefault(k)} posée(s)")
            .ToList();

        Assert.True(disagree.Count == 0,
            $"{file} : le stock et l'implantation rendue ne comptent pas les mêmes pièces — "
            + string.Join(" | ", disagree));

        static string Reference(BarSpec row) => $"{row.Type} {row.Length:0.##} m";
    }

    /// <summary>
    /// The venue's own plan cut down to a space one walk covers whole: three of its charges that need a
    /// palonnier, and two hung straight on a point line. Taken from the real plan rather than invented, so
    /// that the stock which carries all twenty-four certainly carries these five.
    /// </summary>
    private List<Charge> SmallPlan()
    {
        bool OnAPointLine(Charge c) => Math.Abs(c.Y - Geometry.NearestRow(c.Y).Y) <= Geometry.TOL;

        var toHang = _venue.Charges.Where(c => !OnAPointLine(c)).Take(3).ToList();

        Assert.True(toHang.Count == 3,
            $"le plan du site ne porte plus que {toHang.Count} charge(s) hors des lignes de points : "
            + "il n'y a plus de quoi faire chercher la recherche exacte");

        return toHang.Concat(_venue.Charges.Where(OnAPointLine).Take(2)).ToList();
    }

    /// <summary>
    /// A plan drawn from a seed, as in <see cref="BoundsTests"/>: the same seed always gives the same
    /// charges, so a failure is reproduced by its number alone. They fall where the venue's own do - some
    /// on a point line, to be hung straight from it, some between two, needing a palonnier - at x taken off
    /// the beams so each hangs inside a bay, and repeated, so charges share an x and one palonnier can
    /// gather them.
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
}
