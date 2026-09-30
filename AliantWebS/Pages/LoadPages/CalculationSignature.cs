// =============================================================
// CalculationSignature.cs — Everything « Calcul Charpente » reads, as one piece of text, so the MotherGrid
// page can tell whether a result still describes what it shows.
//
// The calculation signs what it was given (ChargeMPComponent, just before it runs); the page signs what it
// holds, and draws the result only while the two are equal. Both sides use this one function, so any
// difference between the rows the calculation read and the rows the page holds - a row added or deleted
// while the calculation waited on the database, an instance the page kept after a failed save - hides the
// result instead of passing it off as current.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AliantWebS.Models;
using RiggingOptimizer.IO;

namespace AliantWebS.Pages.LoadPages;

public static class CalculationSignature
{
    /// <summary>
    /// The scenario, Point0 and the inventory files, then each element and each charge by id. The name is in because a
    /// truss with no pieces recorded is weighed by the length its name gives.
    /// </summary>
    public static string Of(Guid scenarioId, double? point0X, double? point0Y, string inventoryStamp,
        IEnumerable<MotherGridPalonnier>? elements, IEnumerable<ChargeMp>? charges)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double? v) => v?.ToString("R", inv) ?? "";

        var text = new StringBuilder()
            .Append(scenarioId).Append('|')
            .Append(N(point0X)).Append('|').Append(N(point0Y)).Append('|')
            .Append(inventoryStamp);
        foreach (var e in (elements ?? Enumerable.Empty<MotherGridPalonnier>()).OrderBy(e => e.MotherGridPalonnierId))
            text.Append("|e").Append(e.MotherGridPalonnierId).Append(',').Append(e.Famille).Append(',').Append(e.ElementTypeId)
                .Append(',').Append(e.Name)
                .Append(',').Append(N(e.X1)).Append(',').Append(N(e.Y1)).Append(',').Append(N(e.X2)).Append(',').Append(N(e.Y2))
                .Append(',').Append(e.Composition).Append(',').Append(e.Accrochages)
                .Append(',').Append(e.MotherAId).Append(',').Append(e.MotherBId);
        foreach (var c in (charges ?? Enumerable.Empty<ChargeMp>()).OrderBy(c => c.ChargeMpid))
            text.Append("|c").Append(c.ChargeMpid).Append(',').Append(c.MotherGridPalonnierId)
                .Append(',').Append(N(c.X)).Append(',').Append(N(c.Charge));
        return text.ToString();
    }

    /// <summary>
    /// The inventory workbooks the calculation weighs the trusses with - the scenario's (dataDir), else the shared ones -
    /// as full path, size and time: the path shows a switch from the shared copy to the scenario's own. "unreadable" when
    /// the folders cannot be read.
    /// </summary>
    public static string InventoryStamp(string? dataDir, string? commonDataDir)
    {
        try
        {
            var text = new StringBuilder();
            foreach (var name in new[] { InventoryFiles.MothersFileName, InventoryFiles.PalonniersFileName })
            {
                var path = InputFiles.Find(dataDir, name) ?? InputFiles.Find(commonDataDir, name);
                var file = path is null ? null : new FileInfo(path);
                text.Append(path).Append(',').Append(file?.Length).Append(',').Append(file?.LastWriteTimeUtc.Ticks).Append('|');
            }
            return text.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "unreadable";
        }
    }
}
