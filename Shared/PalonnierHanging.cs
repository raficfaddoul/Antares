// =============================================================
// PalonnierHanging.cs — Which mothers a palonnier hangs from, and who decided it.
//
// The rule, as the venue's rigging owner states it (2026-09-11): a palonnier is hung from every mother
// it crosses, and each of its loads goes to the two nearest of them, one on each side. The exception is
// a mother the palonnier would take past its capacity: it is then not hung there. The optimiser applies
// that exception when it places the palonniers, and records what it decided. Anyone may then hook or
// unhook a crossing by hand; that is recorded too, and it takes precedence.
//
// This file is the only reading of that rule. The load calculation, the report and the page that edits
// the crossings all call Resolve, so they cannot disagree about where a palonnier hangs.
// =============================================================

using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helpers
{
    /// <summary>Who decided whether a palonnier hangs from a mother it crosses.</summary>
    public enum HookOrigin
    {
        /// <summary>Nobody: by default a palonnier hangs from every mother it crosses.</summary>
        Default = 0,

        /// <summary>The optimiser, when it placed the palonnier.</summary>
        Optimiser = 1,

        /// <summary>Someone, by hand, after the placement. Takes precedence over the optimiser.</summary>
        Manual = 2,
    }

    /// <summary>One recorded decision: whether a palonnier hangs from one mother, who decided, and why.</summary>
    public sealed record HookRecord(Guid MotherId, bool Hooked, HookOrigin Origin, string? Reason = null);

    /// <summary>
    /// The decisions stored on a palonnier row (<see cref="MotherGridPalonnier.Accrochages"/>), as compact
    /// JSON. At most one record per mother and origin: a hand edit sits beside the optimiser's record
    /// rather than replacing it, so undoing the hand edits brings the optimiser's decision back.
    /// </summary>
    public static class PalonnierHooks
    {
        private sealed class Row
        {
            [JsonPropertyName("m")] public Guid MotherId { get; set; }
            [JsonPropertyName("h")] public bool Hooked { get; set; }
            [JsonPropertyName("o")] public int Origin { get; set; }

            [JsonPropertyName("r")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Reason { get; set; }
        }

        /// <summary>
        /// The records stored on a palonnier. <paramref name="readable"/> is false when the text cannot be
        /// read: the palonnier then hangs as if nothing were recorded, and the caller must say so.
        /// </summary>
        public static List<HookRecord> Parse(string? stored, out bool readable)
        {
            readable = true;
            if (string.IsNullOrWhiteSpace(stored)) return new List<HookRecord>();

            try
            {
                var rows = JsonSerializer.Deserialize<List<Row?>>(stored) ?? new List<Row?>();

                // A null entry is nothing this code writes: the text was changed by other means, and what
                // it meant cannot be known. Damage, like any other - read nothing rather than half of it.
                if (rows.Any(r => r is null))
                {
                    readable = false;
                    return new List<HookRecord>();
                }

                return rows
                    .Where(r => r!.MotherId != Guid.Empty
                                && (r.Origin == (int)HookOrigin.Optimiser || r.Origin == (int)HookOrigin.Manual))
                    .Select(r => new HookRecord(r!.MotherId, r.Hooked, (HookOrigin)r.Origin, r.Reason))
                    .ToList();
            }
            catch (Exception)
            {
                // Whatever the damage - malformed text, a value of the wrong type - the palonnier then
                // hangs as if nothing were recorded, and the caller says the records were not read.
                readable = false;
                return new List<HookRecord>();
            }
        }

        /// <summary>The records stored on a palonnier; unreadable text reads as none.</summary>
        public static List<HookRecord> Parse(string? stored) => Parse(stored, out _);

        /// <summary>The text to store, or null when there is nothing to record. The last record per mother and origin wins.</summary>
        public static string? Serialize(IEnumerable<HookRecord> records)
        {
            var kept = records
                .Where(r => r.MotherId != Guid.Empty && r.Origin != HookOrigin.Default)
                .GroupBy(r => (r.MotherId, r.Origin))
                .Select(g => g.Last())
                .ToList();

            if (kept.Count == 0) return null;

            return JsonSerializer.Serialize(kept.Select(r => new Row
            {
                MotherId = r.MotherId,
                Hooked = r.Hooked,
                Origin = (int)r.Origin,
                Reason = r.Reason,
            }));
        }

        /// <summary>
        /// The text to store after someone sets one crossing by hand: hung from <paramref name="hungFrom"/>,
        /// or from none of the mothers met there when it is null. Every mother met at the crossing gets a
        /// hand record, so that none of the others can take over by default.
        /// </summary>
        public static string? SetByHand(string? stored, IEnumerable<Guid> mothersAtCrossing, Guid? hungFrom)
        {
            var ids = mothersAtCrossing.Where(id => id != Guid.Empty).Distinct().ToList();
            var records = Parse(stored);
            records.RemoveAll(r => r.Origin == HookOrigin.Manual && ids.Contains(r.MotherId));
            records.AddRange(ids.Select(id => new HookRecord(id, id == hungFrom, HookOrigin.Manual)));
            return Serialize(records);
        }

        /// <summary>The stored text without any hand edit: back to the optimiser's decisions and the default.</summary>
        public static string? ClearManual(string? stored) =>
            Serialize(Parse(stored).Where(r => r.Origin != HookOrigin.Manual));

        /// <summary>True when any crossing of this palonnier was set by hand.</summary>
        public static bool HasManual(string? stored) =>
            Parse(stored).Any(r => r.Origin == HookOrigin.Manual);
    }

    /// <summary>A mother met at one crossing, and where the palonnier would hang from it.</summary>
    /// <param name="X">The hanging point, on the mother and clamped to its drawn segment; element frame.</param>
    /// <param name="S">Distance along the palonnier from (X1, Y1), metres, unclamped.</param>
    /// <param name="PastEndM">How far past the mother's drawn end the palonnier passes; 0 when it crosses the mother itself.</param>
    public sealed record HangingCandidate(MotherGridPalonnier Mother, double X, double Y, double S, double PastEndM);

    /// <summary>One place where a palonnier crosses mothers, and whether it hangs there.</summary>
    public sealed class HangingCrossing
    {
        /// <summary>Distance along the palonnier from (X1, Y1), metres.</summary>
        public double S { get; init; }

        /// <summary>The mothers met here: usually one; two at a column line, or where mothers are stacked in a bay.</summary>
        public IReadOnlyList<HangingCandidate> Candidates { get; init; } = Array.Empty<HangingCandidate>();

        /// <summary>The mother the palonnier hangs from here; null when it is not hung here.</summary>
        public HangingCandidate? HungFrom { get; init; }

        public bool Hooked => HungFrom is not null;

        /// <summary>Who decided: nobody (the default), the optimiser, or a hand edit.</summary>
        public HookOrigin Origin { get; init; }

        /// <summary>Why, when the optimiser left it unhooked: the load the mother would have carried.</summary>
        public string? Reason { get; init; }

        /// <summary>"A" or "B" when the crossing is at that end of the palonnier, null in between.</summary>
        public string? End { get; init; }

        /// <summary>
        /// The optimiser's placement of an end beyond the outer columns: hung from the outer bay's mother,
        /// on its line but past its drawn end, which then carries it in cantilever.
        /// </summary>
        public bool BeyondMother { get; init; }
    }

    /// <summary>Where a palonnier hangs: every crossing, hooked or not, in order along it from (X1, Y1).</summary>
    public sealed class HangingResolution
    {
        public IReadOnlyList<HangingCrossing> Crossings { get; init; } = Array.Empty<HangingCrossing>();

        /// <summary>The hanging points, in order along the palonnier.</summary>
        public IReadOnlyList<HangingCandidate> Supports =>
            Crossings.Where(c => c.Hooked).Select(c => c.HungFrom!).ToList();

        /// <summary>Length of the palonnier's drawn segment, metres.</summary>
        public double LengthM { get; init; }

        /// <summary>False when the stored decisions could not be read, so every crossing took the default.</summary>
        public bool RecordsReadable { get; init; } = true;
    }

    public static class PalonnierHanging
    {
        /// <summary>
        /// How far a palonnier and a mother may miss each other and still meet. It covers the optimiser's
        /// own placement tolerance: MotherLines.Plan puts an end within Geometry.TOL of a beam on the bay
        /// towards the middle of the grid, and a line is drawn from beam to beam, so an end just past a beam
        /// sits up to 8 cm beyond the drawn segment of the mother it hangs from. Derived from that constant
        /// so the two cannot drift apart; the extra 2 cm absorbs rounding. Rows are 3,65 m apart and columns
        /// about 7 m, so the band never reaches another line.
        /// </summary>
        public const double ToleranceM = RiggingOptimizer.Core.Geometry.TOL + 0.02;

        /// <summary>
        /// How far past a mother's drawn end a palonnier may pass, between its own two ends, and still
        /// cross it: Geometry.TOL, the band Optimizer.HangOnCrossings uses, so that every crossing found
        /// here is one the optimiser decided - hung, or left out for capacity. A wider band would hang a
        /// palonnier, by default and unchecked, on a mother the optimiser never considered. The extra
        /// micrometre absorbs rounding between the two computations.
        /// </summary>
        public const double CrossingToleranceM = RiggingOptimizer.Core.Geometry.CrossingTol;

        /// <summary>Crossings closer than this along the palonnier are one crossing: the 10 mm used throughout the chain.</summary>
        public const double SameCrossingM = SelfWeightDistribution.CoincidentToleranceM;

        /// <summary>
        /// Every crossing of a palonnier with the mothers, and whether it hangs there.
        ///
        /// <para>Each crossing is decided by the most authoritative decision recorded for the mothers met
        /// there: a hand edit, then the optimiser's, then the default - hung, from the leftmost mother met
        /// there, which is the one an end on a beam is planned onto (MotherLines.Plan). The end mothers
        /// the optimiser recorded before crossings were (MotherAId, MotherBId) count as its decisions.
        /// A decision naming a mother the palonnier no longer meets has nothing to decide, and is ignored:
        /// a palonnier moved by hand hangs where it now crosses.</para>
        /// </summary>
        public static HangingResolution Resolve(MotherGridPalonnier palonnier, IEnumerable<MotherGridPalonnier> motherGrids)
        {
            var records = PalonnierHooks.Parse(palonnier.Accrochages, out var readable);
            double length = LengthOf(palonnier);

            if (palonnier.X1 is null || palonnier.Y1 is null || palonnier.X2 is null || palonnier.Y2 is null || length < 0.001)
                return new HangingResolution { LengthM = length, RecordsReadable = readable };

            var mothers = motherGrids
                .Where(m => m.Famille && m.MotherGridPalonnierId != palonnier.MotherGridPalonnierId)
                .ToList();

            // The decision that stands for each mother: a hand edit over the optimiser's record.
            var decided = new Dictionary<Guid, HookRecord>();
            foreach (var r in records.Where(r => r.Origin == HookOrigin.Optimiser)) decided[r.MotherId] = r;
            foreach (var id in new[] { palonnier.MotherAId, palonnier.MotherBId })
                if (id is { } g && !decided.ContainsKey(g)) decided[g] = new HookRecord(g, true, HookOrigin.Optimiser);
            foreach (var r in records.Where(r => r.Origin == HookOrigin.Manual)) decided[r.MotherId] = r;

            HookRecord? DecisionOf(HangingCandidate c) =>
                decided.TryGetValue(c.Mother.MotherGridPalonnierId, out var d) ? d : null;

            // Between its ends a palonnier crosses a mother within CrossingToleranceM of the mother's drawn
            // segment, the optimiser's band. At its own ends the band is ToleranceM: that end was placed on
            // its mother within Geometry.TOL, and rounding must not lose it.
            var hits = new List<(HangingCandidate Candidate, bool Beyond)>();
            foreach (var m in mothers)
            {
                if (MeetingPoint(palonnier, m, ToleranceM) is not { } hit) continue;

                bool atEnd = Math.Abs(hit.S) <= ToleranceM || Math.Abs(hit.S - length) <= ToleranceM;
                if (!atEnd && hit.PastEnd > CrossingToleranceM) continue;

                hits.Add((new HangingCandidate(m, hit.X, hit.Y, hit.S, hit.PastEnd), false));
            }

            // An end the optimiser placed beyond the outer beams hangs from the outer bay's mother, on
            // its line but past its drawn end: MotherLines.Plan records that overhang rather than moving the
            // end back onto the grid (Mother.XL / Mother.XR carry it). That is
            // honoured for that case only - a record naming the mother, no mother under the end, and the
            // end outside the columns - so that a palonnier moved by hand to another bay is never hung,
            // metres away, from the mother it used to rest on.
            foreach (var (atStart, recordedForEnd) in new[] { (true, palonnier.MotherAId), (false, palonnier.MotherBId) })
            {
                double endS = atStart ? 0.0 : length;
                if (hits.Any(h => Math.Abs(h.Candidate.S - endS) <= ToleranceM)) continue;

                double endX = (double)(atStart ? palonnier.X1 : palonnier.X2)!;
                if (!OutsideColumns(endX)) continue;

                // Any mother with a decision, hooked or not: an end unhooked by hand must remain a crossing,
                // so the panel keeps a row to hook it back and the study says it was unhooked. The decision
                // itself is applied below, as for every other crossing.
                var named = mothers
                    .Where(m => decided.ContainsKey(m.MotherGridPalonnierId))
                    .OrderByDescending(m => m.MotherGridPalonnierId == recordedForEnd)
                    .ThenByDescending(m => decided[m.MotherGridPalonnierId].Hooked)
                    .ThenBy(m => m.MotherGridPalonnierId);

                foreach (var m in named)
                {
                    if (BeyondEndOnLine(palonnier, m, atStart, ToleranceM) is { } b)
                    {
                        hits.Add((new HangingCandidate(m, b.X, b.Y, endS, b.Overhang), true));
                        break;
                    }
                }
            }

            // One crossing per place along the palonnier. Grouped against each group's first point, so a
            // chain of near neighbours cannot drift into one group.
            var groups = new List<List<(HangingCandidate Candidate, bool Beyond)>>();
            foreach (var h in hits.OrderBy(h => h.Candidate.S))
            {
                if (groups.Count > 0 && h.Candidate.S - groups[^1][0].Candidate.S <= SameCrossingM) groups[^1].Add(h);
                else groups.Add(new List<(HangingCandidate, bool)> { h });
            }

            var crossings = new List<HangingCrossing>();
            foreach (var group in groups)
            {
                var candidates = group.Select(x => x.Candidate)
                    .OrderBy(c => Math.Min(c.Mother.X1 ?? 0, c.Mother.X2 ?? 0))
                    .ThenBy(c => Math.Min(c.Mother.Y1 ?? 0, c.Mother.Y2 ?? 0))
                    .ThenBy(c => c.Mother.MotherGridPalonnierId)
                    .ToList();

                var top = candidates
                    .Select(DecisionOf)
                    .Where(d => d is not null)
                    .Select(d => d!.Origin)
                    .DefaultIfEmpty(HookOrigin.Default)
                    .Max();

                HangingCandidate? hungFrom;
                HookRecord? because = null;
                if (top == HookOrigin.Default)
                {
                    hungFrom = candidates[0];
                }
                else
                {
                    hungFrom = candidates.FirstOrDefault(c => DecisionOf(c) is { Hooked: true } d && d.Origin == top);
                    because = hungFrom is not null
                        ? DecisionOf(hungFrom)
                        : candidates.Select(DecisionOf).First(d => d is not null && d.Origin == top);
                }

                double s = (hungFrom ?? candidates[0]).S;
                crossings.Add(new HangingCrossing
                {
                    S = s,
                    Candidates = candidates,
                    HungFrom = hungFrom,
                    Origin = top,
                    Reason = because?.Reason,
                    End = Math.Abs(s) <= ToleranceM ? "A" : Math.Abs(s - length) <= ToleranceM ? "B" : null,
                    BeyondMother = hungFrom is not null && group.Any(x => x.Beyond && ReferenceEquals(x.Candidate, hungFrom)),
                });
            }

            return new HangingResolution { Crossings = crossings, LengthM = length, RecordsReadable = readable };
        }

        /// <summary>How a crossing's state reads to a person: who decided, and why when the optimiser did.</summary>
        public static string DescribeDecision(HangingCrossing crossing) => crossing.Origin switch
        {
            HookOrigin.Manual => crossing.Hooked ? "accroché à la main" : "décroché à la main",
            HookOrigin.Optimiser => crossing.Hooked
                ? "accroché par l’optimiseur"
                : "décroché par l’optimiseur" + (string.IsNullOrWhiteSpace(crossing.Reason) ? "" : " : " + crossing.Reason),
            _ => "accroché par défaut",
        };

        /// <summary>Length of the segment an element is drawn over, metres.</summary>
        public static double LengthOf(MotherGridPalonnier element)
        {
            double dx = (element.X2 ?? 0) - (element.X1 ?? 0);
            double dy = (element.Y2 ?? 0) - (element.Y1 ?? 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Where a palonnier meets a mother, letting either fall short of the other by up to
        /// <paramref name="toleranceM"/>. Null when they are parallel or clearly apart. The point returned
        /// is ON the mother, clamped to its drawn segment, so a palonnier passing a few centimetres past a
        /// mother's end hangs from that end and its load reaches the attachment point the mother rests
        /// on, not the next column. S, the distance along the palonnier, is not clamped.
        /// </summary>
        private static (double X, double Y, double S, double PastEnd)? MeetingPoint(
            MotherGridPalonnier palonnier, MotherGridPalonnier mother, double toleranceM)
        {
            if (palonnier.X1 == null || palonnier.Y1 == null || palonnier.X2 == null || palonnier.Y2 == null) return null;
            if (mother.X1 == null || mother.Y1 == null || mother.X2 == null || mother.Y2 == null) return null;

            double px = (double)palonnier.X1, py = (double)palonnier.Y1;
            double rx = (double)palonnier.X2 - px, ry = (double)palonnier.Y2 - py;
            double qx = (double)mother.X1, qy = (double)mother.Y1;
            double sx = (double)mother.X2 - qx, sy = (double)mother.Y2 - qy;

            double lp = Math.Sqrt(rx * rx + ry * ry);
            double lm = Math.Sqrt(sx * sx + sy * sy);
            if (lp < 0.001 || lm < 0.001) return null;

            // P + t.r = Q + u.s, with cross(a, b) = a.x*b.y - a.y*b.x.
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-9 * lp * lm) return null;          // parallel

            double t = ((qx - px) * sy - (qy - py) * sx) / den;       // along the palonnier, 0..1
            double u = ((qx - px) * ry - (qy - py) * rx) / den;       // along the mother, 0..1

            double along = t * lp, onMother = u * lm;
            if (along < -toleranceM || along > lp + toleranceM) return null;
            if (onMother < -toleranceM || onMother > lm + toleranceM) return null;

            double uc = Math.Clamp(u, 0.0, 1.0);
            double pastEnd = u < 0 ? -onMother : u > 1 ? onMother - lm : 0.0;
            return (qx + uc * sx, qy + uc * sy, along, pastEnd);
        }

        /// <summary>
        /// Whether a palonnier's end lies on a mother's line - within <paramref name="toleranceM"/> of it -
        /// but past one of its ends by more than that, and where. Null otherwise.
        /// </summary>
        private static (double X, double Y, double Overhang)? BeyondEndOnLine(
            MotherGridPalonnier palonnier, MotherGridPalonnier mother, bool atStart, double toleranceM)
        {
            if (palonnier.X1 == null || palonnier.Y1 == null || palonnier.X2 == null || palonnier.Y2 == null) return null;
            if (mother.X1 == null || mother.Y1 == null || mother.X2 == null || mother.Y2 == null) return null;

            double ex = (double)(atStart ? palonnier.X1 : palonnier.X2);
            double ey = (double)(atStart ? palonnier.Y1 : palonnier.Y2);

            double qx = (double)mother.X1, qy = (double)mother.Y1;
            double sx = (double)mother.X2 - qx, sy = (double)mother.Y2 - qy;
            double lm = Math.Sqrt(sx * sx + sy * sy);
            if (lm < 0.001) return null;

            double along = ((ex - qx) * sx + (ey - qy) * sy) / lm;
            double offset = Math.Abs((ex - qx) * sy - (ey - qy) * sx) / lm;
            if (offset > toleranceM) return null;

            double overhang = along < 0 ? -along : along - lm;
            if (overhang <= toleranceM) return null;

            return (qx + along * sx / lm, qy + along * sy / lm, overhang);
        }

        /// <summary>
        /// Whether x lies outside the grid's outer columns, beyond the optimiser's tolerance. In the
        /// optimiser's frame, which is the frame it writes elements in.
        /// </summary>
        private static bool OutsideColumns(double x)
        {
            var columns = RiggingOptimizer.Core.Geometry.ColXList;
            if (columns.Count == 0) return true;
            double tol = RiggingOptimizer.Core.Geometry.TOL;
            return x < columns[0].X - tol || x > columns[^1].X + tol;
        }
    }
}
