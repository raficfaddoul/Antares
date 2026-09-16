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


    public class EntitiesService
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
                var entry = _appDBContext.Entry(entity);
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

        private async Task JournalEditAsync(string action, string target, string details, byte severity = 0)
        {
            if (SuppressJournal || journal == null) return;
            await journal.WriteAsync(JournalCategory.Edition, action, target, details, severity);
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
            try
            {
                busy = true;
                // Described before removal — afterwards the instance may be detached and unreadable.
                string target = SuppressJournal ? null : Describe(entity);

                _appDBContext.Remove(entity);
                var t = _appDBContext.SaveChangesAsync();
                await t;
                busy = false;

                // Deletions are the hardest to reconstruct after the fact — flagged as a warning.
                await JournalEditAsync("entity.delete", target, null, severity: 1);

                return true;
            }
            catch (Exception ex)
            {
                await js.InvokeVoidAsync("alert", "Error: " + ex.Message + ex.InnerException?.Message);
                busy = false;
                return false;
            }

        }

        public async Task<bool> DeleteElementsAsync(params object[] entities)
        {

            AliantWebContext zo = await _DBFactory.CreateDbContextAsync();
                for (int i = 0; i < retrys; i++)
                {
                    if (!busy) break;
                    await Task.Delay(delay);
                }
                busy = true;


            List<Element> elements = new List<Element>();
            foreach (var v in entities)
            {
                Guid elementID = ((Element)v).ElementId;
                Element element = new Element();
                element.ElementId = elementID;
                elements.Add(element);
            }
            zo.RemoveRange(elements);
            await zo.SaveChangesAsync();

                busy = false;

            // Names come from the caller's instances — the stubs above carry only the id.
            await JournalEditAsync("element.delete",
                $"{elements.Count} élément(s)",
                string.Join(", ", entities.OfType<Element>().Select(e => e.Name ?? e.ElementId.ToString())),
                severity: 1);

            return true;

        }


        #endregion
    }
}
