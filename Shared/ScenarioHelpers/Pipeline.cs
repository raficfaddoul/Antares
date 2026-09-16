// =============================================================
// Pipeline.cs — « Générer l'implantation », d'un bout à l'autre.
//
// The orchestration only: what happens, in what order, and what refuses. Each step it calls lives in a file
// of its own - the plan in PlanSource, the settings in OptimizerSettings, the database in
// LayoutPersistence, the plan and the workbook in LayoutArtifacts, what the user reads in
// Pipeline.Messages, the rows in Pipeline.Entities - so that this file can be read in one sitting and says
// the whole story.
//
// Two refusals matter more than anything else here: a layout that leaves a charge unplaced, and one whose
// attachment points do not carry what the charges weigh. Saving REPLACES the scenario's elements, so
// either of those, saved, would destroy a good layout and put nothing in its place.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;

namespace RiggingOptimizer;

public static partial class Pipeline
{
    /// <param name="commonDataDir">
    /// Shared input folder used when the scenario has no copy of one of the workbooks. Optional, so
    /// existing callers keep working.
    /// </param>
    public static async Task GenerateLayout(
        string dataDir,
        string outputDir,
        EntitiesService entitiesService,
        Guid eventMpscenarioId,
        string? commonDataDir = null)
    {
        Directory.CreateDirectory(outputDir);

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // A context of its own, not the circuit's. The shared one is bound to every open page: a Charges row
        // still being typed sits in it as a tracked, modified entity, so a SaveChanges made here would write
        // an edit nobody confirmed - without the page's own name checks and without the journal - and a save
        // that failed would leave its entry stuck there for the next page to trip over. Owning the context
        // means this work writes what it means to write, and disposes of everything else.
        using var db = entitiesService.CreateOwnedContext();

        try
        {
            // ── 1. Le stock ───────────────────────────────────────────
            // The inventory only. The plan of charges comes from the Charges page (§ 3): Charges.xlsx is
            // never opened by the placement.
            Console.WriteLine("Chargement des données...");
            var (pals, mothers) = ExcelLoader.LoadInventory(dataDir, commonDataDir);

            // ── 2. Les ElementType manquants ──────────────────────────
            var category = LayoutPersistence.EnsureElementTypes(db, pals, mothers);

            // ── 3. Le plan de charges, depuis la page Charges ─────────
            var charges = PlanSource.Read(db, eventMpscenarioId);

            Console.WriteLine($"  {charges.Count} charge(s) de la page Charges | {pals.Count} réf. Palonnier "
                              + $"| {mothers.Count} réf. Mother");

            // ── 4. Optimisation ───────────────────────────────────────
            // What the optimiser prefers, and how hard it looks for it, are settings a developer owns
            // (Settings page). The domain code never reads them: they arrive here as options.
            var options = await OptimizerSettings.ReadAsync();
            Console.WriteLine("  " + OptimizerSettings.Describe(options));

            var result = Optimizer.Optimize(charges, pals, mothers, verbose: true, options: options);

            // A layout that leaves loads out is not saved. Saving replaces the scenario's elements, and a
            // stock whose trusses cannot reach across the grid would replace them with nothing at all.
            if (result.Uncovered.Count > 0)
                throw new InvalidOperationException(UncoveredMessage(result, pals, mothers));

            // Every charge carried, yet the attachment points not carrying them: the result contradicts
            // itself (for instance two charges under one name, which the optimiser tells apart by name only).
            if (!result.BalanceOk)
                throw new InvalidOperationException(
                    $"Les points d'accroche portent {Math.Round(result.TotalGrid)} kg pour {Math.Round(result.TotalCharges)} kg "
                    + "de charges : l'implantation calculée n'est pas cohérente et n'a pas été enregistrée. "
                    + "L'implantation existante n'a pas été modifiée.");

            // ── 5. Enregistrement, en une seule transaction ───────────
            var elementTypes = LayoutPersistence.ElementTypesFor(db, result, category);
            var (palEntities, motherEntities) = BuildElementEntities(result, eventMpscenarioId, elementTypes);
            var chargeMpEntities = BuildChargeMps(result, palEntities, motherEntities);

            await LayoutPersistence.ReplaceElementsAsync(db, eventMpscenarioId,
                palEntities.Concat(motherEntities).ToList(), chargeMpEntities);

            // ── 6. Le plan et le classeur ─────────────────────────────
            var (planPath, xlsxPath) = LayoutArtifacts.Write(result, outputDir);

            Console.WriteLine("\n✅ Terminé !");

            // Ce que l'implantation a coûté, et le plancher qu'aucune implantation de ce plan ne peut
            // passer. Both are read off the result - Optimizer.Optimize measures them once at the end of the
            // run (RiggingResult.Cost, RiggingResult.Bound) - and never measured again here, so this
            // summary, the classeur Excel and a comparison between two runs quote the same figures.
            Console.WriteLine($"   Coût : {result.Cost.Describe()}.");
            if (result.Bound is { } bound)
                Console.WriteLine($"   {bound.Describe()} {bound.Gap(result.Cost).Describe()}");
            Console.WriteLine($"   Recherche : {result.Search.Describe()}");

            Console.WriteLine($"   Plan:  {planPath}");
            Console.WriteLine($"   Excel: {xlsxPath}");
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"❌ Fichier manquant: {ex.FileName}");
            // Only the inventory is read from disk now. Naming Charges.xlsx here would send the user to a
            // file the placement never opens, since the plan comes from the Charges page.
            Console.Error.WriteLine($"   Vérifiez que le dossier '{dataDir}' contient:");
            Console.Error.WriteLine($"   {InventoryFiles.MothersFileName}, {InventoryFiles.PalonniersFileName}");

            // Rethrow so the caller can tell the user. Swallowing this made a failed run look exactly like a
            // successful one: the spinner stopped and the output folder stayed empty.
            var missing = Path.GetFileName(ex.FileName) ?? "?";
            throw new InvalidOperationException(
                $"Fichier d'entrée manquant : {missing}. Ajoutez-le aux fichiers du scénario, "
                + "ou au dossier partagé _common/data.", ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Erreur: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            throw;
        }
    }
}
