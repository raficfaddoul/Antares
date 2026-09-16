// =============================================================
// SelfWeightDistribution.cs — Turns a truss's own mass into reactions at the points it rests on.
//
// The existing chain only ever transmitted the scenic loads. The hand-written studies transmit the
// trusses too: in the "LES INTEMPORELS" study the attachment points carry 11 420 daN, which is
// exactly the 5 100 daN of motor loads plus the 6 320 daN of pre rigg listed in its own composition
// table. So a palonnier's mass has to reach the two mothergrids it hangs from, and a mothergrid's mass
// has to reach its charpente points.
//
// This class is deliberately pure — no EF, no spreadsheet, no Blazor — because the one property that
// must never break is arithmetic: the reactions have to sum to the mass, exactly, including the
// negative ones an overhang produces. That is only testable without a database if the maths lives
// on its own.
// =============================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace Helpers
{
    /// <summary>
    /// Distributes a uniformly spread mass over the supports a truss rests on, using the same lever
    /// rule the rest of <see cref="LoadTransmissionHelper"/> applies to point loads.
    /// </summary>
    public static class SelfWeightDistribution
    {
        /// <summary>
        /// Two supports closer than this along the truss are one support. Matches the 10 mm the
        /// transmission already uses to decide that an intersection sits ON a charpente point.
        /// Real cases exist: a palonnier whose x falls on a column line crosses the mothergrids of
        /// both adjacent bays at the same coordinate, which would otherwise give a zero-length span.
        /// </summary>
        public const double CoincidentToleranceM = 0.010;

        /// <summary>
        /// Reaction at each support, in the order the positions were given.
        ///
        /// <para><paramref name="positions"/> are distances along the truss measured from its first
        /// endpoint. They need not be sorted, need not be unique, and need not lie inside
        /// [0, <paramref name="lengthM"/>] — a support projecting slightly outside is a drawing or
        /// Point0 rounding artefact, not a reason to move it.</para>
        ///
        /// <para>The mass is spread uniformly over the drawn length. Note that the truss taken from
        /// stock is usually LONGER than the segment it is drawn over — the optimiser picks the next
        /// bar that covers the span, so an 8,0 m bar routinely bridges 7,25 m — and where the extra
        /// length physically overhangs is recorded nowhere. Spreading the catalogued bar mass over
        /// the drawn segment keeps the total exactly right, which is the property the studies
        /// reconcile against, and invents no geometry.</para>
        /// </summary>
        public static double[] Reactions(IReadOnlyList<double> positions, double lengthM, double massKg)
        {
            var reactions = new double[positions?.Count ?? 0];
            if (positions is null || positions.Count == 0) return reactions;
            if (massKg <= 0 || double.IsNaN(massKg) || double.IsInfinity(massKg)) return reactions;
            if (lengthM <= 1e-6 || double.IsNaN(lengthM) || double.IsInfinity(lengthM)) return reactions;

            // Supports at the same place act as one, otherwise the span between them is zero and the
            // lever rule divides by it. Their share is split equally back over the members.
            var groups = Group(positions);
            var s = groups.Select(g => g.Position).ToArray();
            var n = s.Length;

            var perGroup = new double[n];

            if (n == 1)
            {
                // One support carries everything. Force is conserved, moment is not — the same
                // convention the transmission already uses for a point load with a single support.
                perGroup[0] = massKg;
            }
            else
            {
                var w = massKg / lengthM;

                // Partition [0, L] into intervals, each governed by ONE pair of adjacent supports:
                //   interval 0     = [0,        s1]   (the left overhang and the first bay)
                //   interval k     = [s_k,      s_k+1]
                //   interval n-2   = [s_n-2,    L]    (the last bay and the right overhang)
                // The two reaction coefficients of a pair are affine in the load position and sum to
                // one everywhere, inside the span or outside it. Because they are affine, integrating
                // them over an interval on which the governing pair does not change is EXACTLY the
                // same as putting that interval's whole mass at its centroid. So there is no
                // discretisation error and no separate cantilever branch: an overhang simply places
                // the centroid outside the pair, and one coefficient goes negative on its own, which
                // is the uplift the porte-à-faux formula already produces elsewhere in this chain.
                for (var k = 0; k < n - 1; k++)
                {
                    var lo = k == 0 ? 0.0 : s[k];
                    var hi = k == n - 2 ? lengthM : s[k + 1];

                    var span = s[k + 1] - s[k];
                    if (Math.Abs(span) < 1e-9) continue;    // cannot happen after grouping; belt and braces

                    var mass = w * (hi - lo);
                    var centroid = (lo + hi) / 2.0;

                    perGroup[k] += mass * (s[k + 1] - centroid) / span;
                    perGroup[k + 1] += mass * (centroid - s[k]) / span;
                }
            }

            for (var g = 0; g < n; g++)
            {
                var members = groups[g].Members;
                var share = perGroup[g] / members.Count;
                foreach (var index in members) reactions[index] = share;
            }

            return reactions;
        }

        /// <summary>
        /// How many distinct supports these positions describe, after co-located ones are merged.
        /// Callers use this rather than the raw count, because three crossings within 10 mm of each
        /// other are one support and put the whole mass on one place.
        /// </summary>
        public static int DistinctSupports(IReadOnlyList<double> positions) =>
            positions is null || positions.Count == 0 ? 0 : Group(positions).Count;

        /// <summary>Supports sorted along the truss, with co-located ones merged into one group.</summary>
        private static List<(double Position, List<int> Members)> Group(IReadOnlyList<double> positions)
        {
            var order = Enumerable.Range(0, positions.Count)
                                  .OrderBy(i => positions[i])
                                  .ToList();

            var groups = new List<(double Position, List<int> Members)>();

            foreach (var i in order)
            {
                if (groups.Count > 0 &&
                    Math.Abs(positions[i] - groups[^1].Position) <= CoincidentToleranceM)
                {
                    groups[^1].Members.Add(i);
                    continue;
                }

                groups.Add((positions[i], new List<int> { i }));
            }

            return groups;
        }
    }
}
