using AliantWebS.Models;
using AliantWebS.Reporting;
using Helpers;
using RiggingOptimizer;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// What is stored is what the load calculation finds.
///
/// The optimiser places a layout and works out what each attachment point of the roof carries. That
/// result is then written to the database as rows - one per truss, one per charge - and thrown away. From
/// then on, everything a person sees comes from reading those rows back: Calcul Charpente, the study, the
/// plan. If the rows do not say the same thing the optimiser said, the layout was sized against loads
/// nobody will ever verify, and the study prints figures that belong to a rig that was never placed.
///
/// So these tests do the round trip on the venue's own plan: build the rows (Pipeline), run the load
/// calculation over them (LoadTransmissionHelper) and compare, point by point and palonnier by palonnier,
/// with what the optimiser had. Nothing here touches a database - the rows are built in memory, which is
/// exactly what makes the comparison possible.
/// </summary>
[Collection(EngineCollection.Name)]
public class StoredLayoutTests
{
    private readonly VenuePlan _venue;

    public StoredLayoutTests(VenuePlan venue) => _venue = venue;

    // ── The rows themselves ──────────────────────────────────────

    [Fact]
    public void Every_charge_of_the_plan_is_stored_exactly_once()
    {
        // Stored twice, a load is carried twice by the calculation; stored not at all, it is carried by
        // nothing. A charge is known by its name everywhere after this, so the names must stay distinct.
        var stored = _venue.Stored.Charges;

        Assert.Equal(_venue.Charges.Count, stored.Count);
        Assert.Equal(_venue.Charges.Count, stored.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void A_charge_hung_straight_on_a_line_is_stored_on_that_line()
    {
        // Famille marks a mother line. The 18 loads that sit under a point line hang on the line itself,
        // not on a palonnier, and the row has to say so: stored on a palonnier that does not exist, they
        // would reach the roof by a path nobody drew.
        int onALine = _venue.Stored.Charges.Count(c => c.MotherGridPalonnier.Famille);

        Assert.True(onALine == 18, $"{onALine} charges enregistrées sur une ligne de mother au lieu de 18");
    }

    [Fact]
    public void Every_element_records_the_pieces_it_is_made_of()
    {
        // The composition is the only record of how a truss was assembled. Without it the study falls back
        // to reading a single length out of the element's name, and the crew has no cut list.
        var without = _venue.Stored.Elements
            .Where(e => TrussComposition.Parse(e.Composition, out var readable) is not { Count: > 0 } || !readable)
            .Select(e => $"{e.Name} : « {e.Composition} »")
            .ToList();

        Assert.True(without.Count == 0,
            "élément(s) sans pièces lisibles : " + string.Join(" | ", without));
    }

    [Fact]
    public void The_pieces_add_up_to_the_length_the_element_s_name_gives()
    {
        // The name says which truss it is - "S500 Carrée Acier L=8,4m #1" - and the pieces say what it is
        // made of. The two are read by different parts of the study; if they disagree, Tableau 2 orders a
        // truss the plan does not draw. Five centimetres of slack is the rounding of the printed length.
        var disagree = _venue.Stored.Elements
            .Where(e =>
            {
                var pieces = TrussComposition.Parse(e.Composition, out _);
                return pieces is null
                       || Math.Abs(pieces.Sum() - ReportDataBuilder.ReferenceLengthOf(e)) > 0.051;
            })
            .Select(e => $"{e.Name} : {TrussComposition.Parse(e.Composition, out _)?.Sum()} m de pièces")
            .ToList();

        Assert.True(disagree.Count == 0,
            "élément(s) dont les pièces ne font pas la longueur annoncée : " + string.Join(" | ", disagree));
    }

    [Fact]
    public void Every_name_fits_the_fifty_character_column_it_is_stored_in()
    {
        // MotherGridPalonnier.Name is a 50-character column. A longer name fails the insert with the
        // database's own English message and leaves the whole layout unsaved - after the placement has
        // already replaced nothing and reported success.
        var tooLong = _venue.Stored.Elements.Where(e => (e.Name ?? "").Length > 50).Select(e => e.Name).ToList();

        Assert.True(tooLong.Count == 0, "nom(s) de plus de 50 caractères : " + string.Join(" | ", tooLong));
    }

    // ── The round trip ───────────────────────────────────────────

    [Fact]
    public void Every_attachment_point_carries_what_the_optimiser_put_there()
    {
        // The whole point. The optimiser sized each bay for a load it computed itself; the calculation
        // works the same loads out again from the stored rows alone. Ten grams apart is the rounding of a
        // lever arm - anything more means the two disagree about where the load goes.
        var disagreement = _venue.Stored.PointLoadDisagreement();

        Assert.True(disagreement is null, disagreement);
    }

    [Fact]
    public void Nothing_is_left_out_of_the_transmission()
    {
        // A reserve is the calculation saying it could not transmit something: a truss resting on no
        // support, a charge on a palonnier hooked to nothing, a point of the roof it could not name. Any
        // of those means an attachment point reads lower than the truth. Masses are not counted here -
        // the trusses' own weight is deliberately not transmitted in this comparison, because the
        // optimiser's grid loads are the scenic loads alone.
        var reserves = _venue.Stored.Transmission.Account.Describe(includeUnknownMass: false)
            .Where(r => !r.Contains("Masse"))
            .ToList();

        Assert.True(reserves.Count == 0, "le calcul a laissé des réserves : " + string.Join(" | ", reserves));
    }

    [Fact]
    public void Every_palonnier_hangs_exactly_where_the_optimiser_hung_it()
    {
        // A palonnier hangs from every mother line it crosses, save where the optimiser refused because a
        // bay would have gone past its value - and it recorded that refusal on the row. The calculation
        // follows those records. One crossing hooked that the optimiser left out puts several hundred
        // kilos on a bay that was never sized for them.
        var disagreement = _venue.Stored.HangingDisagreement();

        Assert.True(disagreement is null, disagreement);
    }
}

/// <summary>
/// One optimiser result as the database would hold it, and what the load calculation then finds on it.
///
/// Built with the same two pure functions the placement uses (Pipeline.BuildElementEntities and
/// Pipeline.BuildChargeMps) and read back by the same helper Calcul Charpente uses. Nothing is persisted:
/// the rows only ever exist in memory, with the navigation property each ChargeMp would have come back
/// from a query with. Shared with the stress tests, which do this round trip on a hundred random plans.
/// </summary>
public sealed class StoredLayout
{
    /// <summary>E5 in absolute charpente metres - the venue's Point0. Every stored coordinate is relative to it.</summary>
    public const double Point0X = 33.103;

    /// <summary>Ten grams: below that, a difference is the rounding of a lever arm, not a disagreement.</summary>
    private const double AgreementKg = 0.01;

    private readonly RiggingResult _result;

    public StoredLayout(RiggingResult result)
    {
        _result = result;

        // One element type per truss model, as the scenario's own types would be.
        var types = result.Palonniers.Select(q => q.Bar.Type)
            .Concat(result.Mothers.Select(m => m.Bar.Type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, t => new ElementType { ElementTypeId = Guid.NewGuid(), Name = t },
                StringComparer.OrdinalIgnoreCase);

        var (palonniers, mothers) = Pipeline.BuildElementEntities(result, Guid.NewGuid(), types);
        Palonniers = palonniers;
        Mothers = mothers;
        Elements = palonniers.Concat(mothers).ToList();

        Charges = Pipeline.BuildChargeMps(result, palonniers, mothers);
        foreach (var charge in Charges)
        {
            charge.MotherGridPalonnier =
                Elements.First(e => e.MotherGridPalonnierId == charge.MotherGridPalonnierId);
        }

        // No TrussMassOf: the trusses' own weight is not transmitted, because the optimiser's grid loads
        // are the scenic loads alone. Setting one here would compare two different quantities.
        Transmission = new LoadTransmissionHelper { Point0X = Point0X, Point0Y = 0 };
        var (_, atThePoints) = Transmission.CalculateCompleteLoadTransmissionWithDetails(Charges, Elements);
        PointLoads = atThePoints;
    }

    public List<MotherGridPalonnier> Palonniers { get; }

    public List<MotherGridPalonnier> Mothers { get; }

    /// <summary>Palonniers and mother lines together, as a query over the scenario would return them.</summary>
    public List<MotherGridPalonnier> Elements { get; }

    public List<ChargeMp> Charges { get; }

    public LoadTransmissionHelper Transmission { get; }

    /// <summary>What each attachment point of the roof carries, by its name ("E5", "H3"), as the calculation finds it.</summary>
    public Dictionary<string, double> PointLoads { get; }

    /// <summary>
    /// The attachment point where the calculation and the optimiser disagree most, said in French; null
    /// when they agree everywhere. A point one of them knows nothing about counts as zero on that side,
    /// so a load that reaches the roof by a path only one of them drew is caught too.
    /// </summary>
    public string? PointLoadDisagreement()
    {
        string? worstAt = null;
        double worst = AgreementKg;

        var names = PointLoads.Keys
            .Concat(_result.GridLoads.Keys.Select(k => $"{k.Col}{k.Row}"))
            .Distinct();

        foreach (var name in names)
        {
            double placed = _result.GridLoads
                .Where(kv => $"{kv.Key.Col}{kv.Key.Row}" == name)
                .Sum(kv => kv.Value);
            double calculated = PointLoads.GetValueOrDefault(name);

            double gap = Math.Abs(placed - calculated);
            if (gap > worst)
            {
                worst = gap;
                worstAt = $"{name} : {placed:0.###} kg posés par l'optimiseur, {calculated:0.###} kg trouvés par le calcul";
            }
        }

        return worstAt;
    }

    /// <summary>
    /// The first palonnier the calculation does not hang from the same mother lines as the optimiser,
    /// said in French; null when every one of them agrees.
    /// </summary>
    public string? HangingDisagreement()
    {
        for (int i = 0; i < Palonniers.Count; i++)
        {
            var calculated = PalonnierHanging.Resolve(Palonniers[i], Mothers).Supports
                .Select(s => s.Mother.MotherGridPalonnierId)
                .ToHashSet();
            var placed = _result.Palonniers[i].Supports
                .Select(s => Mothers[_result.Mothers.IndexOf(s.Mother)].MotherGridPalonnierId)
                .ToHashSet();

            if (!calculated.SetEquals(placed))
            {
                return $"{Palonniers[i].Name} : accroché à {placed.Count} ligne(s) par l'optimiseur, "
                       + $"à {calculated.Count} par le calcul";
            }
        }

        return null;
    }
}
