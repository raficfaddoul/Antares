// =============================================================
// LayoutPersistence.cs — Writing a layout into the database, as one change.
//
// A generation replaces a scenario's elements: everything that was there goes, everything computed arrives.
// Half of that is worse than none of it, so it is one transaction, replayed whole by the retrying strategy
// the context is configured with, on a context this work owns rather than the circuit's shared one.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using Helpers;
using Microsoft.EntityFrameworkCore;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;

namespace RiggingOptimizer;

internal static class LayoutPersistence
{
    /// <summary>
    /// The ElementType rows the inventory's models need, created when they are missing, and the category
    /// they live in. Returns null when the category itself is absent - the run goes on, and the elements
    /// are saved without a type rather than not at all.
    /// </summary>
    internal static Category? EnsureElementTypes(
        AliantWebContext db,
        IReadOnlyCollection<RiggingOptimizer.Models.BarSpec> pals,
        IReadOnlyCollection<RiggingOptimizer.Models.BarSpec> mothers)
    {
        var category = db.Categories.FirstOrDefault(x => x.Name.ToLower() == "mothergridpalonnier");
        if (category is null)
        {
            Console.Error.WriteLine("⚠️ Catégorie 'mothergridpalonnier' introuvable — ElementTypes non créés.");
            return null;
        }

        var allBarTypes = pals.Select(b => b.Type)
            .Concat(mothers.Select(b => b.Type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingNames = db.ElementTypes
            .Where(et => et.CategoryId == category.CategoryId)
            .Select(et => et.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newElementTypes = allBarTypes
            .Where(t => !existingNames.Contains(t))
            .Select(t => new ElementType
            {
                ElementTypeId = Guid.NewGuid(),
                Name          = t,
                CategoryId    = category.CategoryId,
            })
            .ToList();

        if (newElementTypes.Count > 0)
        {
            db.ElementTypes.AddRange(newElementTypes);
            db.SaveChanges();
            Console.WriteLine($"  {newElementTypes.Count} ElementType(s) créé(s) dans la catégorie '{category.Name}'.");
        }
        else
        {
            Console.WriteLine("  Tous les ElementTypes existent déjà.");
        }

        return category;
    }

    /// <summary>
    /// The ElementType row of each model the layout uses, by name.
    ///
    /// Looked up the way they were created: in the mothergridpalonnier category. Names are not unique in
    /// ElementTypes and the type pages let anyone type one in any category, so unscoped this picks up a row
    /// belonging to another category - and, once the missing ones have been added beside it, two rows of one
    /// name. Grouped rather than keyed straight, because a stray duplicate must not stop a generation: the
    /// first row of the name is used and the run goes on.
    /// </summary>
    internal static Dictionary<string, ElementType> ElementTypesFor(
        AliantWebContext db, RiggingOptimizer.Models.RiggingResult result, Category? category)
    {
        var names = result.Palonniers.Select(p => p.Bar.Type)
            .Concat(result.Mothers.Select(m => m.Bar.Type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = db.ElementTypes.Where(et => names.Contains(et.Name));
        if (category is not null)
            rows = rows.Where(et => et.CategoryId == category.CategoryId);

        return rows
            .ToList()
            .Where(et => et.Name is not null)
            .GroupBy(et => et.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces a scenario's elements and their charges as ONE change. If anything fails the database keeps
    /// the layout it had, rather than a scenario emptied halfway, and the context forgets the rows it was
    /// about to write.
    /// </summary>
    internal static async Task ReplaceElementsAsync(
        AliantWebContext db,
        Guid eventMpscenarioId,
        List<MotherGridPalonnier> elements,
        List<ChargeMp> chargeMps)
    {
        // The context retries on transient failures (EnableRetryOnFailure, Startup), and such a strategy refuses
        // any query in a transaction opened outside it. So the whole unit runs through the strategy: a retry
        // replays all of it, from a clean tracker, and a failed attempt leaves nothing behind.
        var strategy = db.Database.CreateExecutionStrategy();
        int removed = 0, removedCharges = 0;
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync();

                var existing = db.MotherGridPalonniers
                    .Where(m => m.EventMpscenarioId == eventMpscenarioId)
                    .ToList();

                removed = existing.Count;
                removedCharges = 0;
                if (existing.Count > 0)
                {
                    // Les ChargeMp liées d'abord, pour les clés étrangères.
                    var ids = existing.Select(m => m.MotherGridPalonnierId).ToList();
                    var existingChargeMps = db.ChargeMps.Where(c => ids.Contains(c.MotherGridPalonnierId)).ToList();
                    removedCharges = existingChargeMps.Count;
                    db.ChargeMps.RemoveRange(existingChargeMps);
                    db.MotherGridPalonniers.RemoveRange(existing);
                    await db.SaveChangesAsync();
                }

                db.MotherGridPalonniers.AddRange(elements);
                db.ChargeMps.AddRange(chargeMps);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            });
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }

        Console.WriteLine($"  {removed} élément(s) et {removedCharges} ChargeMp existant(s) remplacé(s) par "
                          + $"{elements.Count(e => !e.Famille)} Palonnier(s), {elements.Count(e => e.Famille)} "
                          + $"ligne(s) de mother et {chargeMps.Count} ChargeMp.");
    }
}
