// =============================================================
// ReportDataBuilder.cs — Fills a RigReport from the database and the scenario's spreadsheets.
//
// Reads only. Every value in the finished document originates here, so anything this class cannot
// source becomes a warning rather than a plausible-looking number: a structural study that invents a
// capacity is worse than one that says the capacity is unknown.
//
// What a truss may carry comes from the inventory through RiggingOptimizer.IO.LoadTable, the one reading
// of the rule the optimiser sizes with, so the study checks what was placed against the values it was
// placed with. Coordinates leave this class in the studies' axes (Helpers.StudyAxes); every calculation
// in it stays in the grid's, as the database does.
// =============================================================

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;

namespace AliantWebS.Reporting;

public sealed class ReportDataBuilder
{
    /// <summary>Beam labels left to right, as the venue numbers them and the reports print them.</summary>
    public static readonly string[] Beams = { "J", "I", "H", "G", "F", "E", "D", "C", "B", "A" };

    /// <summary>Attachment points along each beam.</summary>
    public static readonly int[] Points = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

    /// <summary>
    /// Stands in for an element whose ElementType row has no name. Such an element still has mass and
    /// still feeds attachment points, so it must stay in the bill of materials: dropping it hid it
    /// from Tableau 2, from MassIncomplete and from the load reconciliation in the same breath, and
    /// the three together were the only things that could have noticed its weight was missing.
    /// </summary>
    public const string UntypedFamily = "(type inconnu)";

    /// <summary>The ElementType name of an element, or <see cref="UntypedFamily"/>. One definition,
    /// so the inventory, the drawings, the mass lookup and the checks always agree on what an element is.</summary>
    public static string FamilyOf(IReadOnlyDictionary<Guid, string> typeNames, MotherGridPalonnier element) =>
        typeNames.TryGetValue(element.ElementTypeId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : UntypedFamily;

    /// <summary>Row of the TCharpente limit table that holds each beam's own capacity.</summary>
    private const int BeamCapacityRow = 10;

    private readonly EntitiesService _entities;
    private readonly ScenarioStorageService _storage;

    public ReportDataBuilder(EntitiesService entities, ScenarioStorageService storage)
    {
        _entities = entities;
        _storage = storage;
    }

    public async Task<RigReport> BuildAsync(Event ev, EventMpscenario scenario, ReportContext context)
    {
        var warnings = new List<string>();

        // A gap is a check the study could not carry out. It is not a pass, and it must stop the
        // document concluding that the plan complies. Declared here because the plan of charges,
        // read first, can already be a check that cannot be made: an empty one.
        var gaps = new List<string>();

        var paths = _storage.GetScenarioPaths(ev, scenario);

        // Element and load coordinates are stored relative to the event's Point0, the charpente points
        // absolutely. The origin the load transmission adds, and the one the bay checks below use too.
        double point0X = ev.Point0X ?? 0, point0Y = ev.Point0Y ?? 0;

        // ── Production loads ─────────────────────────────────────
        // Read fresh, through a context of its own: the Charges Scéniques page saves its rows that way, so the circuit's
        // shared context would hand back the values it read the first time - the study would print loads the page has
        // since changed, and the totals check could never clear.
        List<ChargesScenique> charges;
        await using (var db = _entities.CreateOwnedContext())
            charges = await db.ChargesSceniques.AsNoTracking()
                .Where(c => c.EventMpscenarioId == scenario.EventMpscenarioId).ToListAsync();

        // In the studies' axes, as a person reads and types them - X along the beams towards point 9, Y
        // across them towards J - for Tableau 1 and the figures. The stored X and Y are the grid's.
        var loads = charges
            // As a person numbers them - T1, T2 ... T10 - not as text, which gives T1, T10, T11, T2.
            .OrderBy(c => c.Name, NaturalStringComparer.Instance)
            .Select(c => new ProductionLoad(
                c.Name ?? "",
                c.StudyX ?? 0,
                c.StudyY ?? 0,
                c.Charge ?? 0,
                Located: c.X is not null && c.Y is not null,
                // A charge nobody has weighed, told apart from one weighed at 0 daN: its 0 above is a
                // stand-in, not a weight, and everything downstream must say so rather than print it.
                Weighed: c.Charge is not null))
            .ToList();

        // An empty plan is not a plan that passes: every other check below is inert on it - no beam is
        // over its capacity, the printed and the placed totals are both zero - so a warning alone would
        // let the study declare a production nobody entered compliant. It is a gap as well.
        if (loads.Count == 0)
        {
            warnings.Add("Aucune charge scénique n'est enregistrée pour ce scénario.");
            gaps.Add("Aucune charge scénique n'est enregistrée pour ce scénario : l'étude ne porte sur "
                   + "aucune charge et ne vérifie donc rien. Saisissez les charges de la production sur "
                   + "la page Charges, puis relancez le placement (Générer l'implantation) et le calcul "
                   + "charpente.");
        }

        // How a reserve or a gap names a charge of the plan.
        static string Named(ProductionLoad load) =>
            string.IsNullOrWhiteSpace(load.Name) ? "(charge sans nom)" : load.Name.Trim();

        // A load with no position cannot be drawn. Drawn at the origin it would read as a load on E5.
        var unlocated = loads
            .Where(l => !l.Located)
            .Select(Named)
            .ToList();
        if (unlocated.Count > 0)
            warnings.Add($"{unlocated.Count} charge(s) du plan n'ont pas de coordonnées et ne sont pas dessinées "
                       + "sur les figures : " + string.Join(", ", unlocated) + ".");

        // A load with no weight is carried nowhere: Tableau 1 prints an em dash for it and the totals
        // pass over it. What it actually hangs on the charpente is therefore unknown, and an unknown
        // load is a check the study could not make, not a load of 0 daN.
        var unweighed = loads
            .Where(l => !l.Weighed)
            .Select(Named)
            .ToList();
        if (unweighed.Count > 0)
            gaps.Add($"{unweighed.Count} charge(s) du plan n'ont pas de poids : " + string.Join(", ", unweighed)
                   + ". Elles ne sont comptées dans aucun total et ce qu'elles chargent n'a pas pu être "
                   + "vérifié. Renseignez leur poids sur la page Charges, puis relancez le placement "
                   + "(Générer l'implantation) et le calcul charpente.");

        // ── Venue load book ──────────────────────────────────────
        var venue = await BuildVenueAsync(warnings);

        // ── Applied loads per attachment point ───────────────────
        // Read fresh, as the charges above: the calculation and the Charpente page save it through contexts of their own.
        Charpente? charpente;
        await using (var db = _entities.CreateOwnedContext())
            charpente = await db.Charpentes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.EventMpscenarioId == scenario.EventMpscenarioId);

        if (charpente is null)
        {
            warnings.Add("Les charges aux points d'accroche n'ont pas encore été calculées. "
                       + "Lancez « Calcul Charpente » avant d'éditer le rapport.");
            gaps.Add("Les charges aux points d'accroche n'ont pas été calculées.");
        }

        var beamLoads = BuildBeamLoads(venue, charpente, gaps);

        // ── Pre-rig composition ──────────────────────────────────
        var elements = await _entities.GetFilteredEntitiesAsync<MotherGridPalonnier>(
            m => m.EventMpscenarioId == scenario.EventMpscenarioId);

        var elementTypes = await _entities.GetAllEntitiesAsync<ElementType>();
        var typeNames = elementTypes.ToDictionary(t => t.ElementTypeId, t => t.Name ?? "");

        // The inventory - MotherGrill.xlsx and Palonnier.xlsx, the scenario's copies first - gives every
        // truss piece its mass and stock, and every model its charge ponctuelle au milieu, so the masses
        // and the checks below come from the same rows. See RiggingOptimizer.IO.InventoryFiles.
        var inventory = InventoryFiles.Load(paths.DataDir, _storage.CommonDataDir);
        warnings.AddRange(inventory.Warnings);
        foreach (var file in inventory.MissingFiles)
            warnings.Add($"Inventaire introuvable : {file}. Les masses et les vérifications des ponts qu'il "
                       + "décrit ne peuvent pas être faites.");
        var catalog = inventory.Catalog;

        var trusses = BuildTrussInventory(elements, typeNames, catalog, warnings);

        // A row the inventory could not read is left out, and its model then reads another row - the next
        // longer one, or a value computed past what is only the longest READABLE row. The warning says so;
        // for a model this scenario uses, it is also a check the study could not make as the inventory states it.
        gaps.AddRange(SkippedRowGaps(inventory.Skipped, elements, typeNames));

        // ── Allowed loads ────────────────────────────────────────
        var chargeMps = await _entities.GetFilteredEntitiesAsync<ChargeMp>(
            c => c.MotherGridPalonnier.EventMpscenarioId == scenario.EventMpscenarioId,
            "MotherGridPalonnier");

        // Where each palonnier hangs: from every mother it crosses, save where the optimiser or a person
        // recorded otherwise. The same reading Calcul Charpente makes (PalonnierHanging.Resolve), so the
        // spans checked here are the spans the loads were transmitted over.
        var motherElements = elements.Where(e => e.Famille).ToList();
        var palonnierElements = elements.Where(e => !e.Famille).ToList();
        var hanging = palonnierElements.ToDictionary(
            p => p.MotherGridPalonnierId,
            p => global::Helpers.PalonnierHanging.Resolve(p, motherElements));

        var checks = BuildAbaqueChecks(elements, typeNames, chargeMps, inventory.PalonnierTable, warnings,
            hanging.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<double>)kv.Value.Supports.Select(s => s.S).ToList()),
            point0Y: point0Y,
            skippedRows: inventory.Skipped);

        // The crossings a palonnier is deliberately not hung from, and who decided. The study says so:
        // every load on the attachment points follows from where the palonniers hang.
        var unhooked = palonnierElements
            .OrderBy(p => p.Name, NaturalStringComparer.Instance)
            .SelectMany(p => hanging[p.MotherGridPalonnierId].Crossings
                .Where(c => !c.Hooked)
                .Select(c => new UnhookedCrossing(
                    p.Name ?? "",
                    string.Join(" / ", c.Candidates.Select(k => k.Mother.Name ?? "")),
                    c.Origin == global::Helpers.HookOrigin.Manual,
                    c.Reason)))
            .ToList();

        // A mother carries what the palonniers hung on it deliver, and anything placed on it
        // directly. That is only known by running the load transmission, so it is run here - the same
        // helper, the same hanging points and the same truss masses as Calcul Charpente - rather than
        // keeping a second copy of the rule. Its attachment-point table is kept too: compared point by
        // point with the stored one further down, it shows whether the stored table still describes
        // the plan.
        var transmission = new global::Helpers.LoadTransmissionHelper
        {
            Point0X = point0X,
            Point0Y = point0Y,
            TrussMassOf = MassSource(catalog, typeNames),
        };
        var (_, freshPointLoads) = transmission.CalculateCompleteLoadTransmissionWithDetails(chargeMps, elements);

        checks.AddRange(BuildMotherAbaqueChecks(elements, typeNames, transmission.PointLoadsOnMothers,
            inventory.MotherTable, warnings, point0X, point0Y, skippedRows: inventory.Skipped));
        checks = checks
            .OrderBy(c => c.Role)
            .ThenBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Case)
            .ToList();

        // A span no value covers - its model is not in the inventory, it is too long even for a computed
        // value, or the weight a computed value needs is missing - was not verified, and neither was a
        // cantilever or an uplift, which no inventory value covers.
        foreach (var unchecked_ in checks.Where(c => c.AllowedKg is null))
        {
            var whose = $"{unchecked_.Role.Capitalised()} {unchecked_.Family}"
                      + (string.IsNullOrWhiteSpace(unchecked_.WorstElement) ? "" : $" « {unchecked_.WorstElement!.Trim()} »");
            var kg = Math.Round(unchecked_.AppliedKg);
            var metres = unchecked_.SpanM.ToString("0.##", Fr);
            gaps.Add(unchecked_.Case switch
            {
                AbaqueLoadCase.Uplift =>
                    $"{whose} : soulevé de {kg} daN par un palonnier en porte-à-faux, cas que l'inventaire ne couvre pas.",
                AbaqueLoadCase.Cantilever =>
                    $"{whose} : {kg} daN en déport, jusqu'à {metres} m au-delà de son dernier appui"
                    + (unchecked_.SpanLabel is { } past ? $" ({past})" : "")
                    + ", et l'inventaire ne donne aucune charge admissible en déport.",
                _ =>
                    whose
                    + (unchecked_.SpanLabel is { } bay ? $", travée {bay}" : "")
                    + $" : {kg} daN sur {metres} m, charge non vérifiée : "
                    + (unchecked_.NoValueReason ?? "aucune charge admissible n'est disponible") + ".",
            });
        }

        // Everything the transmission had to infer, or could not do - a truss on one support, a
        // hanging point read from the plan, a mother off the grid, a load with nowhere to go - means
        // the loads rest on a layout the software guessed. Calcul Charpente shows these in an alert
        // after it has saved; here they stop the study concluding. Missing masses have their own gap.
        foreach (var reserve in transmission.Account.Describe(includeUnknownMass: false))
            gaps.Add("Transmission des charges : " + reserve);

        // The inventory states what may be HUNG on a truss: the existing studies compare an applied
        // point load against it ("une charge ponctuelle centrée de 2286 daN est autorisée"), and the
        // alu tables allow less at long spans than those trusses weigh, which only makes sense for a
        // payload figure. So the truss's own weight is deliberately absent from this comparison even
        // though it is now present in the attachment-point loads. Said out loud, because the two
        // numbers now come from different conventions on the same page.
        // ── Does the stored table still describe this scenario? ──
        // The hand-written studies transmit the trusses as well as the production: the attachment
        // points of the Intemporels study carry 11 420 daN, which is exactly its 5 100 daN of motor
        // loads plus the 6 320 daN of pre rigg listed in its own composition table. So a table
        // computed by this app should total the loads placed on the palonniers plus the catalogued
        // truss masses. When it does not, it was computed before the trusses were transmitted, or
        // the geometry, the loads or the catalogue have moved since. Either way the stored figures
        // no longer describe this scenario, and a study must not conclude from them.
        //
        // This is a leak detector, not a proof: the total is invariant to WHERE a load landed.
        var placedTotal = chargeMps.Sum(c => c.Charge ?? 0);

        // The plan the study PRINTS (Tableau 1, from ChargesScenique) and the loads the calculation
        // TRANSMITTED (ChargeMp, placed on palonniers) are two separately editable tables. Adding a
        // load to the plan without re-running the optimiser leaves them apart, and every other check
        // here is blind to it because they all work from the transmitted set.
        var printedTotal = loads.Sum(l => l.WeightKg);
        if (Math.Abs(printedTotal - placedTotal) > Math.Max(1.0, 0.001 * Math.Max(printedTotal, placedTotal)))
        {
            gaps.Add($"Le plan de charges totalise {Math.Round(printedTotal)} daN alors que "
                   + $"{Math.Round(placedTotal)} daN sont posés sur les palonniers. Les charges "
                   + "imprimées ne sont pas celles qui ont été transmises à la charpente : "
                   + "relancez le placement puis le calcul charpente.");
        }

        // Equal totals are not the same loads in the same places. The stored layout is not rewritten when a
        // load is moved on the Charges page, and the one-time conversion of the plan to the grid's axes did
        // not convert the layouts computed before it: a charge can sit on other beams than the plan says,
        // and the totals and the point-by-point comparison below cannot see it, since both work from the
        // stored layout. So each placed charge is put back where its element says it is and compared with
        // the plan's charge of the same name.
        gaps.AddRange(LayoutMismatches(chargeMps, elements, charges));

        if (charpente is not null && !catalog.IsEmpty)
        {
            // Summed over the whole stored grid, NOT over beamLoads: BuildBeamLoads returns only the
            // beams it means to print, so a beam whose points sum to zero or to a net uplift is
            // absent from that list and would be missing from this total for ever.
            var storedTotal = 0.0;
            foreach (var beam in Beams)
                foreach (var point in Points)
                    storedTotal += ReadGridValue(charpente, beam, point) ?? 0;

            // The truss mass the transmission is given, element by element (MassSource): the same
            // kilograms the stored table was computed with. Tableau 2's total equals it when every piece
            // has a mass; when one does not, its element transmits nothing while its known pieces still
            // count in Tableau 2, and comparing against that total would flag a leak that is not one.
            var trussMass = trusses.TransmittedMassKg;
            var expectedTotal = placedTotal + trussMass;

            // A reference with no catalogued mass is already a gap of its own; here it only means
            // the expected total is itself a lower bound, so compare against that lower bound.
            var tolerance = Math.Max(1.0, 0.001 * Math.Abs(expectedTotal));

            if (Math.Abs(storedTotal - expectedTotal) > tolerance)
            {
                // Two causes, and the reader cannot tell them apart from here: the table predates a
                // change (to the loads, the geometry, the catalogue, or to self weight being
                // transmitted at all), or the last calculation could not transmit everything and
                // said so at the time. Re-running fixes the first and merely repeats the second.
                gaps.Add($"Le tableau des charges aux points d'accroche totalise "
                       + $"{Math.Round(storedTotal)} daN alors que les charges posées "
                       + $"({Math.Round(placedTotal)} daN) et la masse {(trusses.MassIncomplete ? "connue " : "")}du pré rigg "
                       + $"({Math.Round(trussMass)} daN) en attendent "
                       + $"{Math.Round(expectedTotal)} daN. Soit ce tableau ne correspond plus à ce "
                       + "scénario et il faut relancer le calcul charpente, soit une partie des "
                       + "charges n'a pas pu être transmise lors du dernier calcul — les réserves "
                       + "affichées à ce moment-là en donnent la raison.");
            }
        }

        // ── Does the stored table say what the calculation says now? ──
        // The transmission above ran the same code on the same rows as Calcul Charpente, so an
        // up-to-date stored table matches it to the gram. A point that differs means the stored table
        // came from another plan - a charge moved along its palonnier, an element or the catalogue
        // edited since - or was edited by hand on the Charpente page. The total check above cannot
        // see either: moving a load does not change the total.
        if (charpente is not null)
        {
            var stale = StaleCells(charpente, freshPointLoads);
            if (stale.Count > 0)
            {
                var worstCell = stale[0];
                gaps.Add($"Le tableau des charges aux points d'accroche ne correspond plus au calcul actuel "
                       + $"en {stale.Count} point(s), dont {worstCell.Point} : {Math.Round(worstCell.Stored)} daN "
                       + $"enregistrés pour {Math.Round(worstCell.Fresh)} daN calculés. Le plan ou le catalogue a "
                       + "été modifié depuis le dernier calcul charpente, ou le tableau a été modifié à la "
                       + "main : relancez le calcul charpente.");
            }
        }

        // Once the trusses are transmitted, a piece the catalogue does not price no longer costs only a
        // sentence about the pre rigg mass: the weight of the truss it belongs to is missing from every
        // attachment point it feeds, so every one of those reads lower than the truth. That is a check
        // the study failed to carry out, not a detail.
        if (trusses.MassIncomplete)
        {
            gaps.Add("La masse de certaines pièces de pont est inconnue (absentes de MotherGrill.xlsx et de "
                   + "Palonnier.xlsx, sans poids propre, ou d'une longueur qui n'est pas en stock) : le poids "
                   + "propre des ponts qui en comportent n'est transmis à aucun point d'accroche, et les charges "
                   + "du tableau sont donc sous-estimées.");
        }

        // A truss whose recorded pieces cannot be read, or do not reach the length it is drawn over, is
        // weighed as a truss that is not there: every attachment point it feeds carries a mass nobody placed.
        gaps.AddRange(PieceProblemGaps(trusses.PieceProblems));

        return new RigReport
        {
            Context = context,
            Venue = venue,
            Loads = loads,
            BeamLoads = beamLoads,
            Trusses = trusses,
            AbaqueChecks = checks,
            Warnings = warnings,
            AppliedLoadsKnown = charpente is not null,
            VerificationGaps = gaps,
            PalonnierCount = palonnierElements.Count,
            Unhooked = unhooked,
            MothersCarryDirectLoads = chargeMps.Any(c => (c.Charge ?? 0) != 0
                && motherElements.Any(m => m.MotherGridPalonnierId == c.MotherGridPalonnierId)),
        };
    }

    // ── Venue load book ──────────────────────────────────────────

    private async Task<VenueSpec> BuildVenueAsync(List<string> warnings)
    {
        // TCharpente is a single venue-wide row, not per scenario: these are the building's limits.
        var all = await _entities.GetAllEntitiesAsync<Tcharpente>();
        var limits = all?.FirstOrDefault();

        var pointCapacity = new Dictionary<string, double>();
        var beamCapacity = new Dictionary<string, double>();

        if (limits is null)
        {
            warnings.Add("Le cahier des charges de la salle (table TCharpente) est vide. "
                       + "Les capacités admissibles ne peuvent pas être reportées.");
            return new VenueSpec { Beams = Beams, Points = Points };
        }

        foreach (var beam in Beams)
        {
            foreach (var point in Points)
            {
                // A stored zero is meaningful: it says there is no usable attachment at that position,
                // and the existing reports print it as 0. Only a missing column counts as unknown.
                var v = ReadGridValue(limits, beam, point);
                if (v is { } value) pointCapacity[$"{beam}{point}"] = value;
            }

            var total = ReadGridValue(limits, beam, BeamCapacityRow);
            if (total is { } beamValue && beamValue > 0) beamCapacity[beam] = beamValue;
        }

        if (pointCapacity.Values.All(v => v <= 0))
        {
            warnings.Add("Aucune capacité de point d'accroche n'est renseignée dans le cahier des charges.");
            pointCapacity.Clear();
        }

        if (beamCapacity.Count == 0)
            warnings.Add($"Les capacités totales par poutre ne sont pas renseignées "
                       + $"(ligne {BeamCapacityRow} de la table TCharpente). "
                       + "Les colonnes « TOTAL POUTRE » seront vides.");

        return new VenueSpec
        {
            Beams = Beams,
            Points = Points,
            PointCapacity = pointCapacity,
            BeamCapacity = beamCapacity,
        };
    }

    /// <summary>
    /// Reads one cell of a Charpente-shaped entity. Both tables expose their grid as flat properties
    /// named A1..J10, so reflection is the only way in without listing ninety names by hand.
    /// </summary>
    private static double? ReadGridValue(object entity, string beam, int row)
    {
        var property = entity.GetType().GetProperty(
            $"{beam}{row}", BindingFlags.Public | BindingFlags.Instance);

        return property?.GetValue(entity) switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            _ => null,
        };
    }

    private static List<BeamLoadRow> BuildBeamLoads(
        VenueSpec venue, Charpente? charpente, List<string> gaps)
    {
        var rows = new List<BeamLoadRow>();
        var missingPoints = new List<string>();
        var missingBeams = new List<string>();

        foreach (var beam in Beams)
        {
            var allowed = new List<double?>();
            var applied = new List<double>();
            var overloaded = new List<int>();

            for (var i = 0; i < Points.Length; i++)
            {
                var point = Points[i];
                var cap = venue.CapacityAt(beam, point);
                var load = charpente is null ? 0 : ReadGridValue(charpente, beam, point) ?? 0;

                allowed.Add(cap);
                applied.Add(load);

                if (cap is { } c)
                {
                    if (load > c) overloaded.Add(i);
                }
                else if (load > 0.5)
                {
                    // Loaded, but the load book says nothing about this point. Comparing against
                    // nothing is not a pass.
                    missingPoints.Add($"{beam}{point}");
                }
            }

            var total = applied.Sum();
            var beamCap = venue.CapacityOf(beam);
            if (beamCap is null && total > 0.5) missingBeams.Add(beam);

            rows.Add(new BeamLoadRow
            {
                Beam = beam,
                Allowed = allowed,
                Applied = applied,
                AllowedTotal = beamCap,
                AppliedTotal = total,
                OverloadedPoints = overloaded,
            });
        }

        if (missingPoints.Count > 0)
        {
            gaps.Add($"Capacité inconnue pour {missingPoints.Count} point(s) chargé(s) : "
                   + string.Join(", ", missingPoints) + ".");
        }

        if (missingBeams.Count > 0)
        {
            // The beam total is the binding constraint, well below the sum of its points, so a missing
            // beam capacity leaves the governing check undone.
            gaps.Add($"Capacité totale inconnue pour la ou les poutres chargées : "
                   + string.Join(", ", missingBeams) + ".");
        }

        // The existing reports print only the beams the production actually loads. When the loads were
        // never computed, or nothing is loaded, every beam is kept so the table is never empty and the
        // reader can see the whole load book against which nothing was checked.
        var loaded = rows.Where(r => r.AppliedTotal > 0.5).ToList();
        return loaded.Count > 0 ? loaded : rows;
    }

    // ── Pre-rig composition ──────────────────────────────────────

    /// <summary>
    /// The pre-rig's bill of materials for Tableau 2, PIECE by piece: every element is expanded into the
    /// pieces it is assembled from (<see cref="PiecesOf"/>) before grouping, so that Tableau 2 counts what
    /// the crew takes off the truck, weighs what the transmission weighs - its total is the sum of
    /// <see cref="MassSource"/> - and can be held against the inventory's stock. Also lists the elements
    /// made of several pieces. Public because it is a pure function of its arguments and is worth checking
    /// directly against real element names.
    /// </summary>
    public static TrussInventory BuildTrussInventory(
        List<MotherGridPalonnier> elements,
        Dictionary<Guid, string> typeNames,
        TrussCatalog catalog,
        List<string> warnings)
    {
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<string>();
        var tooShort = new List<string>();
        var problems = new List<TrussPieceProblem>();
        var assembled = new List<AssembledTruss>();

        // One key per piece, and the keys that were read from an element's name rather than its
        // recorded pieces - an element placed before pieces were recorded.
        var pieces = new List<(string Type, long Cm)>();
        var readFromName = new HashSet<(string Type, long Cm)>();
        double transmitted = 0;

        foreach (var element in elements.OrderBy(e => e.Name, NaturalStringComparer.Instance))
        {
            var type = FamilyOf(typeNames, element);
            var read = ReadPieces(element);
            var name = NameOf(element);

            if (read.Unreadable) unreadable.Add(name);

            foreach (var length in read.Pieces)
            {
                var key = ReferenceKey(type, length);
                pieces.Add(key);
                if (!read.Recorded) readFromName.Add(key);
            }

            if (read.Pieces.Count > 1)
                assembled.Add(new AssembledTruss(name, type, global::Helpers.TrussComposition.Describe(read.Pieces)));

            // Exactly what MassSource hands the load transmission for this element.
            transmitted += MassOf(catalog, type, read.Pieces) ?? 0;

            // Pieces that do not reach the length the element is drawn over describe a truss that cannot
            // be there. The optimiser assembles to within Geometry.TOL of the span; beyond that, and a
            // little rounding, the record contradicts the plan.
            var drawn = LengthOf(element);
            var counted = read.Pieces.Sum();
            // Named outside the interpolation: a colon inside a hole starts its format string.
            var described = global::Helpers.TrussComposition.Describe(read.Pieces);

            // Unreadable pieces: counted as one piece of the length in its name, which nothing on record confirms.
            if (read.Unreadable)
                problems.Add(new TrussPieceProblem(name, type, described, counted, drawn, element.Composition?.Trim() ?? ""));

            if (counted + PieceReachToleranceM < drawn)
            {
                tooShort.Add($"« {name} » ({described} pour {drawn.ToString("0.##", Fr)} m)");

                // Once per element: an unreadable record is already there, and says what it is counted as.
                if (!read.Unreadable)
                    problems.Add(new TrussPieceProblem(name, type, described, counted, drawn, null));
            }
        }

        var lines = pieces
            // Keyed at centimetre resolution: the inventory lengths carry floating-point noise, so
            // grouping on the raw double would split one piece into several rows.
            .GroupBy(key => key)
            .Select(g =>
            {
                var lengthM = g.Key.Cm / 100.0;
                var spec = catalog.Find(g.Key.Type, lengthM);
                var label = spec?.Label ?? TrussCatalog.DefaultLabel(g.Key.Type, lengthM);

                // Three ways to have no mass: the piece is absent from the inventory, it is listed with a
                // blank Poids because nobody has sourced one, or its length is listed only for its load
                // value (Nombre = 0). All leave the study unable to state a pre-rig mass, so all are
                // reported the same way.
                if (spec?.UnitWeightKg is null)
                    missing.Add(label);

                return new TrussLine
                {
                    Label = label,
                    Family = g.Key.Type,
                    LengthM = lengthM,
                    Count = g.Count(),
                    UnitWeightKg = spec?.UnitWeightKg,
                    ColorHex = spec?.ColorHex,
                    Keys = { g.Key },
                };
            })
            // A mother and a palonnier can be the same truss drawn from the same stock: the load values
            // differ because the role differs, but the bill of materials has one line for it, which is
            // how the existing studies print it. The catalogue says so by giving both the same label;
            // mass and colour must agree too, so that a catalogue that contradicts itself shows both
            // rows instead of silently keeping one.
            .GroupBy(l => (l.Label, l.ColorHex, l.UnitWeightKg))
            .Select(g => g.Count() == 1 ? g.First() : new TrussLine
            {
                Label = g.Key.Label,
                Family = g.OrderBy(l => l.Family, StringComparer.OrdinalIgnoreCase).First().Family,
                LengthM = g.First().LengthM,
                Count = g.Sum(l => l.Count),
                UnitWeightKg = g.Key.UnitWeightKg,
                ColorHex = g.Key.ColorHex,
                Keys = g.SelectMany(l => l.Keys).ToList(),
            })
            .OrderBy(l => l.Family, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(l => l.LengthM)
            .ToList();

        // The optimiser never takes more pieces than the inventory holds (PieceStock), so a line over
        // its stock, or on a length with no stock at all, was placed by hand, imported, or placed from
        // another inventory than the one on file now.
        var overStock = new List<string>();
        var notInStock = new List<string>();
        var notInStockFromName = false;
        foreach (var line in lines)
        {
            var stock = line.Keys
                .Select(k => (Type: k.Type.Trim().ToLowerInvariant(), k.Cm))
                .Distinct()
                .Sum(k => catalog.StockOf(k.Type, k.Cm / 100.0));

            if (stock <= 0)
            {
                notInStock.Add($"{line.Label} ({line.Count} pièce(s))");
                if (line.Keys.Any(readFromName.Contains)) notInStockFromName = true;
            }
            else if (line.Count > stock)
            {
                overStock.Add($"{line.Label} : {line.Count} pièces pour {stock} en stock");
            }
        }

        if (overStock.Count > 0)
            warnings.Add("Le pré rigg utilise plus de pièces que l'inventaire n'en compte (colonne « Nombre ») : "
                       + string.Join(" ; ", overStock) + ".");

        if (notInStock.Count > 0)
            warnings.Add("Le pré rigg utilise des longueurs qui ne sont pas en stock dans l'inventaire (colonne "
                       + "« Nombre » nulle, ou longueur absente) : " + string.Join(", ", notInStock) + "."
                       + (notInStockFromName
                           ? " Un élément placé avant l'enregistrement de ses pièces est lu comme une seule pièce "
                             + "de la longueur portée dans son nom : relancez le placement pour l'assembler à "
                             + "partir du stock."
                           : ""));

        if (unreadable.Count > 0)
            warnings.Add($"Les pièces enregistrées de {unreadable.Count} élément(s) sont illisibles : "
                       + string.Join(", ", unreadable) + ". Chacun est compté comme une seule pièce de la "
                       + "longueur portée dans son nom.");

        if (tooShort.Count > 0)
            warnings.Add("Les pièces de certains ponts sont plus courtes que la longueur sur laquelle ils sont "
                       + "dessinés : " + string.Join(", ", tooShort) + ".");

        if (missing.Count > 0)
            warnings.Add($"Masse inconnue pour {missing.Count} pièce(s) de pont : "
                       + string.Join(", ", missing)
                       + ". Ajoutez-les à MotherGrill.xlsx ou à Palonnier.xlsx, ou complétez leur colonne "
                       + "« Poids propre » ; une ligne dont le « Nombre » est nul ne donne pas de masse.");

        return new TrussInventory
        {
            Lines = lines,
            Assembled = assembled,
            PieceProblems = problems,
            TransmittedMassKg = transmitted,
        };
    }

    /// <summary>
    /// How far a truss's pieces may fall short of the length it is drawn over: Geometry.TOL, within
    /// which the optimiser assembles to a span, and 2 cm of rounding.
    /// </summary>
    private const double PieceReachToleranceM = RiggingOptimizer.Core.Geometry.TOL + 0.02;

    /// <summary>
    /// The pieces an element is assembled from, metres, longest first: its recorded composition
    /// (MotherGridPalonnier.Composition) when there is a readable one, otherwise a single piece of the
    /// length read from its name - how the elements placed before pieces were recorded are counted.
    /// </summary>
    public static IReadOnlyList<double> PiecesOf(MotherGridPalonnier e) => ReadPieces(e).Pieces;

    /// <summary>
    /// <see cref="PiecesOf"/>, and how it was read: Recorded when from the stored composition, Unreadable
    /// when a composition is stored but cannot be read - reported once by BuildTrussInventory.
    /// </summary>
    private static (IReadOnlyList<double> Pieces, bool Recorded, bool Unreadable) ReadPieces(MotherGridPalonnier e)
    {
        var stored = global::Helpers.TrussComposition.Parse(e.Composition, out var readable);
        if (stored is { Count: > 0 })
            return (stored.OrderByDescending(l => l).ToList(), true, false);

        return (new[] { ReferenceLengthOf(e) }, false, !readable);
    }

    /// <summary>The distance the element spans, which is not the same as the truss it is made of.</summary>
    private static double LengthOf(MotherGridPalonnier e)
    {
        var dx = (e.X2 ?? 0) - (e.X1 ?? 0);
        var dy = (e.Y2 ?? 0) - (e.Y1 ?? 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>An element as a reserve line names it.</summary>
    private static string NameOf(MotherGridPalonnier e) =>
        string.IsNullOrWhiteSpace(e.Name) ? "(élément sans nom)" : e.Name!.Trim();

    // The optimiser names each placed element after the truss it assembled, e.g.
    // "S500 Carrée Acier L=8,4m #1" - its total length. Either decimal separator, since the name is
    // written with the server's culture.
    private static readonly Regex BarLengthInName =
        new(@"L\s*=\s*(\d+(?:[.,]\d+)?)\s*m", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Length of the truss actually installed, as opposed to the gap it bridges, for an element whose
    /// pieces were not recorded. An 8 m bar spanning 7,25 m between two beams is an 8 m bar: that is what
    /// the crew takes off the truck, what the inventory weighs, and what belongs in Tableau 2. Grouping on
    /// the span instead splits one reference into several rows, prints a length nobody can order, and
    /// never matches the inventory. Falls back to the span when the name carries no length.
    /// </summary>
    public static double ReferenceLengthOf(MotherGridPalonnier e)
    {
        var match = BarLengthInName.Match(e.Name ?? "");
        if (match.Success &&
            double.TryParse(
                match.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var length) &&
            length > 0)
        {
            return length;
        }

        return LengthOf(e);
    }

    /// <summary>Grouping key for a truss piece: its type and its length to the centimetre.</summary>
    public static (string Type, long Cm) ReferenceKey(string type, double lengthM) =>
        (type, (long)Math.Round(lengthM * 100));

    // ── Allowed loads ────────────────────────────────────────────

    /// <summary>
    /// The checks of the palonniers. A palonnier hangs from every mother it crosses, so it is a chain of
    /// spans, each carrying the loads between its two hanging points: that is how the load transmission
    /// shares them out, each load going to the two hanging points that bracket it. The loads between two
    /// neighbouring hanging points, wherever they sit between them, may add up to the charge ponctuelle au
    /// milieu of the model at the length immediately above the span, or past the model's longest row to
    /// the value computed at equal moment from it - LoadTable, the rule the optimiser sizes with. Loads
    /// beyond the outermost hanging point are a cantilever, which no value covers. Public because it is a
    /// pure function of its arguments and is worth checking directly.
    /// </summary>
    /// <param name="table">What a palonnier of each model may carry: Inventory.PalonnierTable.</param>
    /// <param name="hangingPoints">Where each palonnier hangs, as distances along it from (X1, Y1): the
    /// supports PalonnierHanging.Resolve gives. A palonnier missing from it, or with fewer than two, is
    /// taken as hung at its two ends; the load transmission reports one that has fewer than two.</param>
    /// <param name="point0Y">The event's Point0 Y, absolute metres. When given, each span is named by the
    /// point lines its hanging points lie on ("lignes 3–5"); when null, spans are not named.</param>
    /// <param name="skippedRows">The inventory rows that could not be read (Inventory.Skipped). Those of
    /// Palonnier.xlsx travel with the checks of their model, which then never claim to have read the
    /// inventory whole.</param>
    public static List<AbaqueCheck> BuildAbaqueChecks(
        List<MotherGridPalonnier> elements,
        IReadOnlyDictionary<Guid, string> typeNames,
        List<ChargeMp> chargeMps,
        LoadTable table,
        List<string> warnings,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>>? hangingPoints = null,
        double? point0Y = null,
        IReadOnlyCollection<SkippedRow>? skippedRows = null)
    {
        var byElement = chargeMps
            .GroupBy(c => c.MotherGridPalonnierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var checks = new List<AbaqueCheck>();

        var byType = elements
            // A charge placed on a mothergrid is the mothergrid's load, checked with the mothers.
            .Where(e => !e.Famille)
            .Select(e =>
            {
                var supports = SupportsAlong(hangingPoints, e.MotherGridPalonnierId, LengthOf(e));
                return new
                {
                    Element = e,
                    // An element with no model is checked too: against no value, which is a gap, rather
                    // than left out of the checks altogether.
                    Type = FamilyOf(typeNames, e),
                    Charges = byElement.TryGetValue(e.MotherGridPalonnierId, out var c) ? c : new List<ChargeMp>(),
                    Supports = supports,
                    Lines = supports.Select(s => point0Y is { } y0 ? PointLineAlong(e, s, y0) : null).ToList(),
                };
            })
            .GroupBy(e => e.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byType)
        {
            var type = group.Key;

            // One row per span: the loads strictly between two neighbouring hanging points. A load on a
            // hanging point goes straight into it and bends neither span.
            var spans = group
                .SelectMany(e => Enumerable.Range(0, e.Supports.Count - 1).Select(k =>
                {
                    double a = e.Supports[k], b = e.Supports[k + 1];
                    return (
                        e.Element,
                        Length: b - a,
                        Load: e.Charges
                            .Where(ch => (ch.X ?? 0) > a + OnSupportM && (ch.X ?? 0) < b - OnSupportM)
                            .Sum(ch => ch.Charge ?? 0),
                        Label: LinesLabel(e.Lines[k], e.Lines[k + 1]));
                }))
                .ToList();

            var centred = CentredChecks(type, AbaqueElementRole.Palonnier, table, spans,
                SkippedOf(skippedRows, type, mothers: false)).ToList();
            checks.AddRange(centred);
            WarnNoValue(centred, type, table, warnings);

            // Loads beyond the outermost hanging points: a cantilever, each end on its own. The inventory
            // gives the centred case only. Read at the overhang length, a centred value overstates what a
            // cantilever may carry - 1 500 daN hung 2,5 m out is 3 750 daN.m at the root, where that
            // model's own centred rows imply about 2 500 - so each is left unverified until a déport value
            // is supplied. The studies quote one (666 daN at 2,8 m) that is not on file.
            var cantilevers = group
                .SelectMany(e =>
                {
                    double first = e.Supports[0], last = e.Supports[^1];
                    var loaded = e.Charges.Where(ch => (ch.Charge ?? 0) > 0).ToList();
                    return new[]
                    {
                        (Past: Cantilever(e.Element, loaded.Where(ch => (ch.X ?? 0) < first - OnSupportM)
                                                           .Select(ch => (ch.Charge ?? 0, first - (ch.X ?? 0)))),
                         Line: e.Lines[0]),
                        (Past: Cantilever(e.Element, loaded.Where(ch => (ch.X ?? 0) > last + OnSupportM)
                                                           .Select(ch => (ch.Charge ?? 0, (ch.X ?? 0) - last))),
                         Line: e.Lines[^1]),
                    };
                })
                .Where(c => c.Past.Load > 0)
                .OrderByDescending(c => c.Past.Moment);

            foreach (var (past, line) in cantilevers)
            {
                checks.Add(new AbaqueCheck
                {
                    Family = DisplayFamily(type),
                    Role = AbaqueElementRole.Palonnier,
                    Case = AbaqueLoadCase.Cantilever,
                    WorstElement = past.Element.Name,
                    AppliedKg = past.Load,
                    SpanM = past.Reach,
                    SpanLabel = line is { } n ? $"ligne {n}" : null,
                    IsGoverning = false,
                    IsHeaviest = false,
                });
            }

            if (centred.Count > 0 && !table.Has(type))
                warnings.Add($"« {type} » ne figure pas dans {InventoryFiles.PalonniersFileName} : la vérification "
                           + "de ces palonniers n'a pas pu être faite.");
        }

        return checks
            .OrderBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Case)
            .ToList();
    }

    /// <summary>
    /// The palonnier checks from an abaque dictionary, as callers built it before LoadTable: read through
    /// a LoadTable of the same rows, so they follow the same rule.
    /// </summary>
    public static List<AbaqueCheck> BuildAbaqueChecks(
        List<MotherGridPalonnier> elements,
        IReadOnlyDictionary<Guid, string> typeNames,
        List<ChargeMp> chargeMps,
        Dictionary<string, List<BarSpec>> abaques,
        List<string> warnings,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>>? hangingPoints = null) =>
        BuildAbaqueChecks(elements, typeNames, chargeMps, LoadTable.From(abaques.Values.SelectMany(rows => rows)),
            warnings, hangingPoints);

    /// <summary>A load within this distance of a hanging point, or of a beam, is on it.</summary>
    private const double OnSupportM = 0.010;

    /// <summary>
    /// A palonnier's hanging points along it, distinct and in order, or its two ends when fewer than two
    /// are known.
    /// </summary>
    private static List<double> SupportsAlong(
        IReadOnlyDictionary<Guid, IReadOnlyList<double>>? hangingPoints, Guid id, double length)
    {
        var distinct = new List<double>();
        if (hangingPoints is not null && hangingPoints.TryGetValue(id, out var known))
        {
            foreach (var s in known.OrderBy(v => v))
                if (distinct.Count == 0 || s - distinct[^1] > OnSupportM) distinct.Add(s);
        }
        return distinct.Count >= 2 ? distinct : new List<double> { 0, length };
    }

    /// <summary>The loads hung beyond one end: their sum, the furthest reach, and their moment about the end.</summary>
    private static (MotherGridPalonnier Element, double Load, double Reach, double Moment) Cantilever(
        MotherGridPalonnier element, IEnumerable<(double Load, double Past)> loads)
    {
        var list = loads.ToList();
        return (element,
                list.Sum(q => q.Load),
                list.Count == 0 ? 0 : list.Max(q => q.Past),
                list.Sum(q => q.Load * q.Past));
    }

    /// <summary>
    /// The checks of the mother lines, bay by bay, as the palonniers are checked span by span. A mother
    /// rests on the charpente points under it (LoadTransmissionHelper.CharpenteSupportsOf, the reading
    /// the load transmission uses); each bay between two neighbouring beams carries the downward loads
    /// strictly inside it - what the palonniers hung on it deliver, their share of the scenic loads and of
    /// their own weight, and any charge hung on the mother itself - and their sum is compared with the
    /// charge ponctuelle au milieu of the model at the length immediately above the bay (LoadTable). A
    /// load within 1 cm of a beam goes straight into it. The mother's own weight is not added: the
    /// inventory states what may be hung on a truss. Uplift is not subtracted - pulled up at one point, a
    /// truss is hardly relieved of the bending from loads elsewhere - and is reported as a case no value
    /// covers, as are the loads beyond the outermost beams, in cantilever. Public because it is a pure
    /// function.
    /// </summary>
    /// <param name="pointLoads">What each element carries, point by point, as the load transmission
    /// computed it: LoadTransmissionHelper.PointLoadsOnMothers.</param>
    /// <param name="table">What a mother of each model may carry: Inventory.MotherTable.</param>
    /// <param name="point0X">The event's Point0, absolute metres: element coordinates are relative to it,
    /// the charpente points absolute.</param>
    /// <param name="skippedRows">The inventory rows that could not be read (Inventory.Skipped). Those of
    /// MotherGrill.xlsx travel with the checks of their model.</param>
    public static List<AbaqueCheck> BuildMotherAbaqueChecks(
        List<MotherGridPalonnier> elements,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlyDictionary<Guid, List<(double X, double Y, double Load)>> pointLoads,
        LoadTable table,
        List<string> warnings,
        double point0X = 0,
        double point0Y = 0,
        IReadOnlyCollection<SkippedRow>? skippedRows = null)
    {
        var checks = new List<AbaqueCheck>();

        var byType = elements
            .Where(e => e.Famille)
            .Select(e =>
            {
                var carried = pointLoads.TryGetValue(e.MotherGridPalonnierId, out var list)
                    ? list
                    : new List<(double X, double Y, double Load)>();
                return new
                {
                    Element = e,
                    Type = FamilyOf(typeNames, e),
                    Length = LengthOf(e),
                    Supports = MotherSupports(e, point0X, point0Y),
                    // Each load at its distance along the mother, the frame the supports are given in.
                    Loads = carried.Select(q => (S: AlongElement(e, q.X, q.Y), q.Load)).ToList(),
                    Uplift = -carried.Where(q => q.Load < 0).Sum(q => q.Load),
                };
            })
            .GroupBy(m => m.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byType)
        {
            var type = group.Key;

            // One row per bay: the downward loads strictly between two neighbouring beams.
            var bays = group
                .SelectMany(m => Enumerable.Range(0, m.Supports.Count - 1).Select(k =>
                {
                    var (nameA, a) = m.Supports[k];
                    var (nameB, b) = m.Supports[k + 1];
                    return (
                        m.Element,
                        Length: b - a,
                        Load: m.Loads
                            .Where(q => q.Load > 0 && q.S > a + OnSupportM && q.S < b - OnSupportM)
                            .Sum(q => q.Load),
                        Label: nameA is not null && nameB is not null ? $"{nameA}–{nameB}" : null);
                }))
                .ToList();

            var centred = CentredChecks(type, AbaqueElementRole.Mother, table, bays,
                SkippedOf(skippedRows, type, mothers: true)).ToList();
            checks.AddRange(centred);
            WarnNoValue(centred, type, table, warnings);

            if (centred.Count > 0 && !table.Has(type))
                warnings.Add($"« {type} » ne figure pas dans {InventoryFiles.MothersFileName} : la vérification "
                           + "de ces mothers n'a pas pu être faite.");

            // A load beyond the outermost beam puts the mother in cantilever: a palonnier the optimiser
            // hung beyond the outer columns, or a charge placed on the mother past its last beam. Only the
            // loads past that beam are in cantilever, so only they are reported - each end of each mother
            // on its own, largest moment first. The inventory gives the centred case only, so each is a
            // check that could not be made.
            var overhanging = group
                .SelectMany(m =>
                {
                    var (firstName, first) = m.Supports[0];
                    var (lastName, last) = m.Supports[^1];
                    var down = m.Loads.Where(q => q.Load > 0).ToList();
                    return new[]
                    {
                        (Past: Cantilever(m.Element, down.Where(q => q.S < first - OnSupportM)
                                                         .Select(q => (q.Load, first - q.S))),
                         Name: firstName),
                        (Past: Cantilever(m.Element, down.Where(q => q.S > last + OnSupportM)
                                                         .Select(q => (q.Load, q.S - last))),
                         Name: lastName),
                    };
                })
                .Where(c => c.Past.Load > 0)
                .OrderByDescending(c => c.Past.Moment);

            foreach (var (past, name) in overhanging)
            {
                checks.Add(new AbaqueCheck
                {
                    Family = DisplayFamily(type),
                    Role = AbaqueElementRole.Mother,
                    Case = AbaqueLoadCase.Cantilever,
                    WorstElement = past.Element.Name,
                    AppliedKg = past.Load,
                    SpanM = past.Reach,
                    SpanLabel = name,
                    IsGoverning = false,
                    IsHeaviest = false,
                });
            }

            // A palonnier's cantilever pulls the far mother up. The inventory gives a downward centred
            // load; nothing in it describes a truss pulled up, so this too is a check that could not be made.
            var lifted = group
                .Where(m => m.Uplift > UpliftToleranceKg)
                .OrderByDescending(m => m.Uplift)
                .FirstOrDefault();

            if (lifted is not null)
            {
                checks.Add(new AbaqueCheck
                {
                    Family = DisplayFamily(type),
                    Role = AbaqueElementRole.Mother,
                    Case = AbaqueLoadCase.Uplift,
                    WorstElement = lifted.Element.Name,
                    AppliedKg = lifted.Uplift,
                    SpanM = lifted.Length,
                    IsGoverning = false,
                    IsHeaviest = false,
                });
            }
        }

        return checks
            .OrderBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Case)
            .ToList();
    }

    /// <summary>
    /// The mother checks from an abaque dictionary, as callers built it before LoadTable: read through a
    /// LoadTable of the same rows, so they follow the same rule.
    /// </summary>
    public static List<AbaqueCheck> BuildMotherAbaqueChecks(
        List<MotherGridPalonnier> elements,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlyDictionary<Guid, List<(double X, double Y, double Load)>> pointLoads,
        Dictionary<string, List<BarSpec>> abaques,
        List<string> warnings,
        double point0X = 0,
        double point0Y = 0) =>
        BuildMotherAbaqueChecks(elements, typeNames, pointLoads,
            LoadTable.From(abaques.Values.SelectMany(rows => rows)), warnings, point0X, point0Y);

    /// <summary>Uplift below this is rounding, not a palonnier lifting the mother.</summary>
    private const double UpliftToleranceKg = 0.5;

    /// <summary>
    /// The beams a mother rests on, named and in order along it from (X1, Y1), those within 1 cm of each
    /// other merged: LoadTransmissionHelper.CharpenteSupportsOf, the supports its loads are transmitted
    /// to. Fewer than two - a mother off the grid, or a wrong Point0, which the transmission reports -
    /// and its two ends stand in, unnamed.
    /// </summary>
    private static List<(string? Name, double S)> MotherSupports(MotherGridPalonnier e, double point0X, double point0Y)
    {
        var merged = new List<(string? Name, double S)>();
        foreach (var (name, s) in global::Helpers.LoadTransmissionHelper.CharpenteSupportsOf(e, point0X, point0Y))
            if (merged.Count == 0 || s - merged[^1].S > OnSupportM) merged.Add((name, s));

        return merged.Count >= 2
            ? merged
            : new List<(string? Name, double S)> { (null, 0), (null, LengthOf(e)) };
    }

    /// <summary>Distance of a point along an element from (X1, Y1), both in the element's frame.</summary>
    private static double AlongElement(MotherGridPalonnier e, double x, double y)
    {
        double x1 = e.X1 ?? 0, y1 = e.Y1 ?? 0;
        double dx = (e.X2 ?? 0) - x1, dy = (e.Y2 ?? 0) - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? 0 : ((x - x1) * dx + (y - y1) * dy) / len;
    }

    /// <summary>A point within this distance of a point line is on it. Point lines are 3,6 m apart or more.</summary>
    private const double PointLineToleranceM = 0.5;

    /// <summary>The point line (1 to 9) the point <paramref name="s"/> metres along an element lies on; null when none.</summary>
    private static int? PointLineAlong(MotherGridPalonnier e, double s, double point0Y)
    {
        if (e.X1 is null || e.Y1 is null || e.X2 is null || e.Y2 is null) return null;
        double dx = e.X2.Value - e.X1.Value, dy = e.Y2.Value - e.Y1.Value;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return null;

        // The charpente points are absolute; the element is relative to Point0.
        double y = e.Y1.Value + s * dy / len + point0Y;

        int? line = null;
        double nearest = double.MaxValue;
        foreach (var point in Points)
        {
            if (!CharpenteCoordinates.Points.TryGetValue($"E{point}", out var at)) continue;
            double off = Math.Abs(at.Y - y);
            if (off <= PointLineToleranceM && off < nearest)
            {
                line = point;
                nearest = off;
            }
        }
        return line;
    }

    /// <summary>A palonnier span named by the point lines of its two hanging points: "lignes 3–5".</summary>
    private static string? LinesLabel(int? a, int? b) =>
        a is { } x && b is { } y && x != y ? $"lignes {Math.Min(x, y)}–{Math.Max(x, y)}" : null;

    /// <summary>
    /// The centred-load checks for one family: the governing case, then every other load over its limit
    /// or with no value, so that no overload - and no unverifiable span - can hide behind the single
    /// governing sentence. The governing case is the load closest to its limit, ranked by utilisation
    /// rather than by load because what a truss may carry falls with span: a lighter load on a longer
    /// span is often the one actually over its limit. It is chosen among the loads that have a value; one
    /// with none cannot be ranked against them, and is reported on its own line. Rows are compared by
    /// position, not by element, since an element has one row per span. The model's unreadable inventory
    /// rows, <paramref name="skipped"/>, go on every check: its value, or its lack of one, rests on the other
    /// rows only.
    /// </summary>
    private static IEnumerable<AbaqueCheck> CentredChecks(
        string type,
        AbaqueElementRole role,
        LoadTable table,
        IEnumerable<(MotherGridPalonnier Element, double Length, double Load, string? Label)> candidates,
        IReadOnlyList<SkippedRow> skipped)
    {
        var evaluated = candidates
            .Where(c => c.Load > 0)
            .Select(c => (c.Element, c.Length, c.Load, c.Label, Allowed: table.At(type, c.Length)))
            .ToList();

        if (evaluated.Count == 0) yield break;

        var judged = Enumerable.Range(0, evaluated.Count)
            .Where(i => evaluated[i].Allowed.Known)
            .ToList();

        int worst = judged.Count > 0
            ? judged.OrderByDescending(i => evaluated[i].Load / evaluated[i].Allowed.Kg!.Value)
                    .ThenByDescending(i => evaluated[i].Load)
                    .First()
            : Enumerable.Range(0, evaluated.Count).OrderByDescending(i => evaluated[i].Load).First();

        double heaviest = evaluated.Max(e => e.Load);
        bool inInventory = table.Has(type);

        AbaqueCheck ToCheck(int i, bool governing)
        {
            var (element, length, load, label, allowed) = evaluated[i];
            bool known = allowed.Known;
            return new AbaqueCheck
            {
                Family = DisplayFamily(type),
                Role = role,
                Case = AbaqueLoadCase.Centered,
                WorstElement = element.Name ?? "",
                AppliedKg = load,
                SpanM = length,
                SpanLabel = label,
                AllowedKg = known ? allowed.Kg : null,
                // The row read, or for a computed value the span it was computed for.
                AllowedSpanM = !known ? null
                    : allowed.Source == AllowedSource.Computed ? allowed.SpanM
                    : allowed.RowLengthM,
                Source = known ? allowed.Source : AllowedSource.None,
                BaseLengthM = allowed.RowLengthM,
                BaseKg = allowed.RowKg,
                KgPerM = allowed.KgPerM,
                NoValueReason = known
                    ? null
                    : QualifiedReason(allowed.Reason ?? "aucune charge admissible n'est disponible", skipped),
                BeyondAbaque = inInventory && !known,
                SkippedRows = skipped,
                IsGoverning = governing,
                IsHeaviest = load >= heaviest - 1e-9,
            };
        }

        yield return ToCheck(worst, governing: true);

        for (int i = 0; i < evaluated.Count; i++)
        {
            if (i == worst) continue;

            var allowed = evaluated[i].Allowed;
            if (!allowed.Known)
            {
                // No value: unverifiable, and never hidden behind the governing case. A model absent from
                // the inventory is reported once, not per element.
                if (inInventory) yield return ToCheck(i, governing: false);
                continue;
            }

            if (evaluated[i].Load > allowed.Kg!.Value) yield return ToCheck(i, governing: false);
        }
    }

    /// <summary>Names every span of a model in the inventory that has no value, and why.</summary>
    private static void WarnNoValue(IEnumerable<AbaqueCheck> checks, string type, LoadTable table, List<string> warnings)
    {
        if (!table.Has(type)) return;       // a model absent from the inventory is reported on its own

        foreach (var c in checks.Where(c => c.AllowedKg is null && c.Case == AbaqueLoadCase.Centered))
        {
            warnings.Add($"{c.Role.Capitalised()} {c.WorstElement}"
                       + (c.SpanLabel is { } bay ? $", travée {bay}" : "")
                       + $" ({c.SpanM.ToString("0.##", Fr)} m) : {c.NoValueReason}. "
                       + "La charge n'a pas pu être vérifiée.");
        }
    }

    /// <summary>
    /// The mass of each truss, for the load transmission. One definition shared by Calcul Charpente and
    /// the report, so the stored loads and the study can never disagree about what a truss weighs: the
    /// masses of the pieces it is assembled from (<see cref="PiecesOf"/>), added up. A truss one of whose
    /// pieces the inventory does not weigh is null, never 0 - and never the known part alone: the
    /// transmission then leaves that truss out and names it, because a mass silently taken too low makes
    /// every point it feeds read low.
    /// </summary>
    public static Func<MotherGridPalonnier, global::Helpers.TrussMass> MassSource(
        TrussCatalog catalog, IReadOnlyDictionary<Guid, string> typeNames) =>
        element =>
        {
            var type = FamilyOf(typeNames, element);
            var pieces = PiecesOf(element);
            return new global::Helpers.TrussMass(MassOf(catalog, type, pieces), MassLabel(catalog, type, pieces));
        };

    /// <summary>The pieces' masses added up; null when any of them has none.</summary>
    private static double? MassOf(TrussCatalog catalog, string type, IReadOnlyList<double> pieces)
    {
        if (pieces.Count == 0) return null;

        double total = 0;
        foreach (var length in pieces)
        {
            if (catalog.Find(type, length)?.UnitWeightKg is not { } kg || kg <= 0) return null;
            total += kg;
        }
        return total;
    }

    /// <summary>How a truss is named where its mass is reported: its piece's label, or its model and pieces.</summary>
    private static string MassLabel(TrussCatalog catalog, string type, IReadOnlyList<double> pieces) =>
        pieces.Count == 1
            ? catalog.Find(type, pieces[0])?.Label ?? TrussCatalog.DefaultLabel(type, pieces[0])
            : type + " " + global::Helpers.TrussComposition.Describe(pieces);

    /// <summary>
    /// The row of a list of inventory rows for a span: the shortest length at least as long as the span -
    /// the length immediately above it - and of two rows at that length, the lower value. Rows only: it
    /// computes nothing past the longest row, which LoadTable.At does. Kept for callers that hold rows;
    /// the study itself reads LoadTable.
    /// </summary>
    public static BarSpec? LookupAbaque(List<BarSpec>? bars, double spanM)
    {
        if (bars is null || bars.Count == 0) return null;

        // Tolerance absorbs the floating-point noise in the inventory lengths (8.399999999999999).
        return bars
            .Where(b => b.Length >= spanM - LoadTable.LengthToleranceM)
            .OrderBy(b => (long)Math.Round(b.Length * 100))
            .ThenBy(b => b.Capacity)
            .FirstOrDefault();
    }

    /// <summary>
    /// Attachment points where a stored table and a fresh calculation disagree by more than
    /// <paramref name="toleranceKg"/>, largest difference first. Public because it is a pure function
    /// worth checking directly.
    /// </summary>
    public static List<(string Point, double Stored, double Fresh)> StaleCells(
        Charpente charpente, IReadOnlyDictionary<string, double> fresh, double toleranceKg = 1.0)
    {
        var stale = new List<(string Point, double Stored, double Fresh)>();

        foreach (var beam in Beams)
            foreach (var point in Points)
            {
                var key = $"{beam}{point}";
                double stored = ReadGridValue(charpente, beam, point) ?? 0;
                double now = fresh.TryGetValue(key, out var f) ? f : 0;
                if (Math.Abs(stored - now) > toleranceKg) stale.Add((key, stored, now));
            }

        return stale.OrderByDescending(c => Math.Abs(c.Stored - c.Fresh)).ToList();
    }

    // ── Does the layout still carry the plan's charges? ─────────

    /// <summary>
    /// How far along its element a placed charge may lie from where the plan puts it: the distance the load
    /// transmission reads. The optimiser projects a palonnier's charges onto its axis, which moves them along
    /// it only where one lies past an end, and moves a charge hung on a mother line at most Geometry.TOL
    /// (8 cm) along it, onto its end (Optimizer.OnLine). A margin on top.
    /// </summary>
    public const double PlacementToleranceM = 0.15;

    /// <summary>
    /// How far across a palonnier a placed charge may lie from where the plan puts it: an inclined
    /// palonnier's charges lie within Geometry.ColinearTol of its axis, those of one along the beams within
    /// Geometry.TOL. A centimetre for the rounding of its ends.
    /// </summary>
    public const double AcrossPalonnierToleranceM = RiggingOptimizer.Core.Geometry.ColinearTol + 0.01;

    /// <summary>How far across a mother line: a charge is hung on one only within Geometry.TOL of its point line.</summary>
    public const double AcrossMotherToleranceM = RiggingOptimizer.Core.Geometry.TOL + 0.01;

    /// <summary>At most this many charges are named in one gap; the rest are counted.</summary>
    private const int NamedInGap = 5;

    /// <summary>
    /// The verification gaps of a layout that no longer carries the plan's charges. Each placed charge
    /// (ChargeMp) is put back where its element says it is - (X1, Y1), then X metres towards (X2, Y2), in the
    /// grid's axes from Point0, as the load transmission reads it - and compared with the plan's charge
    /// (ChargesScenique) of the same name, compared without case after trimming. A gap for charges placed
    /// more than <see cref="PlacementToleranceM"/> from the plan, placed but not in the plan, in the plan but
    /// not placed, of another weight than the plan's, with no weight anywhere, whose position cannot be
    /// rebuilt, or whose name is missing or given twice, which leaves them nothing to be compared with. Every
    /// coordinate printed is in the studies' axes. Public because it is a pure function worth checking directly.
    /// </summary>
    public static List<string> LayoutMismatches(
        IEnumerable<ChargeMp> chargeMps,
        IEnumerable<MotherGridPalonnier> elements,
        IEnumerable<ChargesScenique> plan)
    {
        const string noLonger = "l’implantation ne correspond plus aux charges";
        const string rerun = "Relancez le placement (Générer l’implantation) puis le calcul charpente.";
        const string rerunAfter = "puis relancez le placement (Générer l’implantation) et le calcul charpente.";

        var byId = new Dictionary<Guid, MotherGridPalonnier>();
        foreach (var e in elements) byId.TryAdd(e.MotherGridPalonnierId, e);

        static string Trimmed(string? name) => (name ?? "").Trim();
        var planned = plan
            .GroupBy(p => Trimmed(p.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var placed = chargeMps
            .GroupBy(c => Trimmed(c.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var ambiguous = new List<string>();
        var notPlaced = new List<string>();
        var notInPlan = new List<(string Name, string Carrier)>();
        var moved = new List<(double Distance, string Name, string Carrier, string At, string Planned)>();
        var reweighed = new List<(string Name, double? Placed, double? Planned)>();
        var weightless = new List<string>();
        var unlocated = new List<string>();

        // A charge with no name, or a name given twice, cannot be told apart from another: nothing about it
        // can be compared, so it is a gap of its own rather than a match picked at random.
        if (planned.TryGetValue("", out var unnamedPlanned))
            ambiguous.Add($"{unnamedPlanned.Count} charge(s) sans nom dans le plan de charges");
        if (placed.TryGetValue("", out var unnamedPlaced))
            ambiguous.Add($"{unnamedPlaced.Count} charge(s) sans nom sur l’implantation");

        var names = planned.Keys.Concat(placed.Keys)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, NaturalStringComparer.Instance)
            .ToList();

        foreach (var name in names)
        {
            planned.TryGetValue(name, out var inPlan);
            placed.TryGetValue(name, out var inLayout);

            if (inPlan is { Count: > 1 } || inLayout is { Count: > 1 })
            {
                if (inPlan is { Count: > 1 }) ambiguous.Add($"« {name} » {inPlan.Count} fois dans le plan de charges");
                if (inLayout is { Count: > 1 }) ambiguous.Add($"« {name} » {inLayout.Count} fois sur l’implantation");
                continue;
            }

            var charge = inLayout?[0];
            var carrier = charge is null
                ? null
                : (MotherGridPalonnier?)charge.MotherGridPalonnier ?? byId.GetValueOrDefault(charge.MotherGridPalonnierId);
            var carrierName = carrier is null ? "(élément introuvable)" : NameOf(carrier);

            if (inPlan is null)
            {
                notInPlan.Add((name, carrierName));
                continue;
            }
            if (charge is null)
            {
                notPlaced.Add($"« {name} »");
                continue;
            }
            var planCharge = inPlan[0];

            // The weight the transmission carries against the weight Tableau 1 prints. A charge with none on
            // either side is counted as 0 daN everywhere, which is not a value, so it is a gap too.
            if (charge.Charge is null && planCharge.Charge is null)
                weightless.Add($"« {name} »");
            else if (charge.Charge is not { } kgPlaced || planCharge.Charge is not { } kgPlanned
                     || !(Math.Abs(kgPlaced - kgPlanned) <= Math.Max(1.0, 0.001 * Math.Max(Math.Abs(kgPlaced), Math.Abs(kgPlanned)))))
                reweighed.Add((name, charge.Charge, planCharge.Charge));

            if (carrier is null)
            {
                unlocated.Add($"« {name} » (sur un élément introuvable)");
                continue;
            }
            if (charge.X is not { } along)
            {
                unlocated.Add($"« {name} » (sans position sur « {carrierName} »)");
                continue;
            }
            if (PointOn(carrier, along) is not { } at)
            {
                unlocated.Add($"« {name} » (sur « {carrierName} », dont les coordonnées sont incomplètes)");
                continue;
            }
            if (planCharge.X is not { } px || planCharge.Y is not { } py)
            {
                unlocated.Add($"« {name} » (sans coordonnées dans le plan de charges)");
                continue;
            }

            // Along the element, what the load transmission reads, and across it, what the optimiser allows the
            // element's kind, each against its own tolerance. Written so that an offset that is not a number is
            // not within its tolerance either.
            var (alongOff, acrossOff) = Offsets(carrier, at, px, py);
            double acrossTol = carrier.Famille ? AcrossMotherToleranceM : AcrossPalonnierToleranceM;
            if (!(alongOff <= PlacementToleranceM) || !(acrossOff <= acrossTol))
                moved.Add((Math.Sqrt(alongOff * alongOff + acrossOff * acrossOff), name, carrierName,
                           StudyPoint(at.X, at.Y), StudyPoint(px, py)));
        }

        static string Listed(IReadOnlyList<string> items) =>
            string.Join(", ", items.Take(NamedInGap)) + (items.Count > NamedInGap ? $" (+{items.Count - NamedInGap})" : "");
        static string Weight(double? kg) => kg is { } v ? $"{Math.Round(v).ToString("0", Fr)} daN" : "aucun poids";

        var gaps = new List<string>();

        if (ambiguous.Count > 0)
            gaps.Add("Les charges ne peuvent pas toutes être rapprochées de l’implantation par leur nom : "
                   + Listed(ambiguous) + ". Une charge sans nom, ou dont le nom est donné deux fois, ne peut pas être "
                   + "vérifiée contre le plan de charges. Donnez à chaque charge un nom unique, " + rerunAfter);

        if (notPlaced.Count == 1)
            gaps.Add($"La charge {notPlaced[0]} du plan de charges n’est placée sur aucun élément de l’implantation : "
                   + $"{noLonger}. {rerun}");
        else if (notPlaced.Count > 1)
            gaps.Add($"{notPlaced.Count} charges du plan de charges ne sont placées sur aucun élément de l’implantation : "
                   + $"{Listed(notPlaced)}. L’implantation ne correspond plus aux charges. {rerun}");

        if (notInPlan.Count == 1)
            gaps.Add($"La charge « {notInPlan[0].Name} » est placée sur « {notInPlan[0].Carrier} » mais ne figure pas "
                   + $"dans le plan de charges : {noLonger}. {rerun}");
        else if (notInPlan.Count > 1)
            gaps.Add($"{notInPlan.Count} charges placées sur l’implantation ne figurent pas dans le plan de charges : "
                   + Listed(notInPlan.Select(n => $"« {n.Name} » (sur « {n.Carrier} »)").ToList())
                   + $". L’implantation ne correspond plus aux charges. {rerun}");

        // The furthest first: the worst misplacements are the ones named.
        var worstFirst = moved.OrderByDescending(m => double.IsNaN(m.Distance) ? double.MaxValue : m.Distance).ToList();
        if (worstFirst.Count == 1)
            gaps.Add($"La charge « {worstFirst[0].Name} » est placée en {worstFirst[0].At} sur « {worstFirst[0].Carrier} » "
                   + $"mais en {worstFirst[0].Planned} dans le plan de charges : {noLonger}. {rerun}");
        else if (worstFirst.Count > 1)
            gaps.Add($"{worstFirst.Count} charges sont placées ailleurs que dans le plan de charges : "
                   + Listed(worstFirst.Select(m => $"« {m.Name} » en {m.At} sur « {m.Carrier} » au lieu de {m.Planned}").ToList())
                   + $". L’implantation ne correspond plus aux charges. {rerun}");

        if (reweighed.Count == 1)
            gaps.Add($"La charge « {reweighed[0].Name} » n’a pas le même poids sur l’implantation et dans le plan de "
                   + $"charges ({Weight(reweighed[0].Placed)} sur l’implantation, {Weight(reweighed[0].Planned)} dans le "
                   + $"plan de charges) : {noLonger}. {rerun}");
        else if (reweighed.Count > 1)
            gaps.Add($"{reweighed.Count} charges n’ont pas le même poids sur l’implantation et dans le plan de charges : "
                   + Listed(reweighed.Select(r => $"« {r.Name} » ({Weight(r.Placed)} sur l’implantation, "
                                                 + $"{Weight(r.Planned)} dans le plan de charges)").ToList())
                   + $". L’implantation ne correspond plus aux charges. {rerun}");

        if (weightless.Count == 1)
            gaps.Add($"La charge {weightless[0]} n’a de poids ni sur l’implantation ni dans le plan de charges : elle y "
                   + "est comptée pour 0 daN, ce que l’étude ne peut pas vérifier. Renseignez son poids, " + rerunAfter);
        else if (weightless.Count > 1)
            gaps.Add($"{weightless.Count} charges n’ont de poids ni sur l’implantation ni dans le plan de charges : "
                   + Listed(weightless) + ". Elles y sont comptées pour 0 daN, ce que l’étude ne peut pas vérifier. "
                   + "Renseignez leur poids, " + rerunAfter);

        if (unlocated.Count == 1)
            gaps.Add($"La position de la charge {unlocated[0]} ne peut pas être comparée au plan de charges : "
                   + "l’implantation ne peut pas être vérifiée pour elle. Complétez la position manquante, " + rerunAfter);
        else if (unlocated.Count > 1)
            gaps.Add($"La position de {unlocated.Count} charges ne peut pas être comparée au plan de charges : "
                   + Listed(unlocated) + ". L’implantation ne peut pas être vérifiée pour elles. Complétez les "
                   + "positions manquantes, " + rerunAfter);

        return gaps;
    }

    /// <summary>
    /// The point <paramref name="s"/> metres along an element from (X1, Y1), in the grid's axes from Point0;
    /// null for an element without both ends, or shorter than a millimetre. The reading the load
    /// transmission makes (LoadTransmissionHelper.PointOnElement).
    /// </summary>
    private static (double X, double Y)? PointOn(MotherGridPalonnier e, double s)
    {
        if (e.X1 is not { } x1 || e.Y1 is not { } y1 || e.X2 is not { } x2 || e.Y2 is not { } y2) return null;
        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (!(len >= 0.001)) return null;
        return (x1 + s * dx / len, y1 + s * dy / len);
    }

    /// <summary>
    /// How far the plan's point (<paramref name="px"/>, <paramref name="py"/>) lies from <paramref name="at"/>,
    /// along the element and across it, both as distances. Only for an element <see cref="PointOn"/> reads.
    /// </summary>
    private static (double Along, double Across) Offsets(MotherGridPalonnier e, (double X, double Y) at, double px, double py)
    {
        double dx = e.X2!.Value - e.X1!.Value, dy = e.Y2!.Value - e.Y1!.Value;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / len, uy = dy / len;
        double ox = px - at.X, oy = py - at.Y;
        return (Math.Abs(ox * ux + oy * uy), Math.Abs(ox * uy - oy * ux));
    }

    /// <summary>A position in the grid's axes from Point0, printed in the studies' axes: "(1,20 ; -3,40)".</summary>
    private static string StudyPoint(double gridX, double gridY)
    {
        var (x, y) = global::Helpers.StudyAxes.ToStudy(gridX, gridY);
        // The reflection turns a 0 into -0, which would print as "-0,00".
        static string Metres(double v) => (Math.Abs(v) < 0.005 ? 0.0 : v).ToString("0.00", Fr);
        return $"({Metres(x)} ; {Metres(y)})";
    }

    // ── Unreadable inventory rows ────────────────────────────────

    /// <summary>The rows of one model, in one role's workbook, that could not be read, by line.</summary>
    private static IReadOnlyList<SkippedRow> SkippedOf(IReadOnlyCollection<SkippedRow>? skipped, string type, bool mothers) =>
        skipped is null || skipped.Count == 0
            ? Array.Empty<SkippedRow>()
            : skipped
                .Where(r => r.Mothers == mothers
                            && string.Equals(r.Model.Trim(), type.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Line)
                .ToList();

    /// <summary>
    /// Why there is no value, as LoadTable says it, for a model that lost a row: LoadTable saw the readable
    /// rows only, so its "longest length of the inventory" is the longest READABLE one. The unread rows are
    /// named after the reason rather than worked into it, so that the reason stays qualified whatever
    /// LoadTable's wording becomes.
    /// </summary>
    private static string QualifiedReason(string reason, IReadOnlyList<SkippedRow> skipped) =>
        skipped.Count == 0
            ? reason
            : reason.Replace("la plus grande longueur de l'inventaire", "la plus grande longueur lisible de l'inventaire")
              + " ; " + UnreadInventoryRows.Clause(skipped);

    /// <summary>
    /// A verification gap for every inventory row that could not be read whose model an element of the
    /// scenario uses in that row's role - a mother's model in MotherGrill.xlsx, a palonnier's in
    /// Palonnier.xlsx - the model being the element's ElementType name, compared without case after trimming.
    /// Public because it is a pure function worth checking directly.
    /// </summary>
    public static List<string> SkippedRowGaps(
        IEnumerable<SkippedRow> skipped,
        IEnumerable<MotherGridPalonnier> elements,
        IReadOnlyDictionary<Guid, string> typeNames)
    {
        var used = elements.Select(e => (Mother: e.Famille, Model: FamilyOf(typeNames, e))).ToList();
        var gaps = new List<string>();

        foreach (var row in skipped.OrderBy(r => r.Mothers ? 0 : 1).ThenBy(r => r.Line))
        {
            var model = row.Model.Trim();
            if (!used.Any(u => u.Mother == row.Mothers && string.Equals(u.Model, model, StringComparison.OrdinalIgnoreCase)))
                continue;

            var what = row.LengthM is { } length ? $"« {model} » de {length.ToString("0.##", Fr)} m" : $"« {model} »";
            gaps.Add($"{row.File}, ligne {row.Line} ({what}) : {row.Reason}, ligne ignorée. Des "
                   + (row.Mothers ? "mothers" : "palonniers")
                   + " de ce modèle sont utilisés : leurs charges admissibles sont lues sans cette ligne et peuvent ne "
                   + "pas être celles de l’inventaire. Corrigez cette ligne, puis relancez le placement (Générer "
                   + "l’implantation) et le calcul charpente.");
        }

        return gaps;
    }

    // ── Pieces that do not describe the truss ────────────────────

    /// <summary>
    /// A verification gap for every truss whose recorded pieces cannot be read, or fall short of the length
    /// it is drawn over (BuildTrussInventory): it is weighed as a truss that is not there, so every
    /// attachment point it feeds is wrong. Public because it is a pure function worth checking directly.
    /// </summary>
    public static List<string> PieceProblemGaps(IEnumerable<TrussPieceProblem> problems)
    {
        const string todo = "Corrigez ses pièces sur la page MotherGrid, puis relancez le calcul charpente.";

        return problems.Select(p =>
        {
            var drawn = p.DrawnM.ToString("0.##", Fr);
            return p.Unreadable
                ? $"Les pièces enregistrées de « {p.Element} » ({p.Family}) sont illisibles (« {Shortened(p.Stored!)} ») : "
                  + $"il est compté comme une seule pièce de {p.Pieces}, pour une longueur dessinée de {drawn} m, et son "
                  + "poids propre, comme la charge de chaque point d’accroche qu’il alimente, ne repose sur aucune pièce "
                  + "enregistrée. " + todo
                : $"Les pièces enregistrées de « {p.Element} » ({p.Family}), {p.Pieces}, n’atteignent pas la longueur "
                  + $"sur laquelle il est dessiné, {drawn} m : il est pesé comme un pont de {p.PiecesM.ToString("0.##", Fr)} m, "
                  + "qui n’est pas celui du plan, et la charge de chaque point d’accroche qu’il alimente en est faussée. "
                  + todo;
        }).ToList();
    }

    /// <summary>A stored value as a gap quotes it: at most 40 characters.</summary>
    private static string Shortened(string text) => text.Length <= 40 ? text : text[..40] + "…";

    /// <summary>
    /// The heading a truss family is reported under: its model, as the inventory names it. It used to be
    /// guessed from fragments of the old type names - any "ACIER" printed as "C500 ACIER" - which would
    /// now mislabel models such as "S500 Carrée Acier".
    /// </summary>
    private static string DisplayFamily(string type) => type.Trim();

    /// <summary>French number formatting, matching the existing documents: "2 286", "7,4".</summary>
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
}
