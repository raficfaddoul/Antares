// =============================================================
// Objective.cs — What "better" means, written down once, in words a rigger can review.
//
// Until now the optimiser never said what it was optimising. The preference lived in three places with
// magic numbers in them - a 0,3 added to a count in Optimizer.SelectGreedy, a 0,1 in the sort beside it,
// and the assembly comparison in PieceStock.Compare - and two of them disagreed. Nobody could say what
// the numbers meant, because they meant nothing: they were the smallest values that produced the layout
// somebody wanted on the day. This file replaces them with a specification.
//
// It ENFORCES NOTHING and PLACES NOTHING. It is the index of the rules - each one named beside the code
// that actually applies it - and the yardstick the finished layouts are measured with. It is a domain
// file: no Blazor, no EF, no IO, so a reader can check the rules without a database in front of them.
//
// ── THE ORDER IN FORCE WHEN NOTHING IS STORED ────────────────────────────────────────────────
//   1. fewer palonniers          4. less truss length
//   2. less truss mass hung      5. more margin on the worst bay
//   3. fewer pieces
//
// It is an ORDER, not a weighted sum, and deliberately so. Quantities added together end in tuned
// constants: that is precisely how the 0,3 and the 0,1 happened, and afterwards neither could be
// explained or safely changed. Compared one criterion after another, the preference can be changed by
// moving one line of the DefaultRanking array below, and nothing else in the code moves with it.
//
// CRITERIA 1 AND 2 CAN DISAGREE, and the disagreement is real rigging, not a modelling artefact: one
// inclined palonnier carrying three charges is one hang instead of three, but the long truss it needs
// can weigh more than the three short ones it replaces. The order above counts the hang first - fewer
// hangs is fewer hours on site, fewer points of rigging, fewer things to get wrong at height - and
// accepts the extra kilos when they come. That is a business decision, not an algorithmic one.
//
// ── THE ORDER IS NOW A SETTING, AND THE KEYS ARE WHAT IS STORED ───────────────────────────────
// The owner asked that the order be changeable by a developer from the Settings page rather than by a
// release. So each criterion carries a KEY beside its French name - palonniers, masse, pieces,
// longueur, marge - and a stored order is those keys, comma separated, in the order wanted. The key is
// the identity: a NAME is a label and can be reworded the day a rigger asks for better French, a KEY
// cannot, because it is sitting in the database of every installation. Never rename one.
//
// What a run actually used is read by Parse and written back by Format. Parse never guesses: a stored
// order missing a criterion, repeating one, or naming a word this file does not know falls back to
// DefaultRanking below and hands the caller a French sentence saying so. An unreadable setting must not
// be allowed to quietly change what the optimiser prefers - that would change what the venue gets, and
// nobody would know why.
//
// So the array below is still the argument for an order, and still the order in force whenever nothing
// readable is stored. Changing it changes the default every installation falls back to: change it here,
// with the reason written beside it.
// =============================================================

using System.Globalization;
using RiggingOptimizer.Models;

namespace RiggingOptimizer.Core;

/// <summary>Which way a criterion reads: whether a smaller figure is the better one, or a larger one.</summary>
public enum Prefer
{
    /// <summary>Less of this quantity is a better layout.</summary>
    Less,

    /// <summary>More of this quantity is a better layout.</summary>
    More,
}

/// <summary>
/// A rule that is never traded away for a better figure anywhere, and the code that actually applies it.
/// </summary>
/// <param name="Name">Its short name, as a study would call it.</param>
/// <param name="Rule">What it forbids, in the words of the study.</param>
/// <param name="EnforcedIn">Where it is enforced today - this file only names it.</param>
public sealed record HardConstraint(string Name, string Rule, string EnforcedIn)
{
    public override string ToString() => $"{Name} : {Rule} ({EnforcedIn})";
}

/// <summary>
/// One line of the ranking: the quantity measured, which way it reads, and how close two figures have
/// to be before they count as the same.
/// </summary>
/// <param name="Key">
/// Its identity in a stored order (<see cref="Objective.Parse"/>). Fixed for ever, because it is what
/// sits in the database: reword <paramref name="Name"/> freely, never this.
/// </param>
/// <param name="Name">Its name in the study and on the console. A label - it may be reworded.</param>
/// <param name="Value">The quantity it reads off a measured layout.</param>
/// <param name="Better">Whether less or more of it is better.</param>
/// <param name="Tolerance">Two values this close are equal, and the next criterion decides.</param>
public sealed record Criterion(string Key, string Name, Func<LayoutCost, double> Value, Prefer Better, double Tolerance);

// ── Ce qu'une implantation terminée coûte ────────────────────────────────────
/// <summary>
/// The measured quantities of one finished layout - what it costs, not what it is. Computed once from a
/// <see cref="RiggingResult"/> and carried on it, so that every figure quoted about a layout (the
/// console, the study, a comparison between two runs) is read from the same place and computed the same
/// way.
/// </summary>
/// <param name="Palonniers">How many palonniers are hung.</param>
/// <param name="Lines">How many mother lines are drawn.</param>
/// <param name="Pieces">How many stock pieces are assembled into them, all roles together.</param>
/// <param name="DrawnMetres">The length drawn on the plan (m): every palonnier's span and every line's length.</param>
/// <param name="AssembledMetres">The length taken out of the stock (m): the pieces' own lengths, the excess of a piece longer than needed included.</param>
/// <param name="TrussMassKg">The truss mass hung from the venue's beams (kg). A piece of unknown mass counts as zero here; the study is the place that says so.</param>
/// <param name="WorstBayUse">The largest load/allowed ratio over every bay and every palonnier: 0,37 means the tightest truss uses 37 % of what the inventory allows it.</param>
/// <param name="ChargesDirect">Charges hung straight on a mother line.</param>
/// <param name="ChargesOnPalonniers">Charges hung from a palonnier.</param>
public sealed record LayoutCost(
    int    Palonniers,
    int    Lines,
    int    Pieces,
    double DrawnMetres,
    double AssembledMetres,
    double TrussMassKg,
    double WorstBayUse,
    int    ChargesDirect,
    int    ChargesOnPalonniers)
{
    /// <summary>Nothing placed: what a result carries before it has been measured.</summary>
    public static LayoutCost Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Measures a finished layout. Every sum below is the one the baseline is read with, so a figure
    /// here and a figure in the study are the same figure: an element assembled from pieces counts its
    /// pieces, one that was not still counts as one truss; the drawn length is the geometry, the
    /// assembled length is what left the stock.
    /// </summary>
    public static LayoutCost From(RiggingResult result)
    {
        int pieces = result.Palonniers.Sum(p => p.Assembly?.Count ?? 1)
                   + result.Mothers.Sum(m => m.Assembly?.Count ?? 1);

        double drawn = result.Palonniers.Sum(p => p.Span)
                     + result.Mothers.Sum(m => m.Length);

        double assembled = result.Palonniers.Sum(p => p.Assembly?.LengthM ?? p.Bar?.Length ?? 0)
                         + result.Mothers.Sum(m => m.Assembly?.LengthM ?? m.Bar?.Length ?? 0);

        // A palonnier already adds its pieces up when it is built (Optimizer.BuildPalonnier), and a mass
        // it could not know counts as zero there; a line's mass is unknown as a whole or not at all.
        double mass = result.Palonniers.Sum(p => p.SelfWeightKg)
                    + result.Mothers.Sum(m => m.Assembly?.WeightKg ?? 0);

        // The tightest truss in the layout. A bay with no value (a span no row of the model covers) is
        // not a ratio and is left out: the study reports it, this figure would only hide it.
        double use = 0;
        foreach (var m in result.Mothers)
        {
            var loads = m.BayLoads();
            for (int k = 0; k < loads.Length && k < m.BayAllowed.Count; k++)
            {
                double? allowed = m.BayAllowed[k].Kg;
                if (allowed is > 0) use = Math.Max(use, loads[k] / allowed.Value);
            }
        }
        foreach (var p in result.Palonniers)
        {
            double? allowed = p.Allowed?.Kg;
            if (allowed is > 0) use = Math.Max(use, p.TotalWeight / allowed.Value);
        }

        return new LayoutCost(
            Palonniers:          result.Palonniers.Count,
            Lines:               result.Mothers.Count,
            Pieces:              pieces,
            DrawnMetres:         drawn,
            AssembledMetres:     assembled,
            TrussMassKg:         mass,
            WorstBayUse:         use,
            ChargesDirect:       result.Mothers.Sum(m => m.DirectCharges.Count),
            ChargesOnPalonniers: result.Palonniers.Sum(p => p.Charges.Count));
    }

    /// <summary>The one line the console prints and the study quotes.</summary>
    public string Describe()
    {
        var fr = Objective.Fr;
        return $"{Palonniers} palonnier(s), {Lines} ligne(s) de mother, {Pieces} pièce(s), "
             + $"{DrawnMetres.ToString("N2", fr)} m tracés ({AssembledMetres.ToString("N2", fr)} m assemblés), "
             + $"{TrussMassKg.ToString("N1", fr)} kg de poutre, travée la plus chargée à "
             + $"{(WorstBayUse * 100).ToString("N0", fr)} % de la charge admise, "
             + $"{ChargesDirect} charge(s) en direct et {ChargesOnPalonniers} sur palonnier";
    }

    public override string ToString() => Describe();
}

// ── L'objectif ───────────────────────────────────────────────────────────────
/// <summary>
/// The rules that are never traded away, and the order the rest is weighed in. See the file's header
/// for why it is an order and not a sum, and why changing it is a business decision.
/// </summary>
public static class Objective
{
    /// <summary>The optimiser speaks French to its reader, wherever it prints a figure.</summary>
    internal static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ── Combien près est égal ────────────────────────────────────────────────
    // A quantity that is a double never lands on the same bits twice: the same mass summed in a
    // different order differs in the last place. A gram, a millimetre, or a millionth of an allowed load
    // is not a reason to prefer one layout over another - so at that distance the criterion says
    // "equal" and the next one decides.

    /// <summary>A gram apart is the same mass.</summary>
    public const double MassToleranceKg = 0.001;

    /// <summary>A millimetre apart is the same length.</summary>
    public const double LengthToleranceM = 0.001;

    /// <summary>A millionth of the allowed load apart is the same use of a truss.</summary>
    public const double UseTolerance = 1e-6;

    // ══════════════════════════════════════════════════════════
    // (a) LES CONTRAINTES DURES
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The things no layout may do, whatever it would win by doing them. They are not criteria and they
    /// have no weight: a layout that breaks one is not a worse layout, it is not a layout. None of them
    /// is enforced here - this list says what the rule is and names the code that applies it, so that a
    /// rigger reading the rules and a developer reading the code are reading the same four rules.
    /// </summary>
    public static readonly IReadOnlyList<HardConstraint> HardConstraints = new HardConstraint[]
    {
        // Nothing is left hanging in the air. A layout that cannot reach a charge is reported and
        // refused, never saved: the saving replaces the scenario's elements, so a half-placement would
        // replace a good study with a worse one.
        new("Toute charge portée",
            "Chaque charge est portée par un palonnier ou par une ligne de mother ; aucune n'est laissée de côté.",
            "Pipeline.GenerateLayout (garde sur RiggingResult.Uncovered)"),

        // The inventory's charge ponctuelle au milieu, read at the length immediately above the span
        // (LoadTable). Every bay of every line, and every palonnier hanging from its two ends, stays
        // within it. This is the rule the whole tool exists to respect.
        new("Charge admise jamais dépassée",
            "Aucune travée de mother ni aucun palonnier ne porte plus que la charge admise de son modèle à sa portée.",
            "MotherLines.Size, Optimizer.AssemblyFor / SelectGreedy, Optimizer.HangOnCrossings"),

        // The venue owns a finite number of trusses. A layout that uses a piece twice is a drawing, not
        // a plan: the stock hands each piece out once and throws rather than lend one it does not have.
        new("Jamais plus de pièces que le stock",
            "Chaque pièce assemblée sort de l'inventaire et n'y est comptée qu'une fois.",
            "PieceStock.Take (Assembly.cs)"),

        // The setting on the Settings page. When it is on, the six hand-written studies' habit is the
        // rule: palonniers along the beams, mothers across them, nothing inclined.
        new("Orientations orthogonales uniquement",
            "Quand le réglage est actif, aucun palonnier incliné n'est proposé ni placé.",
            "OptimizerOptions.OrthogonalOnly → GroupGenerator.Generate(allowBiais:)"),
    };

    // ══════════════════════════════════════════════════════════
    // (c) LE CLASSEMENT
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The order in force whenever nothing readable is stored, first line first. Compare walks the
    /// ranking it is given and stops at the first criterion that separates two layouts; the
    /// ones below it never come into play. To change the default, move a line - and write down why
    /// beside it. It is also the whole list of criteria: a page offering the order to a developer has
    /// nowhere else to read the keys and their French labels from.
    /// </summary>
    public static readonly IReadOnlyList<Criterion> DefaultRanking = new Criterion[]
    {
        // 1. Each palonnier is a hang: a truss to carry up, two or more points to rig, a knot of work at
        //    height, and its own mass added to the mothers that carry it. It is the cost the crew feels.
        new("palonniers", "palonniers", c => c.Palonniers, Prefer.Less, 0),

        // 2. The mass that actually reaches the venue's beams. It is what the charpente is checked
        //    against, and the one figure a hall can refuse outright.
        new("masse", "masse de poutre suspendue (kg)", c => c.TrussMassKg, Prefer.Less, MassToleranceKg),

        // 3. Every piece is assembly labour on the floor, and every joint between two pieces is an
        //    assumption: that it was made, made right, and made with the pins it was drawn with.
        new("pieces", "pièces assemblées", c => c.Pieces, Prefer.Less, 0),

        // 4. Material out of the stock - the metres that leave the inventory and cannot serve elsewhere
        //    in the same show. That is the assembled length, not the drawn one: a 5,6 m piece used to
        //    span 4,2 m costs the stock 5,6 m. DrawnMetres is the figure the studies quote and is
        //    measured too; it is not what this criterion is about.
        new("longueur", "longueur de poutre (m)", c => c.AssembledMetres, Prefer.Less, LengthToleranceM),

        // 5. Headroom on the tightest truss, for what the production adds after the study is signed - a
        //    motor swapped for a heavier one, a bar of lights that grew. Last, because it is comfort:
        //    every layout that gets this far already respects every allowed load.
        new("marge", "marge sur la travée la plus chargée", c => 1 - c.WorstBayUse, Prefer.More, UseTolerance),
    };

    // ══════════════════════════════════════════════════════════
    // (d) LE CLASSEMENT ENREGISTRÉ
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The ranking a stored setting asks for - the five keys, comma separated, in the order wanted -
    /// or <see cref="DefaultRanking"/> when there is nothing to read or nothing readable.
    /// <para>
    /// Each key exactly once, in any order: an order that forgets a criterion is not an order, and one
    /// that repeats a criterion would silently drop another. On anything else - a missing key, a repeat,
    /// a word this file does not know - the default comes back and <paramref name="problem"/> says in
    /// French what was wrong and that the default is in use. NEVER a partial order patched up with
    /// guesses: the whole point of storing the order is that a study can say which one it was built
    /// under, and a guess would make that sentence a lie.
    /// </para>
    /// <para>
    /// Nothing stored is not a problem, and leaves <paramref name="problem"/> null: it means the default,
    /// which is exactly what the caller gets.
    /// </para>
    /// </summary>
    /// <param name="stored">The setting's value, e.g. "palonniers,masse,pieces,longueur,marge".</param>
    /// <param name="problem">Null when the value was read; the French sentence to show otherwise.</param>
    public static IReadOnlyList<Criterion> Parse(string? stored, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(stored)) return DefaultRanking;

        // Comma separated is the stored form; a semicolon or a stray space is somebody typing the value
        // by hand and meaning the same thing. No key contains any of these, so nothing is ambiguous.
        var keys = stored.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var ordered = new List<Criterion>(DefaultRanking.Count);
        foreach (var key in keys)
        {
            var criterion = DefaultRanking.FirstOrDefault(
                c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

            if (criterion is null)
                return Default($"« {key} » n'est pas un critère connu", out problem);

            if (ordered.Any(c => ReferenceEquals(c, criterion)))
                return Default($"le critère « {criterion.Key} » y figure deux fois", out problem);

            ordered.Add(criterion);
        }

        var missing = DefaultRanking
            .Where(c => !ordered.Any(o => ReferenceEquals(o, c)))
            .Select(c => $"« {c.Key} »")
            .ToList();

        return missing.Count > 0
            ? Default($"il manque {string.Join(", ", missing)}", out problem)
            : ordered;
    }

    /// <summary>The default, and the sentence saying why it is being used instead of what was stored.</summary>
    private static IReadOnlyList<Criterion> Default(string reason, out string problem)
    {
        problem = $"Le classement enregistré est illisible : {reason}. "
                + $"Le classement par défaut est appliqué ({Format(DefaultRanking)}).";
        return DefaultRanking;
    }

    /// <summary>
    /// The stored form of a ranking: its keys, comma separated, in order. What <see cref="Parse"/> reads
    /// back, and what a result quotes to say which objective it was built under.
    /// </summary>
    public static string Format(IReadOnlyList<Criterion> ranking) =>
        string.Join(",", ranking.Select(c => c.Key));

    // ══════════════════════════════════════════════════════════
    // (e) COMPARER DEUX IMPLANTATIONS
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Which of two measured layouts is the better one, by the given ranking: negative when
    /// <paramref name="a"/> is better, positive when <paramref name="b"/> is, zero when nothing in the
    /// ranking separates them. Lexicographic - the first criterion that separates them decides alone.
    /// </summary>
    public static int Compare(LayoutCost a, LayoutCost b, IReadOnlyList<Criterion> ranking)
    {
        foreach (var criterion in ranking)
        {
            double va = criterion.Value(a), vb = criterion.Value(b);
            if (Math.Abs(va - vb) <= criterion.Tolerance) continue;   // this close is equal: next criterion
            int order = va < vb ? -1 : 1;
            return criterion.Better == Prefer.Less ? order : -order;
        }
        return 0;
    }

    /// <summary>By the default order, for a caller that has no ranking of its own in hand.</summary>
    public static int Compare(LayoutCost a, LayoutCost b) => Compare(a, b, DefaultRanking);

    /// <summary>Whether <paramref name="a"/> is the layout to keep.</summary>
    public static bool IsBetter(LayoutCost a, LayoutCost b, IReadOnlyList<Criterion> ranking) =>
        Compare(a, b, ranking) < 0;

    /// <summary>Whether <paramref name="a"/> is the layout to keep, by the default order.</summary>
    public static bool IsBetter(LayoutCost a, LayoutCost b) => Compare(a, b, DefaultRanking) < 0;

    /// <summary>
    /// The first criterion that separates two layouts, for a study that has to say why one was chosen.
    /// Null when the ranking cannot tell them apart.
    /// </summary>
    public static Criterion? Decides(LayoutCost a, LayoutCost b, IReadOnlyList<Criterion> ranking) =>
        ranking.FirstOrDefault(c => Math.Abs(c.Value(a) - c.Value(b)) > c.Tolerance);

    /// <summary>The first criterion that separates two layouts, by the default order.</summary>
    public static Criterion? Decides(LayoutCost a, LayoutCost b) => Decides(a, b, DefaultRanking);

    /// <summary>The ranking in one line, for the console and the study.</summary>
    public static string DescribeRanking(IReadOnlyList<Criterion> ranking) =>
        "Classement : " + string.Join(", puis ", ranking.Select((c, i) =>
            $"{i + 1}. {(c.Better == Prefer.Less ? "moins de" : "plus de")} {c.Name}"));

    /// <summary>The default ranking in one line, for a caller that has none of its own in hand.</summary>
    public static string DescribeRanking() => DescribeRanking(DefaultRanking);
}
