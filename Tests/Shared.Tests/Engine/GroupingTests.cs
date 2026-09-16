using RiggingOptimizer.Core;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// The groups of charges one palonnier could carry - everything the placement then chooses between.
///
/// Two kinds. Charges sharing an x are carried by a palonnier along the beams, hung from the point line
/// below them to the one above: those two lines are where the load leaves the palonnier, so a group is
/// always drawn between them and never between the charges themselves. Charges lying on an inclined line
/// are carried by one inclined palonnier - but only while they really are on ONE line.
///
/// That last word is the subject of this file. The axis a palonnier is built on runs from its lowest
/// charge in y to its highest, and every charge must lie within Geometry.ColinearTol (0,20 m) of THAT
/// axis - not of some other line that happens to pass near them, such as the one through the two charges
/// furthest apart in x. Measured against the wrong line, a charge ends up hanging 0,30 m beside a truss
/// the study then verifies as if it were underneath it.
/// </summary>
public class GroupingTests
{
    /// <summary>With no stock to consult, every span and every load is buildable: what is checked here is the geometry.</summary>
    private static readonly Func<double, double, bool> Anything = (_, _) => true;

    // ── Palonniers along the beams ───────────────────────────────

    [Fact]
    public void Charges_sharing_an_x_give_every_group_of_them_one_palonnier_could_carry()
    {
        // Three loads on one vertical: seven groups - each load alone, each pair, and all three. The
        // placement needs them all, because which grouping is cheapest depends on the stock it has left.
        var charges = new List<Charge>
        {
            new() { Name = "A", X = 2.0, Y = 1.0, Weight = 100 },
            new() { Name = "B", X = 2.0, Y = 2.0, Weight = 100 },
            new() { Name = "C", X = 2.0, Y = 3.0, Weight = 100 },
        };

        var groups = GroupGenerator.Generate(charges, Anything);

        Assert.True(groups.Count == 7,
            $"{groups.Count} groupes au lieu de 7 : "
            + string.Join(" ; ", groups.Select(g => $"{g.Type} {string.Join(",", g.Names)}")));
        Assert.All(groups, g => Assert.Equal(PalonnierDirection.Y, g.Type));
        Assert.All(groups, g => Assert.Equal(g.A.X, g.B.X, 9));
    }

    [Fact]
    public void A_group_along_the_beams_hangs_from_the_point_line_below_its_charges_to_the_one_above()
    {
        // A palonnier is hung from point lines, not from the charges: its two ends are grid lines, and its
        // span is the gap between them. Ending it at the charges would size it for a shorter truss than
        // the one that has to be built.
        var charges = new List<Charge>
        {
            new() { Name = "A", X = 2.0, Y = 1.0, Weight = 100 },
            new() { Name = "B", X = 2.0, Y = 2.0, Weight = 100 },
        };

        var group = GroupGenerator.Generate(charges, Anything).Single(g => g.Charges.Count == 2);

        Assert.True(OnAPointLine(group.A.Y), $"l'extrémité basse {group.A.Y} n'est pas sur une ligne de points");
        Assert.True(OnAPointLine(group.B.Y), $"l'extrémité haute {group.B.Y} n'est pas sur une ligne de points");
        Assert.True(group.A.Y <= 1.0 + Geometry.TOL && group.B.Y >= 2.0 - Geometry.TOL,
            $"les charges (1,0 et 2,0) ne sont pas encadrées par {group.A.Y} et {group.B.Y}");
        Assert.Equal(group.B.Y - group.A.Y, group.Span, 9);
    }

    // ── Inclined palonniers ──────────────────────────────────────

    [Fact]
    public void Two_charges_at_the_same_height_are_not_one_inclined_palonnier()
    {
        // Side by side along a point line, they are a mother line's business, not a palonnier's: an
        // inclined palonnier between them would hang from a single point line at both ends.
        var charges = new List<Charge>
        {
            new() { Name = "A", X = 0.0, Y = 0.0, Weight = 100 },
            new() { Name = "B", X = 4.0, Y = 0.02, Weight = 100 },
        };

        var groups = GroupGenerator.Generate(charges, Anything);

        Assert.DoesNotContain(groups, g => g.Charges.Count == 2);
    }

    [Fact]
    public void An_inclined_palonnier_carries_at_most_four_charges()
    {
        // Five loads exactly on one line. Beyond four, the number of groups to weigh grows faster than the
        // placement can use them, and a truss with five hanging loads on it is not how the venue rigs.
        var charges = Enumerable.Range(0, 5)
            .Select(i => new Charge { Name = $"A{i}", X = i, Y = i, Weight = 100 })
            .ToList();

        var inclined = GroupGenerator.Generate(charges, Anything)
            .Where(g => g.Type == PalonnierDirection.Biais)
            .ToList();

        Assert.Contains(inclined, g => g.Charges.Count == 4);
        Assert.True(inclined.Max(g => g.Charges.Count) == 4,
            $"un groupe incliné de {inclined.Max(g => g.Charges.Count)} charges a été proposé");
    }

    [Fact]
    public void Three_charges_are_not_one_inclined_palonnier_when_one_lies_0_30_m_off_its_axis()
    {
        // Q1, Q2 and Q3 are near the line through the two furthest apart in x. But the palonnier is built
        // from the lowest charge in y to the highest - Q1 to Q3 - and from THAT axis, Q2 lies 0,30 m away.
        // Measured against the wrong line, this group would look collinear and be built.
        var charges = new List<Charge>
        {
            new() { Name = "Q1", X = 0.0, Y = 0.0, Weight = 100 },
            new() { Name = "Q2", X = 4.0, Y = 0.1, Weight = 100 },
            new() { Name = "Q3", X = 2.0, Y = 0.2, Weight = 100 },
        };

        var groups = GroupGenerator.Generate(charges, Anything);

        Assert.DoesNotContain(groups, g => g.Type == PalonnierDirection.Biais && g.Charges.Count == 3);
    }

    [Fact]
    public void Every_inclined_palonnier_holds_its_charges_within_0_20_m_of_its_own_axis()
    {
        // Two hundred plans: three or four loads scattered up to 0,30 m either side of a random line, plus
        // three anywhere. The seed is fixed, so a failure can be replayed. Every inclined group of three or
        // four that comes out is measured against its own axis - the line from its own end A to its own end
        // B - which is the axis the study projects the charges onto and the crew builds the truss along.
        var rnd = new Random(4242);
        int inclinedGroups = 0, beyond = 0;
        double worst = 0;

        for (int seed = 0; seed < 200; seed++)
        {
            double x0 = rnd.NextDouble() * 20 - 10, y0 = rnd.NextDouble() * 20 - 10;
            double angle = rnd.NextDouble() * Math.PI;

            var plan = new List<Charge>();
            int onLine = rnd.Next(3, 5);
            for (int i = 0; i < onLine; i++)
            {
                double s = rnd.NextDouble() * 6, off = (rnd.NextDouble() - 0.5) * 0.6;
                plan.Add(new Charge
                {
                    Name = $"L{seed}_{i}",
                    X = x0 + s * Math.Cos(angle) - off * Math.Sin(angle),
                    Y = y0 + s * Math.Sin(angle) + off * Math.Cos(angle),
                    Weight = 100,
                });
            }
            for (int i = 0; i < 3; i++)
            {
                plan.Add(new Charge
                {
                    Name = $"O{seed}_{i}",
                    X = rnd.NextDouble() * 20 - 10,
                    Y = rnd.NextDouble() * 20 - 10,
                    Weight = 100,
                });
            }

            foreach (var group in GroupGenerator.Generate(plan, Anything)
                         .Where(g => g.Type == PalonnierDirection.Biais && g.Charges.Count >= 3))
            {
                inclinedGroups++;
                double off = FurthestFromAxis(group);
                worst = Math.Max(worst, off);
                if (off > Geometry.ColinearTol + 1e-3) beyond++;
            }
        }

        // Said out loud, because a rule nothing ever exercised is a rule nobody is testing.
        Assert.True(inclinedGroups > 20,
            $"seulement {inclinedGroups} groupes inclinés de trois ou quatre charges : le contrôle n'a rien à vérifier");
        Assert.True(beyond == 0,
            $"{beyond} groupes inclinés sur {inclinedGroups} ont une charge au-delà de "
            + $"{Geometry.ColinearTol} m de leur axe, la pire à {worst:0.###} m");
    }

    [Fact]
    public void With_orientations_orthogonales_uniquement_no_inclined_group_is_offered()
    {
        // Every hand-written study rigs along and across the beams only. With the setting on, the charges
        // may end up on more palonniers - but never on an inclined one.
        var charges = new List<Charge>
        {
            new() { Name = "D1", X = 0.0, Y = 0.0, Weight = 100 },
            new() { Name = "D2", X = 2.0, Y = 1.3, Weight = 100 },
            new() { Name = "D3", X = 4.0, Y = 2.6, Weight = 100 },
        };

        var groups = GroupGenerator.Generate(charges, Anything, allowBiais: false);

        Assert.DoesNotContain(groups, g => g.Type == PalonnierDirection.Biais);
        Assert.NotEmpty(groups);
    }

    // ── How a group is measured ──────────────────────────────────

    /// <summary>The charge furthest from the group's own axis, A to B, in metres.</summary>
    private static double FurthestFromAxis(CandidateGroup group)
    {
        double dx = group.B.X - group.A.X, dy = group.B.Y - group.A.Y;
        double norm = Math.Sqrt(dx * dx + dy * dy);

        return group.Charges.Max(c => Math.Abs(dx * (c.Y - group.A.Y) - dy * (c.X - group.A.X)) / norm);
    }

    private static bool OnAPointLine(double y) => Geometry.RowYList.Any(r => Math.Abs(r.Y - y) < 1e-9);
}
