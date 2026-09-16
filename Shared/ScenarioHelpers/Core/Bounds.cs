// =============================================================
// Bounds.cs — How far from optimal, cheaply: the floor no layout of this plan could go below.
//
// The optimiser is greedy. Greedy answers are not optimal ones, and until now nothing in the tool could
// say by how much - a study could report 6 palonniers without anyone knowing whether 5 were possible.
// These bounds are the cheap half of that answer: computed per run from the same inputs the optimiser
// gets, in a few milliseconds, they give the study a number to sit beside its own.
//
// ── WHAT THESE ARE NOT ───────────────────────────────────────────────────────────────────────
// A bound does NOT prove that a better layout exists, and it is not a target. It says one thing only:
// no layout of this plan can do better than this figure. A greedy answer sitting on the bound is
// optimal on that criterion and the search can stop; a greedy answer far above it may still be the best
// there is - the bound is simply weak, and every bound here is deliberately weak rather than clever.
//
// That is the rule the whole file is written to: A LOWER BOUND MUST NEVER ASSUME. Wherever the geometry
// leaves a choice - a charge that could hang from either of two point lines, a palonnier whose span is
// not fixed - the choice contributes nothing, because assuming the cheaper of two possibilities once
// would tell a study it is closer to optimal than it is. That is the one mistake a bound must not make;
// being too low is only unhelpful, being too high is wrong.
//
// Pure functions over the optimiser's own inputs: no stock is taken, no result is read, nothing placed.
// =============================================================

using RiggingOptimizer.Models;
using static RiggingOptimizer.Core.Geometry;

namespace RiggingOptimizer.Core;

// ── Le plancher, par critère ─────────────────────────────────────────────────
/// <summary>
/// What no layout of one plan can go below. Each figure is a floor on the matching quantity of
/// <see cref="LayoutCost"/>, never an estimate of it.
/// </summary>
/// <param name="Palonniers">The fewest palonniers the geometry can be covered with.</param>
/// <param name="Pieces">The fewest stock pieces the forced trusses can be assembled from.</param>
/// <param name="MotherMetres">The mother length (m) no layout can avoid drawing.</param>
public sealed record LayoutBound(int Palonniers, int Pieces, double MotherMetres)
{
    /// <summary>Nothing to carry: every floor is zero.</summary>
    public static LayoutBound Empty { get; } = new(0, 0, 0);

    /// <summary>The one line the console prints and the study quotes.</summary>
    public string Describe() =>
        $"Plancher théorique : au moins {Palonniers} palonnier(s), {Pieces} pièce(s) et "
        + $"{MotherMetres.ToString("N2", Objective.Fr)} m de ligne de mother — "
        + "aucune implantation de ce plan ne peut faire moins.";

    /// <summary>
    /// What a measured layout spends above the floor, criterion by criterion. Never negative on the
    /// palonniers and the pieces: those two floors bound exactly what the layout counts.
    /// <para>
    /// The metres are read with care. <see cref="LayoutCost.DrawnMetres"/> is the whole layout, mother
    /// lines AND palonniers; <see cref="MotherMetres"/> floors the mother lines alone, since no honest
    /// floor exists for a palonnier's span. So that gap is an upper limit on what could be saved, and
    /// never a target: most of it is palonnier length that had to be drawn.
    /// </para>
    /// </summary>
    public LayoutGap Gap(LayoutCost cost) => new(
        cost.Palonniers  - Palonniers,
        cost.Pieces      - Pieces,
        cost.DrawnMetres - MotherMetres);
}

/// <summary>What a layout spends above the floor. See <see cref="LayoutBound.Gap"/> for how to read it.</summary>
/// <param name="Palonniers">Palonniers above the fewest possible.</param>
/// <param name="Pieces">Pieces above the fewest possible.</param>
/// <param name="Metres">Drawn metres above the mother-line floor - palonnier length included, so an upper limit.</param>
public sealed record LayoutGap(int Palonniers, int Pieces, double Metres)
{
    public string Describe() =>
        $"Écart au plancher : {Palonniers} palonnier(s), {Pieces} pièce(s), "
        + $"{Metres.ToString("N2", Objective.Fr)} m (longueur des palonniers comprise).";
}

// ── Les bornes ───────────────────────────────────────────────────────────────
/// <summary>
/// Lower bounds on what any layout of a plan could cost. See the file's header for what they are not.
/// </summary>
public static class Bounds
{
    /// <summary>
    /// The three floors for one plan.
    /// </summary>
    /// <param name="charges">The plan's charges, in the grid's axes - the same list the optimiser gets.</param>
    /// <param name="motherStock">The pieces of MotherGrill.xlsx, for the piece floor.</param>
    /// <param name="palonnierStock">The pieces of Palonnier.xlsx. Optional: without it a palonnier is floored at one piece.</param>
    /// <param name="options">The optimiser's settings: only OrthogonalOnly changes a bound.</param>
    public static LayoutBound For(
        IReadOnlyList<Charge> charges,
        PieceStock motherStock,
        PieceStock? palonnierStock = null,
        OptimizerOptions? options = null)
        => new(
            PalonnierCountBound(charges, options),
            PieceCountBound(charges, motherStock, palonnierStock, options),
            MotherMetresBound(charges, options));

    // ══════════════════════════════════════════════════════════
    // MÈTRES DE MOTHER
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The mother length no layout can avoid drawing (m).
    /// <para>
    /// A point line needs a line only if something MUST hang there, and what must hang there is a charge
    /// sitting on that point line, within Geometry.TOL of it: wherever else the layout puts its
    /// palonniers, that charge hangs from that line. The run of bays is then fixed by the leftmost and
    /// the rightmost such demand, snapped to the beams that bracket them and reaching past the outermost
    /// beam where a demand lies beyond it - which is exactly what MotherLines.Plan computes, minus the
    /// freedom of model and of pieces. So this asks Plan itself, with the forced demands alone, rather
    /// than copying its rules and letting the two drift apart.
    /// </para>
    /// <para>
    /// A charge that is NOT within TOL of a point line hangs from a palonnier whose ends the layout is
    /// free to place on either of the two lines bracketing it, and free to place anywhere along them. It
    /// forces no line and no bay by itself, so it contributes nothing here. A bound must never assume.
    /// </para>
    /// <para>
    /// Note for the caller: Plan builds Mother objects, and a Mother takes the next id from a static
    /// counter. These are thrown away, but their ids are spent - so call this AFTER the run's real lines
    /// are planned (Optimizer.Optimize does, at the very end), never in the middle of one.
    /// </para>
    /// <para>
    /// OrthogonalOnly loosens nothing here - a charge on a point line hangs from that line whether or
    /// not inclined palonniers are allowed - and the parameter is kept only so the three bounds read the
    /// same inputs the optimiser does.
    /// </para>
    /// </summary>
    public static double MotherMetresBound(IReadOnlyList<Charge> charges, OptimizerOptions? options = null)
        => MotherLines.Plan(ForcedDemands(charges)).Sum(line => line.Length);

    // ══════════════════════════════════════════════════════════
    // NOMBRE DE PALONNIERS
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The fewest palonniers the plan can be covered with.
    /// <para>
    /// A charge further than Geometry.TOL from every point line cannot hang on a line: it needs a
    /// palonnier. So the count of those charges, divided by the most charges ONE palonnier could ever
    /// carry given the geometry alone - charges sharing an x within TOL for a palonnier along the beams,
    /// or lying within Geometry.ColinearTol of one axis when inclined palonniers are allowed.
    /// </para>
    /// <para>
    /// It is weak, and honestly so: it ignores the stock entirely. No piece may be long enough to reach
    /// across that group, and no model strong enough to carry it - in which case the real layout needs
    /// more palonniers than this, which is exactly the direction a floor is allowed to be wrong in. The
    /// group size is counted generously for the same reason: over-counting what one palonnier can carry
    /// lowers the floor, and a lower floor can never over-estimate.
    /// </para>
    /// </summary>
    public static int PalonnierCountBound(IReadOnlyList<Charge> charges, OptimizerOptions? options = null)
    {
        options ??= OptimizerOptions.Default;

        var hung = charges.Where(c => Math.Abs(c.Y - NearestRow(c.Y).Y) > TOL).ToList();
        if (hung.Count == 0) return 0;

        // Along the beams: the charges that share an x. Counted around each charge in turn rather than by
        // buckets, so a chain of charges each within TOL of the next counts as one group - generous, and
        // generous is the safe side here.
        int most = 1;
        foreach (var c in hung)
            most = Math.Max(most, hung.Count(o => Math.Abs(o.X - c.X) <= TOL));

        // Inclined: the charges lying near one axis, measured as GroupGenerator measures collinearity -
        // the distance from the axis through two of them. No cap on the group size here: GroupGenerator
        // stops at four charges, and a floor that ignored that stop is only ever lower.
        if (!options.OrthogonalOnly)
        {
            for (int i = 0; i < hung.Count; i++)
            {
                for (int j = i + 1; j < hung.Count; j++)
                {
                    double ax = hung[j].X - hung[i].X, ay = hung[j].Y - hung[i].Y;
                    double norm = Math.Sqrt(ax * ax + ay * ay);
                    if (norm < 1e-9) continue;

                    int near = hung.Count(o =>
                        Math.Abs(ax * (o.Y - hung[i].Y) - ay * (o.X - hung[i].X)) / norm <= ColinearTol);
                    most = Math.Max(most, near);
                }
            }
        }

        return (hung.Count + most - 1) / most;   // arrondi supérieur, en entiers
    }

    // ══════════════════════════════════════════════════════════
    // NOMBRE DE PIÈCES
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// The fewest stock pieces the plan can be assembled from.
    /// <para>
    /// For every truss the geometry forces - each mother line of <see cref="MotherMetresBound"/>, and
    /// each palonnier of <see cref="PalonnierCountBound"/> - the span divided by the longest piece in
    /// that role's stock, rounded up. Assembling from the longest piece there is gives the fewest
    /// pieces, so nothing can do better; a real layout picks its model for what a bay must carry, not
    /// for length, and so uses more.
    /// </para>
    /// <para>
    /// A palonnier's span is not forced by the geometry, but its ends land on two different point lines
    /// (Geometry.EnclosingRows), so it is at least as long as the closest two point lines are apart.
    /// Without a palonnier stock to measure against, it counts as one piece - the floor of floors.
    /// </para>
    /// </summary>
    public static int PieceCountBound(
        IReadOnlyList<Charge> charges,
        PieceStock motherStock,
        PieceStock? palonnierStock = null,
        OptimizerOptions? options = null)
    {
        double longestMother = LongestPiece(motherStock);

        int pieces = 0;
        foreach (var line in MotherLines.Plan(ForcedDemands(charges)))
            pieces += PiecesFor(line.Length, longestMother);

        int perPalonnier = 1;
        if (palonnierStock is not null)
            perPalonnier = Math.Max(1, PiecesFor(ClosestRowsApart(), LongestPiece(palonnierStock)));

        return pieces + PalonnierCountBound(charges, options) * perPalonnier;
    }

    // ── Ce qui sert aux trois ────────────────────────────────────────────────

    /// <summary>
    /// The demands no layout can move: a charge within Geometry.TOL of a point line, at its own x. The
    /// others are free and are left out - see <see cref="MotherMetresBound"/>.
    /// </summary>
    private static List<(int Row, double Y, double X)> ForcedDemands(IReadOnlyList<Charge> charges)
    {
        var forced = new List<(int Row, double Y, double X)>();
        foreach (var c in charges)
        {
            var (row, y) = NearestRow(c.Y);
            if (Math.Abs(c.Y - y) <= TOL) forced.Add((row, y, c.X));
        }
        return forced;
    }

    /// <summary>The longest piece the inventory holds for one role, whatever its model. Zero when it holds none.</summary>
    private static double LongestPiece(PieceStock stock) =>
        stock.Rows.Select(r => r.Length).DefaultIfEmpty(0).Max();

    /// <summary>
    /// How many pieces of <paramref name="longestPieceM"/> it takes to reach <paramref name="lengthM"/>,
    /// asked for the way the optimiser asks it (MotherLines.Size and Optimizer.AssemblyFor both allow
    /// themselves Geometry.TOL short). Zero when the inventory holds no piece at all.
    /// </summary>
    private static int PiecesFor(double lengthM, double longestPieceM) =>
        longestPieceM <= 0 ? 0 : (int)Math.Ceiling(Math.Max(0.01, lengthM - TOL) / longestPieceM - 1e-9);

    /// <summary>The distance between the two closest point lines (m): the shortest a palonnier can be.</summary>
    private static double ClosestRowsApart()
    {
        double gap = double.PositiveInfinity;
        for (int k = 1; k < RowYList.Count; k++)
            gap = Math.Min(gap, RowYList[k].Y - RowYList[k - 1].Y);
        return double.IsInfinity(gap) ? 0 : gap;
    }
}
