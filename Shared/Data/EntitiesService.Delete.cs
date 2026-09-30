// =============================================================
// EntitiesService.Delete.cs — Deletes that leave the circuit's shared context as they found it.
//
// Until 2026-09-18 « Supprimer le scénario » went through DeleteEntityAsync: Remove(scenario) on the shared context,
// then SaveChanges. The database refuses it for any scenario with charges scéniques (FK_ChargesScenique_EventMPScenario
// is NO ACTION; the model's ClientSetNull on a non-nullable key), and a refused delete stayed queued in the shared
// context: every later save of the circuit failed ('association severed'), or - its charges not tracked - a later save
// committed the deletes of the scenario's elements, ChargeMP and Charpente although the user had been told it failed.
//
// Now a scenario - and an event, which failed the same way - is deleted through a context of its own, children first, in
// one transaction (DeleteScenarioAsync, DeleteEventAsync), and any other delete that fails puts the shared context back as
// it was (TrackerSnapshot, DeleteEntityAsync).
// =============================================================

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.JSInterop;

namespace AliantWebS.Data
{
    public partial class EntitiesService
    {
        #region Scenario and event deletes through an owned context
        /// <summary>What a scenario delete removed with the scenario.</summary>
        public sealed record ScenarioDeletion(int ChargesSceniques, int Elements, int ElementCharges, int Charpentes);

        /// <summary>What an event delete removed with the event.</summary>
        public sealed record EventDeletion(int Scenarios, int ChargesSceniques, int Elements, int ElementCharges, int Charpentes,
                                           int Equipments, int EquipmentAttributes, int Axes, int LoadCaseLinks);

        /// <summary>
        /// Deletes a scenario and everything hanging off it - its charges scéniques, its Charpente row, its elements
        /// (mothergrids and palonniers) and their ChargeMP - as ONE change, children first, through a context of its own
        /// (RunOwnedDeleteAsync). Children first works whatever the delete rules of the foreign keys (the charges' is NO
        /// ACTION in the database); the mothergrids and palonniers go in one statement (their mother links stay within a
        /// scenario and have no foreign key).
        ///
        /// If the database refuses, nothing is deleted, the shared context is untouched, the user is told and null is
        /// returned. Once the transaction has committed nothing reports a failure: <paramref name="afterDelete"/> runs
        /// at once, with no await before it (the layout takes the scenario off its list there, so nothing resolves it in
        /// the meantime, and moves its folder to the trash - only once the row is gone), and returns what to journal
        /// about it; then the shared context forgets the rows, and one journal row records the delete. A scenario already
        /// gone (deleted from another tab) counts as deleted, and is not journaled a second time.
        /// </summary>
        public async Task<ScenarioDeletion?> DeleteScenarioAsync(EventMpscenario scenario, Func<string?>? afterDelete = null)
        {
            Guid scenarioId = scenario.EventMpscenarioId;
            Guid eventId = scenario.EventId;
            string target = Describe(scenario);   // before: afterwards the instance is detached
            DeletedRows rows;
            try
            {
                rows = await RunOwnedDeleteAsync(async (db, d) =>
                {
                    await d.Step(db.ChargeMps.Where(c => c.MotherGridPalonnier.EventMpscenarioId == scenarioId), c => c.ChargeMpid);
                    await d.Step(db.MotherGridPalonniers.Where(m => m.EventMpscenarioId == scenarioId), m => m.MotherGridPalonnierId);
                    await d.Step(db.ChargesSceniques.Where(c => c.EventMpscenarioId == scenarioId), c => c.ChargesSceniqueId);
                    await d.Step(db.Charpentes.Where(c => c.EventMpscenarioId == scenarioId), c => c.CharpenteId);
                    await d.Step(db.EventMpscenarios.Where(s => s.EventMpscenarioId == scenarioId), s => s.EventMpscenarioId);
                });
            }
            catch (Exception ex)
            {
                // Rolled back: nothing deleted, and the shared context was never touched.
                LogSaveFailure("delete", target, ex);
                await js.InvokeVoidAsync("alert",
                    $"Le scénario « {scenario.Name} » n'a pas été supprimé : {ex.GetBaseException().Message}");
                return null;
            }

            // Forgotten even when it was already gone (deleted from another tab): what this circuit still holds of it goes too.
            rows.Include<EventMpscenario>(scenarioId);
            var deleted = new ScenarioDeletion(rows.Count<ChargesScenique>(), rows.Count<MotherGridPalonnier>(),
                                               rows.Count<ChargeMp>(), rows.Count<Charpente>());
            string? afterwards = await AfterOwnedDeleteAsync(rows, afterDelete, target);

            // One row for the scenario and what went with it - the cascade is part of the one user action - under its
            // event. None when the scenario was already gone: whoever deleted it has journaled it.
            if (rows.Count<EventMpscenario>() > 0)
                await JournalEditAsync("entity.delete", target,
                    $"{deleted.ChargesSceniques} charge(s) scénique(s), {deleted.Elements} mother grid(s) et palonnier(s), "
                    + $"{deleted.ElementCharges} charge(s) MP, {deleted.Charpentes} charpente(s)"
                    + (afterwards is null ? "" : $" ; {afterwards}"),
                    severity: 1, eventId: eventId);

            return deleted;
        }

        /// <summary>
        /// Deletes an event and everything that is the event's - its scenarios with all a scenario delete removes, its
        /// equipments (each placed for this event) with their attributes, its axes and its links to load cases - as ONE
        /// change, children first, through a context of its own, as DeleteScenarioAsync does; the old single DELETE was
        /// refused as soon as one scenario had charges scéniques. Not the event's, and left alone: its structure, the
        /// load cases themselves, the elements, the types, the journal. Same contract as DeleteScenarioAsync: null and
        /// nothing changed when refused; once committed, <paramref name="afterDelete"/> (the event's folder to the trash),
        /// then the shared context forgets the rows, then one journal row - none when the event was already gone.
        /// </summary>
        public async Task<EventDeletion?> DeleteEventAsync(Event eventt, Func<string?>? afterDelete = null)
        {
            Guid eventId = eventt.EventId;
            Guid structureId = eventt.StructureId;
            string target = Describe(eventt);
            DeletedRows rows;
            try
            {
                rows = await RunOwnedDeleteAsync(async (db, d) =>
                {
                    // Its scenarios: DeleteScenarioAsync's statements, for every scenario of the event at once.
                    await d.Step(db.ChargeMps.Where(c => c.MotherGridPalonnier.EventMpscenario.EventId == eventId), c => c.ChargeMpid);
                    await d.Step(db.MotherGridPalonniers.Where(m => m.EventMpscenario.EventId == eventId), m => m.MotherGridPalonnierId);
                    await d.Step(db.ChargesSceniques.Where(c => c.EventMpscenario.EventId == eventId), c => c.ChargesSceniqueId);
                    await d.Step(db.Charpentes.Where(c => c.EventMpscenario.EventId == eventId), c => c.CharpenteId);
                    await d.Step(db.EventMpscenarios.Where(s => s.EventId == eventId), s => s.EventMpscenarioId);
                    // Its own rows.
                    await d.Step(db.AttributeEquipements.Where(a => a.Equipement.EventId == eventId), a => a.AttributeEquipementId);
                    await d.Step(db.Equipements.Where(q => q.EventId == eventId), q => q.EquipementId);
                    await d.Step(db.Axes.Where(a => a.EventId == eventId), a => a.AxeId);
                    await d.Step(db.EventLoadCases.Where(l => l.EventId == eventId), l => l.EventLoadCaseId);
                    await d.Step(db.Events.Where(e => e.EventId == eventId), e => e.EventId);
                });
            }
            catch (Exception ex)
            {
                LogSaveFailure("delete", target, ex);
                await js.InvokeVoidAsync("alert",
                    $"L'événement « {eventt.Name} » n'a pas été supprimé : {ex.GetBaseException().Message}");
                return null;
            }

            rows.Include<Event>(eventId);   // forgotten even when it was already gone, as for a scenario
            var deleted = new EventDeletion(rows.Count<EventMpscenario>(), rows.Count<ChargesScenique>(),
                                            rows.Count<MotherGridPalonnier>(), rows.Count<ChargeMp>(), rows.Count<Charpente>(),
                                            rows.Count<Equipement>(), rows.Count<AttributeEquipement>(), rows.Count<Axe>(),
                                            rows.Count<EventLoadCase>());
            string? afterwards = await AfterOwnedDeleteAsync(rows, afterDelete, target);

            if (rows.Count<Event>() > 0)
                await JournalEditAsync("entity.delete", target,
                    $"{deleted.Scenarios} scénario(s) : {deleted.ChargesSceniques} charge(s) scénique(s), "
                    + $"{deleted.Elements} mother grid(s) et palonnier(s), {deleted.ElementCharges} charge(s) MP, "
                    + $"{deleted.Charpentes} charpente(s) ; {deleted.Equipments} équipement(s) "
                    + $"({deleted.EquipmentAttributes} attribut(s)), {deleted.Axes} axe(s), {deleted.LoadCaseLinks} lien(s) aux cas de charge"
                    + (afterwards is null ? "" : $" ; {afterwards}"),
                    severity: 1, eventId: eventId, structureId: structureId);

            return deleted;
        }

        /// <summary>What an owned delete removed: how many rows per table, and the keys of those rows per entity type, read
        /// in the transaction just before each DELETE (rows this circuit never loaded included - their tracked dependents
        /// must go too).</summary>
        private sealed class DeletedRows
        {
            private readonly IModel model;
            private readonly Dictionary<Type, int> counts = new();
            public readonly Dictionary<string, HashSet<object>> Keys = new();

            public DeletedRows(IModel model) => this.model = model;

            /// <summary>How many rows of this table the DELETEs removed.</summary>
            public int Count<T>() => counts.TryGetValue(typeof(T), out var n) ? n : 0;

            /// <summary>The keys of this table's rows read for deletion so far, and those Include added.</summary>
            public HashSet<object> KeysOf<T>() =>
                Keys.TryGetValue(model.FindEntityType(typeof(T))!.Name, out var set) ? new HashSet<object>(set) : new HashSet<object>();

            public async Task Step<T>(IQueryable<T> rows, System.Linq.Expressions.Expression<Func<T, Guid>> key) where T : class
            {
                foreach (var k in await rows.Select(key).ToListAsync()) Include<T>(k);
                counts[typeof(T)] = Count<T>() + await rows.ExecuteDeleteAsync();
            }

            /// <summary>A row the shared context must forget as deleted - also when this delete found it already gone.</summary>
            public void Include<T>(object key)
            {
                string name = model.FindEntityType(typeof(T))!.Name;
                if (!Keys.TryGetValue(name, out var set)) Keys[name] = set = new HashSet<object>();
                set.Add(key);
            }
        }

        /// <summary>
        /// Runs the given DELETE statements through a context of its own, in ONE transaction. The contexts retry on
        /// transient failures (EnableRetryOnFailure), which refuses a transaction begun outside its strategy: the whole
        /// unit runs inside it, and a retry replays it from the start with fresh keys.
        ///
        /// Off the circuit's thread, as « Générer l'implantation » runs (AntaresConceptionLayout.GenerateLayout): while the
        /// transaction holds its locks, a page redrawn on the circuit reads these tables synchronously through the shared
        /// context and would block on them, and the transaction's next step would be queued behind that read on the same
        /// thread - a freeze until the command timeout, then the circuit ends. Off the thread, such a read simply waits
        /// the few milliseconds until the commit. Only the owned context is touched in there.
        /// </summary>
        private Task<DeletedRows> RunOwnedDeleteAsync(Func<AliantWebContext, DeletedRows, Task> statements) =>
            Task.Run(async () =>
            {
                await using var db = CreateOwnedContext();
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    var rows = new DeletedRows(db.Model);   // a retry starts over
                    await using var transaction = await db.Database.BeginTransactionAsync();
                    await statements(db, rows);
                    await transaction.CommitAsync();
                    return rows;
                });
            });

        /// <summary>
        /// After an owned delete committed - nothing here reports a failure: <paramref name="afterDelete"/> at once, then
        /// the shared context forgets the deleted rows, like every other method here touches it: after the other users of
        /// it. Returns what afterDelete said, to journal.
        /// </summary>
        private async Task<string?> AfterOwnedDeleteAsync(DeletedRows rows, Func<string?>? afterDelete, string target)
        {
            string? afterwards = null;
            try
            {
                afterwards = afterDelete?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntitiesService] {target} deleted; what follows the delete failed: {ex.Message}");
            }

            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                await Task.Delay(delay);
            }
            busy = true;
            try
            {
                ForgetDeleted(_appDBContext, rows.Keys);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntitiesService] {target} deleted, but the shared context could not forget its rows: {ex.Message}");
            }
            finally
            {
                busy = false;
            }
            return afterwards;
        }

        /// <summary>
        /// Which of these rows are no longer in the database - asked through a context of its own, one lookup by key per
        /// row (a join row of a many-to-many, keyed by two columns, included). DeleteEntityAsync asks it about the rows a
        /// refused delete was removing.
        /// </summary>
        private async Task<List<EntityEntry>> RowsGoneAsync(IEnumerable<EntityEntry> entries)
        {
            var gone = new List<EntityEntry>();
            await using var db = CreateOwnedContext();
            foreach (var e in entries)
            {
                var pk = e.Metadata.FindPrimaryKey();
                if (pk is null) continue;
                var keys = pk.Properties.Select(p => e.Property(p.Name).CurrentValue).ToArray();
                if (keys.Any(k => k is null)) continue;
                object? found = e.Metadata.HasSharedClrType
                    ? await db.Set<Dictionary<string, object>>(e.Metadata.Name).FindAsync(keys!)
                    : await db.FindAsync(e.Metadata.ClrType, keys!);
                if (found is null) gone.Add(e);
            }
            return gone;
        }

        /// <summary>
        /// Rows the database no longer has (RowsGoneAsync), after the snapshot of a refused delete was restored: the shared
        /// context forgets them - by the rows themselves, whatever their key - and what hangs off them, as if the delete had
        /// removed them.
        /// </summary>
        private void ForgetGoneRows(IReadOnlyList<EntityEntry> gone)
        {
            // A row Remove had started tracking (it hung off an object this circuit no longer tracked) is tracked again for
            // the fix-up that takes it out of the collections of the rows it hangs off.
            foreach (var e in gone)
                if (e.State == EntityState.Detached) e.State = EntityState.Unchanged;
            ForgetDeleted(_appDBContext, new Dictionary<string, HashSet<object>>(), gone.Select(e => e.Entity));
        }

        /// <summary>
        /// Takes out of the shared context every row that is no longer in the database, given the keys of the deleted rows
        /// per entity type (IEntityType.Name) and/or the gone rows themselves, as a successful SaveChanges takes out the
        /// rows it deleted:
        /// - a row whose key is in the deleted set, or one of the given rows;
        /// - a row that hangs off a gone row by a foreign key's value or by its navigation - a dependent the delete took
        ///   along, an unsaved row (a join row of a many-to-many included), and also a stored row whose foreign key, as
        ///   this circuit holds it, points at a gone row (an unsaved edit, or a value changed in the database since it was
        ///   read: either way the row is read again when a page next loads it) - repeated until nothing more is found, so
        ///   dependents of dependents go too, loaded navigation or not.
        /// Leaves first to Deleted, then out, with no cascade: EF then removes each one from the collections of the
        /// surviving rows it hangs off (Event.EventMpscenarios, ElementType.MotherGridPalonniers, Structure.Events,
        /// Element.Equipements, LoadCase.EventLoadCases...), so no later DetectChanges finds them there.
        /// </summary>
        private static void ForgetDeleted(DbContext db, IReadOnlyDictionary<string, HashSet<object>> deleted,
                                          IEnumerable<object>? goneRows = null)
        {
            var tracker = db.ChangeTracker;
            var entries = tracker.Entries().ToList();   // DetectChanges once: an edit a page made since is seen
            object? Key(EntityEntry e)
            {
                var pk = e.Metadata.FindPrimaryKey();
                return pk is { Properties.Count: 1 } ? e.Property(pk.Properties[0].Name).CurrentValue : null;
            }

            // The keys of every gone row by entity type: the deleted rows, then each row found gone below.
            var goneKeys = deleted.ToDictionary(d => d.Key, d => new HashSet<object>(d.Value));
            bool IsGone(string type, object? key) => key != null && goneKeys.TryGetValue(type, out var set) && set.Contains(key);
            var gone = new HashSet<object>(ReferenceEqualityComparer.Instance);
            void MarkGone(EntityEntry e)
            {
                gone.Add(e.Entity);
                if (Key(e) is not { } key) return;
                if (!goneKeys.TryGetValue(e.Metadata.Name, out var set)) goneKeys[e.Metadata.Name] = set = new HashSet<object>();
                set.Add(key);
            }
            bool PointsAtGone(EntityEntry e)
            {
                foreach (var fk in e.Metadata.GetForeignKeys())
                {
                    if (fk.Properties.Count != 1) continue;
                    if (IsGone(fk.PrincipalEntityType.Name, e.Property(fk.Properties[0].Name).CurrentValue)) return true;
                    if (fk.DependentToPrincipal is { } nav && e.Reference(nav.Name).CurrentValue is { } target && gone.Contains(target))
                        return true;
                }
                return false;
            }

            var given = new HashSet<object>(goneRows ?? Enumerable.Empty<object>(), ReferenceEqualityComparer.Instance);
            foreach (var e in entries)
                if (given.Contains(e.Entity) || IsGone(e.Metadata.Name, Key(e))) MarkGone(e);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var e in entries.Where(e => !gone.Contains(e.Entity)))
                    if (PointsAtGone(e)) { MarkGone(e); changed = true; }
            }

            // Leaves first: a row is marked Deleted only when no other gone row still hangs off it (no conceptual null).
            var goneEntries = entries.Where(e => gone.Contains(e.Entity)).ToList();
            var dependents = goneEntries.ToDictionary(e => e.Entity, _ => 0, ReferenceEqualityComparer.Instance);
            var parentsOf = new Dictionary<object, List<object>>(ReferenceEqualityComparer.Instance);
            var byKey = goneEntries.Where(e => Key(e) != null)
                .GroupBy(e => (e.Metadata.Name, Key(e)!)).ToDictionary(g => g.Key, g => g.First().Entity);
            foreach (var e in goneEntries)
            {
                var parents = new List<object>();
                foreach (var fk in e.Metadata.GetForeignKeys())
                {
                    if (fk.Properties.Count != 1) continue;
                    object? parent = null;
                    if (fk.DependentToPrincipal is { } nav && e.Reference(nav.Name).CurrentValue is { } t && gone.Contains(t)) parent = t;
                    else if (e.Property(fk.Properties[0].Name).CurrentValue is { } v
                             && byKey.TryGetValue((fk.PrincipalEntityType.Name, v), out var p)) parent = p;
                    if (parent != null && !ReferenceEquals(parent, e.Entity)) { parents.Add(parent); dependents[parent]++; }
                }
                parentsOf[e.Entity] = parents;
            }
            var order = new List<EntityEntry>();
            var pending = goneEntries.ToList();
            while (pending.Count > 0)
            {
                var leaves = pending.Where(e => dependents[e.Entity] == 0).ToList();
                if (leaves.Count == 0) leaves = pending.ToList();   // a cycle: none in this model
                foreach (var e in leaves)
                {
                    order.Add(e);
                    pending.Remove(e);
                    foreach (var p in parentsOf[e.Entity]) dependents[p]--;
                }
            }

            bool detect = tracker.AutoDetectChangesEnabled;
            var cascade = tracker.CascadeDeleteTiming;
            var orphans = tracker.DeleteOrphansTiming;
            tracker.AutoDetectChangesEnabled = false;
            // Nothing is marked Deleted by a cascade while the rows are taken out: every tracked dependent is in the list
            // above (the search follows foreign-key values). Should one be missed all the same, detaching its principal
            // would still null its foreign key or cascade-delete it (EF's delete fix-up does not look at these timings).
            tracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            tracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
            try
            {
                // An Added row goes straight out (EF fixes the collections up for it too); the others through Deleted, as
                // SaveChanges does with the rows it deleted - Detached straight from Unchanged or Modified, a row would
                // stay in the collections of the rows it hangs off.
                foreach (var e in order) e.State = e.State == EntityState.Added ? EntityState.Detached : EntityState.Deleted;
                foreach (var e in order) if (e.State == EntityState.Deleted) e.State = EntityState.Detached;
            }
            finally
            {
                tracker.DeleteOrphansTiming = orphans;
                tracker.CascadeDeleteTiming = cascade;
                tracker.AutoDetectChangesEnabled = detect;
            }

            // Out of the collections of the rows they hang off that this circuit does not track (EF's fix-up above only
            // reaches tracked ones): an object a page kept after it was deleted would otherwise still hold them, and a later
            // Remove or Update of it would attach them again.
            foreach (var e in order)
                foreach (var n in e.Metadata.GetNavigations().Where(n => !n.IsCollection && n.Inverse is { IsCollection: true }))
                    if (n.PropertyInfo?.GetValue(e.Entity) is { } principal && n.Inverse!.PropertyInfo?.GetValue(principal) is IList owners)
                        owners.Remove(e.Entity);

            // The forgotten objects no longer hold one another: their collections are emptied (a scenario its charges,
            // elements and Charpente; an element its ChargeMP; an event its scenarios) and their references to one another
            // cleared (a Charpente its scenario, a ChargeMP its element). A page that still holds one and hands it to
            // Update() then attaches that row alone - for a row gone from the database one UPDATE that affects nothing,
            // which UpdateEntityAsync detaches again - instead of its whole deleted graph. Their references to rows that
            // remain (an element's ElementType) are kept: pages still showing them read those.
            foreach (var e in order)
                foreach (var n in e.Metadata.GetNavigations().Where(n => n.PropertyInfo != null))
                {
                    var value = n.PropertyInfo!.GetValue(e.Entity);
                    if (n.IsCollection)
                    {
                        if (value is IList list) list.Clear();
                    }
                    else if (value != null && gone.Contains(value) && n.PropertyInfo.CanWrite)
                    {
                        n.PropertyInfo.SetValue(e.Entity, null);
                    }
                }
        }
        #endregion

        #region Failed delete: the shared context put back
        /// <summary>
        /// The shared context as a delete found it, so that a delete the database refuses is undone in memory too.
        ///
        /// Remove(entity) does not wait for SaveChanges. EF's cascade timing is Immediate: it marks the entity Deleted,
        /// marks Deleted every tracked row that cascades from it, takes tracked Added rows that cascade from it out of
        /// the context (and out of the collections of the rows they hang off), and for a tracked row whose relationship
        /// does not cascade (ClientSetNull): a required foreign key - ChargesScenique → EventMPScenario - gets a
        /// "conceptual null" and Remove throws at the first such row; an optional one is set to null and the row marked
        /// Modified. A failed SaveChanges changes none of it back, and every later SaveChanges of the circuit sends it
        /// again. Restore undoes all of it.
        ///
        /// The failed SaveChanges itself changes one thing more: the original key of an Added row becomes the id SQL
        /// Server generated for it in the INSERT that was rolled back.
        ///
        /// Taken just before Remove: every entry's state; for entries that are not Unchanged their current and original
        /// values, modified flags and temporary keys; for Added entries the rows their navigations point to, which of
        /// their navigations are marked loaded, and the order of the collections they sit in. Entries() runs
        /// DetectChanges first, so an edit a page made on its entities since the last detection (a grid bound to them) is
        /// recorded as Modified - which is how SaveChanges and every Entry()/Entries() call see it anyway
        /// (AutoDetectChangesEnabled is never turned off in this application).
        /// </summary>
        internal sealed class TrackerSnapshot
        {
            private sealed class Item
            {
                public object Entity = null!;
                // The entry itself, not looked up again by object: a join row of a many-to-many (AspNetUserRole) is a
                // property bag, which db.Entry() cannot look up once the cascade has detached it - its own entry can
                // still be put back.
                public EntityEntry Entry = null!;
                public EntityState State;
                // Not for Unchanged entries: their values are their original values, which Remove does not change.
                public string[]? Names;
                public object?[]? Current;
                public object?[]? Original;
                public bool[]? Modified;
                public bool[]? Temporary;
                // Added entries only: the cascade takes them out of the context, and EF's fix-up with them.
                public (string Name, object? Target)[]? References;
                public (object Owner, string Name, object[] Items)[]? Collections;
                public (string Name, bool Loaded)[]? Loaded;
            }

            private readonly List<Item> items = new();
            private readonly HashSet<object> tracked = new(ReferenceEqualityComparer.Instance);
            private readonly Dictionary<object, EntityState> states = new(ReferenceEqualityComparer.Instance);

            /// <summary>The state an entity had when this was taken - Detached when it was not tracked.</summary>
            public EntityState StateOf(object entity) =>
                states.TryGetValue(entity, out var state) ? state : EntityState.Detached;

            public static TrackerSnapshot Take(DbContext db)
            {
                var snapshot = new TrackerSnapshot();
                foreach (var e in db.ChangeTracker.Entries())   // DetectChanges, then every entry
                {
                    var item = new Item { Entity = e.Entity, Entry = e, State = e.State };
                    if (e.State != EntityState.Unchanged)
                    {
                        var props = e.Properties.ToList();
                        item.Names = props.Select(p => p.Metadata.Name).ToArray();
                        item.Current = props.Select(p => p.CurrentValue).ToArray();
                        item.Original = props.Select(p => p.OriginalValue).ToArray();
                        item.Modified = props.Select(p => p.IsModified).ToArray();
                        item.Temporary = props.Select(p => p.IsTemporary).ToArray();
                    }
                    if (e.State == EntityState.Added)
                    {
                        var references = e.References.ToList();
                        item.References = references.Select(r => (r.Metadata.Name, r.CurrentValue)).ToArray();
                        var collections = new List<(object, string, object[])>();
                        foreach (var r in references)
                        {
                            var inverse = (r.Metadata as INavigation)?.Inverse;
                            if (r.CurrentValue == null || inverse == null || !inverse.IsCollection || inverse.PropertyInfo == null) continue;
                            if (inverse.PropertyInfo.GetValue(r.CurrentValue) is IEnumerable list)
                                collections.Add((r.CurrentValue, inverse.Name, list.Cast<object>().ToArray()));
                        }
                        item.Collections = collections.ToArray();
                        item.Loaded = e.Navigations.Select(n => (n.Metadata.Name, n.IsLoaded)).ToArray();
                    }
                    snapshot.items.Add(item);
                    snapshot.tracked.Add(e.Entity);
                    snapshot.states[e.Entity] = e.State;
                }
                return snapshot;
            }

            /// <summary>
            /// Puts the shared context back as it was taken: after a failed Remove/SaveChanges the entries have the same
            /// states, values, flags and navigations, the edits nobody saved yet included, and nothing is left queued.
            /// </summary>
            public void Restore(DbContext db)
            {
                var tracker = db.ChangeTracker;
                bool detect = tracker.AutoDetectChangesEnabled;
                var cascade = tracker.CascadeDeleteTiming;
                var orphans = tracker.DeleteOrphansTiming;
                // No DetectChanges halfway through, and no state change here may cascade to other rows.
                tracker.AutoDetectChangesEnabled = false;
                tracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
                tracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
                try
                {
                    // 1. Rows Remove started tracking - it attaches an untracked entity and every row it reaches - go out
                    //    again. Through Unchanged: Deleted -> Detached would null the keys of tracked rows pointing at them.
                    foreach (var e in tracker.Entries().Where(e => !tracked.Contains(e.Entity)).ToList())
                    {
                        if (e.State is EntityState.Deleted or EntityState.Modified) e.State = EntityState.Unchanged;
                        e.State = EntityState.Detached;
                    }

                    // 2. Added rows the cascade took out: their navigations first (EF nulled the ones pointing at a row it
                    //    took out), then tracked again, the rows they point to first, each with its temporary key back.
                    var readd = items.Where(i => i.State == EntityState.Added && i.Entry.State == EntityState.Detached).ToList();
                    foreach (var i in readd)
                    {
                        var e = i.Entry;
                        foreach (var (name, target) in i.References!)
                            if (!ReferenceEquals(e.Navigation(name).CurrentValue, target))
                                e.Navigation(name).CurrentValue = target;
                    }
                    var pending = new HashSet<object>(readd.Select(i => i.Entity), ReferenceEqualityComparer.Instance);
                    while (pending.Count > 0)
                    {
                        var ready = readd.Where(i => pending.Contains(i.Entity)
                            && i.References!.All(r => r.Target == null || !pending.Contains(r.Target))).ToList();
                        if (ready.Count == 0) ready = readd.Where(i => pending.Contains(i.Entity)).ToList();   // a cycle
                        foreach (var i in ready)
                        {
                            var e = i.Entry;
                            e.State = EntityState.Added;
                            for (int k = 0; k < i.Names!.Length; k++)
                            {
                                var p = e.Property(i.Names[k]);
                                if (!p.Metadata.IsPrimaryKey() || !i.Temporary![k]) continue;
                                if (!Equals(p.CurrentValue, i.Current![k])) p.CurrentValue = i.Current[k];
                                p.IsTemporary = true;
                            }
                            pending.Remove(i.Entity);
                        }
                    }

                    // 3. Every entry whose state Remove changed, and every entry that had changes of its own.
                    foreach (var i in items)
                    {
                        var e = i.Entry;
                        if (i.State == EntityState.Unchanged)
                        {
                            if (e.State == EntityState.Unchanged) continue;
                            if (e.State == EntityState.Modified)
                            {
                                // A conceptual null: the foreign key keeps its value, flagged null and marked modified.
                                // Writing the value back clears the flag; un-marking it returns the row to Unchanged.
                                foreach (var p in e.Properties)
                                    if (p.IsModified) { p.CurrentValue = p.OriginalValue; p.IsModified = false; }
                            }
                            if (e.State != EntityState.Unchanged) e.State = EntityState.Unchanged;   // Deleted -> Unchanged
                            continue;
                        }

                        // Deleted -> Modified marks every property modified: the flags are put right below.
                        if (e.State != i.State) e.State = i.State;
                        for (int k = 0; k < i.Names!.Length; k++)
                        {
                            var p = e.Property(i.Names[k]);
                            // Also clears a conceptual null: EF writes a value equal to the current one when the flag is set.
                            if (!p.Metadata.IsPrimaryKey()) p.CurrentValue = i.Current![k];
                        }
                        if (i.State == EntityState.Deleted) continue;
                        for (int k = 0; k < i.Names.Length; k++)
                        {
                            var p = e.Property(i.Names[k]);
                            // An Added row's original key is the id SQL Server generated in the rolled-back INSERT.
                            if (!Equals(p.OriginalValue, i.Original![k])) p.OriginalValue = i.Original[k];
                            if (i.State == EntityState.Modified && p.IsModified != i.Modified![k]) p.IsModified = i.Modified[k];
                        }
                    }

                    // 4. A row tracked again starts with no navigation marked loaded; and it was appended to the
                    //    collections it sits in: both put back.
                    foreach (var i in readd)
                    {
                        var e = i.Entry;
                        foreach (var (name, loaded) in i.Loaded!)
                            if (e.Navigation(name).IsLoaded != loaded) e.Navigation(name).IsLoaded = loaded;
                    }
                    foreach (var i in readd)
                        foreach (var (owner, name, before) in i.Collections!)
                        {
                            if (db.Entry(owner).Navigation(name).CurrentValue is not IList list
                                || list.Cast<object>().SequenceEqual(before, ReferenceEqualityComparer.Instance)) continue;
                            list.Clear();
                            foreach (var x in before) list.Add(x);
                        }
                }
                finally
                {
                    tracker.DeleteOrphansTiming = orphans;
                    tracker.CascadeDeleteTiming = cascade;
                    tracker.AutoDetectChangesEnabled = detect;
                }
            }
        }
        #endregion
    }
}
