// =============================================================
// Pipeline.Entities.cs — Turning a finished layout into rows.
//
// Pure: a RiggingResult in, entities out, no database and no clock. That is what lets the stored layout be
// checked against the load calculation in a test, without a scenario, a context or a circuit - which is how
// the disagreement at G9 was found.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using Helpers;
using Microsoft.EntityFrameworkCore;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;

namespace RiggingOptimizer;

public static partial class Pipeline
{
    /// <summary>
    /// The rows to persist for one optimiser run - mothergrids and palonniers, each in the
    /// optimiser's order - with every palonnier recording where it hangs: the mothergrids at its two
    /// ends, and in <see cref="MotherGridPalonnier.Accrochages"/> every mothergrid it crosses that the
    /// optimiser hung it from or left out (Optimizer.HangOnCrossings), with the reason for each one left
    /// out. The load calculation follows those records, so every mothergrid is found to carry what the
    /// optimiser sized it for. Pure, so the records can be checked without a database.
    /// </summary>
    public static (List<MotherGridPalonnier> Palonniers, List<MotherGridPalonnier> Mothers) BuildElementEntities(
        RiggingOptimizer.Models.RiggingResult result,
        Guid eventMpscenarioId,
        IReadOnlyDictionary<string, ElementType> elementTypes)
    {
        // Mothergrids first, so that each palonnier can point at the rows it hangs from.
        var motherEntities = result.Mothers.Select((m, i) =>
        {
            elementTypes.TryGetValue(m.Bar.Type, out var et);
            return new MotherGridPalonnier
            {
                MotherGridPalonnierId = Guid.NewGuid(),
                EventMpscenarioId     = eventMpscenarioId,
                ElementTypeId         = et?.ElementTypeId ?? Guid.Empty,
                Name                  = $"{m.Bar.Type} L={m.Bar.Length:F1}m #{i + 1}",
                Famille               = true,
                X1                    = m.XL,
                Y1                    = m.Y,
                X2                    = m.XR,
                Y2                    = m.Y,
                Composition           = m.Assembly?.Composition,
            };
        }).ToList();

        // result.Mothers is the stock's own list of placed mothergrids - the very instances the
        // optimiser hung each palonnier from - so identity, not geometry, maps one to its row.
        // Geometry could not: when a mothergrid is full the optimiser stacks a second one in the
        // same bay, and the two are then indistinguishable on the plan.
        var rowOf = new Dictionary<RiggingOptimizer.Models.Mother, Guid>(ReferenceEqualityComparer.Instance);
        var nameOf = new Dictionary<RiggingOptimizer.Models.Mother, string>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < result.Mothers.Count; k++)
        {
            rowOf[result.Mothers[k]] = motherEntities[k].MotherGridPalonnierId;
            nameOf[result.Mothers[k]] = motherEntities[k].Name ?? "";
        }

        Guid? RowOf(RiggingOptimizer.Models.Mother? m) =>
            m is not null && rowOf.TryGetValue(m, out var id) ? id : null;

        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        // Why a crossing was left out: the load a mother would have carried, as it stood when the plan was
        // placed. Said as such, because the loads may be edited afterwards and the record stays until the
        // plan is placed again or the crossing is hooked by hand.
        string ReasonOf(RiggingOptimizer.Models.SkippedCrossing k) =>
            "au placement, "
            + (ReferenceEquals(k.Overloaded, k.Mother)
                ? "une travée du mother aurait porté "
                : $"une travée du mother « {nameOf.GetValueOrDefault(k.Overloaded, k.Overloaded.ToString())} » aurait porté ")
            + $"{k.WouldCarryKg.ToString("N0", fr)} kg pour {k.CapacityKg.ToString("N0", fr)} kg admissibles";

        // Every crossing the optimiser decided: hung from each support, left off each skipped mother.
        string? HooksOf(RiggingOptimizer.Models.Palonnier p) => PalonnierHooks.Serialize(
            p.Supports
                .Where(s => RowOf(s.Mother) is not null)
                .Select(s => new HookRecord(RowOf(s.Mother)!.Value, true, HookOrigin.Optimiser))
                .Concat(p.Skipped
                    .Where(k => RowOf(k.Mother) is not null)
                    .Select(k => new HookRecord(RowOf(k.Mother)!.Value, false, HookOrigin.Optimiser, ReasonOf(k)))));

        var palEntities = result.Palonniers.Select((p, i) =>
        {
            elementTypes.TryGetValue(p.Bar.Type, out var et);
            return new MotherGridPalonnier
            {
                MotherGridPalonnierId = Guid.NewGuid(),
                EventMpscenarioId     = eventMpscenarioId,
                ElementTypeId         = et?.ElementTypeId ?? Guid.Empty,
                Name                  = $"{p.Bar.Type} L={p.Bar.Length:F1}m #{i + 1}",
                Famille               = false,
                X1                    = p.A.X,
                Y1                    = p.A.Y,
                X2                    = p.B.X,
                Y2                    = p.B.Y,
                MotherAId             = RowOf(p.MotherA),
                MotherBId             = RowOf(p.MotherB),
                Accrochages           = HooksOf(p),
                Composition           = p.Assembly?.Composition,
            };
        }).ToList();

        return (palEntities, motherEntities);
    }

    /// <summary>
    /// The charges to store for one optimiser run: each on the palonnier that carries it, at its distance
    /// along it from (X1, Y1); each charge hung straight on a mother line on that line, at its distance
    /// from the line's X1. Pure, so it can be checked without a database.
    /// </summary>
    public static List<ChargeMp> BuildChargeMps(
        RiggingOptimizer.Models.RiggingResult result,
        List<MotherGridPalonnier> palEntities,
        List<MotherGridPalonnier> motherEntities)
    {
        var chargeMps = result.Palonniers
            .SelectMany((p, i) => p.Charges.Select((c, j) =>
            {
                // Project the charge's position onto the palonnier to get its distance from end A.
                double dx  = p.B.X - p.A.X;
                double dy  = p.B.Y - p.A.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                double distAlongBar = len > 0
                    ? ((c.X - p.A.X) * dx + (c.Y - p.A.Y) * dy) / len
                    : 0;

                return new ChargeMp
                {
                    ChargeMpid            = Guid.NewGuid(),
                    MotherGridPalonnierId = palEntities[i].MotherGridPalonnierId,
                    Name                  = c.Name,
                    Numero                = j + 1,
                    X                     = distAlongBar,   // ← distance from A along bar (meters)
                    Charge                = c.Weight,
                };
            }))
            .ToList();

        for (int k = 0; k < result.Mothers.Count; k++)
        {
            var m = result.Mothers[k];
            int numero = 1;
            foreach (var c in m.DirectCharges)
            {
                chargeMps.Add(new ChargeMp
                {
                    ChargeMpid            = Guid.NewGuid(),
                    MotherGridPalonnierId = motherEntities[k].MotherGridPalonnierId,
                    Name                  = c.Name,
                    Numero                = numero++,
                    // Along the line from X1 = XL; a charge a few centimetres past an end is on it (Optimizer.OnLine).
                    X                     = RiggingOptimizer.Core.Optimizer.OnLine(m, c.X) - m.XL,
                    Charge                = c.Weight,
                });
            }
        }

        return chargeMps;
    }
}
