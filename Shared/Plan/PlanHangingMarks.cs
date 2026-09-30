// =============================================================
// PlanHangingMarks.cs — Where the selected palonnier hangs, as marks for the plan.
//
// A green dot where the palonnier is hooked on a mother, a red cross where it crosses one without being
// hooked (Helpers.PalonnierHanging decides). The Mother Grid page computes them and hands them to the plan,
// which only draws them (PlanHangingMark, declared in PlanScene.cs): the plan itself needs no hanging rules,
// nor the Accrochages columns they read. This is the one file of Shared/Plan that needs both, so a project
// without them takes the others and leaves this one out.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AliantWebS.Models;

namespace AliantWebS.Plan;

public static class PlanHangingMarks
{
    /// <summary>
    /// The marks of the selected palonnier on the mothers it crosses: none unless a palonnier (not a mother) is
    /// selected and the mothers are known.
    /// </summary>
    public static IReadOnlyList<PlanHangingMark> For(MotherGridPalonnier? selected, IEnumerable<MotherGridPalonnier>? motherGrids)
    {
        if (selected is not { Famille: false } || motherGrids is null)
            return Array.Empty<PlanHangingMark>();

        var hanging = global::Helpers.PalonnierHanging.Resolve(selected, motherGrids);
        return hanging.Crossings
            .Select(crossing =>
            {
                var at = crossing.HungFrom ?? crossing.Candidates[0];
                var why = global::Helpers.PalonnierHanging.DescribeDecision(crossing);
                return crossing.Hooked
                    ? new PlanHangingMark(at.X, at.Y, true, $"Accroché à {at.Mother.Name} — {why}")
                    : new PlanHangingMark(at.X, at.Y, false, $"Non accroché à {at.Mother.Name} — {why}");
            })
            .ToList();
    }
}
