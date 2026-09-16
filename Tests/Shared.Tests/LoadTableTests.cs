using AliantWebS.Models;
using AliantWebS.Reporting;
using RiggingOptimizer.IO;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// What a truss may carry over one span - the owner's rule, and the one every verified study rests on:
/// the loads between two neighbouring hanging points, wherever they sit between them, may add up to the
/// "Charge ponctuelle au milieu" of the same model read at the length immediately above the span. Past the
/// model's longest row the value is computed at equal moment, and past twice that row there is no value at
/// all. Read from the venue's own Palonnier.xlsx and MotherGrill.xlsx, so these are the figures the studies
/// quote; the elements are built here, since what is being checked is the rule, not a stored layout.
/// </summary>
public class LoadTableTests
{
    private static readonly Guid PalonnierType = Guid.NewGuid();
    private static readonly Guid MotherType = Guid.NewGuid();
    private static readonly Guid RetiredType = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> TypeNames = new()
    {
        [PalonnierType] = "S500 Carrée Alu",
        [MotherType] = "C500F / TABMA",
        [RetiredType] = "Palonnier_STACCO_Alu",
    };

    private static Inventory Venue() => InventoryFiles.Load(Fixtures.Dir, null);

    // ── The length a span is read at ─────────────────────────────

    [Fact]
    public void A_span_is_read_at_the_length_immediately_above_it()
    {
        // S500 Carrée Alu is listed at 2,8 m (2 345 kg) and at 3 m (2 000 kg). Reading the longer row is
        // the conservative way round: a longer truss of the same model is allowed less.
        var rows = Venue().PalonnierAbaques["S500 Carrée Alu"];

        Assert.Equal(2345, ReportDataBuilder.LookupAbaque(rows, 2.5)!.Capacity, 6);
        Assert.Equal(2345, ReportDataBuilder.LookupAbaque(rows, 2.8)!.Capacity, 6);
        Assert.Equal(2000, ReportDataBuilder.LookupAbaque(rows, 2.9)!.Capacity, 6);

        // Shorter than every row on file: the shortest row stands, rather than no value at all.
        Assert.Equal(2.8, ReportDataBuilder.LookupAbaque(rows, 1.0)!.Length, 6);
    }

    [Fact]
    public void A_span_past_the_longest_row_reads_no_row_at_all()
    {
        var rows = Venue().PalonnierAbaques["S500 Carrée Alu"];

        // Never the 3 m row: reading the longest row for a longer span would allow more than the truss
        // carries, which is the one mistake this rule exists to prevent.
        Assert.True(ReportDataBuilder.LookupAbaque(rows, 3.5) is null,
            "une portée de 3,5 m dépasse la plus grande longueur du modèle : aucune ligne ne doit être lue");
    }

    [Fact]
    public void A_model_never_borrows_another_model_s_rows()
    {
        // ALC500 at 2,5 m reads its own 3 m row, 5 793 kg - not the 2 345 kg of the model next to it in
        // the same workbook.
        var inv = Venue();

        Assert.Equal(5793, ReportDataBuilder.LookupAbaque(inv.PalonnierAbaques["ALC500"], 2.5)!.Capacity, 6);
    }

    // ── A palonnier, span by span ────────────────────────────────

    [Fact]
    public void A_palonnier_is_checked_between_its_hanging_points_span_by_span()
    {
        // A 3 m S500 Carrée Alu hung at 0, 1,2 and 3 m: its spans are 1,2 m and 1,8 m, and both read the
        // 2,8 m row. 2 400 kg sit in the first, 2 000 kg in the second, and 5 000 kg hang on the middle
        // hanging point, which bends neither span because the load goes straight into the point.
        var inv = Venue();
        var pal = NewElement(PalonnierType, "S500 Carrée Alu L=3,0m #1", mother: false, 0, 0, 0, 3);
        var loads = new List<ChargeMp>
        {
            NewLoad(pal, 0.5, 1500), NewLoad(pal, 1.0, 900),
            NewLoad(pal, 1.2, 5000),
            NewLoad(pal, 2.0, 2000),
        };
        var hungAtThree = new Dictionary<Guid, IReadOnlyList<double>>
        {
            [pal.MotherGridPalonnierId] = new[] { 0.0, 1.2, 3.0 },
        };

        var checks = ReportDataBuilder.BuildAbaqueChecks(
                new List<MotherGridPalonnier> { pal }, TypeNames, loads, inv.PalonnierAbaques,
                new List<string>(), hungAtThree)
            .Where(c => c.Case == AbaqueLoadCase.Centered)
            .ToList();

        var governing = checks.FirstOrDefault(c => c.IsGoverning);
        Assert.True(governing is not null, "aucune travée n'a été retenue comme cas dimensionnant");
        Assert.True(governing!.AppliedKg == 2400 && governing.AllowedKg == 2345,
            $"cas dimensionnant : {governing.AppliedKg} kg contre {governing.AllowedKg} kg admissibles");
        Assert.Equal(1.2, governing.SpanM, 6);
        Assert.Equal(2.8, governing.AllowedSpanM!.Value, 6);
        Assert.False(governing.Ok, "2 400 kg sur 1,2 m dépassent les 2 345 kg lus à 2,8 m");

        // The 5 000 kg on the hanging point are in neither span.
        Assert.True(checks.All(c => c.AppliedKg is 2400 or 2000),
            "charges par travée : " + string.Join(", ", checks.Select(c => c.AppliedKg)));

        // The second span is within its limit, so it is not reported: the study shows the case that
        // governs and the cases that fail, not every span of the rig.
        Assert.True(checks.Count == 1, $"{checks.Count} travées rapportées au lieu d'une seule");

        // Reported under the model's own name, which is how the crew finds the truss on the plan.
        Assert.Equal("S500 Carrée Alu", governing.Family);
    }

    [Fact]
    public void A_palonnier_hung_at_its_ends_only_carries_everything_over_one_span()
    {
        // The same palonnier with no hanging points recorded: one 3 m span, which reads the 3 m row, and
        // every load on it counts - including the one that sat on a hanging point when there were any.
        var inv = Venue();
        var pal = NewElement(PalonnierType, "S500 Carrée Alu L=3,0m #1", mother: false, 0, 0, 0, 3);
        var loads = new List<ChargeMp>
        {
            NewLoad(pal, 0.5, 1500), NewLoad(pal, 1.0, 900), NewLoad(pal, 1.2, 5000), NewLoad(pal, 2.0, 2000),
        };

        var check = ReportDataBuilder.BuildAbaqueChecks(
                new List<MotherGridPalonnier> { pal }, TypeNames, loads, inv.PalonnierAbaques, new List<string>())
            .Single(c => c.Case == AbaqueLoadCase.Centered);

        Assert.True(check.AppliedKg == 9400 && check.AllowedKg == 2000,
            $"suspendu à ses deux extrémités : {check.AppliedKg} kg contre {check.AllowedKg} kg admissibles");
        Assert.Equal(3.0, check.SpanM, 6);
    }

    // ── Past the longest row on file ─────────────────────────────

    [Fact]
    public void Past_the_longest_row_the_value_is_computed_at_equal_moment()
    {
        // A 3,5 m palonnier drawn by hand, of a model whose longest row is 3 m (2 000 kg, 35 kg). Within
        // twice that row, so the value is computed rather than refused: a centred load P bends a truss by
        // P·L/4 and its own weight p per metre by p·L²/8, and the 3,5 m value is the P of equal moment.
        var inv = Venue();
        var drawn = NewElement(PalonnierType, "dessiné", mother: false, 0, 0, 0, 3.5);
        var warnings = new List<string>();

        var check = ReportDataBuilder.BuildAbaqueChecks(
                new List<MotherGridPalonnier> { drawn }, TypeNames,
                new List<ChargeMp> { NewLoad(drawn, 1.75, 500) }, inv.PalonnierAbaques, warnings)
            .Single(c => c.Case == AbaqueLoadCase.Centered);

        double perM = 35.0 / 3.0;
        double moment = 2000 * 3.0 / 4 + perM * 3.0 * 3.0 / 8;
        double expected = 4 * (moment - perM * 3.5 * 3.5 / 8) / 3.5;   // 1 709 kg

        Assert.True(check.AllowedKg is not null && Math.Abs(check.AllowedKg.Value - expected) <= 1,
            $"valeur calculée {check.AllowedKg} kg au lieu de {expected:0.#} kg");

        // The study has to say the figure was computed and from which row, or a reader cannot tell it from
        // one the manufacturer published.
        Assert.True(check.IsComputed, $"la valeur doit être annoncée comme calculée (source : {check.Source})");
        Assert.False(check.BeyondAbaque, "3,5 m reste dans le domaine où une valeur se calcule");
        Assert.Equal(3.0, check.BaseLengthM!.Value, 6);
        Assert.Equal(2000, check.BaseKg!.Value, 6);
        Assert.Equal(perM, check.KgPerM!.Value, 6);

        // A computed value is not a gap: it needs no warning.
        Assert.True(!warnings.Any(w => w.Contains("dessiné")), string.Join(" | ", warnings));
    }

    [Fact]
    public void Past_twice_the_longest_row_there_is_no_value_at_all()
    {
        // 6,5 m on a model that stops at 3 m. At such spans deflection governs and the formula would allow
        // more than the truss carries, so nothing is computed: the span is left unverified and named.
        var inv = Venue();
        var tooLong = NewElement(PalonnierType, "trop long", mother: false, 0, 0, 0, 6.5);
        var warnings = new List<string>();

        var check = ReportDataBuilder.BuildAbaqueChecks(
                new List<MotherGridPalonnier> { tooLong }, TypeNames,
                new List<ChargeMp> { NewLoad(tooLong, 3.25, 500) }, inv.PalonnierAbaques, warnings)
            .Single(c => c.Case == AbaqueLoadCase.Centered);

        Assert.True(check.AllowedKg is null, $"aucune valeur ne doit être donnée, {check.AllowedKg} kg lus");
        Assert.True(check.BeyondAbaque, "le modèle est à l'inventaire, mais la portée dépasse ce qu'il couvre");
        Assert.False(check.IsComputed, "rien ne se calcule au-delà de deux fois la plus grande longueur");

        // Never passed in silence: a span nobody can verify is a gap the study has to print.
        Assert.True(warnings.Any(w => w.Contains("trop long") && w.Contains("6,5")),
            "la travée non vérifiée doit être nommée : " + string.Join(" | ", warnings));
    }

    [Fact]
    public void A_model_the_inventory_no_longer_lists_is_not_verified()
    {
        // An element placed from an older inventory: its model is nowhere in Palonnier.xlsx, so it has no
        // value - which is different from having one and exceeding it, and the warning says which file to
        // look in.
        var inv = Venue();
        var old = NewElement(RetiredType, "Palonnier_STACCO_Alu L=8,4m #3", mother: false, 0, 0, 0, 7.4);
        var warnings = new List<string>();

        var check = ReportDataBuilder.BuildAbaqueChecks(
                new List<MotherGridPalonnier> { old }, TypeNames,
                new List<ChargeMp> { NewLoad(old, 3, 800) }, inv.PalonnierAbaques, warnings)
            .Single(c => c.Case == AbaqueLoadCase.Centered);

        Assert.True(check.AllowedKg is null, $"un modèle absent de l'inventaire n'a pas de valeur, {check.AllowedKg} kg lus");
        Assert.False(check.BeyondAbaque, "le modèle manque, ce n'est pas la portée qui dépasse l'inventaire");
        Assert.True(warnings.Any(w => w.Contains($"« Palonnier_STACCO_Alu » ne figure pas dans {InventoryFiles.PalonniersFileName}")),
            "l'avertissement doit nommer le fichier : " + string.Join(" | ", warnings));
    }

    // ── A mother line, bay by bay ────────────────────────────────

    [Fact]
    public void A_mother_is_checked_against_the_mothers_workbook()
    {
        // 5 600 kg carried over 5,2 m by a C500F / TABMA: read at 5,6 m, the length immediately above the
        // bay, which allows 5 430 kg.
        var inv = Venue();
        var mother = NewElement(MotherType, "C500F / TABMA L=5,6m #1", mother: true, 0, 0, 5.2, 0);
        var carried = new Dictionary<Guid, List<(double X, double Y, double Load)>>
        {
            [mother.MotherGridPalonnierId] = new() { (1.0, 0, 3000), (3.0, 0, 2600) },
        };

        var check = ReportDataBuilder.BuildMotherAbaqueChecks(
                new List<MotherGridPalonnier> { mother }, TypeNames, carried, inv.MotherAbaques, new List<string>())
            .Single(c => c.Case == AbaqueLoadCase.Centered);

        Assert.True(check.AppliedKg == 5600 && check.AllowedKg == 5430,
            $"{check.AppliedKg} kg contre {check.AllowedKg} kg admissibles");
        Assert.Equal(5.6, check.AllowedSpanM!.Value, 6);
        Assert.False(check.Ok, "5 600 kg dépassent les 5 430 kg lus à 5,6 m");
    }

    [Fact]
    public void A_mother_is_never_read_from_the_palonniers_workbook()
    {
        // The same mother read against Palonnier.xlsx: its model is not there, so there is no value. A
        // palonnier's value borrowed for a mother line would be read over a bay several times as long.
        var inv = Venue();
        var mother = NewElement(MotherType, "C500F / TABMA L=5,6m #1", mother: true, 0, 0, 5.2, 0);
        var carried = new Dictionary<Guid, List<(double X, double Y, double Load)>>
        {
            [mother.MotherGridPalonnierId] = new() { (1.0, 0, 3000), (3.0, 0, 2600) },
        };

        var check = ReportDataBuilder.BuildMotherAbaqueChecks(
                new List<MotherGridPalonnier> { mother }, TypeNames, carried, inv.PalonnierAbaques, new List<string>())
            .Single(c => c.Case == AbaqueLoadCase.Centered);

        Assert.True(check.AllowedKg is null,
            $"aucune valeur ne doit venir du fichier des palonniers, {check.AllowedKg} kg lus");
    }

    private static MotherGridPalonnier NewElement(Guid type, string name, bool mother,
        double x1, double y1, double x2, double y2) => new()
    {
        MotherGridPalonnierId = Guid.NewGuid(),
        ElementTypeId = type,
        Name = name,
        Famille = mother,
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
    };

    private static ChargeMp NewLoad(MotherGridPalonnier on, double x, double kg) => new()
    {
        ChargeMpid = Guid.NewGuid(),
        MotherGridPalonnierId = on.MotherGridPalonnierId,
        Name = $"C{x}",
        X = x,
        Charge = kg,
    };
}
