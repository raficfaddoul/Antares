using AliantWebS.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

namespace Structural
{
    public class IdentificationResult
    {
        public double Kpi1 { get; set; }
        public double Kpi2 { get; set; }
        public double Kpi3 { get; set; }

        public double TauxMoyen { get; set; }
        public double Ptaux0_25 { get; set; }
        public double Ptaux25_50 { get; set; }
        public double Ptaux50_75 { get; set; }
        public double Ptaux75_100 { get; set; }
        public double TauxMaximal { get; set; }
        public Dictionary<Guid,double> Rigidités { get; set; }
        public Dictionary<Guid, double> TauxDetravail { get; set; }


        public async Task Save(AliantWebS.Models.Structure structure)
        {
            string path = FileManagementService.GetFilesLocation(structure, @"wwwroot\data").Item1;
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string fileName = Path.Combine(path, "IdentificationResults.dat");

            string json = JsonSerializer.Serialize(this);

            await File.WriteAllTextAsync(fileName, json);     

        }

        public async Task<IdentificationResult> Load(AliantWebS.Models.Structure structure)
        {
            string path = FileManagementService.GetFilesLocation(structure, @"wwwroot\data").Item1;

            string fileName = Path.Combine(path, "IdentificationResults.dat");

            if (!File.Exists(fileName))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(fileName);



            return JsonSerializer.Deserialize<IdentificationResult>(json);
        }
    }
}
