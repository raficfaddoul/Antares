using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace AliantWebS.Data
{
    public class FileManagementService
    {

        /*     public async Task WriteSelectedFiles(IFileListEntry[] files, Object obj)
             {
                 string s = string.Empty;
                 foreach (var file in files)
                 {
                     Stream stream = file.Data;
                     //  var path = $"{env.WebRootPath}\\{file.Name}";

                     string path = @"Images\";

                     if (obj.GetType() == typeof(Element))
                     {
                         path = path + ((Element)obj).Structure.Guid;

                         s = ((Element)obj).Guid.ToString("N");
                         path = path + @"\" + s[0];
                     }
                     else if (obj.GetType() == typeof(Structure))
                     {
                         path = path + ((Structure)obj).Guid;
                     }



                     if (!Directory.Exists(path))
                     {
                         Directory.CreateDirectory(path);
                     }

                     string fullPath = path + @"\" + s + file.Name;

                     FileStream fs = File.Create(fullPath);
                     await stream.CopyToAsync(fs);
                     stream.Close();
                     fs.Close();
                 }
             }*/

        static public (string path,string guidElement, string guidDefect, string guidEvent) GetFilesLocation(Object obj, string folder)
        {
            string guidElement = string.Empty;
            string guidDefect = string.Empty;
            string guidEvent = string.Empty;
            string path = folder;

            //if (obj.GetType() == typeof(Element))
            //{
            //    path = path + ((Element)obj).Structure.Guid;

            //    guidElement = ((Element)obj).Guid.ToString("N");
            //    path = path + @"\" + guidElement[0];
            //}
            //else if (obj.GetType() == typeof(Structure))
            //{
            //    path = path + ((Structure)obj).Guid;
            //}
            //else if (obj.GetType() == typeof(Defect))
            //{
            //    path = path + ((Defect)obj).Element.Structure.Guid;

            //    guidElement = ((Defect)obj).Element.Guid.ToString("N");
            //    guidDefect = ((Defect)obj).Guid.ToString("N");

            //    path = path + @"\" + guidElement[0];
            //}
            //rafic????
            
            // The foreign keys, not the Structure navigations: same value, but always set, even on an entity read
            // without its Structure (a NullReferenceException here used to lose the upload).
            if (obj.GetType() == typeof(Event))
            {
                path = Path.Combine(path, ((Event)obj).StructureId.ToString());
                path = Path.Combine(path,"Event");

                guidEvent = ((Event)obj).EventId.ToString("N");
                path = Path.Combine(path, guidEvent[0].ToString());
            }
            if (obj.GetType() == typeof(Element))
            {
                path = Path.Combine(path, ((Element)obj).StructureId.ToString());

                guidElement = ((Element)obj).ElementId.ToString("N");
                path = Path.Combine(path, guidElement[0].ToString());
            }
            else if (obj.GetType() == typeof(Structure))
            {
                path = Path.Combine(path, ((Structure)obj).StructureId.ToString());
            }
            else if (obj.GetType() == typeof(Site))
            {
                path = Path.Combine(path, ((Site)obj).SiteId.ToString());
            }

                return (path,guidElement,guidDefect, guidEvent);
        }

        static public async Task deleteFile(string fileName)
        {
            File.Delete(fileName);
        }

        static public async Task<List<string>> GetPathForFilesAsync(Object obj,bool recurseSubdirectories, string folder = @"Images/")
        {
         
            const int charactersFromGuid = 10;
            List<string> v = new List<string>();

            if (obj is null) return await Task.FromResult(v);

            var (path, guidElement, guidDefect, guidEvent) = GetFilesLocation(obj, folder);

            EnumerationOptions eo = new EnumerationOptions();
            eo.RecurseSubdirectories = recurseSubdirectories;

            if (Directory.Exists(path))
            {
                if (obj.GetType() == typeof(Event))
                {

                    v = Directory.EnumerateFiles(path, "*" + guidEvent.Substring(1, charactersFromGuid) + "*", eo).ToList();

                }
                if (obj.GetType() == typeof(Element))
                {
     
                        v = Directory.EnumerateFiles(path, "*" + guidElement.Substring(1, charactersFromGuid) + "*",eo).ToList();
        
                }
                else if ((obj.GetType() == typeof(Structure)) || (obj.GetType() == typeof(Site)))
                {

                    v = Directory.EnumerateFiles(path,"*",eo).ToList();
                }   
            }

            return await Task.FromResult(v);
        }
    }

}
