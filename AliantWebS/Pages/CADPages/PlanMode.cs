namespace AliantWebS.Pages.CADPages;

/// <summary>
/// What a press on the plan does (SVGComponent.Mode). Each page says which it wants. Middle-button drag and Shift+drag
/// pan in every mode.
/// </summary>
public enum PlanMode
{
    /// <summary>Look only: a left drag pans. Sensors are clickable where the page allows it.</summary>
    View,

    /// <summary>A left drag draws a line (LineDrawn). The toolbar offers snapping, Selection and Erase.</summary>
    Draw,

    /// <summary>A click selects a palonnier, a mother or a load; a left drag pans. Same toolbar as Draw.</summary>
    Select,

    /// <summary>A click places a sensor (PointPlaced); a left drag pans.</summary>
    PlaceSensor,
}

/// <summary>A point placed on the plan: absolute millimetres in the grid's axes, as sensor positions are stored.</summary>
public readonly record struct PlanPoint(double Xmm, double Ymm);
