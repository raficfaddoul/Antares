using AliantWebS.Models;
using AliantWebS.Plan;
using Helpers;
using Xunit;

namespace Shared.Tests.Plan;

/// <summary>
/// The plan marks where the selected palonnier hangs exactly as the load calculation hangs it.
///
/// A green dot where a palonnier is hooked on a mother, a red cross where it crosses one without being hooked. The
/// Mother Grid page computes these marks so the plan component itself needs no hanging rules; the one reading of
/// the rule stays PalonnierHanging.Resolve, shared with Calcul Charpente and the study. So every mark must be a
/// crossing Resolve found - at the point it hangs from, or its first candidate when it is not hung - saying who
/// decided, in the words DescribeDecision uses. Nothing is marked unless a palonnier is selected.
/// </summary>
public class PlanHangingMarksTests
{
    private const double Tolerance = 1e-9;

    // The venue's grid relative to E5: beams G and E, point lines 3, 4 and 5.
    private const double G = 18.697 - 33.103, E = 0;
    private const double Row3 = 7.4, Row4 = 3.7, Row5 = 0;

    private static MotherGridPalonnier El(string name, bool mother, double x1, double y1, double x2, double y2) => new()
    {
        MotherGridPalonnierId = Guid.NewGuid(), ElementTypeId = Guid.NewGuid(), Famille = mother, Name = name,
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
    };

    /// <summary>
    /// Three mother lines over bay G-E, and a palonnier along the beams at x = -10,8 from line 3 to line 5: it ends on
    /// M3 and M5 and crosses M4 in between. The optimiser left M4 unhooked for capacity; someone unhooked M5 by hand;
    /// M3 is hooked by default.
    /// </summary>
    private sealed class Rig
    {
        public MotherGridPalonnier M3 { get; } = El("M3 S500 L=14,4m", true, G, Row3, E, Row3);
        public MotherGridPalonnier M4 { get; } = El("M4 S500 L=14,4m", true, G, Row4, E, Row4);
        public MotherGridPalonnier M5 { get; } = El("M5 S500 L=14,4m", true, G, Row5, E, Row5);
        public MotherGridPalonnier Palonnier { get; } = El("P300 L=8,0m #1", false, -10.8, Row3, -10.8, Row5);

        public List<MotherGridPalonnier> Mothers => new() { M3, M4, M5 };

        public Rig()
        {
            Palonnier.Accrochages = PalonnierHooks.Serialize(new[]
            {
                new HookRecord(M4.MotherGridPalonnierId, false, HookOrigin.Optimiser, "M4 porterait 1 250 kg"),
                new HookRecord(M5.MotherGridPalonnierId, false, HookOrigin.Manual),
            });
        }
    }

    [Fact]
    public void Nothing_is_marked_without_a_selected_palonnier()
    {
        var rig = new Rig();

        Assert.Empty(PlanHangingMarks.For(null, rig.Mothers));
        Assert.Empty(PlanHangingMarks.For(rig.M4, rig.Mothers.Append(rig.Palonnier)));
    }

    [Fact]
    public void Nothing_is_marked_when_the_mothers_are_not_known()
    {
        var rig = new Rig();

        Assert.Empty(PlanHangingMarks.For(rig.Palonnier, null));
    }

    [Fact]
    public void A_palonnier_crossing_three_mothers_is_marked_at_each_crossing_with_who_decided()
    {
        var rig = new Rig();

        var marks = PlanHangingMarks.For(rig.Palonnier, rig.Mothers);

        Assert.Equal(3, marks.Count);
        Assert.Equal(new[] { true, false, false }, marks.Select(m => m.Hooked));
        Assert.Equal(
            new[]
            {
                "Accroché à M3 S500 L=14,4m — accroché par défaut",
                "Non accroché à M4 S500 L=14,4m — décroché par l’optimiseur : M4 porterait 1 250 kg",
                "Non accroché à M5 S500 L=14,4m — décroché à la main",
            },
            marks.Select(m => m.Title));

        var expected = new[] { (-10.8, Row3), (-10.8, Row4), (-10.8, Row5) };
        for (int i = 0; i < 3; i++)
            Assert.True(Math.Abs(marks[i].X - expected[i].Item1) <= Tolerance && Math.Abs(marks[i].Y - expected[i].Item2) <= Tolerance,
                $"marque {i} attendue en {expected[i]}, obtenue ({marks[i].X} ; {marks[i].Y})");
    }

    [Fact]
    public void Every_mark_is_a_crossing_as_PalonnierHanging_resolves_it()
    {
        var rig = new Rig();
        // A hand hook on M4 now overrides the optimiser's refusal there.
        rig.Palonnier.Accrochages = PalonnierHooks.SetByHand(rig.Palonnier.Accrochages, new[] { rig.M4.MotherGridPalonnierId },
            rig.M4.MotherGridPalonnierId);

        var marks = PlanHangingMarks.For(rig.Palonnier, rig.Mothers);
        var crossings = PalonnierHanging.Resolve(rig.Palonnier, rig.Mothers).Crossings;

        Assert.Equal(crossings.Count, marks.Count);
        for (int i = 0; i < crossings.Count; i++)
        {
            var crossing = crossings[i];
            var at = crossing.HungFrom ?? crossing.Candidates[0];
            var why = PalonnierHanging.DescribeDecision(crossing);

            Assert.Equal(crossing.Hooked, marks[i].Hooked);
            Assert.Equal(at.X, marks[i].X);
            Assert.Equal(at.Y, marks[i].Y);
            Assert.Equal((crossing.Hooked ? "Accroché à " : "Non accroché à ") + at.Mother.Name + " — " + why, marks[i].Title);
        }

        Assert.True(marks[1].Hooked, "le crochet posé à la main sur M4 l'emporte sur le refus de l'optimiseur");
        Assert.Equal("Accroché à M4 S500 L=14,4m — accroché à la main", marks[1].Title);
    }

    [Fact]
    public void Where_two_mothers_meet_on_a_beam_the_mark_names_the_mother_it_hangs_from()
    {
        // A palonnier along beam E, from line 3 to line 5. On line 3 two mothers meet at E: G-E ends there, E-D starts
        // there. By default the palonnier hangs from the leftmost, G-E; by hand it can hang from E-D, or from neither,
        // and a crossing hung from neither is named after its first mother.
        var left = El("M3 G-E", true, G, Row3, E, Row3);
        var right = El("M3 E-D", true, E, Row3, 40.247 - 33.103, Row3);
        var below = El("M5 G-E", true, G, Row5, E, Row5);
        var palonnier = El("P300 L=8,0m #2", false, E, Row3, E, Row5);
        var mothers = new List<MotherGridPalonnier> { right, below, left };
        var both = new[] { left.MotherGridPalonnierId, right.MotherGridPalonnierId };

        var byDefault = PlanHangingMarks.For(palonnier, mothers);
        Assert.Equal(2, byDefault.Count);
        Assert.Equal("Accroché à M3 G-E — accroché par défaut", byDefault[0].Title);

        palonnier.Accrochages = PalonnierHooks.SetByHand(null, both, right.MotherGridPalonnierId);
        Assert.Equal("Accroché à M3 E-D — accroché à la main", PlanHangingMarks.For(palonnier, mothers)[0].Title);

        palonnier.Accrochages = PalonnierHooks.SetByHand(null, both, null);
        var unhooked = PlanHangingMarks.For(palonnier, mothers)[0];
        Assert.False(unhooked.Hooked, "décroché à la main des deux mothers");
        Assert.Equal("Non accroché à M3 G-E — décroché à la main", unhooked.Title);
        Assert.True(Math.Abs(unhooked.X - E) <= Tolerance && Math.Abs(unhooked.Y - Row3) <= Tolerance,
            $"marque attendue en (0 ; 7,4), obtenue ({unhooked.X} ; {unhooked.Y})");
    }
}
