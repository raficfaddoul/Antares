using AliantWebS.Models;
using AliantWebS.Reporting;
using Helpers;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Report;

/// <summary>
/// The check the study is actually trusted for: what each truss carries, span by span, against what the
/// inventory allows it to carry there.
///
/// A palonnier is checked between the points it hangs from, a mother between the beams it rests on, and the
/// allowed load is read at the length immediately ABOVE the span - never interpolated, never at the row
/// below, which would allow more than the inventory does. Past the longest row of a model the value is
/// computed at equal moment from that row, and past twice it there is no value at all: a span nobody can
/// justify must stop the study rather than pass it.
///
/// Everything is built in code - elements, charges, inventory rows - so these rules are checked against
/// their arguments and nothing else: no database, no workbook, no scenario.
/// </summary>
public class AbaqueCheckTests
{
    // ── The venue's axes, relative to Point0 = E5 ────────────────
    private const double P0X = 33.103, P0Y = 0;
    private const double G = 18.697 - P0X, F = 25.947 - P0X, E = 0, D = 40.247 - P0X;
    private const double Row1 = 15.15, Row3 = 7.4, Row5 = 0, Row6 = -3.7;

    // ═══════════════════════════════════════════════════════════════
    // Palonniers, span by span
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_palonnier_span_longer_than_every_row_takes_a_value_computed_at_equal_moment()
    {
        var scene = new Scene();

        var check = Assert.Single(scene.PalonnierChecks().Where(c => c.IsGoverning));

        // 900 daN between lines 3 and 5, which is 7,4 m, on a model whose longest row is 4 m: the value is
        // deduced from that row at equal moment (M = P·L/4 + p·L²/8), never read at 4 m as though the span
        // were 4 m long.
        Assert.Equal("lignes 3–5", check.SpanLabel);
        Assert.Equal(900.0, check.AppliedKg, 2);
        Assert.Equal(AllowedSource.Computed, check.Source);
        Assert.Equal(453.75, check.AllowedKg!.Value, 2);
        Assert.Equal(7.4, check.AllowedSpanM!.Value, 2);
        Assert.Equal(4.0, check.BaseLengthM!.Value, 2);
        Assert.Equal(900.0, check.BaseKg!.Value, 2);
        Assert.Equal(12.5, check.KgPerM!.Value, 3);
        Assert.True(check.IsComputed && !check.Ok, "900 daN dépasse la valeur calculée de 453,75 daN");
    }

    [Fact]
    public void A_span_of_more_than_twice_the_longest_row_has_no_value_and_says_why()
    {
        var scene = new Scene();

        var far = Assert.Single(scene.PalonnierChecks().Where(c => c.WorstElement == scene.LongPalonnier.Name));

        // 15,15 m on a model whose longest row is 4 m. Extrapolating that far would be inventing a truss;
        // the study says instead that this load could not be checked, which blocks its conclusion.
        Assert.Null(far.AllowedKg);
        Assert.True(far.BeyondAbaque, "au-delà de l'abaque, et non un modèle absent de l'inventaire");
        Assert.Equal("lignes 1–5", far.SpanLabel);
        Assert.Contains("2 fois", far.NoValueReason ?? "");
    }

    [Fact]
    public void A_palonnier_span_that_passes_is_not_printed()
    {
        var scene = new Scene();

        // Only what the reader must act on: the governing case and the one that could not be checked. The
        // 3,7 m span carries 300 daN of the 900 it is allowed and has nothing to say.
        Assert.Equal(2, scene.PalonnierChecks().Count);
    }

    [Fact]
    public void The_abaque_dictionary_overload_reads_the_same_rule_as_the_load_table()
    {
        var scene = new Scene();

        // Kept for the callers that built their abaques before LoadTable existed. It must not become a
        // second, older reading of the inventory.
        var legacy = ReportDataBuilder.BuildAbaqueChecks(
            scene.Elements, scene.TypeNames, scene.ChargeMps,
            scene.PalonnierRows.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.ToList()),
            new List<string>(), scene.HangingPoints);

        Assert.Equal(453.75, Assert.Single(legacy.Where(c => c.IsGoverning)).AllowedKg!.Value, 2);
    }

    // ═══════════════════════════════════════════════════════════════
    // Mothers, bay by bay
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_mother_bay_is_read_at_the_length_immediately_above_the_span()
    {
        var scene = new Scene();

        var check = Assert.Single(scene.MotherChecks().Where(c => c.IsGoverning));

        // F3–E3 is 7,156 m, so the value is the 8,4 m row's - the next length up - and not the 5,6 m one's.
        // The 8,4 m row has Nombre = 0: a length the venue owns none of still says what a bay may carry.
        Assert.Equal("F3–E3", check.SpanLabel);
        Assert.Equal(2500.0, check.AppliedKg, 2);
        Assert.Equal(7.156, check.SpanM, 3);
        Assert.Equal(AllowedSource.Table, check.Source);
        Assert.Equal(2286.0, check.AllowedKg!.Value, 2);
        Assert.Equal(8.4, check.AllowedSpanM!.Value, 2);
        Assert.False(check.Ok, "2 500 daN dépasse les 2 286 daN de la ligne de 8,4 m");
    }

    [Fact]
    public void A_load_past_the_last_beam_is_a_cantilever_named_after_that_beam()
    {
        var scene = new Scene();

        var cantilever = Assert.Single(scene.MotherChecks().Where(c => c.Case == AbaqueLoadCase.Cantilever));

        // A load 40 cm past D3 does not bend a bay: it hangs off the end, and is reported as what it is.
        Assert.Equal("D3", cantilever.SpanLabel);
        Assert.Equal(300.0, cantilever.AppliedKg, 2);
        Assert.Equal(0.4, cantilever.SpanM, 3);
    }

    [Fact]
    public void A_palonnier_pulling_a_mother_upwards_is_reported_as_uplift()
    {
        var scene = new Scene();

        // No abaque on file covers a mothergrid pulled up by a palonnier's cantilever, so it is neither
        // passed nor silently dropped: it is reported, with the load that does the pulling.
        Assert.Equal(120.0, Assert.Single(scene.MotherChecks().Where(c => c.Case == AbaqueLoadCase.Uplift)).AppliedKg, 2);
    }

    [Fact]
    public void A_mother_bay_that_passes_is_not_printed()
    {
        var scene = new Scene();

        // Of the four bays that carry something, only the governing one is printed; the load sitting on E3
        // bends no bay at all.
        Assert.Single(scene.MotherChecks().Where(c => c.Case == AbaqueLoadCase.Centered));
    }

    [Fact]
    public void A_wrong_Point0_names_the_bays_after_the_supports_the_transmission_itself_finds()
    {
        var scene = new Scene();

        // With Point0 at the origin instead of E5 the mother line lands under J3 and I3. The study must name
        // the bays the load transmission actually transmitted to - the same reading, right or wrong - so that
        // a study and the loads it checks can never describe two different pre-rigs. The wrong Point0 is
        // reported separately, as a gap: the transmission finds those ends off the grid.
        var shifted = LoadTransmissionHelper.CharpenteSupportsOf(scene.Mother, 0, 0).Select(s => s.Name).ToList();
        Assert.Equal("J3,I3", string.Join(",", shifted));

        var offGrid = ReportDataBuilder.BuildMotherAbaqueChecks(
            scene.Elements, scene.TypeNames, scene.PointLoads, scene.MotherTable, new List<string>(), 0, 0);

        var named = offGrid.Where(c => c.Case == AbaqueLoadCase.Centered && c.SpanLabel is not null).ToList();
        Assert.NotEmpty(named);
        Assert.All(named, c => Assert.Equal("J3–I3", c.SpanLabel));
    }

    // ═══════════════════════════════════════════════════════════════
    // Values read from an inventory a row of which could not be read
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_palonnier_check_carries_the_unread_rows_of_its_own_model_and_file_only()
    {
        var scene = new Scene();

        var check = Assert.Single(scene.PalonnierChecks(Unread).Where(c => c.IsGoverning));

        // Of the five unread rows, exactly one can have changed this value: the P300 of Palonnier.xlsx. The
        // P300 row of MotherGrill.xlsx is a mother's row and cannot; the S500 and Q100 rows are other models.
        Assert.True(check.InventoryIncomplete, "la vérification doit se dire incomplète");
        Assert.True(Assert.Single(check.SkippedRows) is { Line: 3, Mothers: false }, "la ligne 3 de Palonnier.xlsx, seule");

        // And the value itself is unchanged: an unread row qualifies a figure, it does not replace it.
        Assert.Equal(453.75, check.AllowedKg!.Value, 2);
    }

    [Fact]
    public void With_every_row_read_a_check_claims_nothing_about_unread_rows()
    {
        var scene = new Scene();

        Assert.All(scene.PalonnierChecks(), c => Assert.False(c.InventoryIncomplete));
    }

    [Fact]
    public void Without_one_of_its_rows_a_missing_value_says_lisible_and_names_the_row()
    {
        var scene = new Scene();
        var warnings = new List<string>();

        var far = Assert.Single(scene.PalonnierChecks(Unread, warnings).Where(c => c.WorstElement == scene.LongPalonnier.Name));
        var reason = far.NoValueReason ?? "";

        // "La plus grande longueur de l'inventaire" would be a claim about the inventory the reader cannot
        // check: one of its rows was not read, and a longer readable row might have existed.
        Assert.Null(far.AllowedKg);
        Assert.Contains("la plus grande longueur lisible de l'inventaire", reason);
        Assert.DoesNotContain("la plus grande longueur de l'inventaire", reason);
        Assert.Contains("la ligne 3 de Palonnier.xlsx, de ce modèle, n'a pas pu être lue", reason);
        Assert.Contains(warnings, w => w.Contains("longueur lisible") && w.Contains("n'a pas pu être lue"));
    }

    [Fact]
    public void A_mother_check_carries_the_unread_rows_of_MotherGrill_for_its_model()
    {
        var scene = new Scene();

        var check = Assert.Single(scene.MotherChecks(Unread).Where(c => c.IsGoverning));

        Assert.True(check.InventoryIncomplete, "la vérification doit se dire incomplète");
        Assert.Equal(new[] { 4, 5 }, check.SkippedRows.Select(r => r.Line));
        Assert.All(check.SkippedRows, r => Assert.True(r.Mothers, "seules les lignes de MotherGrill.xlsx"));
    }

    // ── The inventory rows nobody could read ─────────────────────

    /// <summary>
    /// The rows InventoryFiles.Load skips in the venue's own workbooks, as records: the reading of the
    /// workbooks themselves is TrussInventoryTests' subject, what the checks do with the result is this one's.
    /// </summary>
    private static readonly List<SkippedRow> Unread = new()
    {
        new("MotherGrill.xlsx", true, "S500", 7.5, 4, "charge ponctuelle au milieu illisible"),
        new("MotherGrill.xlsx", true, "S500", null, 5, "longueur illisible"),
        new("MotherGrill.xlsx", true, "Q100", null, 9, "longueur illisible"),
        new("MotherGrill.xlsx", true, "P300", 4.0, 10, "charge ponctuelle au milieu illisible"),
        new("Palonnier.xlsx", false, "P300", 6.0, 3, "charge ponctuelle au milieu illisible"),
    };

    // ── The scenario ─────────────────────────────────────────────

    private static MotherGridPalonnier El(
        string name, Guid type, bool mother, double x1, double y1, double x2, double y2, string? composition) => new()
    {
        MotherGridPalonnierId = Guid.NewGuid(), ElementTypeId = type, Famille = mother, Name = name,
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Composition = composition,
    };

    /// <summary>
    /// One mother line across the grid and two palonniers hung under it, loaded so that every case the study
    /// can report appears once. A fresh one per test: no test may see what another one did.
    /// </summary>
    private sealed class Scene
    {
        public readonly Guid S500Id = Guid.NewGuid();
        public readonly Guid P300Id = Guid.NewGuid();

        public readonly Dictionary<Guid, string> TypeNames;
        public readonly List<BarSpec> PalonnierRows;
        public readonly LoadTable MotherTable, PalonnierTable;

        public readonly MotherGridPalonnier Mother, Palonnier, LongPalonnier;
        public readonly List<MotherGridPalonnier> Elements;

        public readonly List<ChargeMp> ChargeMps;
        public readonly Dictionary<Guid, IReadOnlyList<double>> HangingPoints;
        public readonly Dictionary<Guid, List<(double X, double Y, double Load)>> PointLoads;

        public Scene()
        {
            TypeNames = new Dictionary<Guid, string> { [S500Id] = "S500", [P300Id] = "P300" };

            MotherTable = LoadTable.From(new List<BarSpec>
            {
                new() { Type = "S500", Length = 2.8, StockInit = 10, Stock = 10, Capacity = 3000, WeightKg = 80 },
                new() { Type = "S500", Length = 5.6, StockInit = 10, Stock = 10, Capacity = 2500, WeightKg = 160 },
                // Nombre = 0: no stock, but the row the 7,156 m bay is read at.
                new() { Type = "S500", Length = 8.399999999999999, StockInit = 0, Stock = 0, Capacity = 2286, WeightKg = 240 },
            });
            PalonnierRows = new List<BarSpec>
            {
                new() { Type = "P300", Length = 4.0, StockInit = 6, Stock = 6, Capacity = 900, WeightKg = 50 },
            };
            PalonnierTable = LoadTable.From(PalonnierRows);

            Mother = El("S500 L=22,4m #1", S500Id, true, G, Row3, D, Row3, "[560,560,560,560]");
            Palonnier = El("P300 L=12,0m #1", P300Id, false, -10.8, Row3, -10.8, Row6, "[400,400,400]");
            LongPalonnier = El("P300 L=16,0m #2", P300Id, false, -5.0, Row1, -5.0, Row5, "[400,400,400,400]");
            var oldMother = El("S500 L=8,4m #9", S500Id, true, F, -11.05, E, -11.05, null);
            var unreadable = El("S500 L=2,8m #7", S500Id, true, -1.0, -15.151, 1.0, -15.151, "oops");
            var partial = El("S500 L=15,6m #5", S500Id, true, D, 3.7, D + 15.0, 3.7, "[999,560]");
            Elements = new List<MotherGridPalonnier> { Mother, Palonnier, LongPalonnier, oldMother, unreadable, partial };

            ChargeMps = new List<ChargeMp>
            {
                Charge("T1", Palonnier, 3.0, 500),
                Charge("T2", Palonnier, 5.0, 400),
                Charge("T3", Palonnier, 9.0, 300),
                Charge("T4", LongPalonnier, 5.0, 200),
            };
            // Where each palonnier hangs, as distances along it: T1 and T2 fall in the 7,4 m span between
            // lines 3 and 5, T3 in the 3,7 m one below it.
            HangingPoints = new Dictionary<Guid, IReadOnlyList<double>>
            {
                [Palonnier.MotherGridPalonnierId] = new List<double> { 0, 7.4, 11.1 },
                [LongPalonnier.MotherGridPalonnierId] = new List<double> { 0, 15.15 },
            };

            PointLoads = new Dictionary<Guid, List<(double X, double Y, double Load)>>
            {
                [Mother.MotherGridPalonnierId] = new()
                {
                    (-10.8, Row3, 1500),        // G3–F3
                    (-3.5, Row3, 2500),         // F3–E3, over 2 286
                    (E + 0.005, Row3, 999),     // on E3: bends no bay
                    (3.6, Row3, 800),           // E3–D3
                    (5.0, Row3, 700),           // E3–D3
                    (D + 0.4, Row3, 300),       // past D3: cantilever
                    (4.0, Row3, -120),          // uplift
                },
            };
        }

        public List<AbaqueCheck> PalonnierChecks(
            IReadOnlyCollection<SkippedRow>? unread = null, List<string>? warnings = null) =>
            ReportDataBuilder.BuildAbaqueChecks(Elements, TypeNames, ChargeMps, PalonnierTable,
                warnings ?? new List<string>(), HangingPoints, point0Y: P0Y, skippedRows: unread);

        public List<AbaqueCheck> MotherChecks(IReadOnlyCollection<SkippedRow>? unread = null) =>
            ReportDataBuilder.BuildMotherAbaqueChecks(Elements, TypeNames, PointLoads, MotherTable,
                new List<string>(), P0X, P0Y, skippedRows: unread);

        private static ChargeMp Charge(string name, MotherGridPalonnier on, double x, double kg) => new()
        {
            ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = on.MotherGridPalonnierId,
            MotherGridPalonnier = on, Name = name, X = x, Charge = kg,
        };
    }
}
