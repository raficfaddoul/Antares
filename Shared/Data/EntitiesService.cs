using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;
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

        public EntitiesService(AliantWebContext appDBContext, AppData appData, IJSRuntime js, IDbContextFactory<AliantWebContext> DBFactory)
        {
            this.appData = appData;
            _appDBContext = appDBContext;
            this.js = js;
            _DBFactory = DBFactory;


            cts = new CancellationTokenSource();
            //  ct = cts.Token;
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
                    var t1 = _appDBContext.AddAsync(entity);
                    var t2 = _appDBContext.SaveChangesAsync();
                    var v = await t1;
                    var v2 = await t2;
                    busy = false;
                appData.SaveChanges = false;
                return (T)(v.Entity);

                }
                catch (Exception ex)
                {
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
                var t1 = _appDBContext.AddRangeAsync(entites);
                var t2 = _appDBContext.SaveChangesAsync();

                await t1;
                await t2;

                busy = false;
            appData.SaveChanges = false;
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
                _appDBContext.Update(entity);
                var t = _appDBContext.SaveChangesAsync();
                await t;
                busy = false;
            appData.SaveChanges = false;
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
            _appDBContext.UpdateRange(entities);
            var t = _appDBContext.SaveChangesAsync();
            await t;
            busy = false;
            appData.SaveChanges = false;
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
                _appDBContext.Remove(entity);
                var t = _appDBContext.SaveChangesAsync();
                await t;
                busy = false;
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
                return true;
     
        }


        #endregion
    }
}
