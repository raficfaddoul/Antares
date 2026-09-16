// =============================================================
// Optimizer.cs  — Placement des palonniers et des lignes de mother
// Équivalent Python: select_groups_greedy(), build_palonnier(), optimize_rigging()
//
// 1. A charge on a point line (within Geometry.TOL) hangs straight on that line's mother, as the
//    hand-written studies hang most motors.
// 2. The others go on palonniers along the beams - and inclined ones, unless OrthogonalOnly - grouped
//    greedily. Each is assembled from pieces of Palonnier.xlsx and sized with all its loads against the
//    value at its span: what the study checks when it hangs from its two ends only.
// 3. Every point line with something to carry gets continuous mother lines over the bays that hold it
//    (MotherLines), assembled from pieces of MotherGrill.xlsx, each bay sized with the sum of its loads.
// 4. Each palonnier then hangs from every line it crosses, unless that would take a bay past its value.
//
// Every piece comes from a PieceStock: no layout uses more pieces than the inventory holds. Coordinates
// are the grid's (Helpers.StudyAxes).
//
// Steps 3 to 7 are not the greedy path's own: they are Build below, the one construction that turns a
// CHOSEN set of palonnier groups into a finished layout. The greedy selection calls it once; the exact
// search (ExactSearch.cs) calls it for every grouping it tries. Written twice, the two would drift and
// the two answers would stop being comparable - so it is written once.
// =============================================================

using System.Diagnostics;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.Core;

public static class Optimizer
{
    // ══════════════════════════════════════════════════════════
    // SÉLECTION GLOUTONNE
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Chooses palonniers greedily - most charges first, an inclined one on a tie, then the shortest span -
    /// taking each one's pieces from <paramref name="stock"/> as it is chosen, so that a later choice sees
    /// only what is left. A charge no remaining piece can carry is left out; the caller reports it.
    /// </summary>
    /// <remarks>
    /// WHY GREEDY, AND WHY IT IS A CHOICE. Every subset of charges one palonnier could carry is a
    /// candidate, and the subsets grow faster than a search can follow. Greedy takes the best group it
    /// can still assemble out of what is left of the stock, and never reconsiders: fast, and readable
    /// off the plan afterwards, which is what a study needs. It is not optimal, and it is always run:
    /// at <see cref="SearchLevel.Exact"/> its layout is the incumbent the search has to beat
    /// (ExactSearch.cs), so the search can only ever return this layout or one the ranking prefers.
    /// </remarks>
    public static List<CandidateGroup> SelectGreedy(
        List<CandidateGroup> allGroups,
        List<Charge> charges,
        PieceStock stock,
        LoadTable table)
    {
        var uncovered = charges.Select(c => c.Name).ToHashSet();
        var selected  = new List<CandidateGroup>();

        // Plus de charges d'abord, portée croissante ensuite. The sort also favoured an inclined group
        // here, by a 0,1 that could never change an outcome and is gone: whichever way the two types are
        // interleaved by the sort, a round keeps the FIRST group of the best (count, type) class it
        // meets - the scan is strict, so no later equal displaces it - and within one class the spans
        // only grow, so that first group is the shortest-span one either way. Which type is preferred is
        // decided by PreferGroup below, and nowhere else.
        var candidates = allGroups
            .OrderByDescending(g => g.Names.Count)
            .ThenBy(g => g.Span)
            .ToList();

        while (uncovered.Count > 0)
        {
            // What the stock can still assemble, worked out once per span and load in each round. The load
            // is rounded up, so a group is only ever sized for at least what it carries.
            var fits = new Dictionary<(long Cm, long Kg), Assembly?>();
            Assembly? Fit(CandidateGroup g)
            {
                var key = ((long)Math.Round(g.Span * 100), (long)Math.Ceiling(g.Total - 1e-9));
                if (!fits.TryGetValue(key, out var a))
                    fits[key] = a = AssemblyFor(stock, table, g.Span, key.Item2);
                return a;
            }

            CandidateGroup? best = null;
            Assembly? bestAssembly = null;

            foreach (var g in candidates)
            {
                // Toutes les charges du groupe doivent être encore libres
                if (!g.Names.IsSubsetOf(uncovered)) continue;

                // Strictly better, or the one in hand stays. Among groups the preference cannot tell
                // apart, the first met wins - and the sort meets the shortest span first.
                if (best is not null && PreferGroup(g, best) >= 0) continue;

                var a = Fit(g);
                if (a is null) continue;
                (best, bestAssembly) = (g, a);
            }

            if (best is null) break;   // what is left cannot be carried by what is left in stock

            stock.Take(bestAssembly!);
            best.Assembly = bestAssembly;
            selected.Add(best);
            uncovered.ExceptWith(best.Names);
        }

        return selected;
    }

    /// <summary>
    /// Which of two candidate groups a greedy round takes: negative when <paramref name="a"/> is the one
    /// to keep, zero when the preference cannot tell them apart. The whole preference, once and in
    /// order, where it used to be a 0,3 added to a count in one place and a 0,1 added to a sort in
    /// another. It ranks candidates, not finished layouts: <see cref="Objective"/> does that, and the
    /// two agree on what they can - a group here is chosen for being one hang instead of two.
    /// <para>
    /// It does NOT move with the ranking a developer stores. A candidate group is not a layout - it has
    /// no mass hung, no pieces assembled, no bay loaded - so there is nothing here for a reordered
    /// ranking to read. What honours that order is the comparison between FINISHED layouts: the exact
    /// search (ExactSearch.cs) is where a reordered ranking changes which one comes back.
    /// </para>
    /// </summary>
    private static int PreferGroup(CandidateGroup a, CandidateGroup b)
    {
        // 1. Le groupe qui couvre le plus de charges. One palonnier instead of two is one hang instead
        //    of two, and a knot of rigging work saved (Objective.DefaultRanking, criterion 1).
        if (a.Names.Count != b.Names.Count) return b.Names.Count.CompareTo(a.Names.Count);

        // 2. À nombre égal, le palonnier incliné. It gathers charges that no palonnier along the beams
        //    can reach, so taking it now keeps the groups still to come large: the orthogonal group it
        //    beats can usually be formed again later, the inclined one often cannot.
        int ra = a.Type == PalonnierDirection.Biais ? 0 : 1;
        int rb = b.Type == PalonnierDirection.Biais ? 0 : 1;
        if (ra != rb) return ra.CompareTo(rb);

        // 3. À égalité, la portée la plus courte: less truss out of the stock, and more load allowed at
        //    that span. This rule was never written anywhere - it fell out of the sort order and a
        //    strict comparison - and it decides real layouts, so here it is, in words.
        return a.Span.CompareTo(b.Span);
    }

    /// <summary>
    /// The best palonnier the stock can assemble for a span and a load, without taking it: pieces of one
    /// model reaching the span within Geometry.TOL, whose value at the span carries the load.
    /// </summary>
    public static Assembly? AssemblyFor(PieceStock stock, LoadTable table, double span, double load) =>
        stock.Best(Math.Max(0.01, span - TOL), model => table.At(model, span).Carries(load));

    // ══════════════════════════════════════════════════════════
    // CONSTRUCTION D'UN PALONNIER
    // ══════════════════════════════════════════════════════════
    public static Palonnier BuildPalonnier(
        CandidateGroup group, LoadTable table, Func<BarSpec, double?>? massOf = null)
    {
        var assembly = group.Assembly
            ?? throw new InvalidOperationException($"Palonnier sans pièces : [{string.Join(",", group.Names)}]");
        var allowed = table.At(assembly.Type, group.Span);

        var pal = new Palonnier
        {
            Bar       = assembly.AsBar(allowed.Kg ?? 0),
            Assembly  = assembly,
            Allowed   = allowed,
            Direction = group.Type,
            A         = group.A,
            B         = group.B,
            Charges   = group.Charges,
        };
        pal.ComputeReactions();

        // Son poids propre, la somme de ses pièces, que l'étude compte dans la charge des mothers. Une masse
        // inconnue compte pour zéro ici ; l'étude, elle, le signale.
        pal.SelfWeightKg = Math.Max(0, assembly.Pieces.Sum(p => massOf?.Invoke(p) ?? p.WeightKg ?? 0));
        return pal;
    }

    // ══════════════════════════════════════════════════════════
    // ALGORITHME PRINCIPAL
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Places a whole plan: the charges on a point line straight on their mother line, the others on
    /// palonniers, then the lines, then the crossings. How hard it looks for the palonnier grouping is
    /// <see cref="OptimizerOptions.Search"/>, and what "better" means while it looks is
    /// <see cref="OptimizerOptions.Ranking"/> - both settings, both passed in. NOTHING HERE READS THE
    /// DATABASE: the pipeline reads the Settings page and hands the options over, so this file stays a
    /// domain file a reader can check without a database in front of them.
    /// </summary>
    public static RiggingResult Optimize(
        List<Charge>  charges,
        List<BarSpec> palonniersBars,
        List<BarSpec> motherBars,
        bool verbose = true,
        Func<BarSpec, double?>? massOf = null,     // masse d'une pièce ; par défaut son poids propre
        OptimizerOptions? options = null)
    {
        options ??= OptimizerOptions.Default;

        // Le classement en vigueur pour ce run : celui des options, ou celui par défaut quand elles n'en
        // portent pas. Résolu une fois ici, pour que la comparaison et ce que le résultat en dit soient
        // le même classement.
        var ranking = options.RankingInForce;

        // Réinitialiser les compteurs d'ID
        Mother.ResetCounter();
        Palonnier.ResetCounter();

        // Les valeurs de chaque fichier, lignes de Nombre = 0 comprises ; les pièces, celles en stock.
        var palTable    = LoadTable.From(palonniersBars);
        var motherTable = LoadTable.From(motherBars);
        var palStock    = new PieceStock(palonniersBars);
        var motherStock = new PieceStock(motherBars);

        double totalW = charges.Sum(c => c.Weight);

        if (verbose)
        {
            PrintSep('═');
            Console.WriteLine("  RIGGING OPTIMIZER v6 — ANTARES  (lignes de mother, pièces assemblées)");
            PrintSep('═');
            Console.WriteLine($"\n  {charges.Count} charges  |  Poids total: {totalW:F0} kg"
                              + (options.OrthogonalOnly ? "  |  orientations orthogonales uniquement" : "")
                              + (options.Search == SearchLevel.Exact ? "  |  recherche exacte" : "") + "\n");
        }

        // ── Étape 1: Charges sur une ligne de points : directement sur la ligne de mother ──
        var direct = new List<(Charge Charge, int Row, double Y)>();
        var toHang = new List<Charge>();
        foreach (var c in charges)
        {
            var (row, ry) = NearestRow(c.Y);
            if (Math.Abs(c.Y - ry) <= TOL) direct.Add((c, row, ry));
            else toHang.Add(c);
        }
        if (verbose) Console.WriteLine($"  {direct.Count} charge(s) sur une ligne de points, {toHang.Count} sur palonnier");

        // ── Étape 2: Palonniers ──────────────────────────────────
        var feasible = new Dictionary<(long Cm, long Kg), bool>();
        bool Feasible(double span, double load)
        {
            var key = ((long)Math.Round(span * 100), (long)Math.Ceiling(load - 1e-9));
            if (!feasible.TryGetValue(key, out var ok))
                feasible[key] = ok = AssemblyFor(palStock, palTable, span, key.Item2) is not null;
            return ok;
        }

        var allGroups = GroupGenerator.Generate(toHang, Feasible, allowBiais: !options.OrthogonalOnly);
        if (verbose)
            Console.WriteLine($"  → {allGroups.Count(g => g.Type == PalonnierDirection.Y)} groupes Y, "
                              + $"{allGroups.Count(g => g.Type == PalonnierDirection.Biais)} groupes biais");

        // L'atelier : tout ce qu'une construction demande, dans un seul objet, pour que le glouton et la
        // recherche exacte bâtissent de la même façon à partir des mêmes matériaux.
        var workshop = new LayoutWorkshop(charges, direct, palStock, palTable, motherStock, motherTable,
                                          massOf, options);

        // L'état plein des deux stocks, avant que rien ne soit posé. Le glouton va consommer ; la
        // recherche exacte doit repartir d'ici, sans quoi ses branches se disputeraient les pièces que
        // le glouton a déjà prises.
        var palFull    = StockMark.Of(palStock);
        var motherFull = StockMark.Of(motherStock);

        var clock = Stopwatch.StartNew();

        // ── Étape 3: Le glouton ──────────────────────────────────
        // Toujours exécuté : c'est la réponse au niveau "bounds", et l'implantation de départ - celle
        // qu'il faut battre - au niveau "exact".
        var selected = SelectGreedy(allGroups, toHang, palStock, palTable);
        var result   = workshop.Build(selected);

        int  examined = 1;
        bool proven   = false;

        // ── Étape 4: La recherche exacte, quand elle est demandée ─
        // Elle ne peut rendre que cette implantation-ci ou une que le classement lui préfère
        // (ExactSearch.cs) : allumer le réglage ne peut donc pas dégrader un plan.
        if (options.Search == SearchLevel.Exact)
        {
            result = ExactSearch.Run(result, workshop, allGroups, toHang, palFull, motherFull,
                                     out int searched, out proven);
            examined += searched;
        }

        clock.Stop();

        // Ce qui a réellement tourné, à côté de ce qui en est sorti : sous quel classement, à quel
        // niveau, prouvé ou non, en combien d'implantations et en combien de temps. Une étude qui ne
        // peut pas dire sous quel objectif elle a été bâtie n'est pas traçable.
        result.Search = new SearchOutcome(Objective.Format(ranking), options.Search, proven, examined,
                                          clock.Elapsed.TotalMilliseconds);

        // Le plancher qu'aucune implantation de ce plan ne peut passer. En dernier, exprès : il planifie
        // des lignes jetables qui dépensent des ids de Mother, et les vraies lignes doivent avoir pris
        // les leurs d'abord (Bounds.MotherMetresBound). Il ne lit du stock que les longueurs en
        // inventaire, que la recherche n'a pas touchées.
        if (options.WithBounds)
            result.Bound = Bounds.For(charges, motherStock, palStock, options);

        if (verbose) Report(result, ranking);

        return result;
    }

    // ══════════════════════════════════════════════════════════
    // D'UN CHOIX DE GROUPES À UNE IMPLANTATION FINIE
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Builds the layout a chosen set of palonnier groups gives: the palonniers themselves, the mother
    /// lines under them, the charges hung straight on a line, every crossing hung where it fits, the
    /// loads at the grid points, and what the whole thing costs.
    /// <para>
    /// This is steps 3 to 7 of the run, and it belongs to no path in particular: the greedy selection
    /// calls it once, the exact search calls it for every grouping it tries. Each group must already
    /// carry the <see cref="CandidateGroup.Assembly"/> it was chosen with - choosing the pieces is the
    /// caller's business, because that is what the caller is searching over.
    /// </para>
    /// <para>
    /// It takes pieces out of the workshop's two stocks and gives back what it could not use, so calling
    /// it twice takes twice: a caller trying several layouts rewinds the stocks between attempts
    /// (<see cref="StockMark"/>).
    /// </para>
    /// <para>
    /// A charge nothing could carry is not an exception here: it comes back in
    /// <see cref="RiggingResult.Uncovered"/> with a sentence in Errors, and the caller decides what that
    /// is worth. Pipeline.GenerateLayout refuses to save such a layout.
    /// </para>
    /// </summary>
    public static RiggingResult Build(LayoutWorkshop w, IReadOnlyList<CandidateGroup> groups)
    {
        var errors     = new List<string>();
        var palonniers = groups.Select(g => BuildPalonnier(g, w.PalTable, w.MassOf)).ToList();

        // ── Étape 3: Lignes de mother ────────────────────────────
        var demands = palonniers
            .SelectMany(p => new[] { OnRow(p.A), OnRow(p.B) })
            .Concat(w.Direct.Select(d => (d.Row, d.Y, d.Charge.X)))
            .ToList();
        var lines = MotherLines.Plan(demands);

        foreach (var p in palonniers)
        {
            p.MotherA = MotherLines.LineAt(lines, NearestRow(p.A.Y).Row, p.A.X);
            p.MotherB = MotherLines.LineAt(lines, NearestRow(p.B.Y).Row, p.B.X);
        }
        foreach (var p in palonniers.Where(p => p.MotherA is null || p.MotherB is null
                                                || ReferenceEquals(p.MotherA, p.MotherB)).ToList())
        {
            errors.Add($"{p} : aucune ligne de mother à l'une de ses extrémités.");
            w.PalStock.Give(p.Assembly!);
            palonniers.Remove(p);
        }

        foreach (var d in w.Direct)
        {
            var line = MotherLines.LineAt(lines, d.Row, d.Charge.X);
            if (line is null) errors.Add($"{d.Charge.Name} : aucune ligne de mother à sa position.");
            else line.DirectCharges.Add(d.Charge);
        }

        // ── Étape 4: Pièces de chaque ligne, les palonniers accrochés à leurs deux extrémités ──
        foreach (var p in palonniers) HangAtEnds(p);

        var failed = new HashSet<Mother>(ReferenceEqualityComparer.Instance);
        foreach (var line in lines)
        {
            if (!MotherLines.Size(line, PointsOn(line, palonniers), w.MotherStock, w.MotherTable, out var problem))
            {
                failed.Add(line);
                errors.Add(problem!);
            }
        }

        if (failed.Count > 0)
        {
            // What hangs on a line that cannot be built is not carried: it is reported, and its pieces go back.
            foreach (var p in palonniers.Where(p => failed.Contains(p.MotherA!) || failed.Contains(p.MotherB!)).ToList())
            {
                w.PalStock.Give(p.Assembly!);
                palonniers.Remove(p);
            }
            lines.RemoveAll(l => failed.Contains(l));

            // A line left with nothing to carry goes back to stock.
            foreach (var line in lines.Where(l => l.DirectCharges.Count == 0
                         && !palonniers.Any(p => ReferenceEquals(p.MotherA, l) || ReferenceEquals(p.MotherB, l))).ToList())
            {
                if (line.Assembly is not null) w.MotherStock.Give(line.Assembly);
                lines.Remove(line);
            }
        }

        // ── Étape 5: Accrochage à toutes les lignes croisées ─────
        // Comme la salle les accroche : chaque palonnier pend de toutes les lignes qu'il croise, et chaque
        // charge va aux deux plus proches. Une ligne dont une travée dépasserait sa valeur est laissée.
        HangOnCrossings(palonniers, lines);

        // ── Étape 6: Charges aux points de grille ─────────────────
        foreach (var m in lines) m.Compute();

        var gridLoads = new Dictionary<(string Col, int Row), double>();
        foreach (var m in lines)
        {
            foreach (var (col, f) in m.ColumnLoads)
            {
                if (Math.Abs(f) < 1e-6) continue;
                var key = (col, m.Row);
                gridLoads[key] = Math.Round(gridLoads.GetValueOrDefault(key, 0) + f, 3);
            }
        }

        // ── Étape 7: Ce qui n'est pas porté, et les vérifications ──
        var carried = new HashSet<string>(palonniers.SelectMany(p => p.Names)
            .Concat(lines.SelectMany(l => l.DirectCharges.Select(c => c.Name))));
        var uncoveredCharges = w.Charges.Where(c => !carried.Contains(c.Name)).ToList();
        foreach (var c in uncoveredCharges)
            errors.Add($"{c.Name} ({c.Weight:F0} kg) n'est portée ni par un palonnier ni par un mother.");

        double totalW = w.Charges.Sum(c => c.Weight);
        double carriedTotal = w.Charges.Where(c => carried.Contains(c.Name)).Sum(c => c.Weight);
        double totalGrid = gridLoads.Values.Sum();
        bool balanceOk = Math.Abs(totalGrid - carriedTotal) < 2.0;

        var result = new RiggingResult
        {
            Charges      = w.Charges,
            Palonniers   = palonniers,
            Mothers      = lines,
            GridLoads    = gridLoads,
            PalsStock    = w.PalStock.Snapshot(),
            MomsStock    = w.MotherStock.Snapshot(),
            TotalCharges = totalW,
            TotalGrid    = totalGrid,
            BalanceOk    = balanceOk,
            Errors       = errors.Concat(CapacityErrors(palonniers, lines)).ToList(),
            Uncovered    = uncoveredCharges,
            Options      = w.Options,
        };

        // What this layout cost, measured once on the finished result. Measured here rather than by each
        // caller in its own way, so the console, the study, and the search comparing two layouts quote
        // the same figures, computed the same way. The stock snapshots are taken here too: a caller that
        // rewinds the stocks to try another grouping leaves this result saying exactly what IT used.
        result.Cost = LayoutCost.From(result);
        return result;
    }

    /// <summary>
    /// The layout's own contradictions: a palonnier carrying more than its value at its span, or a bay
    /// of a line over its own. Written into the result's Errors and printed by the report from this one
    /// function, so the two can never disagree about what is wrong.
    /// </summary>
    private static List<string> CapacityErrors(IReadOnlyList<Palonnier> palonniers, IReadOnlyList<Mother> lines)
    {
        var capErrors = new List<string>();
        foreach (var p in palonniers)
        {
            if (p.Allowed is null || !p.Allowed.Carries(p.TotalWeight))
                capErrors.Add($"{p}: {p.TotalWeight:F0}kg pour {p.Allowed?.Kg ?? 0:F0}kg admissibles sur {p.Span:F2}m");
        }
        foreach (var m in lines)
        {
            if (m.WorstBay(m.Points) is { } worst)
                capErrors.Add($"{m}: travée {m.Columns[worst.Bay].Col}-{m.Columns[worst.Bay + 1].Col} "
                              + $"{worst.Load:F0}kg pour {worst.Allowed:F0}kg admissibles");
        }
        return capErrors;
    }

    // ══════════════════════════════════════════════════════════
    // CE QUE LA CONSOLE EN DIT
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The run's report, read off the finished result alone - never off the live stocks nor off a
    /// variable left over from the placement. At the exact level the run builds many layouts and rewinds
    /// the stocks between them, so the only honest account of what was used is the one the returned
    /// layout carries.
    /// </summary>
    private static void Report(RiggingResult result, IReadOnlyList<Criterion> ranking)
    {
        PrintSep('─'); Console.WriteLine("  Palonniers"); PrintSep('─');
        foreach (var p in result.Palonniers)
        {
            string tag = p.Direction == PalonnierDirection.Biais ? "↗" : "│";
            Console.WriteLine($"  {tag} {p}  {p.Assembly}");
            Console.WriteLine($"      charges: [{string.Join(", ", p.Names)}]  total={p.TotalWeight:F0}kg"
                              + $" / {p.Allowed?.Kg ?? 0:F0}kg sur {p.Span:F2}m");
            Console.WriteLine($"      accroché à {string.Join(", ", p.Supports.Select(s => $"{s.Mother} ({s.R:F0}kg)"))}");
            foreach (var k in p.Skipped)
                Console.WriteLine($"      non accroché à {k.Mother} : une travée de {k.Overloaded} porterait "
                                  + $"{k.WouldCarryKg:F0}kg > {k.CapacityKg:F0}kg");
        }

        PrintSep('─'); Console.WriteLine($"  Lignes de mother ({result.Mothers.Count})"); PrintSep('─');
        foreach (var m in result.Mothers)
        {
            var loads = m.BayLoads();
            string bays = string.Join("  ", Enumerable.Range(0, m.BayCount).Select(k =>
                $"{m.Columns[k].Col}-{m.Columns[k + 1].Col} {loads[k]:F0}/{m.BayAllowed[k].Kg ?? 0:F0}"));
            Console.WriteLine($"  {m}  {m.Assembly}  [{bays}]");
            Console.WriteLine($"     → {string.Join("   ", m.ColumnLoads.Select(kv => $"{kv.Key}{m.Row}={kv.Value:F0}kg"))}");
        }

        PrintSep('─'); Console.WriteLine("  Points de grille"); PrintSep('─');
        foreach (var ((col, row), load) in result.GridLoads.OrderBy(kv => kv.Key.Col).ThenBy(kv => kv.Key.Row))
            Console.WriteLine($"  {col}{row}: {load:F0} kg");

        // Ce qui est porté : le total moins ce qui ne l'est pas, donc le même chiffre que la vérification
        // d'équilibre a utilisé.
        double carriedTotal = result.TotalCharges - result.Uncovered.Sum(c => c.Weight);
        Console.WriteLine($"\n  Total charges:       {result.TotalCharges:F0} kg (portées : {carriedTotal:F0} kg)");
        Console.WriteLine($"  Total points grille: {result.TotalGrid:F0} kg");
        Console.WriteLine(result.BalanceOk ? "  ✅ Équilibre vérifié"
                                           : $"  ❌ Déséquilibre: {Math.Abs(result.TotalGrid - carriedTotal):F1}kg");

        // Les erreurs de capacité sont relues du même endroit que Build les a écrites, et retirées du
        // reste pour que la console garde ses deux blocs : ce qui dépasse une valeur admise d'un côté,
        // ce qui n'a pas pu être posé de l'autre.
        var capErrors = CapacityErrors(result.Palonniers, result.Mothers);
        if (capErrors.Count > 0)
        { Console.WriteLine("\n  ⚠️  Erreurs capacité:"); foreach (var e in capErrors) Console.WriteLine($"    • {e}"); }
        else Console.WriteLine("  ✅ Toutes les capacités respectées");

        var otherErrors = result.Errors.Where(e => !capErrors.Contains(e)).ToList();
        if (otherErrors.Count > 0)
        { Console.WriteLine("\n  ⚠️  Erreurs:"); foreach (var e in otherErrors) Console.WriteLine($"    • {e}"); }

        PrintSep('─'); Console.WriteLine("  Pièces utilisées"); PrintSep('─');
        foreach (var b in result.PalsStock.Concat(result.MomsStock))
        {
            int used = b.StockInit - b.Stock;
            if (used > 0) Console.WriteLine($"    {b.Type} {b.Length:F1}m : {used} / {b.StockInit}");
        }

        PrintSep('─'); Console.WriteLine("  Ce que coûte cette implantation"); PrintSep('─');
        Console.WriteLine($"  {result.Cost.Describe()}");
        Console.WriteLine($"  {Objective.DescribeRanking(ranking)}");
        Console.WriteLine($"  {result.Search.Describe()}");
        if (result.Bound is { } bound)
        {
            Console.WriteLine($"  {bound.Describe()}");
            Console.WriteLine($"  {bound.Gap(result.Cost).Describe()}");
        }
    }

    private static (int Row, double Y, double X) OnRow((double X, double Y) end)
    {
        var (row, ry) = NearestRow(end.Y);
        return (row, ry, end.X);
    }

    // A palonnier hung from its two ends only, with what each end carries.
    private static void HangAtEnds(Palonnier p)
    {
        p.Supports.Clear();
        p.Skipped.Clear();
        if (p.MotherA is not null)
            p.Supports.Add(new PalonnierSupport { Mother = p.MotherA, S = 0, X = p.A.X, Y = p.A.Y });
        if (p.MotherB is not null && !ReferenceEquals(p.MotherB, p.MotherA))
            p.Supports.Add(new PalonnierSupport { Mother = p.MotherB, S = p.Span, X = p.B.X, Y = p.B.Y });
        SetReactions(p, p.Supports);
    }

    // What hangs on a line: each palonnier's share where it hangs from it - its charges and its own weight -
    // and the charges hung on the line itself. With a palonnier's supports replaced, to try a change.
    private static List<SuspensionPoint> PointsOn(
        Mother line,
        IEnumerable<Palonnier> palonniers,
        Palonnier? replaced = null,
        IReadOnlyList<PalonnierSupport>? replacement = null)
    {
        var points = new List<SuspensionPoint>();
        foreach (var p in palonniers)
        {
            IReadOnlyList<PalonnierSupport> supports =
                ReferenceEquals(p, replaced) && replacement is not null ? replacement : p.Supports;
            foreach (var t in supports)
            {
                if (ReferenceEquals(t.Mother, line))
                    points.Add(new SuspensionPoint { X = OnLine(line, t.X), F = t.R, W = t.W, Src = p.ToString() });
            }
        }
        foreach (var c in line.DirectCharges)
            points.Add(new SuspensionPoint { X = OnLine(line, c.X), F = c.Weight, W = 0, Src = c.Name });
        return points;
    }

    /// <summary>
    /// Where a hanging point sits on a line. One placed a few centimetres past the line's drawn end -
    /// MotherLines.Plan ends a line at a beam when the point is within Geometry.TOL of it - is on that end,
    /// and its load goes straight into the beam there: the calculation charpente moves it the same way
    /// (PalonnierHanging, and the stored position of a charge hung on the line), so the optimiser sizes
    /// every bay for what the calculation will find. The palonnier's own geometry is not moved.
    /// </summary>
    public static double OnLine(Mother line, double x) => Math.Clamp(x, line.XL, line.XR);

    // ══════════════════════════════════════════════════════════
    // ACCROCHAGE AUX LIGNES CROISÉES
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Hangs every palonnier from every mother line it crosses between its two ends, as the venue rigs
    /// them: each load then goes to the two nearest lines, one on each side. A crossing is left unhooked,
    /// and recorded in <see cref="Palonnier.Skipped"/>, when hanging the palonnier there would take a bay
    /// of a line past its value - counted as the study counts it: the charges and the palonniers' own
    /// weight between two beams, against the value of the line's model at the bay's span. Run once every
    /// palonnier and line is placed. The lines' points are then rebuilt from the supports, so each line
    /// carries exactly what hangs on it.
    /// </summary>
    public static void HangOnCrossings(IReadOnlyList<Palonnier> palonniers, IReadOnlyList<Mother> mothers)
    {
        foreach (var p in palonniers) HangAtEnds(p);

        // The crossings in between, one per point line: the lines strictly between the ends' lines that
        // reach the palonnier there, within Geometry.TOL - the tolerance the ends are placed with.
        var pending = new List<(Palonnier P, double S, double X, double Y, List<Mother> Candidates)>();
        foreach (var p in palonniers)
        {
            double dy = p.B.Y - p.A.Y;
            if (Math.Abs(dy) < 1e-9) continue;
            double yLo = Math.Min(p.A.Y, p.B.Y) + TOL, yHi = Math.Max(p.A.Y, p.B.Y) - TOL;

            var crossings = mothers
                .Where(m => m.Y > yLo && m.Y < yHi && !p.Supports.Any(s => ReferenceEquals(s.Mother, m)))
                .Select(m => (M: m, X: p.A.X + (m.Y - p.A.Y) / dy * (p.B.X - p.A.X)))
                .Where(c => c.X >= c.M.XL - CrossingTol && c.X <= c.M.XR + CrossingTol)
                .GroupBy(c => c.M.Row);

            foreach (var line in crossings)
            {
                var first = line.First();
                pending.Add((p, ProjOnAxis(first.X, first.M.Y, p.A.X, p.A.Y, p.B.X, p.B.Y), first.X, first.M.Y,
                    line.OrderBy(c => c.M.XL).ThenBy(c => c.M.Id).Select(c => c.M).ToList()));
            }
        }
        pending = pending.OrderBy(c => c.P.Id).ThenBy(c => c.S).ToList();

        // Hang where it fits. Hanging a palonnier takes load off the lines either side, which can make room
        // for a crossing refused earlier, so go round again until nothing more can be hung.
        var hung = new bool[pending.Count];
        for (bool progress = true; progress;)
        {
            progress = false;
            for (int i = 0; i < pending.Count; i++)
            {
                if (hung[i]) continue;
                var (p, s, x, y, candidates) = pending[i];

                foreach (var m in candidates)
                {
                    var trial = WithSupport(p, m, s, x, y);
                    if (Overload(p, trial, m, palonniers) is not null) continue;

                    p.Supports.Clear();
                    p.Supports.AddRange(trial);
                    hung[i] = true;
                    progress = true;
                    break;
                }
            }
        }

        // What could not be hung, and which bay it would have overloaded.
        for (int i = 0; i < pending.Count; i++)
        {
            if (hung[i]) continue;
            var (p, s, x, y, candidates) = pending[i];
            foreach (var m in candidates)
            {
                var over = Overload(p, WithSupport(p, m, s, x, y), m, palonniers);
                p.Skipped.Add(over is { } o
                    ? new SkippedCrossing(m, s, o.Mother, o.Load, o.Allowed)
                    : new SkippedCrossing(m, s, m, 0, m.Capacity));
            }
        }

        // Each line carries exactly what hangs on it.
        foreach (var m in mothers)
        {
            m.Points.Clear();
            m.Points.AddRange(PointsOn(m, palonniers));
        }
        foreach (var p in palonniers)
        {
            p.RA = p.Supports.FirstOrDefault(t => ReferenceEquals(t.Mother, p.MotherA))?.R ?? 0;
            p.RB = p.Supports.LastOrDefault(t => ReferenceEquals(t.Mother, p.MotherB))?.R ?? 0;
        }
    }

    /// <summary>
    /// What each support of a palonnier carries: each charge goes to the two supports that bracket it,
    /// by the lever rule; beyond the outermost, to the two nearest by the same affine rule - the
    /// cantilever formula. It is the rule the load calculation applies to the same palonnier
    /// (Helpers.LoadTransmissionHelper), so every line is sized for what it will be found to carry.
    /// </summary>
    public static double[] SupportReactions(Palonnier p, IReadOnlyList<PalonnierSupport> supports)
    {
        var r = new double[supports.Count];
        if (supports.Count == 0) return r;

        foreach (var c in p.Charges)
        {
            if (supports.Count == 1) { r[0] += c.Weight; continue; }

            double s = ProjOnAxis(c.X, c.Y, p.A.X, p.A.Y, p.B.X, p.B.Y);

            int a = -1, b = -1;
            for (int k = 0; k < supports.Count; k++)
            {
                if (supports[k].S <= s && (a < 0 || supports[k].S > supports[a].S)) a = k;
                if (supports[k].S > s && (b < 0 || supports[k].S < supports[b].S)) b = k;
            }

            if (a < 0 || b < 0)
            {
                var nearest = Enumerable.Range(0, supports.Count)
                    .OrderBy(k => Math.Abs(supports[k].S - s))
                    .Take(2)
                    .ToArray();
                (a, b) = (nearest[0], nearest[1]);
            }

            double span = supports[b].S - supports[a].S;
            if (Math.Abs(span) < 1e-9) { r[a] += c.Weight / 2; r[b] += c.Weight / 2; continue; }

            r[a] += c.Weight * (supports[b].S - s) / span;
            r[b] += c.Weight * (s - supports[a].S) / span;
        }

        return r;
    }

    // The palonnier's supports with one more, each with what it would then carry.
    private static List<PalonnierSupport> WithSupport(Palonnier p, Mother m, double s, double x, double y)
    {
        var trial = p.Supports
            .Select(t => new PalonnierSupport { Mother = t.Mother, S = t.S, X = t.X, Y = t.Y })
            .Append(new PalonnierSupport { Mother = m, S = s, X = x, Y = y })
            .OrderBy(t => t.S)
            .ToList();

        SetReactions(p, trial);
        return trial;
    }

    // What each support carries of the palonnier's charges (R) and of its own weight (W).
    private static void SetReactions(Palonnier p, List<PalonnierSupport> supports)
    {
        var r = SupportReactions(p, supports);
        var w = p.SelfWeightKg > 0
            ? global::Helpers.SelfWeightDistribution.Reactions(supports.Select(t => t.S).ToList(), p.Span, p.SelfWeightKg)
            : new double[supports.Count];

        for (int k = 0; k < supports.Count; k++)
        {
            supports[k].R = r[k];
            supports[k].W = w[k];
        }
    }

    // A line one of whose bays would rise past its value with these supports - the line being added
    // first - with the bay's load and value. Null when every bay stays within its value.
    private static (Mother Mother, double Load, double Allowed)? Overload(
        Palonnier p, List<PalonnierSupport> trial, Mother added, IReadOnlyList<Palonnier> palonniers)
    {
        var affected = trial.Select(t => t.Mother)
            .Concat(p.Supports.Select(t => t.Mother))
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<Mother>()
            .OrderByDescending(m => ReferenceEquals(m, added))
            .ToList();

        foreach (var m in affected)
        {
            var before = m.BayLoadsOf(PointsOn(m, palonniers));
            var after = m.BayLoadsOf(PointsOn(m, palonniers, p, trial));
            for (int k = 0; k < after.Length; k++)
            {
                if (after[k] <= before[k] + 1e-9) continue;
                var allowed = k < m.BayAllowed.Count ? m.BayAllowed[k] : null;
                if (allowed is null || !allowed.Carries(after[k])) return (m, after[k], allowed?.Kg ?? 0);
            }
        }
        return null;
    }

    private static void PrintSep(char c) => Console.WriteLine(new string(c, 72));
}

// ── L'atelier ────────────────────────────────────────────────
/// <summary>
/// What building one layout takes, in one object: the plan's charges, the ones that hang straight on a
/// point line, the two load tables, the two stocks, how a piece's mass is read, and the run's options.
/// <para>
/// It exists so that <see cref="Optimizer.Build"/> can be called from two places with the same materials
/// - once by the greedy path, and once per grouping by the exact search. Everything in it is settled
/// before any palonnier is placed, except the two stocks, which are the only things a build consumes.
/// </para>
/// </summary>
/// <param name="Charges">The plan's charges, in the grid's axes.</param>
/// <param name="Direct">The charges sitting on a point line, with the row they hang from.</param>
/// <param name="PalStock">The pieces of Palonnier.xlsx still available.</param>
/// <param name="PalTable">What a palonnier model admits at a span.</param>
/// <param name="MotherStock">The pieces of MotherGrill.xlsx still available.</param>
/// <param name="MotherTable">What a mother model admits at a bay's span.</param>
/// <param name="MassOf">A piece's mass; its own weight when null.</param>
/// <param name="Options">The run's settings, the ranking included.</param>
public sealed record LayoutWorkshop(
    List<Charge>                                      Charges,
    IReadOnlyList<(Charge Charge, int Row, double Y)> Direct,
    PieceStock                                        PalStock,
    LoadTable                                         PalTable,
    PieceStock                                        MotherStock,
    LoadTable                                         MotherTable,
    Func<BarSpec, double?>?                           MassOf,
    OptimizerOptions                                  Options)
{
    /// <summary>The finished layout these chosen groups give. See <see cref="Optimizer.Build"/>.</summary>
    public RiggingResult Build(IReadOnlyList<CandidateGroup> groups) => Optimizer.Build(this, groups);
}
