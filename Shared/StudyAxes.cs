// =============================================================
// StudyAxes.cs — The axes the studies use, and the grid the calculations work in.
//
// Every coordinate a person reads or types follows the hand-written studies (Figure 2 of each): the
// origin at E5, X along the beams, positive towards point 9, and Y across the beams, positive towards
// beam J. The calculations keep their own axes - the optimiser's grid, the load transmission,
// CharpenteCoordinates, and the element and load coordinates stored in the database: X across the
// beams, positive towards beam A, and Y along them, positive towards point 1.
//
// Both are measured from the event's Point0 (E5 at Antarès) and they differ by a reflection:
// (x, y) -> (-y, -x). That is its own inverse, so converting twice gives back what was given.
//
// Convert only where coordinates meet a person or a file: the pages, the plan, Charges.xlsx and the
// element import, the DXF, the Word study and the Excel report. Never inside the calculations.
// =============================================================

namespace Helpers;

public static class StudyAxes
{
    /// <summary>A position relative to Point0, from the grid's axes to the studies'.</summary>
    public static (double X, double Y) ToStudy(double gridX, double gridY) => (-gridY, -gridX);

    /// <summary>A position relative to Point0, from the studies' axes to the grid's.</summary>
    public static (double X, double Y) ToGrid(double studyX, double studyY) => (-studyY, -studyX);

    /// <summary>The studies' X (along the beams, towards point 9) of a position whose grid Y is given.</summary>
    public static double StudyX(double gridY) => -gridY;

    /// <summary>The studies' Y (across the beams, towards J) of a position whose grid X is given.</summary>
    public static double StudyY(double gridX) => -gridX;

    /// <summary>The grid's X of a position whose studies' Y is given.</summary>
    public static double GridX(double studyY) => -studyY;

    /// <summary>The grid's Y of a position whose studies' X is given.</summary>
    public static double GridY(double studyX) => -studyX;
}
