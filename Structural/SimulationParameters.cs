using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AliantWebS.Data;
using AliantWebS.Models;

namespace Structural
{
    public class SimulationParameters: IDisposable
    {
        public string SimulationID { get; set; }
        public string ShortDescription { get; set; }
        public int FileOpens { get; set; } = 2;
        public int SimulationNum { get; set; } = 50;
        public int Duree { get; set; } = 5;
        public int SamplingFrequency { get; set; } = 1000;
        public int Multiplicity { get; set; } = 1;
        public int Divider { get; set; } = 1;
        public string Path { get; set; }
        public bool oneElementAtATime { get; set; } = true;

        public int? GlobalSeed { get; set; } = null;
        // public int? LocalSeed { get; set; } = null;

        public bool WriteCSV { get; set; } = true;

        public void Dispose()
        { }

    }
}
