// =============================================================
// PlanSource.cs — Le plan de charges, tel que la page Charges le tient.
//
// The plan IS these rows. The Charges page is where a load is added, moved or weighed, so an edit made
// there has to reach the optimiser; Charges.xlsx is only one way of filling that page (« Import Excel »),
// and reading the workbook here made every such edit invisible.
//
// Everything the placement needs is checked before it places anything, and all of it at once: the page
// shows the refusal as a single alert, so a user fixes one thing and is told about the next is the failure
// to avoid.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using Helpers;
using Microsoft.EntityFrameworkCore;
using Charge = RiggingOptimizer.Models.Charge;

namespace RiggingOptimizer;

internal static class PlanSource
{
    /// <summary>
    /// The scenario's scenic loads, as the optimiser takes them. Throws <see cref="InvalidDataException"/>
    /// with every problem of the plan in one sentence when it cannot be placed as it stands - and refuses
    /// before anything is deleted, because saving replaces the scenario's elements.
    /// </summary>
    internal static List<Charge> Read(AliantWebContext db, Guid eventMpscenarioId)
    {
        // AsNoTracking: the layout is replaced later inside a retrying execution strategy that clears the
        // tracker and replays its whole block, so nothing tracked may still be in flight.
        var plan = db.ChargesSceniques
            .AsNoTracking()
            .Where(c => c.EventMpscenarioId == eventMpscenarioId)
            .ToList();

        if (plan.Count == 0)
            throw new InvalidDataException(
                "Le scénario n'a aucune charge : la page Charges est vide. Ajoutez-y les charges, ou "
                + "importez-les avec le bouton « Import Excel », puis relancez la génération. "
                + "L'implantation existante n'a pas été modifiée.");

        // The order is pinned because the optimiser is greedy: it fills each truss with the loads in the
        // order it meets them, so the same plan read in another order is placed differently from one run to
        // the next. Numéro first where it is set, then the name the way a person numbers loads - T2 before T10.
        var ordered = plan
            .OrderBy(c => c.Numero ?? int.MaxValue)
            .ThenBy(c => c.Name, AliantWebS.Reporting.NaturalStringComparer.Instance)
            .ToList();

        var problems = new List<string>();
        var alreadyNamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        foreach (var row in ordered)
        {
            var name = (row.Name ?? "").Trim();

            // The coordinates as the Charges page shows them - the studies' axes - and « ? » for what is
            // not set, never completed with a 0, which would place the load on E5 without saying so.
            string x = row.Y is null ? "?" : StudyAxes.StudyX(row.Y.Value).ToString("0.##", fr);
            string y = row.X is null ? "?" : StudyAxes.StudyY(row.X.Value).ToString("0.##", fr);
            string kg = row.Charge is null ? "?" : row.Charge.Value.ToString("0.##", fr);

            // A row with no name cannot be pointed at by a number: the index is the placement's own sorted
            // order, not the order the page lists. So it is told apart by what it does have - where it
            // stands and what it weighs - which is what the user reads on the page.
            string label = name.Length > 0
                ? $"« {name} »"
                : $"la charge en X = {x} m, Y = {y} m ({kg} kg)";

            if (name.Length == 0)
                problems.Add($"{label} : sans nom");
            else if (!alreadyNamed.Add(name))
                // The optimiser, the stored ChargeMp and the study all tell charges apart by name alone,
                // so two under one name is exactly what makes the balance check fail later.
                problems.Add($"« {name} » : deux charges portent ce nom");

            if (row.X is null || row.Y is null)
                problems.Add($"{label} : position incomplète (X = {x} m, Y = {y} m)");

            // 0 kg is allowed: a load point that weighs nothing still has to be reached by a truss, and the
            // study still lists it. A missing weight, or a negative one, is not.
            if (row.Charge is null)
                problems.Add($"{label} : charge manquante");
            else if (row.Charge < 0)
                problems.Add($"{label} : charge négative ({row.Charge.Value.ToString("0.##", fr)} kg)");
        }

        if (problems.Count > 0)
            throw new InvalidDataException(
                "Le plan de charges de la page Charges ne peut pas être placé : "
                + string.Join(" ; ", problems.Take(10))
                + (problems.Count > 10 ? $" (+{problems.Count - 10})" : "")
                + ". L'implantation existante n'a pas été modifiée.");

        // The stored X and Y are already in the grid's axes - the one-time migration in Program.cs converted
        // the rows that were not - and RiggingOptimizer.Models.Charge uses those same axes. So this is a
        // straight copy: converting here would reflect the whole plan through the origin, and nothing
        // downstream would notice.
        return ordered.Select(c => new Charge
        {
            Name   = c.Name!.Trim(),
            X      = c.X!.Value,
            Y      = c.Y!.Value,
            Weight = c.Charge!.Value,
        }).ToList();
    }
}
