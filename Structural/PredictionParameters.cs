using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AliantWebS.Data;
using AliantWebS.Models;

namespace Structural
{
    public class PredictionParameters : IDisposable
    {
        public string PredictionID { get; set; }
        public string ShortDescription { get; set; }
        public string ModelID { get; set; }

        public int SamplingFrequency { get; set; }
        public int Duree { get; set; }


        public void Dispose()
        { }

    }
}
