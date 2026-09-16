using System.ComponentModel.DataAnnotations.Schema;
using Helpers;

namespace AliantWebS.Models;

/// <summary>
/// The load's position in the studies' axes (Helpers.StudyAxes) - X along the beams towards point 9,
/// Y across them towards J - for the pages and the files people read and type. The stored X and Y stay
/// in the grid's axes. Not mapped, and in their own file, so re-scaffolding the entity cannot remove them.
/// </summary>
public partial class ChargesScenique
{
    [NotMapped]
    public double? StudyX
    {
        get => Y is { } y ? StudyAxes.StudyX(y) : null;
        set => Y = value is { } v ? StudyAxes.GridY(v) : null;
    }

    [NotMapped]
    public double? StudyY
    {
        get => X is { } x ? StudyAxes.StudyY(x) : null;
        set => X = value is { } v ? StudyAxes.GridX(v) : null;
    }
}
