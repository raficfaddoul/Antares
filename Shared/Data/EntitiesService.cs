using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Data.Common;
//using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.JSInterop;

namespace AliantWebS.Data
{


    public partial class EntitiesService
    {
        #region Property
        public AliantWebContext _appDBContext;

           private readonly IDbContextFactory<AliantWebContext> _DBFactory;

        /// <summary>
        /// A context of its own, for work that must not write through the circuit's shared one.
        ///
        /// _appDBContext is scoped to the Blazor circuit and shared with every page: a grid that is still
        /// being edited holds tracked rows in it, so a SaveChanges made elsewhere flushes edits nobody
        /// confirmed, and a failed save leaves an entry stuck there for the next one to trip over. Work that
        /// owns a unit - the layout generation replacing a scenario's elements in one transaction - takes
        /// its own context here and disposes it, leaving the page's context untouched either way.
        ///
        /// The caller owns what it gets and must dispose it.
        /// </summary>
        public AliantWebContext CreateOwnedContext() => _DBFactory.CreateDbContext();


        private bool busy = false;
        private int delay = 5;
        private int retrys = 20;

        public CancellationTokenSource cts;
        //   public CancellationToken ct;

        public List<Task> tasksList = new List<Task>();

        #endregion
        #region Constructor

        AppData appData;

        IJSRuntime js;

        readonly JournalService journal;

        /// <summary>
        /// Turns off journaling for the next writes on this service. Set it around bookkeeping the
        /// user did not ask for — the placeholder attribute rows AttributesComponent creates when an
        /// element is opened, for instance — so the journal stays a record of intent, not of plumbing.
        /// </summary>
        public bool SuppressJournal { get; set; }

        public EntitiesService(AliantWebContext appDBContext, AppData appData, IJSRuntime js,
                               IDbContextFactory<AliantWebContext> DBFactory, JournalService journal)
        {
            this.appData = appData;
            _appDBContext = appDBContext;
            this.js = js;
            _DBFactory = DBFactory;
            this.journal = journal;


            cts = new CancellationTokenSource();
            //  ct = cts.Token;
        }
        #endregion

        #region Journaling
        // Every page edit funnels through the methods below, so this is the one place that can record
        // "every edit" without touching each page. The explicit journal calls in the pages remain:
        // they carry intent ("threshold raised from 1200 to 1500"), which a generic row cannot.

        /// <summary>Human-readable name of an entity: its Name property, else its key.</summary>
        private static string Describe(object entity)
        {
            if (entity == null) return null;

            var type = entity.GetType();
            string typeName = type.Name;

            string label = null;
            var nameProp = type.GetProperty("Name");
            if (nameProp != null && nameProp.PropertyType == typeof(string))
                label = nameProp.GetValue(entity) as string;

            if (string.IsNullOrWhiteSpace(label))
            {
                var keyProp = type.GetProperty(typeName + "Id") ?? type.GetProperty(typeName + "ID");
                label = keyProp?.GetValue(entity)?.ToString();
            }

            return string.IsNullOrWhiteSpace(label) ? typeName : $"{typeName} « {label} »";
        }

        /// <summary>
        /// Which properties actually changed, as "Field: old → new". Must be called BEFORE
        /// SaveChanges — afterwards EF resets original values to the saved ones and every diff is
        /// gone. Returns null when nothing measurable changed (a detached entity passed to Update()
        /// has no original values to compare against).
        /// </summary>
        private string DescribeChanges(object entity)
        {
            try
            {
                return DescribeChanges(_appDBContext.Entry(entity));
            }
            catch
            {
                return null;   // never let describing a change break the change
            }
        }

        /// <summary>The same, for an entry of any context - an owned one's included (UpdateOwnedAsync).</summary>
        private static string DescribeChanges(EntityEntry entry)
        {
            try
            {
                var lines = new List<string>();

                foreach (var p in entry.Properties)
                {
                    if (p.Metadata.IsPrimaryKey()) continue;

                    var oldValue = p.OriginalValue;
                    var newValue = p.CurrentValue;
                    if (Equals(oldValue, newValue)) continue;

                    lines.Add($"{p.Metadata.Name}: {Show(oldValue)} → {Show(newValue)}");
                }

                return lines.Count == 0 ? null : string.Join("\n", lines);
            }
            catch
            {
                return null;   // never let describing a change break the change
            }
        }

        private static string Show(object v)
        {
            if (v == null) return "(vide)";
            var s = v.ToString();
            if (string.IsNullOrWhiteSpace(s)) return "(vide)";
            return s.Length <= 200 ? s : s.Substring(0, 200) + "…";
        }

        private async Task JournalEditAsync(string action, string target, string details, byte severity = 0,
                                            Guid? eventId = null, Guid? structureId = null)
        {
            if (SuppressJournal || journal == null) return;
            await journal.WriteAsync(JournalCategory.Edition, action, target, details, severity,
                                     structureId: structureId, eventId: eventId);
        }
        #endregion




        public async Task LoadReferenceAsync(object entity, params string[] propertyName)
        {


            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break; ;
                await Task.Delay(delay);
            }
            busy = true;

            foreach (string s in propertyName)
            {

                await _appDBContext.Entry(entity).Reference(s).LoadAsync();
            }
            busy = false;


        }

        public void LoadReference(object entity, params string[] propertyName)
        {
            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                Thread.Sleep(delay);
            }
            busy = true;
            foreach (string s in propertyName)
            {

                _appDBContext.Entry(entity).Reference(s).Load();
            }
            busy = false;
        }

        public void LoadCollection(object entity, params string[] collectionNames)
        {
            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                Thread.Sleep(delay);
            }
            busy = true;
            foreach (var collectionName in collectionNames)
            {
                _appDBContext.Entry(entity).Collection(collectionName).Load();
            }
            busy = false;
        }

        /// <summary>
        /// The rows of an entity's collection as the database holds them now.
        ///
        /// A navigation collection is not a reliable picture of the database. LoadCollection does nothing once
        /// EF marks the collection loaded, even if rows were written since by another user or through an owned
        /// context; and a collection never loaded still holds whatever other queries of the circuit happened to
        /// track - the tree loads elements without their AttributeElements, so they are empty unless some page
        /// read them. This asks the database every time.
        ///
        /// The rows come back tracked. A row this context already tracks keeps its in-memory values, which are
        /// what the next SaveChanges writes; rows marked for deletion in this context are left out. The
        /// navigation itself is not trusted afterwards either: use the list returned.
        /// </summary>
        public async Task<List<TRelated>> QueryCollectionAsync<TEntity, TRelated>(
            TEntity entity, Expression<Func<TEntity, IEnumerable<TRelated>>> collection)
            where TEntity : class
            where TRelated : class
        {
            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                await Task.Delay(delay);
            }
            busy = true;
            try
            {
                var rows = await _appDBContext.Entry(entity).Collection(collection).Query().ToListAsync();
                return rows.Where(r => _appDBContext.Entry(r).State != EntityState.Deleted).ToList();
            }
            finally
            {
                busy = false;
            }
        }
        #region Get List of entities
        public async Task<List<T>> GetAllEntitiesAsync<T>(params string[] includes) where T : class
        {


            //_appDBContext = await _DBFactory.CreateDbContextAsync();
            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                await Task.Delay(delay);
            }
            busy = true;

            List<T> v;
            if (includes.Length != 0)
            {
                var vv = _appDBContext.Set<T>().Include(includes[0]);
                for (int i = 1; i < includes.Length; i++)
                {
                    vv = vv.Include(includes[i]);
                }
                v = vv.ToList();
            }
            else
            {
                v = await _appDBContext.Set<T>().ToListAsync();
            }


            busy = false;
            return v;

        }



        public async Task<List<T>> GetFilteredEntitiesAsync<T>(Func<T, bool> predicate, params string[] includes) where T : class
        {
 
                //_appDBContext = await _DBFactory.CreateDbContextAsync();

                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;
            List<T> v;
            if (includes.Length != 0)
            {
                var vv = _appDBContext.Set<T>().Include(includes[0]);
                for (int i = 1; i < includes.Length; i++)
                {
                    vv = vv.Include(includes[i]);
                }
                v = vv.Where<T>(predicate).ToList();
            }
            else
            {
                v = _appDBContext.Set<T>().Where<T>(predicate).ToList();
            }
            busy = false;
            return await Task.FromResult(v);

          
          
  
        }


        #endregion
        #region Get entity by Id
        public async Task<T> GetEntityAsync<T>(Guid entityID) where T : class
        {

                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }

                busy = true;
            //_appDBContext = await _DBFactory.CreateDbContextAsync();
            var entity = (await _appDBContext.FindAsync<T>(entityID));

    
                busy = false;
                return entity;

        }

        public T GetEntity<T>(long entityID) where T : class
        {

            //_appDBContext = await _DBFactory.CreateDbContextAsync();
            busy = true;
            var entity = _appDBContext.Find<T>(entityID);

            busy = false;

            return entity;



        }

        public async Task<T> GetEntityAsync<T>(int entityID) where T : class
        {
  
                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;
                //_appDBContext = await _DBFactory.CreateDbContextAsync();
                var t = _appDBContext.FindAsync<T>(new object[] { entityID }, cts.Token);
                var entity = await t;

                busy = false;
                return entity;

        }

        public async Task<T> GetFilteredFirstEntityAsync<T>(Func<T, bool> predicate) where T : class
        {

                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;
                //_appDBContext = await _DBFactory.CreateDbContextAsync();
                var entity = _appDBContext.Set<T>().Where<T>(predicate).AsQueryable<T>().FirstOrDefault<T>();
                busy = false;
            return await Task.FromResult(entity);

        }

        #endregion



        #region Save failures
        /// <summary>
        /// True when a save failed because a unique index or primary key already holds the value
        /// (SQL Server errors 2601 and 2627) - "this row already exists" rather than a real fault.
        /// EF wraps the SqlException; the whole chain is searched so a further wrapper does not hide it.
        /// </summary>
        public static bool IsUniqueViolation(Exception ex)
        {
            if (ex is not DbUpdateException) return false;

            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                if (inner is Microsoft.Data.SqlClient.SqlException sql
                    && sql.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Any(e => e.Number == 2601 || e.Number == 2627))
                    return true;
            }
            return false;
        }

        private static void LogSaveFailure(string operation, string? target, Exception ex)
        {
            // DbUpdateException's own message only says "see the inner exception"; the SQL Server one names
            // the index and the duplicate value.
            string kind = IsUniqueViolation(ex) ? "unique key violation" : ex.GetType().Name;
            Console.WriteLine($"[EntitiesService] {operation} of {target} failed ({kind}): {ex.GetBaseException().Message}");
        }

        /// <summary>
        /// Takes what a failed insert added back out of the circuit's shared context.
        ///
        /// Add marks the entity, and every untracked entity reachable from it, as Added; a failed
        /// SaveChanges leaves them that way - still tracked, still in the collections of the rows they hang
        /// off (an Element pasted with its AttributeElements) - and the next SaveChanges any page makes in
        /// this circuit sends them again and fails again. The walk only follows Added entities and stops at
        /// anything Unchanged or Modified, so the rows they point to stay tracked, and so do the Added rows
        /// of another page that is saving on the same context. DbUpdateException.Entries is deliberately not
        /// used: EF lists every row of the failed batch there, that other page's pending rows included, and
        /// cannot say which one SQL Server refused.
        /// </summary>
        private void DetachFailedInsert(IEnumerable<object>? roots)
        {
            try
            {
                var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                var added = new List<EntityEntry>();
                var pending = new Stack<object>(roots ?? Enumerable.Empty<object>());

                while (pending.Count > 0)
                {
                    var entity = pending.Pop();
                    if (entity == null || !visited.Add(entity)) continue;

                    var entry = _appDBContext.Entry(entity);
                    if (entry.State != EntityState.Added) continue;
                    added.Add(entry);

                    foreach (var navigation in entry.Navigations)
                    {
                        if (navigation is CollectionEntry collection)
                        {
                            // Copied: a detach can fix up the very collection being read.
                            if (collection.CurrentValue != null)
                                foreach (var related in collection.CurrentValue.Cast<object>().ToList())
                                    pending.Push(related);
                        }
                        else if (navigation is ReferenceEntry reference && reference.CurrentValue != null)
                        {
                            pending.Push(reference.CurrentValue);
                        }
                    }
                }

                // Listed before anything is detached, and only when some key was set by the caller: a key
                // EF generated for this insert is temporary and no other instance can hold it.
                var tracked = added.Any(HasKeyWithoutTemporaryValue)
                    ? _appDBContext.ChangeTracker.Entries().ToList()
                    : null;

                // Latest found first: dependents go before the principal they were reached from.
                for (int i = added.Count - 1; i >= 0; i--)
                {
                    if (tracked != null && ClashesWithTrackedKey(added[i], tracked)) continue;
                    added[i].State = EntityState.Detached;
                }
            }
            catch (Exception cleanupEx)
            {
                // Never let the cleanup hide the failure the caller is about to be told about.
                Console.WriteLine($"[EntitiesService] could not detach a failed insert: {cleanupEx.Message}");
            }
        }

        /// <summary>
        /// Whether another tracked, not deleted, instance holds the same key as this Added entry.
        ///
        /// That is what Add leaves behind when it throws "another instance with the same key value is
        /// already being tracked": EF 8 has already marked the entity Added but never counted it among
        /// the context's pending changes. Detaching it takes one off a count that never included it, and
        /// from then on a SaveChanges with a single pending change saves nothing and reports success -
        /// worse than the failing save it would have replaced. Such an entry is left where it is.
        /// </summary>
        private static bool ClashesWithTrackedKey(EntityEntry entry, List<EntityEntry> tracked)
        {
            var keys = entry.Metadata.GetKeys().Where(k => !KeyHasTemporaryValue(entry, k)).ToList();
            if (keys.Count == 0) return false;

            var rootType = entry.Metadata.GetRootType();
            foreach (var other in tracked)
            {
                if (ReferenceEquals(other.Entity, entry.Entity) || other.State == EntityState.Deleted
                    || other.Metadata.GetRootType() != rootType)
                    continue;

                foreach (var key in keys)
                    if (key.Properties.All(p => Equals(other.Property(p.Name).CurrentValue, entry.Property(p.Name).CurrentValue)))
                        return true;
            }
            return false;
        }

        private static bool KeyHasTemporaryValue(EntityEntry entry, Microsoft.EntityFrameworkCore.Metadata.IKey key)
            => key.Properties.Any(p => entry.Property(p.Name).IsTemporary);

        private static bool HasKeyWithoutTemporaryValue(EntityEntry entry)
            => entry.Metadata.GetKeys().Any(k => !KeyHasTemporaryValue(entry, k));
        #endregion

        #region Insert entity
        public async Task<T> InsertEntityAsync<T>(T entity)
        {

                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;
                //_appDBContext = await _DBFactory.CreateDbContextAsync();
                try
                {
                    // Awaited one after the other: if the add fails, no SaveChanges may still be running on
                    // the context when the catch below detaches what it added.
                    var v = await _appDBContext.AddAsync(entity);
                    await _appDBContext.SaveChangesAsync();
                    busy = false;
                appData.SaveChanges = false;

                await JournalEditAsync("entity.insert", Describe(v.Entity), null);

                return (T)(v.Entity);

                }
                catch (Exception ex)
                {
                // Callers still learn of the failure from the null, but the entity must not stay Added in the
                // shared context: the next save anywhere in the circuit would send it again and fail with it.
                // Since the unique indexes on AttributeElement and AttributeTypeElementType, a duplicate
                // insert ends here rather than being written, so this is no longer a rare path.
                DetachFailedInsert(new object[] { entity });
                LogSaveFailure("insert", entity?.GetType().Name ?? typeof(T).Name, ex);
                busy = false;
                return default(T);
               
                }

        }

        public async Task<bool> InsertEntitiesAsync<T>(List<T> entites) where T : class
        {

                //_appDBContext = await _DBFactory.CreateDbContextAsync();
                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;
                try
                {
                    // In order, for the same reason as InsertEntityAsync.
                    await _appDBContext.AddRangeAsync(entites);
                    await _appDBContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // This one has always thrown and still does; the batch is only taken out of the shared
                    // context first, so that it is not sent again by the next save elsewhere.
                    DetachFailedInsert(entites?.Cast<object>());
                    LogSaveFailure("insert", $"{typeof(T).Name} × {entites?.Count ?? 0}", ex);
                    throw;
                }
                finally
                {
                    busy = false;
                }
            appData.SaveChanges = false;

            // One row for the batch: a bulk insert is one user action, not N.
            await JournalEditAsync("entity.insert.bulk",
                $"{typeof(T).Name} × {entites?.Count ?? 0}", null);

            return true;

        }
        /// <summary>
        /// Inserts one row through a context of its own, disposed here: the circuit's shared context never holds it, so
        /// nothing of this row is left behind when it is deleted later - by this tab or by another one - and a refused
        /// insert leaves nothing queued for the next save of another page.
        ///
        /// The caller keeps its object as plain data, with the key the database gave it. Journals like InsertEntityAsync.
        /// </summary>
        public async Task<bool> InsertOwnedAsync<T>(T entity) where T : class
        {
            try
            {
                await using var db = CreateOwnedContext();
                db.Add(entity);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LogSaveFailure("insert", entity?.GetType().Name ?? typeof(T).Name, ex);
                await js.InvokeVoidAsync("alert", $"L'enregistrement a échoué : {ex.GetBaseException().Message}");
                return false;
            }
            appData.SaveChanges = false;   // as InsertEntityAsync does: the tab has just written something

            await JournalEditAsync("entity.insert", Describe(entity), null);

            return true;
        }

        /// <summary>
        /// Inserts rows through a context of its own, in one SaveChanges (one transaction): all of them or none, and the
        /// shared context never holds them. Like InsertEntitiesAsync it throws when the database refuses them - the caller
        /// says why - and journals one row for the batch.
        /// </summary>
        public async Task<bool> InsertOwnedRangeAsync<T>(IReadOnlyCollection<T> entities) where T : class
        {
            try
            {
                await using var db = CreateOwnedContext();
                db.AddRange(entities);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LogSaveFailure("insert", $"{typeof(T).Name} × {entities.Count}", ex);
                throw;
            }
            appData.SaveChanges = false;

            await JournalEditAsync("entity.insert.bulk", $"{typeof(T).Name} × {entities.Count}", null);

            return true;
        }
        #endregion



        #region Update entity
        public async Task<bool> UpdateEntityAsync(object entity)
        {

                //_appDBContext = await _DBFactory.CreateDbContextAsync();
                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;
                // Captured before SaveChanges: afterwards the original values are gone.
                string changes = SuppressJournal ? null : DescribeChanges(entity);
                string target  = SuppressJournal ? null : Describe(entity);

                try
                {
                    _appDBContext.Update(entity);
                    await _appDBContext.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    // A failed update stays queued in this circuit's shared context, and every later save
                    // would send it again and fail the same way: the whole session would stop saving.
                    // Forget it. The caller is told, and the row is read again when a page next loads it.
                    foreach (var failed in ex.Entries) failed.State = EntityState.Detached;
                    LogSaveFailure("update", entity?.GetType().Name, ex);
                    throw;
                }
                finally
                {
                    busy = false;
                }
            appData.SaveChanges = false;

            await JournalEditAsync("entity.update", target, changes);

            return true;

        }

        public async Task<bool> UpdateEntitesAsync(IEnumerable<object> entities)
        {

            //_appDBContext = await _DBFactory.CreateDbContextAsync();
            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                await Task.Delay(delay);
            }
            busy = true;
            string bulkTarget = SuppressJournal ? null
                : $"{entities?.FirstOrDefault()?.GetType().Name ?? "entité"} × {entities?.Count() ?? 0}";

            _appDBContext.UpdateRange(entities);
            var t = _appDBContext.SaveChangesAsync();
            await t;
            busy = false;
            appData.SaveChanges = false;

            await JournalEditAsync("entity.update.bulk", bulkTarget, null);

            return true;

        }

        /// <summary>
        /// Saves the values of a row a page holds as plain data (read untracked), through a context of its own: the stored
        /// row is read there and given the page's values, so only the columns that changed are written and the journal
        /// says which ("Field: old → new"), as UpdateEntityAsync does for a tracked row. Nothing else is sent: not another
        /// page's pending edits, not the objects this one points to.
        ///
        /// Throws, as UpdateEntityAsync does, when the database refuses the values, and DbUpdateConcurrencyException when
        /// the row no longer exists (deleted from another tab): the page keeps the row in edit and says why.
        /// </summary>
        public async Task<bool> UpdateOwnedAsync<T>(T entity) where T : class
        {
            await using var db = CreateOwnedContext();
            var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties
                .Select(k => k.PropertyInfo!.GetValue(entity)).ToArray();
            var stored = await db.Set<T>().FindAsync(key)
                         ?? throw new DbUpdateConcurrencyException(
                             "cette ligne n'existe plus : elle a été supprimée entre-temps (autre onglet ou autre utilisateur).");

            var entry = db.Entry(stored);
            entry.CurrentValues.SetValues(entity);
            string changes = SuppressJournal ? null : DescribeChanges(entry);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LogSaveFailure("update", typeof(T).Name, ex);
                throw;
            }
            appData.SaveChanges = false;

            await JournalEditAsync("entity.update", Describe(entity), changes);

            return true;
        }

        /// <summary>
        /// Stores a row a page holds as plain data, through a context of its own: inserted when it has no key yet, updated
        /// otherwise (UpdateOwnedAsync) - what UpdateEntityAsync did for a new row of the shared context. Throws when the
        /// database refuses it, DbUpdateConcurrencyException when the row to update no longer exists. A row inserted
        /// keeps the key the database gave it, so the next save updates it.
        /// </summary>
        public async Task<bool> SaveOwnedAsync<T>(T entity) where T : class
        {
            await using var db = CreateOwnedContext();
            var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties.Single();
            var id = key.PropertyInfo!.GetValue(entity);
            if (id != null && !id.Equals(Activator.CreateInstance(key.ClrType)))
                return await UpdateOwnedAsync(entity);

            try
            {
                db.Add(entity);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LogSaveFailure("insert", typeof(T).Name, ex);
                throw;
            }
            appData.SaveChanges = false;

            await JournalEditAsync("entity.insert", Describe(entity), null);

            return true;
        }

        /// <summary>
        /// After a row was saved through a context of its own (UpdateOwnedAsync), gives its saved values to the circuit's
        /// shared context too, when that context tracks the row (a list or a calculation of this tab read it earlier): a
        /// tracking query never overwrites a tracked row, so the tab would go on showing and computing with the old
        /// values. The tracked row ends Unchanged - nothing is sent again - and a changed foreign key moves its navigation.
        /// </summary>
        public async Task RefreshTrackedAsync<T>(T saved) where T : class
        {
            for (int i = 0; i < retrys; i++)
            {
                if (!busy) break;
                await Task.Delay(delay);
            }

            var key = _appDBContext.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties;
            object?[] KeyOf(object entity) => key.Select(p => p.PropertyInfo!.GetValue(entity)).ToArray();
            var wanted = KeyOf(saved);
            var entry = _appDBContext.ChangeTracker.Entries<T>().FirstOrDefault(e => KeyOf(e.Entity).SequenceEqual(wanted));
            if (entry == null || ReferenceEquals(entry.Entity, saved) || entry.State is EntityState.Added or EntityState.Deleted)
                return;

            entry.CurrentValues.SetValues(saved);
            entry.OriginalValues.SetValues(saved);
            entry.State = EntityState.Unchanged;
        }

        /// <summary>
        /// Stores a scenario's Charpente row (SaveOwnedAsync), keeping to one row per scenario - the database holds to it
        /// too (UX_Charpente_EventMPScenarioID). A new object - the scenario had no row when the page or the calculation
        /// read it - takes the key of a row another tab created meanwhile, and updates it rather than adding a second;
        /// also when that row appears between this look and the insert, which the index then refuses.
        /// </summary>
        public async Task SaveCharpenteAsync(Charpente charpente)
        {
            if (charpente.CharpenteId == Guid.Empty)
                charpente.CharpenteId = await CharpenteIdOfAsync(charpente.EventMpscenarioId);

            try
            {
                await SaveOwnedAsync(charpente);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                var id = await CharpenteIdOfAsync(charpente.EventMpscenarioId);
                if (id == Guid.Empty) throw;
                charpente.CharpenteId = id;
                await UpdateOwnedAsync(charpente);
            }
        }

        private async Task<Guid> CharpenteIdOfAsync(Guid scenarioId)
        {
            await using var db = CreateOwnedContext();
            return await db.Charpentes.Where(c => c.EventMpscenarioId == scenarioId)
                .Select(c => c.CharpenteId).FirstOrDefaultAsync();
        }
        #endregion

        #region Delete entity
        public async Task<bool> DeleteEntityAsync(object entity)
        {

                //_appDBContext = await _DBFactory.CreateDbContextAsync();
                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
            TrackerSnapshot? before = null;
            try
            {
                busy = true;
                // Described before removal — afterwards the instance may be detached and unreadable.
                string target = SuppressJournal ? null : Describe(entity);

                // An object this circuit no longer tracks - deleted a moment ago by a double click, or kept by a page after
                // another tab deleted it: if its row is gone there is nothing to delete, and Remove would only attach it
                // again with everything it still holds.
                var root = _appDBContext.Entry(entity);
                if (root.State == EntityState.Detached && (await RowsGoneAsync(new[] { root })).Count > 0)
                {
                    busy = false;
                    return true;   // no alert, and no journal row: whoever deleted it has journaled it
                }

                // A DELETE that affects 0 rows proves only that THAT row is gone - deleted from another tab or by another
                // user (no table here has a concurrency token); so does an UPDATE that Remove itself caused (a dependent whose
                // optional foreign key it set to null) and that affects 0 rows. The database is then asked which of the rows
                // this delete was removing or so changing are gone; they are forgotten all at once (RowsGoneAsync,
                // ForgetGoneRows), and the delete is tried again only while the row asked for still exists - a row deleted
                // meanwhile, once more.
                for (int attempt = 1; ; attempt++)
                {
                    // Taken before Remove, which changes the shared context at once (see TrackerSnapshot): a delete the
                    // database refuses must not stay queued there for every later save of the circuit to send again.
                    before = TrackerSnapshot.Take(_appDBContext);
                    _appDBContext.Remove(entity);
                    try
                    {
                        await _appDBContext.SaveChangesAsync();
                        before = null;   // saved: from here on (the journal) a failure must not bring the deleted rows back
                        break;
                    }
                    catch (DbUpdateConcurrencyException concurrency)
                        when (attempt < 4 && concurrency.Entries.Count > 0
                              && concurrency.Entries.All(e => e.State == EntityState.Deleted
                                                              || (e.State == EntityState.Modified
                                                                  && before!.StateOf(e.Entity) == EntityState.Unchanged)))
                    {
                        // Every row the delete was removing, and every row Remove itself modified - all at once, not only the
                        // one the database stopped at, so several rows gone elsewhere cost one retry, not one each.
                        var removing = _appDBContext.ChangeTracker.Entries()
                            .Where(e => e.State == EntityState.Deleted
                                        || (e.State == EntityState.Modified && before!.StateOf(e.Entity) == EntityState.Unchanged))
                            .ToList();
                        before!.Restore(_appDBContext);
                        before = null;
                        var gone = await RowsGoneAsync(removing);
                        ForgetGoneRows(gone);
                        if (gone.Any(g => ReferenceEquals(g.Entity, entity)))
                        {
                            busy = false;
                            return true;   // no alert, and no journal row: whoever deleted it has journaled it
                        }
                    }
                }
                busy = false;

                // Deletions are the hardest to reconstruct after the fact — flagged as a warning.
                await JournalEditAsync("entity.delete", target, null, severity: 1);

                return true;
            }
            catch (Exception ex)
            {
                if (before != null)
                {
                    try
                    {
                        before.Restore(_appDBContext);
                    }
                    catch (Exception restoreEx)
                    {
                        // Never let the cleanup hide the failure the user is about to be told about.
                        Console.WriteLine($"[EntitiesService] could not undo a failed delete in the shared context: {restoreEx.Message}");
                    }
                }
                // Before the alert: the other users of the shared context must not wait while it is open.
                busy = false;
                await js.InvokeVoidAsync("alert", "Error: " + ex.Message + ex.InnerException?.Message);
                return false;
            }

        }

        /// <summary>
        /// Deletes one row through a context of its own, by its key, in one statement: the database removes what cascades
        /// from it, and nothing is attached or left queued anywhere. A row already deleted elsewhere (another tab, another
        /// user) counts as deleted: true, nothing journaled. A refusal is shown and returns false.
        /// </summary>
        public async Task<bool> DeleteOwnedAsync<T>(T entity) where T : class
        {
            string target = Describe(entity);
            int deleted;
            try
            {
                await using var db = CreateOwnedContext();
                var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties.Single();
                var id = key.PropertyInfo!.GetValue(entity);
                // Read through a closure, so that EF sends the key as a parameter: a constant would be written into the
                // SQL, one query to compile and one plan to cache per row deleted.
                Expression<Func<object?>> captured = () => id;
                var row = Expression.Parameter(typeof(T), "row");
                var sameKey = Expression.Lambda<Func<T, bool>>(
                    Expression.Equal(Expression.Property(row, key.PropertyInfo!), Expression.Convert(captured.Body, key.ClrType)), row);
                deleted = await db.Set<T>().Where(sameKey).ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                LogSaveFailure("delete", target, ex);
                await js.InvokeVoidAsync("alert", $"La suppression a échoué : {ex.GetBaseException().Message}");
                return false;
            }

            if (deleted > 0)
                await JournalEditAsync("entity.delete", target, null, severity: 1);

            return true;
        }

        /// <summary>
        /// Deletes elements - a branch of the tree - as ONE change, through a context of its own (RunOwnedDeleteAsync), then
        /// takes them out of the circuit's shared context with everything it holds of them: their attributes, their static
        /// load links, their equipments and those equipments' attributes, which the database deletes with them (cascade).
        ///
        /// It used to delete id-only stand-ins through a context it never disposed: the real Element objects the tree had
        /// loaded, and their attributes, stayed in the shared context as copies of rows that no longer existed, and the next
        /// write touching them - deleting their element type, for one - failed. A refused delete escaped with the service
        /// still marked busy. Now a refused delete changes nothing, is shown, and returns false.
        /// </summary>
        public async Task<bool> DeleteElementsAsync(params object[] entities)
        {
            var elements = entities.OfType<Element>().ToList();
            var ids = elements.Select(el => el.ElementId).Distinct().ToList();
            string target = $"{ids.Count} élément(s)";
            DeletedRows rows;
            try
            {
                // One statement for the whole branch: the parent links between its elements (FK_Element_Element1, the one
                // link to an element that does not cascade) are checked once it has run, when both ends are gone - so the
                // elements need no unhooking first, and none can be left half-deleted.
                rows = await RunOwnedDeleteAsync(async (db, d) =>
                    await d.Step(db.Elements.Where(el => ids.Contains(el.ElementId)), el => el.ElementId));
            }
            catch (Exception ex)
            {
                // Rolled back: nothing deleted, and the shared context was never touched.
                LogSaveFailure("delete", target, ex);
                await js.InvokeVoidAsync("alert", $"Les éléments n'ont pas été supprimés : {ex.GetBaseException().Message}");
                return false;
            }

            var removed = rows.KeysOf<Element>();                // read before the Includes below
            foreach (var id in ids) rows.Include<Element>(id);   // an element already deleted elsewhere is forgotten too
            await AfterOwnedDeleteAsync(rows, null, target);

            // Only what this delete removed, as the scenario and event deletes do: a branch already deleted from another
            // tab was journaled there. Names come from the caller's instances.
            int count = rows.Count<Element>();
            if (count > 0)
                await JournalEditAsync("element.delete", $"{count} élément(s)",
                    string.Join(", ", elements.Where(el => removed.Contains(el.ElementId)).DistinctBy(el => el.ElementId)
                                              .Select(el => el.Name ?? el.ElementId.ToString())),
                    severity: 1);

            return true;
        }


        #endregion
    }
}
