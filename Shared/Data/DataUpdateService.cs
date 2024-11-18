using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AliantWebS.Data
{
    public delegate void DataUpdatedHandler(string chartType, string name);
    public class DataUpdateService
    {
        public event DataUpdatedHandler OnDataUpdated;
        public string address { get; private set; }
        public long t { get; private set; }
        public double x { get; private set; }
        public double y { get; private set; }
        public double z { get; private set; }


        public void UpdateData(string chartType,string name, string address,long t, double x, double y, double z)
        {
            this.address = address;
            this.t = t;
            this.x = x;
            this.y = y;
            this.z = z;
            OnDataUpdated?.Invoke(chartType,name);
        }
    }

}
