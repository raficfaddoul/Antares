using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AliantWebS.Data
{
    public class Tag
    {
        public string id { get; set; }
        public double x { get; set; }
        public double y { get; set; }
        public Guid defectID { get; set; }
        public string comment { get; set; }
    }


}
