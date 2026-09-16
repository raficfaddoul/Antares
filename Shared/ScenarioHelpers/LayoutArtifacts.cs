// =============================================================
// LayoutArtifacts.cs — Le plan et le classeur qu'une génération laisse derrière elle.
//
// Written after the layout is saved, and deliberately after: these two files are a view of what was placed,
// not part of placing it. A failure here costs a picture, never the implantation.
// =============================================================

using RiggingOptimizer.IO;
using RiggingOptimizer.Models;

namespace RiggingOptimizer;

internal static class LayoutArtifacts
{
    /// <summary>The plan drawing and the workbook, written into the scenario's output folder.</summary>
    internal static (string PlanPath, string XlsxPath) Write(RiggingResult result, string outputDir)
    {
        Console.WriteLine("\nGénération du plan...");
        string planPath = Path.Combine(outputDir, "rigging_plan_v5.png");
        PlanDrawer.Draw(result, planPath);

        Console.WriteLine("Génération du rapport Excel...");
        string xlsxPath = Path.Combine(outputDir, "rigging_report_v5.xlsx");
        ExcelExporter.Export(result, xlsxPath);

        return (planPath, xlsxPath);
    }
}
