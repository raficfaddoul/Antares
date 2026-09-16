// =============================================================
// Geometry.cs  — Constantes et fonctions géométriques de la grille
// Source: AliantWebS.Models.CharpenteCoordinates (coordonnées réelles)
// Coordonnées en mètres, relatives au point E5 (centre de la grille)
// =============================================================

using AliantWebS.Models;

namespace RiggingOptimizer.Core;

public static class Geometry
{
    // ── Tolérances ───────────────────────────────────────────────
    public const double TOL          = 0.08;   // tolérance position (m)

    // Distance maximale au-delà de l'extrémité dessinée d'une Mother à laquelle un palonnier la croise
    // encore, entre ses propres extrémités. UNE seule constante, lue par l'optimiseur
    // (Optimizer.HangOnCrossings) et par le calcul (Helpers.PalonnierHanging) : tout croisement que le
    // calcul trouve est un croisement que l'optimiseur a tranché. Le micromètre absorbe les arrondis.
    public const double CrossingTol  = TOL + 1e-6;
    public const double ColinearTol  = 0.20;   // tolérance colinéarité (m)

    // ── Colonnes I→A (index 0→8, centrées sur E=index 4) ────────
    //
    // La poutre J existe dans la salle et dans le cahier des charges, mais pas ici, et c'est voulu : les six
    // études manuscrites ne l'utilisent jamais. Chacune la cite une seule fois, au § 2.3, pour dire ce
    // qu'elle admet — 500 daN un point sur deux (J1, J3, J5, J7, J9), zéro aux autres, 2 500 daN pour toute
    // la poutre, contre 9 000 à 13 500 pour les autres — et aucune ne la fait figurer dans son tableau des
    // charges par point d'accroche. Elle est aussi à 3,797 m de I, là où toutes les autres travées font 7,14
    // à 7,70 m : une demi-travée en rive. L'optimiseur implante donc la grille que les études implantent.
    //
    // Attention, la dissymétrie est réelle : le calcul de charge, lui, connaît J, parce qu'il balaie
    // CharpenteCoordinates (LoadTransmissionHelper.CharpenteSupportsOf). Une ligne de mother qui déborde
    // au-delà de I peut donc se voir attribuer une charge sur un point J que l'optimiseur n'a jamais prévu.
    public static readonly string[] Cols = { "I","H","G","F","E","D","C","B","A" };
    public static readonly int[]    Rows = { 1,2,3,4,5,6,7,8,9 };

    // Listes triées par valeur (utilisées pour recherche encadrante)
    public static readonly IReadOnlyList<(string Col, double X)> ColXList;
    public static readonly IReadOnlyList<(int Row, double Y)>    RowYList;

    // Grille complète: (col,row) → (x,y)
    public static readonly IReadOnlyDictionary<(string, int), (double X, double Y)> Grid;

    // ── Initialisation statique ───────────────────────────────────
    static Geometry()
    {
        // Point de référence: E5 = centre de la grille
        double refX = CharpenteCoordinates.Points["E5"].X;   // m (CharpenteCoordinates is now in metres)

        // ── X de chaque poutre, relatif à E, en mètres (via la ligne 5) ────────
        var colX = new Dictionary<string, double>();
        foreach (var col in Cols)
        {
            if (CharpenteCoordinates.Points.TryGetValue($"{col}5", out var pt))
                colX[col] = Math.Round(pt.X - refX, 6);
        }

        // ── Y réel de chaque ligne de points, en mètres (via la colonne E) ──
        var rowY = new Dictionary<int, double>();
        foreach (int r in Rows)
        {
            if (CharpenteCoordinates.Points.TryGetValue($"E{r}", out var pt))
                rowY[r] = Math.Round(pt.Y, 6);
        }

        // ── Listes triées ─────────────────────────────────────────
        ColXList = colX.OrderBy(kv => kv.Value)
                       .Select(kv => (kv.Key, kv.Value))
                       .ToList();

        RowYList = rowY.OrderBy(kv => kv.Value)
                       .Select(kv => (kv.Key, kv.Value))
                       .ToList();

        // ── Grille complète ───────────────────────────────────────
        var grid = new Dictionary<(string, int), (double, double)>();
        foreach (var (col, cx) in ColXList)
            foreach (var (row, ry) in RowYList)
                grid[(col, row)] = (cx, ry);
        Grid = grid;
    }

    // ── Ligne de grille la plus proche de y ──────────────────────
    public static (int Row, double Y) NearestRow(double y)
        => RowYList.MinBy(kv => Math.Abs(kv.Y - y));

    // ── Lignes de grille encadrant [yMin, yMax] ──────────────────
    // Retourne (ligneEnDessous, ligneAuDessus) garantissant des réactions positives
    public static ((int Row, double Y) Below, (int Row, double Y) Above) EnclosingRows(double yMin, double yMax)
    {
        // Ligne immédiatement en dessous de yMin
        (int Row, double Y) below = RowYList[0];
        foreach (var rv in RowYList)
            if (rv.Y <= yMin + TOL) below = rv;

        // Ligne immédiatement au dessus de yMax
        (int Row, double Y) above = RowYList[^1];
        foreach (var rv in RowYList)
        {
            if (rv.Y >= yMax - TOL) { above = rv; break; }
        }

        // Si identiques: prendre la suivante
        if (below.Row == above.Row)
        {
            int idx = RowYList.ToList().FindIndex(rv => rv.Row == above.Row);
            if (idx < RowYList.Count - 1)
                above = RowYList[idx + 1];
            else
                below = RowYList[idx - 1];
        }

        return (below, above);
    }

    // ── Projection scalaire de P sur l'axe A→B ───────────────────
    // Retourne la distance signée depuis A (normalisée par |AB|)
    public static double ProjOnAxis(double px, double py,
                                    double ax, double ay,
                                    double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return 0.0;
        return ((px - ax) * dx + (py - ay) * dy) / len;
    }

    // ── Réactions en 2 points A et B ─────────────────────────────
    // charges: liste de (x, y, weight)
    public static (double Ra, double Rb) Reactions2Pt(
        IEnumerable<(double X, double Y, double W)> charges,
        (double X, double Y) A,
        (double X, double Y) B)
    {
        double len = Math.Sqrt(Math.Pow(B.X - A.X, 2) + Math.Pow(B.Y - A.Y, 2));
        double total = charges.Sum(c => c.W);
        if (len < 1e-9) return (total / 2, total / 2);

        double moment = charges.Sum(c =>
            c.W * ProjOnAxis(c.X, c.Y, A.X, A.Y, B.X, B.Y));
        double rB = moment / len;
        double rA = total - rB;
        return (Math.Round(rA, 3), Math.Round(rB, 3));
    }
}
