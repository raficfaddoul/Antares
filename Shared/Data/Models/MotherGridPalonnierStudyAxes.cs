using System.ComponentModel.DataAnnotations.Schema;
using Helpers;

namespace AliantWebS.Models;

/// <summary>
/// The element's ends in the studies' axes (Helpers.StudyAxes) - X along the beams towards point 9, Y
/// across them towards J - for the pages and the files people read and type. The stored X1..Y2 stay in
/// the grid's axes. Not mapped, and in their own file, so re-scaffolding the entity cannot remove them.
/// </summary>
public partial class MotherGridPalonnier
{
    [NotMapped]
    public double? StudyX1
    {
        get => Y1 is { } y ? StudyAxes.StudyX(y) : null;
        set => Y1 = value is { } v ? StudyAxes.GridY(v) : null;
    }

    [NotMapped]
    public double? StudyY1
    {
        get => X1 is { } x ? StudyAxes.StudyY(x) : null;
        set => X1 = value is { } v ? StudyAxes.GridX(v) : null;
    }

    [NotMapped]
    public double? StudyX2
    {
        get => Y2 is { } y ? StudyAxes.StudyX(y) : null;
        set => Y2 = value is { } v ? StudyAxes.GridY(v) : null;
    }

    [NotMapped]
    public double? StudyY2
    {
        get => X2 is { } x ? StudyAxes.StudyY(x) : null;
        set => X2 = value is { } v ? StudyAxes.GridX(v) : null;
    }
}
