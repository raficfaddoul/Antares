// =============================================================
// ExactSearch.cs — Toutes les façons de grouper les charges, et la meilleure qu'on garde.
//
// WHAT IS BEING SEARCHED. A charge that does not sit on a point line has to hang from a palonnier, and a
// palonnier carries a GROUP of such charges (GroupGenerator lists every group one palonnier could carry).
// Choosing the layout is therefore choosing a set of groups that covers every one of those charges
// EXACTLY ONCE - no charge left out, no charge hanging from two palonniers. The greedy path picks the
// biggest group it can still assemble, then the next, and never looks back; it is fast and it is often
// right, but nothing tells it that two medium groups would have beaten the big one it took first.
//
// This file tries them all. For every complete cover it builds the layout exactly the way the greedy path
// builds its own - the same Optimizer.Build, the same stocks, the same mother lines and crossings -
// measures it (LayoutCost), and keeps it only when Objective says the ranking prefers it.
//
// ── THE ONE GUARANTEE ────────────────────────────────────────────────────────────────────────
// THE SEARCH STARTS FROM THE GREEDY LAYOUT AND CAN ONLY REPLACE IT WITH SOMETHING THE RANKING PREFERS.
// The greedy answer is the incumbent before the first branch is opened; a candidate replaces it only
// after being built, measured and compared, and anything that goes wrong - a branch that cannot be
// built, an exception, the budget running out - leaves the incumbent standing. So turning the setting on
// cannot make a layout worse, which is the only promise a user needs before turning it on. The one
// exception is in the other direction: a spotless layout replaces a greedy one that left a charge in the
// air or broke an allowed load, because those are hard constraints (Objective.HardConstraints) and a
// layout that breaks one is not a worse layout, it is not a layout.
//
// ── WHAT IS CUT, AND WHY CUTTING IS SAFE ─────────────────────────────────────────────────────
// Two things keep the walk down to a size a page can wait for.
//   1. THE ORDER OF THE WALK. At each step the charge with the FEWEST remaining groups is the one we
//      decide for. A charge only two palonniers could ever carry splits the walk in two; leaving it for
//      later would have us discover the same dead end thousands of times over.
//   2. THE CUT ITSELF. The palonnier count of a layout can only grow as the walk goes deeper - every
//      group added is one more palonnier, and the charges still uncovered need at least one more. So as
//      soon as the branch in hand already needs more palonniers than the best layout found so far, no
//      completion of it can win on the first criterion, and the whole branch is abandoned unexamined.
//      Nothing is lost: a criterion lower down the ranking never overturns the first one. That cut is
//      only made when the ranking in force actually starts with the palonniers - a developer who moved
//      that line has changed what "worse" means, and the cut would no longer be sound, so it is dropped.
//
// ── WHAT "PROVEN" MEANS, EXACTLY ─────────────────────────────────────────────────────────────
// Proven says: among EVERY way of covering the charges with the candidate groups, none gives a layout
// this ranking prefers to the one returned. It does not say that no better layout exists in the world -
// a different run of mother lines, another choice of pieces, a group no generator proposed, are all
// outside what was walked. And it is claimed only when the whole space was walked: the budget stopping
// the walk, a branch that failed, or a charge no group can carry all leave it false. A search that is
// not sure says so.
//
// A domain file: no Blazor, no EF, no file nor database read - the settings arrive in OptimizerOptions,
// and the only thing written anywhere is the one console line saying a search had to be abandoned.
// =============================================================

using System.Diagnostics;
using RiggingOptimizer.Models;

namespace RiggingOptimizer.Core;

// ── Un repère dans le stock ──────────────────────────────────
/// <summary>
/// Where a stock stood at a moment, so a caller can put it back there.
/// <para>
/// The stock is shared by every attempt: a branch that assembles three palonniers takes those pieces out
/// of the inventory, and the next branch would be searching with a stock the first one had emptied -
/// and would be told, wrongly, that its own grouping cannot be assembled. A mark taken before a branch
/// and restored after it makes each branch see the same inventory.
/// </para>
/// <para>
/// It reads the state from <see cref="PieceStock.Snapshot"/> and puts it back with the stock's own Give
/// and Take: the stock keeps its counting to itself, and this class only ever asks it for what it
/// already offers.
/// </para>
/// </summary>
public sealed class StockMark
{
    private readonly PieceStock _stock;
    private readonly int[]      _left;

    private StockMark(PieceStock stock, int[] left)
    {
        _stock = stock;
        _left  = left;
    }

    /// <summary>The stock as it stands now.</summary>
    public static StockMark Of(PieceStock stock) =>
        new(stock, stock.Snapshot().Select(row => row.Stock).ToArray());

    /// <summary>
    /// Puts the stock back exactly as it was: what a branch took comes back, and what a branch gave back
    /// twice - a build hands pieces back when it has to drop a palonnier - is taken out again. Restoring
    /// an already restored mark changes nothing, so it is safe on every path out of a branch.
    /// </summary>
    public void Restore()
    {
        var rows = _stock.Rows;
        for (int i = 0; i < rows.Count && i < _left.Length; i++)
        {
            int now = _stock.Left(rows[i]);
            if (now == _left[i]) continue;

            // Une "botte" de pièces d'une seule ligne d'inventaire : le stock ne sait rendre et prendre
            // que des assemblages, et celui-ci n'est jamais accroché à quoi que ce soit.
            var parcel = new Assembly(rows[i].Type, Enumerable.Repeat(rows[i], Math.Abs(now - _left[i])));
            if (now < _left[i]) _stock.Give(parcel);
            else                _stock.Take(parcel);
        }
    }
}

// ── La recherche ─────────────────────────────────────────────
/// <summary>
/// Every way of covering the charges that need a palonnier, and the best layout among them by the
/// ranking. See the file's header for what is searched, what is cut, and what "proven" is worth.
/// </summary>
public static class ExactSearch
{
    /// <summary>
    /// Walks the covers and returns the layout to keep: <paramref name="incumbent"/> itself, or one the
    /// ranking prefers to it.
    /// </summary>
    /// <param name="incumbent">The greedy layout, already built and measured. Returned unless beaten.</param>
    /// <param name="workshop">The bench every attempt is built on - the same one the greedy layout used.</param>
    /// <param name="allGroups">The candidate groups, as GroupGenerator produced them for this plan.</param>
    /// <param name="toHang">The charges that need a palonnier: what a cover has to cover.</param>
    /// <param name="palFull">The palonnier stock before anything was placed; the walk starts from there.</param>
    /// <param name="motherFull">The mother stock before anything was placed.</param>
    /// <param name="examined">How many complete layouts the walk built and measured.</param>
    /// <param name="proven">Whether the whole space was walked - see the file's header.</param>
    public static RiggingResult Run(
        RiggingResult                 incumbent,
        LayoutWorkshop                workshop,
        IReadOnlyList<CandidateGroup> allGroups,
        IReadOnlyList<Charge>         toHang,
        StockMark                     palFull,
        StockMark                     motherFull,
        out int                       examined,
        out bool                      proven)
    {
        examined = 0;
        proven   = false;

        var ranking = workshop.Options.RankingInForce;
        var best    = incumbent;

        // Rien à grouper : aucune charge ne demande de palonnier, il n'existe qu'une implantation et le
        // glouton l'a déjà construite. La parcourir entièrement, c'est ne rien parcourir du tout.
        if (toHang.Count == 0)
        {
            proven = true;
            return best;
        }

        // Les groupes qui couvrent chaque charge, dans l'ordre où le glouton les préférerait : les plus
        // gros d'abord, puis les plus courts. Les bonnes couvertures sortent tôt, et plus tôt la
        // meilleure est trouvée, plus l'élagage coupe de branches.
        var optionsFor = new Dictionary<string, List<CandidateGroup>>();
        foreach (var c in toHang) optionsFor[c.Name] = new List<CandidateGroup>();
        foreach (var g in allGroups.OrderByDescending(g => g.Names.Count).ThenBy(g => g.Span))
            foreach (var name in g.Names)
                if (optionsFor.TryGetValue(name, out var list)) list.Add(g);

        // Une charge qu'aucun groupe ne couvre : il n'existe aucune couverture complète de ce plan, donc
        // rien à chercher et rien à prouver. Le glouton reste, avec ses charges non portées et ce qu'il
        // en dit ; c'est au pipeline de refuser l'implantation.
        if (optionsFor.Values.Any(list => list.Count == 0)) return best;

        // Le classement peut avoir été réordonné depuis la page Réglages. L'élagage sur le nombre de
        // palonniers ne vaut que s'il reste le premier critère, et à la baisse : sinon, on ne coupe rien
        // et la recherche se contente du budget. La clé, elle, ne changera jamais (Objective.cs).
        bool countFirst = ranking.Count > 0
                       && string.Equals(ranking[0].Key, "palonniers", StringComparison.OrdinalIgnoreCase)
                       && ranking[0].Better == Prefer.Less;

        int  budgetMs    = workshop.Options.SearchBudgetMs;
        int  budgetNodes = workshop.Options.SearchBudgetNodes;
        int  nodes       = 0;
        bool stopped     = false;   // le budget a coupé la marche : ce qui reste n'a pas été regardé
        bool failed      = false;   // quelque chose est tombé : on garde ce qu'on a, sans rien prouver

        var uncovered  = new HashSet<string>(toHang.Select(c => c.Name));
        var chosen     = new List<CandidateGroup>();
        var assemblies = new List<Assembly>();
        int built      = 0;

        // La recherche repart du stock plein : le glouton vient d'y puiser, et ses pièces appartiennent à
        // son implantation à lui, qui est déjà construite et mesurée.
        palFull.Restore();
        motherFull.Restore();

        var clock = Stopwatch.StartNew();

        try
        {
            Cover();
            proven = !stopped && !failed;
        }
        catch (Exception ex)
        {
            // Rien de ce que fait cette recherche ne doit pouvoir faire échouer une génération que le
            // glouton avait menée à bout : l'implantation de départ reste, et rien n'est prouvé.
            Console.WriteLine($"  ⚠️  Recherche exacte interrompue : {ex.Message}. "
                              + "L'implantation gloutonne est conservée.");
            proven = false;
        }
        finally
        {
            palFull.Restore();
            motherFull.Restore();
        }

        // Une implantation qui laisse une charge en l'air ou qui dépasse une charge admise n'est
        // l'optimum de rien : la marche a eu beau tout parcourir, elle n'a rien trouvé de mieux, ce qui
        // n'est pas la même chose que d'avoir trouvé la meilleure.
        if (best.Uncovered.Count > 0 || best.Errors.Count > 0) proven = false;

        examined = built;
        return best;

        // ── La marche en profondeur ──────────────────────────────
        void Cover()
        {
            // Le budget, vérifié à chaque nœud pour qu'aucune branche ne puisse le manquer : le temps
            // pour la page, le compte de nœuds pour que deux machines rendent la même implantation.
            if (nodes >= budgetNodes || clock.ElapsedMilliseconds >= budgetMs)
            {
                stopped = true;
                return;
            }
            nodes++;

            // Plus rien à couvrir : la couverture est complète, on la construit et on la mesure.
            if (uncovered.Count == 0)
            {
                Examine();
                return;
            }

            // L'élagage : chaque groupe déjà choisi est un palonnier, et ce qui reste à couvrir en
            // demande au moins un de plus. Si ce total dépasse déjà le meilleur, aucune façon de finir
            // cette branche ne peut gagner sur le premier critère - inutile d'aller voir.
            // Il ne vaut que si le meilleur en main porte bien toutes les charges : comparer un nombre de
            // palonniers à celui d'une implantation qui en laisse tomber couperait à tort.
            if (countFirst && best.Uncovered.Count == 0 && chosen.Count + 1 > best.Cost.Palonniers) return;

            // La charge qui a le moins de groupes encore utilisables. C'est elle que l'on décide : celle
            // qui laisse le moins de choix est celle qui refermera le plus vite les branches sans issue.
            string? pick   = null;
            int     fewest = int.MaxValue;
            foreach (var name in uncovered)
            {
                int count = 0;
                foreach (var g in optionsFor[name])
                    if (g.Names.IsSubsetOf(uncovered)) count++;

                if (count >= fewest) continue;
                (pick, fewest) = (name, count);
                if (fewest == 0) break;
            }

            // Aucun groupe ne peut plus porter cette charge sans empiéter sur une charge déjà prise :
            // cette branche ne mène à aucune couverture complète.
            if (pick is null || fewest == 0) return;

            foreach (var g in optionsFor[pick])
            {
                if (stopped) return;
                if (!g.Names.IsSubsetOf(uncovered)) continue;

                // Les pièces de ce palonnier, prises comme le glouton les prend - même portée, même
                // charge arrondie au kilo supérieur - pour qu'un même groupe donne le même palonnier
                // des deux côtés. Le stock restant peut ne plus rien permettre : ce groupe-là est alors
                // hors d'atteinte dans cette branche, pas dans les autres.
                var assembly = Optimizer.AssemblyFor(
                    workshop.PalStock, workshop.PalTable, g.Span, Math.Ceiling(g.Total - 1e-9));
                if (assembly is null) continue;

                // Le repère est pris avant de toucher au stock, et rendu quoi qu'il arrive plus bas. Le
                // stock des mothers, lui, n'est touché que par une construction, qui le remet elle-même :
                // une branche n'a donc que celui des palonniers à rembobiner.
                var mark = StockMark.Of(workshop.PalStock);

                workshop.PalStock.Take(assembly);
                chosen.Add(g);
                assemblies.Add(assembly);
                uncovered.ExceptWith(g.Names);

                Cover();

                uncovered.UnionWith(g.Names);
                assemblies.RemoveAt(assemblies.Count - 1);
                chosen.RemoveAt(chosen.Count - 1);
                mark.Restore();
            }
        }

        // ── Une couverture complète, construite et pesée ─────────
        void Examine()
        {
            var palMark    = StockMark.Of(workshop.PalStock);
            var motherMark = StockMark.Of(workshop.MotherStock);
            try
            {
                // Chaque groupe repart avec les pièces choisies dans CETTE branche. Les implantations
                // déjà construites n'en souffrent pas : un palonnier garde son propre assemblage dès
                // qu'il est bâti (Optimizer.BuildPalonnier).
                for (int i = 0; i < chosen.Count; i++) chosen[i].Assembly = assemblies[i];

                // Les ids repartent de 1 à chaque tentative, pour que l'implantation retenue se lise
                // comme n'importe quelle autre - P1, P2, M1 - et non avec les numéros du millième essai.
                Mother.ResetCounter();
                Palonnier.ResetCounter();

                var candidate = workshop.Build(chosen);
                built++;

                if (Keep(candidate)) best = candidate;
            }
            catch (Exception ex)
            {
                // Une branche qui ne se construit pas n'est pas une erreur de l'utilisateur : elle est
                // simplement abandonnée. Mais elle n'a pas été regardée, donc plus rien ne sera prouvé.
                // Dite une seule fois : il y a autant de branches que de façons de grouper les charges.
                if (!failed)
                    Console.WriteLine($"  ⚠️  Un groupement n'a pas pu être construit ({ex.Message}) ; "
                                      + "il est abandonné et la recherche continue.");
                failed = true;
            }
            finally
            {
                // Le stock revient à ce qu'il était avant cette tentative, que la construction ait
                // abouti, échoué ou rendu des pièces en chemin.
                palMark.Restore();
                motherMark.Restore();
            }
        }

        // ── Garde-t-on celle-ci ? ────────────────────────────────
        bool Keep(RiggingResult candidate)
        {
            // Une implantation qui laisse une charge en l'air, ou qui dépasse une charge admise, n'est
            // pas une implantation (Objective.HardConstraints). Le classement, lui, ne compte ni les
            // charges portées ni les dépassements : une telle implantation paraîtrait MOINS chère,
            // justement parce qu'elle porte moins. Elle est écartée avant toute comparaison.
            if (candidate.Uncovered.Count > 0 || candidate.Errors.Count > 0) return false;

            // Le glouton, lui, peut n'y être pas arrivé. Une implantation sans reproche remplace alors
            // celle qui en a, quel que soit le classement : ce sont les contraintes dures qui parlent,
            // pas l'ordre des critères.
            if (best.Uncovered.Count > 0 || best.Errors.Count > 0) return true;

            return Objective.Compare(candidate.Cost, best.Cost, ranking) < 0;
        }
    }
}
