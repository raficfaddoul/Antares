using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Structural;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace AliantDesktop
{
    public partial class IbrahimForm : Form
    {

        public List<Guid> elementsIDs { get; set; }

        public List<Guid> ChannelsDataTypeIDs { get; set; }



        public int nbDDL { get; set; }

        public System.DateTime startDate1 { get; set; }
        public System.DateTime endDate1 { get; set; }

        public System.DateTime startDate2 { get; set; }
        public System.DateTime endDate2 { get; set; }

        int nbTimeSteps { get; set; } = 1000;
        double timeStep { get; set; } = 1000;
        string shiftType { get; set; } = "Automatic";
        int nbModes { get; set; } = 10;
        int nbAnalysisSteps { get; set; } = 100;
        int nbSeriesAveraging { get; set; } = 20;

        Structural.Structure structuralStructure;

        public AliantWebContext dbContext { get; set; }
        public Guid StructureID { get; set; }
        private AliantWebS.Models.Structure Structure { get; set; }


        public IbrahimForm()
        {
            InitializeComponent();
            this.Load += IbrahimForm_Load;
        }

        private async void IbrahimForm_Load(object sender, EventArgs e)
        {
            Structure = await dbContext.Structures.FindAsync(StructureID);

            dbContext.Entry(Structure).Collection("Elements").Load();

        }

        private async Task Calcul1()
        {
   

            List<Element> elements = new List<Element>();
            List<Element> nodes = new List<Element>();


            nodes = await dbContext.Elements
    .Include(e => e.StaticLoadElements)
        .ThenInclude(sle => sle.StaticLoad)
    .Include(e => e.StaticPointLoadElements)
        .ThenInclude(sple => sple.StaticPointLoad)
    .Include(e => e.DynamicLoadElements)
        .ThenInclude(dle => dle.DynamicLoad)
    .Include(e => e.DynamicPointLoadElements)
        .ThenInclude(dple => dple.DynamicPointLoad)
    .Include(e => e.MobileLoadElements)
        .ThenInclude(mle => mle.MobileLoad)
    .Include(e => e.ElementType)
    .Include(e => e.AttributeElements)
        .ThenInclude(ae => ae.AttributeType)
    .Where(a => a.StructureId == Structure.StructureId && a.ElementType.Name == "Node")
    .ToListAsync();



            elements = await dbContext.Elements
                .Include(e => e.StaticLoadElements)
                    .ThenInclude(sle => sle.StaticLoad)
                .Include(e => e.StaticPointLoadElements)
                    .ThenInclude(sple => sple.StaticPointLoad)
                .Include(e => e.DynamicLoadElements)
                    .ThenInclude(dle => dle.DynamicLoad)
                .Include(e => e.DynamicPointLoadElements)
                    .ThenInclude(dple => dple.DynamicPointLoad)
                .Include(e => e.MobileLoadElements)
                    .ThenInclude(mle => mle.MobileLoad)
                .Include(e => e.ElementType)
                .Include(e => e.AttributeElements)
            .ThenInclude(ae => ae.AttributeType)
                .Where(a => a.StructureId == Structure.StructureId
                            && a.ElementType != null
                            && a.ElementType.Name == "Beam2d")
                .ToListAsync();


            structuralStructure = Structural.Simulation.CreateStructure1(Structure, null, elements, nodes);

            double[] ValeursPropres = null;
            double[,] VecteursPropres = null;





            ValeursPropres = Structural.StructuralVibration.Calcul_ValeursPropres(structuralStructure.element,
        structuralStructure.noeud, structuralStructure.MasseNodale, structuralStructure.CL, out VecteursPropres);




            ////////////////////////////////////////




            structuralStructure.element.Rows[13]["E"] = structuralStructure.element.Rows[13].Field<double>("E") * 0.2;

            ValeursPropres = Structural.StructuralVibration.Calcul_ValeursPropres(structuralStructure.element,
        structuralStructure.noeud, structuralStructure.MasseNodale, structuralStructure.CL, out VecteursPropres);


        }

        private async Task Calcul2()
        {
            List<Element> sensors = await dbContext.Elements
    .Include(e => e.ElementType)
        .ThenInclude(et => et.ChannelDataTypeElementTypes)
    .Where(e => elementsIDs.Contains(e.ElementId)
                && e.ElementType != null
                && e.ElementType.ChannelDataTypeElementTypes != null
                && e.ElementType.ChannelDataTypeElementTypes.Count > 0)
    .ToListAsync();

            IEnumerable<Guid?> elementTypesIDs = sensors.Select(x => x.ElementTypeId);

            List<ChannelDataType> channelDataTypeList = await dbContext.ChannelDataTypes
    .Where(a => ChannelsDataTypeIDs.Contains(a.ChannelDataTypeId))
    .ToListAsync();

            var v = await Task.Run(() => Structural.Simulation.GetSensedNodes(Structure, dbContext, sensors));
            int nbDDL = v.nodes.Count;
            int[] ddlSensors = v.sensedNodesIndex.ToArray().Select(x => (int)x.Value).ToArray();





            object[,] la = Helpers.Helpers.ConvertTo2DArray(await InfluxService.GetDataByTag(sensors.Select(e => e.ElementId.ToString()).ToList(), channelDataTypeList.Select(x => x.ChannelDataTypeId.ToString()).ToList(), startDate1, endDate1));

            if (la.Length == 0)
            {
                MessageBox.Show("No related Data in the database...", "Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }



            var table = la;

            // Get distinct sensorIDs and sort them
            var distinctSensors = new SortedSet<string>();
            for (int i = 0; i < table.GetLength(0); i++)
            {
                distinctSensors.Add(table[i, 1].ToString());
            }

            var sensorsList = distinctSensors.ToList();
            int numSensors = sensorsList.Count;

            var distinctDates = new HashSet<System.DateTime>();
            for (int i = 0; i < table.GetLength(0); i++)
            {
                distinctDates.Add((System.DateTime)table[i, 0]);
            }

            // Adjust the size of the pivoted table
            object[,] pivotedTable = new object[distinctDates.Count, numSensors + 1];



            // Populate the pivoted table
            Dictionary<System.DateTime, int> timeRowMapping = new Dictionary<System.DateTime, int>();
            int currentRow = 0;

            for (int i = 0; i < table.GetLength(0); i++)
            {
                System.DateTime time = (System.DateTime)table[i, 0];
                string sensor = table[i, 1].ToString();
                object value = table[i, 2];

                if (!timeRowMapping.ContainsKey(time))
                {
                    timeRowMapping[time] = currentRow;
                    pivotedTable[currentRow, 0] = time;
                    currentRow++;
                }

                int sensorColumn = sensorsList.IndexOf(sensor) + 1;
                pivotedTable[timeRowMapping[time], sensorColumn] = value;
            }



            var qtime = Helpers.Helpers.ConvertToDoubleArray(pivotedTable);

            int numColumns = pivotedTable.GetLength(1);

            for (int i = 0; i < numColumns; i++)
            {
                Helpers.Helpers.InterpolateColumn(qtime, i);
            }

            var result = Structural.SP.GetSubsequences(Helpers.Helpers.ConvertToDoubleMatrix(qtime), sequenceLength: 1000, thresholdPercentage: 0.4, refSensor: 1);

            List<int> trigerPoints = result.TriggerPoints;
            List<double[,]> subsequences = result.Subsequences;

            int nbPseudoSeries = 5;
            int initialShift = 200;

            var resultIbrahim = IdentifierIbrahim(nbDDL, ddlSensors,
                        nbTimeSteps, timeStep, trigerPoints, subsequences, nbPseudoSeries, shiftType, initialShift, nbAnalysisSteps, nbModes, nbSeriesAveraging);
            double[] frequency = resultIbrahim.Frequency;
            double[,] modeShape = resultIbrahim.ModeShape;




            // ////// exemple de save
            // Structural.IdentificationResult ir = new Structural.IdentificationResult();
            // ir.Kpi1 = 76;
            // ir.Kpi2 = 85;
            // ir.Kpi3 = 58;
            // ir.Rigidités = new List<double>() { 1.0075, 0.9982, 0.9747, 0.9801, 1.0098, 1.0015, 0.9829, 0.9749, 0.9731, 0.9753, 0.9907, 0.9983, 1.014, 0.9677, 0.9569, 1.013, 0.9965, 1, 1.0004, 0.9945, 0.989, 0.9908, 0.9991, 0.9966, 0.9873, 0.991, 1.0008, 0.9973 };

            // await ir.Save(appData.Structure);


            // //// exemple de load
            // Structural.IdentificationResult ir1 = new Structural.IdentificationResult();
            // ir1 = await ir1.Load(appData.Structure);




        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="nbDDL"></param>
        /// <param name="ddlSensors"></param>
        /// <param name="nbTimeSteps"></param>
        /// <param name="timeStep"></param>
        /// <param name="startingTimes"></param>
        /// <param name="Xaccelerations"></param>
        /// <param name="nbPseudoSeries"></param>
        /// <param name="shiftType"></param>
        /// <param name="initialShift"></param>
        /// <param name="nbAnalysisSteps"></param>
        /// <param name="nbModes"></param>
        /// <param name="nbSeriesAveraging"></param>
        /// <param name="frequency"></param>
        /// <param name="modeShape"></param>

        public (double[] Frequency, double[,] ModeShape) IdentifierIbrahim(int nbDDL, int[] ddlSensors,
        int nbTimeSteps, double timeStep, List<int> startingTimes, List<double[,]> Xaccelerations,
        int nbPseudoSeries, string shiftType, int initialShift, int nbAnalysisSteps, int nbModes,
        int nbSeriesAveraging)
        {

            double[] frequency = new double[20];

            double[,] modeShape = new double[20, 2];

            return (Frequency: frequency, ModeShape: modeShape);
        }


    }
}
