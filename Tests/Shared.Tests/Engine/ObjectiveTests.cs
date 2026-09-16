using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// What "better" means, and what a finished layout costs.
///
/// The preference used to live in three places with magic numbers in them - a 0,3 added to a count, a 0,1
/// added to a sort - and two of them disagreed; Objective replaced them with an order a rigger can read,
/// and the Settings page can now store that order. Two things therefore have to hold whatever anyone
/// stores: an order nobody can read must never quietly change what the optimiser prefers (it would change
/// what the venue gets, and nobody would know why), and the comparison must stop at the first criterion
/// that separates two layouts - a criterion lower down never overturns the one above it.
///
/// Nothing here reads a workbook or a database. A LayoutCost is built in code, which is exactly what
/// Objective being a domain file is for: the rules can be checked without an inventory in front of you.
/// </summary>
public class ObjectiveTests
{
    // ══════════════════════════════════════════════════════════
    // Le classement
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_ranking_is_the_five_criteria_each_exactly_once()
    {
        var keys = Objective.DefaultRanking.Select(c => c.Key).ToList();

        // A key is an identity, not a label: it sits in the database of every installation, and the page
        // that offers the order to a developer reads the keys from here and nowhere else. One missing, one
        // repeated or one renamed and every stored order stops parsing - see Parse below.
        Assert.Equal(
            new[] { "longueur", "marge", "masse", "palonniers", "pieces" },
            keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(5, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // The order in force whenever nothing readable is stored, as the file's header writes it down:
        // fewer palonniers first, margin last. Changing it is a business decision, so it is pinned here -
        // moving a line in Objective.DefaultRanking must break this test and nothing else.
        Assert.Equal("palonniers,masse,pieces,longueur,marge", Objective.Format(Objective.DefaultRanking));

        // Each line carries what a comparison needs: a name to print, a tolerance, and which way it reads.
        Assert.All(Objective.DefaultRanking, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name), $"le critère « {c.Key} » n'a pas de nom à afficher");
            Assert.True(c.Tolerance >= 0, $"le critère « {c.Key} » a une tolérance négative : {c.Tolerance}");
        });

        // The margin is the one criterion where MORE is better. Without it, nothing would ever exercise the
        // other half of Compare.
        Assert.Contains(Objective.DefaultRanking, c => c.Key == "marge" && c.Better == Prefer.More);
    }

    // ══════════════════════════════════════════════════════════
    // Le classement enregistré : Parse et Format
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Parse_reads_the_five_keys_in_any_order()
    {
        // The whole point of storing the order is that a developer can move a line from the Settings page
        // instead of waiting for a release, so every one of the 120 orders has to come back as it was
        // written - and as the SAME criteria, since what Compare reads off a layout hangs on them.
        var reversed = Objective.Parse("marge,longueur,pieces,masse,palonniers", out var problem);

        Assert.True(problem is null, "un ordre complet et lisible n'est pas un problème : " + problem);
        Assert.Equal("marge,longueur,pieces,masse,palonniers", Objective.Format(reversed));
        Assert.All(reversed, c => Assert.Contains(c, Objective.DefaultRanking));
    }

    [Fact]
    public void Parse_accepts_any_separator_a_person_may_type_and_any_case()
    {
        // Comma separated is the stored form; a semicolon, a stray space or a line break is somebody typing
        // the value by hand and meaning the same thing. No key contains any of them, so nothing is
        // ambiguous - and a key typed in capitals is the same key, since it travels through the database.
        var typed = Objective.Parse("PALONNIERS; Masse\tPiEcEs\r\n longueur , MARGE  ", out var problem);

        Assert.True(problem is null, "les séparateurs acceptés ne doivent pas rendre le réglage illisible : " + problem);
        Assert.Equal(Objective.Format(Objective.DefaultRanking), Objective.Format(typed));
    }

    [Fact]
    public void Nothing_stored_is_the_default_and_is_not_a_problem()
    {
        // An installation that has never opened the Settings page stores nothing. That is not a fault to
        // report, it means "the default", which is exactly what the caller gets.
        foreach (var stored in new string?[] { null, "", "   " })
        {
            var ranking = Objective.Parse(stored, out var problem);

            Assert.Same(Objective.DefaultRanking, ranking);
            Assert.True(problem is null, $"« {stored ?? "null"} » ne doit rien signaler : " + problem);
        }
    }

    [Fact]
    public void A_stored_order_missing_a_criterion_falls_back_to_the_default_and_says_so()
    {
        // An order that forgets a criterion is not an order: the forgotten one would decide nothing, and
        // nobody reading the study could say why. NEVER patched up with a guess - the whole point of
        // storing the order is that a study can say which one it was built under.
        var ranking = Objective.Parse("palonniers,masse,pieces,longueur", out var problem);

        Assert.Same(Objective.DefaultRanking, ranking);
        Assert.True(problem is not null, "un classement incomplet doit être signalé");
        Assert.Contains("marge", problem!);
        Assert.Contains("par défaut", problem);
        Assert.Contains(Objective.Format(Objective.DefaultRanking), problem);
    }

    [Fact]
    public void A_stored_order_repeating_a_criterion_falls_back_to_the_default_and_says_so()
    {
        // A repeat silently drops another criterion, which is the same fault as forgetting one, only
        // harder to see when reading the setting.
        var ranking = Objective.Parse("palonniers,palonniers,masse,pieces,longueur", out var problem);

        Assert.Same(Objective.DefaultRanking, ranking);
        Assert.True(problem is not null, "un critère répété doit être signalé");
        Assert.Contains("palonniers", problem!);
        Assert.Contains("deux fois", problem);
    }

    [Fact]
    public void A_stored_order_naming_an_unknown_word_falls_back_to_the_default_and_says_so()
    {
        // A word this file does not know is most often an old key, or a label typed in place of a key. It
        // must not be passed over in silence: a setting nobody can read must never change what the
        // optimiser prefers.
        var ranking = Objective.Parse("palonniers,masse,pieces,longueur,poids", out var problem);

        Assert.Same(Objective.DefaultRanking, ranking);
        Assert.True(problem is not null, "un mot inconnu doit être signalé");
        Assert.Contains("poids", problem!);
        Assert.Contains("critère connu", problem);
    }

    [Fact]
    public void Format_writes_what_Parse_reads_back_for_every_one_of_the_120_orders()
    {
        // Format is what a result quotes to say which objective it was built under (SearchOutcome.Ranking),
        // and Parse is what the next run reads. If the two ever disagree - about the separator, the case,
        // the order - a study would name a ranking that was not the one applied.
        foreach (var order in EveryOrder(Objective.DefaultRanking.ToList()))
        {
            var readBack = Objective.Parse(Objective.Format(order), out var problem);

            Assert.True(problem is null, $"« {Objective.Format(order)} » doit se relire sans problème : {problem}");
            Assert.Equal(order.Count, readBack.Count);
            for (int i = 0; i < order.Count; i++)
                Assert.Same(order[i], readBack[i]);
        }
    }

    // ══════════════════════════════════════════════════════════
    // Comparer deux implantations
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void The_first_criterion_decides_alone_whatever_the_rest_say()
    {
        // One palonnier fewer is one hang fewer: a truss less to carry up, two points less to rig, a knot
        // of work at height saved. The order says to take it even when everything else gets worse, which is
        // the business decision written into DefaultRanking - not an artefact of a weighted sum.
        var fewerHangs = Layout(palonniers: 4, massKg: 5_000, pieces: 40, assembledM: 300, worstUse: 0.99);
        var everythingElse = Layout(palonniers: 5, massKg: 100, pieces: 4, assembledM: 20, worstUse: 0.10);

        Assert.True(Objective.Compare(fewerHangs, everythingElse) < 0,
            "le premier critère doit trancher seul : " + fewerHangs.Describe() + " vs " + everythingElse.Describe());
        Assert.True(Objective.IsBetter(fewerHangs, everythingElse));
        Assert.True(Objective.Compare(everythingElse, fewerHangs) > 0, "la comparaison doit être symétrique");
    }

    [Fact]
    public void A_criterion_below_a_tie_never_overturns_the_one_above_it()
    {
        // Same number of palonniers, so the mass decides - and the mass alone. The three criteria under it
        // all favour the heavier layout and none of them gets a say, which is what "lexicographic" means
        // and why the order can be changed by moving one line.
        var lighter = Layout(palonniers: 3, massKg: 900, pieces: 40, assembledM: 300, worstUse: 0.99);
        var heavier = Layout(palonniers: 3, massKg: 1_000, pieces: 4, assembledM: 20, worstUse: 0.10);

        Assert.True(Objective.Compare(lighter, heavier) < 0,
            "la masse doit trancher : " + lighter.Describe() + " vs " + heavier.Describe());
    }

    [Fact]
    public void Two_figures_within_the_tolerance_are_the_same_figure_and_the_next_criterion_decides()
    {
        // A double summed in a different order differs in the last place, and a gram is not a reason to
        // prefer one layout over another. At that distance the criterion says "equal" and hands over.
        var lighterByHalfAGram = Layout(palonniers: 3, massKg: 1_000.0000, pieces: 6, assembledM: 40, worstUse: 0.5);
        var fewerPieces = Layout(palonniers: 3, massKg: 1_000.0005, pieces: 4, assembledM: 40, worstUse: 0.5);

        Assert.True(Objective.MassToleranceKg > 0.0005, "le demi-gramme doit rester dans la tolérance de masse");
        Assert.True(Objective.Compare(lighterByHalfAGram, fewerPieces) > 0,
            "un demi-gramme ne doit pas décider : c'est le nombre de pièces qui tranche");
    }

    [Fact]
    public void The_margin_is_the_criterion_where_the_larger_figure_is_the_better_one()
    {
        // Headroom on the tightest truss, for what the production adds after the study is signed. Read the
        // way of the other four, it would make the tightest layout the best one.
        var roomy = Layout(palonniers: 3, massKg: 1_000, pieces: 6, assembledM: 40, worstUse: 0.30);
        var tight = Layout(palonniers: 3, massKg: 1_000, pieces: 6, assembledM: 40, worstUse: 0.95);

        Assert.True(Objective.Compare(roomy, tight) < 0,
            "la travée la plus chargée à 30 % doit être préférée à 95 % : "
            + roomy.Describe() + " vs " + tight.Describe());
    }

    [Fact]
    public void Compare_obeys_the_ranking_it_is_given_and_moving_a_line_changes_the_verdict()
    {
        // The same two layouts, weighed under two stored orders. This is what a developer actually changes
        // from the Settings page, and it must reach the comparison - the default is only what applies when
        // nothing readable is stored.
        var fewHangsHeavy = Layout(palonniers: 2, massKg: 2_000, pieces: 6, assembledM: 40, worstUse: 0.5);
        var moreHangsLight = Layout(palonniers: 3, massKg: 1_000, pieces: 6, assembledM: 40, worstUse: 0.5);

        Assert.True(Objective.Compare(fewHangsHeavy, moreHangsLight) < 0,
            "par défaut, le nombre de palonniers passe avant la masse");

        var massFirst = Objective.Parse("masse,palonniers,pieces,longueur,marge", out var problem);
        Assert.True(problem is null, problem);

        Assert.True(Objective.Compare(fewHangsHeavy, moreHangsLight, massFirst) > 0,
            "masse en tête, l'implantation la plus légère doit l'emporter");
        Assert.True(Objective.IsBetter(moreHangsLight, fewHangsHeavy, massFirst));
    }

    [Fact]
    public void Decides_names_the_criterion_that_separated_two_layouts()
    {
        // A study that says one layout was chosen has to be able to say what for. Same palonniers, same
        // pieces below: the mass is what separates these two.
        var lighter = Layout(palonniers: 3, massKg: 1_000, pieces: 6, assembledM: 40, worstUse: 0.5);
        var heavier = Layout(palonniers: 3, massKg: 1_200, pieces: 4, assembledM: 40, worstUse: 0.5);

        Assert.Equal("masse", Objective.Decides(lighter, heavier)!.Key);

        // Under a stored order that puts the pieces first, it is the pieces that separate them: Decides
        // reads the ranking it is handed, exactly as Compare does.
        var piecesFirst = Objective.Parse("pieces,palonniers,masse,longueur,marge", out _);
        Assert.Equal("pieces", Objective.Decides(lighter, heavier, piecesFirst)!.Key);

        // Nothing separates a layout from itself, and the study must not invent a reason.
        Assert.True(Objective.Decides(lighter, lighter) is null,
            "aucun critère ne sépare une implantation d'elle-même");
    }

    // ══════════════════════════════════════════════════════════
    // Mesurer une implantation terminée
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void LayoutCost_measures_every_quantity_of_a_finished_layout()
    {
        // One palonnier of two 2,8 m pieces carrying two charges, hung over a mother line of two 5,6 m
        // pieces drawn across one bay, with a third charge hung straight on the line. Every figure the
        // console prints, the study quotes and the search compares is read from here, so each one is
        // checked against what the layout visibly is.
        var palPiece = new BarSpec { Type = "P", Length = 2.8, StockInit = 4, Stock = 4, Capacity = 2_345, WeightKg = 31 };
        var motherPiece = new BarSpec { Type = "M", Length = 5.6, StockInit = 4, Stock = 4, Capacity = 3_429, WeightKg = 95.6 };

        var hung = new Assembly("P", new[] { palPiece, palPiece });
        var pal = new Palonnier
        {
            Assembly = hung,
            Bar = hung.AsBar(2_000),
            Allowed = new AllowedLoad(2_000, AllowedSource.Table, 5.6, 5.6, 2_000),
            Direction = PalonnierDirection.Y,
            A = (0, 0),
            B = (0, 5.6),
            Charges = new List<Charge>
            {
                new() { Name = "C1", X = 0, Y = 1.5, Weight = 400 },
                new() { Name = "C2", X = 0, Y = 4.0, Weight = 600 },
            },
            SelfWeightKg = 62,   // ses deux pièces, comme Optimizer.BuildPalonnier les compte
        };

        var assembled = new Assembly("M", new[] { motherPiece, motherPiece });
        var line = new Mother
        {
            Row = 5,
            Y = 0,
            Columns = new List<(string Col, double X)> { ("E", 0.0), ("D", 7.144) },
            XL = 0,
            XR = 7.144,
            Assembly = assembled,
            Bar = assembled.AsBar(1_500),
        };
        line.BayAllowed.Add(new AllowedLoad(1_500, AllowedSource.Table, 7.144, 8.4, 1_500));
        line.AddPoint(3.0, 1_000, "P1", 62);          // l'extrémité du palonnier : ses charges et son poids propre
        line.DirectCharges.Add(new Charge { Name = "C3", X = 5.0, Y = 0, Weight = 200 });
        line.AddPoint(5.0, 200, "C3");

        var cost = LayoutCost.From(new RiggingResult
        {
            Charges = pal.Charges.Concat(line.DirectCharges).ToList(),
            Palonniers = new List<Palonnier> { pal },
            Mothers = new List<Mother> { line },
        });

        Assert.Equal(1, cost.Palonniers);
        Assert.Equal(1, cost.Lines);

        // Chaque pièce assemblée, tous rôles confondus : 2 + 2.
        Assert.Equal(4, cost.Pieces);

        // Tracé sur le plan : la portée du palonnier et la longueur de la ligne.
        Assert.Equal(5.6 + 7.144, cost.DrawnMetres, 6);

        // Sorti du stock : la longueur des pièces elles-mêmes, l'excédent compris - 11,2 m de poutre pour
        // 7,144 m tracés. C'est ce chiffre-là que le critère « longueur » lit, jamais le tracé.
        Assert.Equal(5.6 + 11.2, cost.AssembledMetres, 6);

        // La masse suspendue aux poutres de la salle : le poids propre du palonnier et celui de la ligne.
        Assert.Equal(62 + 191.2, cost.TrussMassKg, 6);

        // La travée la plus chargée : 1 000 kg de charges + 62 kg de poids propre + 200 kg en direct, pour
        // 1 500 kg admis - plus serrée que le palonnier, à 1 000 kg pour 2 000 kg admis.
        Assert.Equal(1_262.0 / 1_500.0, cost.WorstBayUse, 9);

        Assert.Equal(1, cost.ChargesDirect);
        Assert.Equal(2, cost.ChargesOnPalonniers);
    }

    [Fact]
    public void The_worst_use_reads_the_palonniers_as_well_as_the_bays()
    {
        // A palonnier hung from its two ends is checked at its span like any bay, and it is often the
        // tightest truss in the rig. Read off the lines alone, the figure would say the layout has room it
        // has not got.
        var line = OneBayLine(allowedKg: 3_000, loadKg: 300);   // 10 % de la charge admise
        var pal = LoadedPalonnier(allowedKg: 1_000, loadKg: 900);   // 90 % de la sienne

        var cost = LayoutCost.From(new RiggingResult
        {
            Palonniers = new List<Palonnier> { pal },
            Mothers = new List<Mother> { line },
        });

        Assert.Equal(0.9, cost.WorstBayUse, 9);
    }

    [Fact]
    public void A_bay_the_inventory_gives_no_value_for_is_left_out_of_the_worst_use()
    {
        // A span no row of the model covers is not a ratio: counted as zero it would flatter the layout,
        // counted as infinite it would drown every real figure. The study is the place that reports it.
        var line = OneBayLine(allowedKg: null, loadKg: 1_200);

        var cost = LayoutCost.From(new RiggingResult { Mothers = new List<Mother> { line } });

        Assert.Equal(0.0, cost.WorstBayUse, 9);
    }

    [Fact]
    public void An_element_assembled_from_no_stock_piece_still_counts_as_one_truss()
    {
        // An element placed by hand, or read back from an older scenario, has a bar and no pieces. It is
        // still a truss on the plan - one piece, its own length - and its mass counts as zero rather than
        // as a light truss, because zero is what the layout actually knows.
        var line = new Mother
        {
            Row = 5,
            Y = 0,
            Columns = new List<(string Col, double X)> { ("E", 0.0), ("D", 7.144) },
            XL = 0,
            XR = 7.144,
            Bar = new BarSpec { Type = "M", Length = 8.4, Capacity = 2_000, WeightKg = 120 },
        };

        var cost = LayoutCost.From(new RiggingResult { Mothers = new List<Mother> { line } });

        Assert.Equal(1, cost.Pieces);
        Assert.Equal(8.4, cost.AssembledMetres, 6);
        Assert.Equal(7.144, cost.DrawnMetres, 6);
        Assert.Equal(0.0, cost.TrussMassKg, 6);
    }

    // ── Ce qui sert aux essais ───────────────────────────────────

    /// <summary>A measured layout with only the five ranked quantities filled in.</summary>
    private static LayoutCost Layout(int palonniers, double massKg, int pieces, double assembledM, double worstUse) =>
        new(Palonniers: palonniers,
            Lines: 1,
            Pieces: pieces,
            DrawnMetres: assembledM,
            AssembledMetres: assembledM,
            TrussMassKg: massKg,
            WorstBayUse: worstUse,
            ChargesDirect: 0,
            ChargesOnPalonniers: 0);

    /// <summary>One line over one bay, carrying <paramref name="loadKg"/> in the middle of it.</summary>
    private static Mother OneBayLine(double? allowedKg, double loadKg)
    {
        var line = new Mother
        {
            Row = 5,
            Y = 0,
            Columns = new List<(string Col, double X)> { ("E", 0.0), ("D", 7.144) },
            XL = 0,
            XR = 7.144,
        };
        line.BayAllowed.Add(allowedKg is { } kg
            ? new AllowedLoad(kg, AllowedSource.Table, 7.144, 8.4, kg)
            : AllowedLoad.None(7.144, "aucun modèle de l'inventaire ne couvre cette portée"));
        line.AddPoint(3.5, loadKg, "C1");
        return line;
    }

    /// <summary>One palonnier carrying <paramref name="loadKg"/> for <paramref name="allowedKg"/> admitted.</summary>
    private static Palonnier LoadedPalonnier(double allowedKg, double loadKg) => new()
    {
        Bar = new BarSpec { Type = "P", Length = 4.0, Capacity = allowedKg },
        Allowed = new AllowedLoad(allowedKg, AllowedSource.Table, 4.0, 4.0, allowedKg),
        Direction = PalonnierDirection.Y,
        A = (0, 0),
        B = (0, 4.0),
        Charges = new List<Charge> { new() { Name = "C1", X = 0, Y = 2.0, Weight = loadKg } },
    };

    /// <summary>Every order the five criteria can be stored in - the whole space the setting can hold.</summary>
    private static IEnumerable<List<Criterion>> EveryOrder(List<Criterion> remaining)
    {
        if (remaining.Count == 0)
        {
            yield return new List<Criterion>();
            yield break;
        }

        for (int i = 0; i < remaining.Count; i++)
        {
            var rest = new List<Criterion>(remaining);
            rest.RemoveAt(i);
            foreach (var tail in EveryOrder(rest))
            {
                tail.Insert(0, remaining[i]);
                yield return tail;
            }
        }
    }
}
