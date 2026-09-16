using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Accord.Math.Transforms;
using Accord.Math;
using System.Collections.Concurrent;
using System.Numerics;
using HDF5CSharp;
using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.AspNetCore.Components;

using System.Globalization;
using MathNet.Numerics.LinearAlgebra;
using System.Runtime.Intrinsics.X86;
using Microsoft.SqlServer.Management.Smo;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;


namespace Structural
{
    public partial class Simulation
    {
        public Simulation()
        {

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="structure"></param>
        /// <param name="entitiesService"></param>
        /// <returns></returns>

        static public (Dictionary<Guid, int> sensedNodesIndex, List<Element> sensors, List<Element> beamElements, List<Element> nodes, Dictionary<Guid, Guid?> sensorNodeDic) 
            GetSensedNodes(AliantWebS.Models.Structure structure, AliantWebS.Models.AliantWebContext dbContext, List<Element> sensors = null)
        {
            Dictionary<Guid, int> sensedNodesIndex = new Dictionary<Guid, int>();
            // List<Element> sensors = new List<Element>();
            List<Element> beamElements = new List<Element>();
            List<Element> nodes = new List<Element>();

            List<Guid> elList = structure.Elements.Select(e => e.ElementId).ToList();

            nodes = dbContext.Elements
    .Include(e => e.ElementType)
    .Include(e => e.AttributeElements)
        .ThenInclude(ae => ae.AttributeType)
    .Where(a => elList.Contains(a.ElementId)
                && a.ElementType != null
                && a.ElementType.Name == "Node")
    .ToListAsync().Result;


            beamElements = dbContext.Elements
                .Include(e => e.ElementType)
                .Include(e => e.AttributeElements)
                    .ThenInclude(ae => ae.AttributeType)
                .Where(e => elList.Contains(e.ElementId)
                            && e.ElementType != null
                            && e.ElementType.Name == "Beam2d")
                .ToListAsync().Result;

            Dictionary<Guid, Guid?> sensorNodeDictionnary = new Dictionary<Guid, Guid?>();



            IEnumerable<Element> elements;

            if (sensors != null)
            {
                elements = sensors;
            }
            else
            {
                elements = structure.Elements;
                sensors = elements.ToList();
            }

            foreach (Element element in elements)
            {


                if (element.ElementType == null) continue;
                if (element.ElementType.Category == null) dbContext.Entry(element.ElementType).Reference("Category").Load();
                if (element.ElementType.Category == null) continue;
                if (element.ElementType.Category.Name.Trim().ToLower() == "sensing device")
                {
                    sensorNodeDictionnary.Add(element.ElementId, element.ParentId);

                }
            }

            int ind = 0;
            foreach (Element node in nodes)
            {
                if (sensorNodeDictionnary.Values.Contains(node.ElementId))
                {
                    sensedNodesIndex.Add(node.ElementId, ind);
                }

                ind++;
            }



            return (sensedNodesIndex: sensedNodesIndex, sensors: sensors, beamElements: beamElements, nodes: nodes, sensorNodeDic: sensorNodeDictionnary);
        }



        static public async Task<(List<Element> beams, List<(Element nmg, Element nst)> noeudsSurCables, List<(Guid nodeid, double chargeAdmissible, double chargeReelle)> resultat)> Verify(AliantWebS.Models.Structure structure, EntitiesService entitiesService)
        {
            Random rnd = new Random();

            List<Element> mothergrids = new List<Element>();
            List<Element> noeudsSceniques = new List<Element>();

            entitiesService._appDBContext.Database.SetCommandTimeout(180);

            var parentnss = await entitiesService.GetFilteredFirstEntityAsync<Element>(x => x.Name.Trim() == "Noeuds Structure scénique");

            noeudsSceniques = await entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && (a.ElementType?.Name == "Noeud3d") && (a.ParentId == parentnss.ElementId)),
    "ElementType");

            //foreach (var n in noeudsSceniques)
            //{
            //    entitiesService.LoadCollection(n,"AttributeElements.AttributeType");
            //}


            mothergrids = await entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && ((a.ElementType?.Name == "Treillis3d"))),
                "Equipements",
                 "ElementType");

            //foreach (var n in mothergrids)
            //{
            //    entitiesService.LoadCollection(n,"AttributeElements.AttributeType");
            //}

            List<(Element nmg, Element nst)> noeudsSurCables = new List<(Element, Element)>();



            var cables = await entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && (a.ElementType?.Name == "Câble")),
"ElementType");
            foreach (var cable in cables)
            {
                var attributes = await entitiesService.GetFilteredEntitiesAsync<AttributeElement>(e => e.ElementId == cable.ElementId && e.AttributeType.Name.Contains("Noeud"), "AttributeType");

                var at1 = attributes.FirstOrDefault(a => a.AttributeType.Name == "Noeud1");
                var at2 = attributes.FirstOrDefault(a => a.AttributeType.Name == "Noeud2");

                var localNodes = await entitiesService.GetFilteredEntitiesAsync<Element>(n => (n.StructureId == structure.StructureId) && ((n.Name == at1.Value) || (n.Name == at2.Value)));

                var n1 = localNodes.Find(x => x.Name == at1.Value);
                var n2 = localNodes.Find(x => x.Name == at2.Value);

                noeudsSurCables.Add((n1, n2));

            }
            Dictionary<Guid, double> ChargeNotale = new Dictionary<Guid, double>();


            //     var motherGridsStructure = CreateStructure3D(mothergrids, noeudsSceniques, ChargeNotale, entitiesService);

            // do whatever must be done here

            List<(Guid nodeid, double chargeAdmissible, double chargeReelle)> resultat = new List<(Guid, double, double)>();
            for (int i = 0; i < noeudsSurCables.Count(); i++)
            {
                try
                {
                    resultat.Add((noeudsSurCables[i].nst.ElementId, 1500.0, 500.0 + 1100 * rnd.NextDouble()));
                }
                catch { }
            }



            //// this section is after the structural calculation on the mother grids

            List<Element> beams = new List<Element>();
            List<Element> nodes = new List<Element>();
            var parentn = await entitiesService.GetFilteredFirstEntityAsync<Element>(x => ((x.StructureId == structure.StructureId) && (x.Name.Trim() == "Noeuds")));

            nodes = entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && (a.ElementType?.Name == "Noeud3d") && (a.ParentId == parentn.ParentId)),
    "ElementType").Result;
            beams = entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && ((a.ElementType?.Name == "Beam3d"))),
                "Equipements",
                 "ElementType").Result;

            //     var structuralStructure = CreateStructure3D(beams, nodes, ChargeNotale, entitiesService);

            return (beams, noeudsSurCables, resultat);
        }


        static public Structure CreateStructure3D(AliantWebS.Models.Structure structure, Dictionary<LoadCase, double> selectedLoadCases, Dictionary<Guid, (int dof,double value)> ChargeNodale, EntitiesService entitiesService)
        {
            //List<(Guid id, List<int> noeudElementInd,int dof, double value, double position)> ChargesStatiquePonctuelles = new List<(Guid id, List<int> noeudInd,int dof, double value, double position)>();

            List<(Guid id, List<int> elInd, int dof, double startValue, double endValue)> ChargesStatiqueReparties = new List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue)>();
            List<(Guid id, List<int> elInd, int dof, double value, double position)> ChargesStatiquePonctuelles = new List<(Guid id, List<int> noeudInd, int dof, double value, double position)>();
            List<(Guid id, List<int> elInd, int dof, double startValue, double endValue, Dictionary<double, double> tempFunc)> ChargesDynamiqueReparties = new List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue, Dictionary<double, double> tempFunc)>();
            List<(Guid id, List<int> elInd, int dof, double value, double position, Dictionary<double, double> tempFunc)> ChargesDynamiquePonctuelles = new List<(Guid id, List<int> noeudInd, int dof, double value, double position, Dictionary<double, double> tempFunc)>();
            List<(Guid id, List<int> elInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL,
                double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)> ChargesMobiles = new List<(Guid id, List<int> noeudInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL, double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)>();





            List<Element> elements;
            List<Element> nodes;


            nodes = entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && (a.ElementType?.Name == "Noeud3d")),
    "StaticLoadElements.StaticLoad",
    "StaticPointLoadElements.StaticPointLoad",

    "ElementType", "AttributeElements.AttributeType").Result;
            elements = entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && ((a.ElementType?.Name == "Beam3d"))),
                "StaticLoadElements.StaticLoad",
                "StaticPointLoadElements.StaticPointLoad",

                "ElementType", "AttributeElements.AttributeType").Result;







            Structural.Structure structura = new Structure();


            List<double[]> noeuds = new List<double[]>();
            List<string[]> CL = new List<string[]>();
            List<Guid> guidsNoeuds = new List<Guid>();

            int index = 0;
            foreach (Element node in nodes)
            {
                double x = double.Parse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "X").Value, CultureInfo.InvariantCulture);
                double y = double.Parse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Y").Value, CultureInfo.InvariantCulture);
                double z = double.Parse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Z").Value, CultureInfo.InvariantCulture);
                noeuds.Add(new double[] { x, y, z });
                guidsNoeuds.Add(node.ElementId);

                bool success = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Constraint_X")?.Value, out bool u);
                if (!success)
                {
                    // Handle the case where the conversion was not successful
                    u = false; // or set a default value
                }

                bool success1 = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Constraint_Y")?.Value, out bool v);
                if (!success1)
                {
                    // Handle the case where the conversion was not successful
                    v = false; // or set a default value
                }
                bool success3 = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Constraint_Z")?.Value, out bool w);
                if (!success3)
                {
                    // Handle the case where the conversion was not successful
                    w = false; // or set a default value
                }
                bool success4 = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Constraint_RX")?.Value, out bool rx);
                if (!success4)
                {
                    // Handle the case where the conversion was not successful
                    rx = false; // or set a default value
                }
                bool success5 = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Constraint_RY")?.Value, out bool ry);
                if (!success5)
                {
                    // Handle the case where the conversion was not successful
                    ry = false; // or set a default value
                }
                bool success6 = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Constraint_RZ")?.Value, out bool rz);
                if (!success6)
                {
                    // Handle the case where the conversion was not successful
                    rz = false; // or set a default value
                }


                if (u)
                {
                    CL.Add(new string[] { index.ToString(), "U" });
                }

                if (v)
                {
                    CL.Add(new string[] { index.ToString(), "V" });
                }

                if (w)
                {
                    CL.Add(new string[] { index.ToString(), "W" });
                }

                if (rx)
                {
                    CL.Add(new string[] { index.ToString(), "RX" });
                }

                if (ry)
                {
                    CL.Add(new string[] { index.ToString(), "RY" });
                }

                if (rz)
                {
                    CL.Add(new string[] { index.ToString(), "RZ" });
                }
                double? mass = Convert.ToDouble(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim().ToLowerInvariant() == "mass")?.Value);
                if ((mass != null) && (mass > 0))
                {
                    structura.MasseNodale.Rows.Add(index, mass);
                }

                if ((ChargeNodale != null) && (ChargeNodale.Keys.Contains(node.ElementId)))
                {
                    ChargesStatiquePonctuelles.Add((node.ElementId, new List<int>() { index }, ChargeNodale[node.ElementId].dof, ChargeNodale[node.ElementId].value, 0));
                }

                if (selectedLoadCases != null)
                {
                    AddCharge(node, index, selectedLoadCases, ChargesStatiqueReparties, ChargesStatiquePonctuelles,
    ChargesDynamiqueReparties, ChargesDynamiquePonctuelles, ChargesMobiles);
                }

                index++;
            }

            structura.noeud = noeuds;
            structura.guidsNoeuds = guidsNoeuds;
            structura.CL = CL;

            DataTable element;

            element = Structure.makeElementsDataTable();

            index = 0;
            foreach (Element el in elements)
            {
                string? noeud1 = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Noeud1").Value;
                string? noeud2 = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Noeud2").Value;

                double e = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "E")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempE) ? 1E6 * tempE : 0;
                double s = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "S")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempS) ? tempS : 0;
                double iy = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Iy")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempIy) ? tempIy : 0;
                double iz = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Iz")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempIz) ? tempIz : 0;
                double G = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "G")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempG) ? tempG : 0;


                double rho = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Rho")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempRho) ? tempRho : 0;
                double b = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "b")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempB) ? tempB : 0;
                double h = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "h")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempH) ? tempH : 0;

                string materiau = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Matériau").Value;

                switch (materiau)
                {
                    case "S235": e = 210000; break;
                    case "C25/30": e = 31000; break;
                    case "GL30h": e = 12000; break;
                    case "C24": e = 9000; break;
                }
                //double e = 1E6 * double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "E").Value, CultureInfo.InvariantCulture);
                //double s = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "S").Value, CultureInfo.InvariantCulture);
                //double i = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "I").Value, CultureInfo.InvariantCulture);
                //double rho = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Rho").Value, CultureInfo.InvariantCulture);
                //double b = 1E6 * double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "b").Value, CultureInfo.InvariantCulture);
                //double h = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "h").Value, CultureInfo.InvariantCulture);

                // Vérification de la cohérence de la structure
                int indexnoeud1 = nodes.FindIndex(x => x.Name == noeud1);
                int indexnoeud2 = nodes.FindIndex(x => x.Name == noeud2);

                if (indexnoeud1 == -1 || indexnoeud2 == -1)
                {
                    int hh = indexnoeud1;
                }
                element.Rows.Add(indexnoeud1, indexnoeud2, e, G, s, iy, iz, rho, b, h, el.ElementId);

                foreach (var eq in el.Equipements)
                {
                    ChargesStatiquePonctuelles.Add((eq.EquipementId, new List<int>() { index },1, (eq.EquipementType.Weight ?? 0), eq.ProjectionParameter ?? 0));
                }
                if (selectedLoadCases != null)
                {
                    AddCharge(el, index, selectedLoadCases, ChargesStatiqueReparties, ChargesStatiquePonctuelles,
 ChargesDynamiqueReparties, ChargesDynamiquePonctuelles, ChargesMobiles);
                }

                index++;

            }

            structura.element = element;



            structura.ChargesStatiquePonctuelles = ChargesStatiquePonctuelles;



            structura.nddl = structura.noeud.Count * 6;

            return structura;

        }

        static public async Task<Structure> CreateStructure(AliantWebS.Models.Structure structure, Dictionary<LoadCase, double> selectedLoadCases, EntitiesService entitiesService)
        {
            List<Element> elements = new List<Element>();
            List<Element> nodes = new List<Element>();

            nodes = await entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && (a.ElementType?.Name == "Node")),
    "StaticLoadElements.StaticLoad",
    "StaticPointLoadElements.StaticPointLoad",
    "DynamicLoadElements.DynamicLoad",
    "DynamicPointLoadElements.DynamicPointLoad",
    "MobileLoadElements.MobileLoad",
    "ElementType", "AttributeElements.AttributeType");
            elements = await entitiesService.GetFilteredEntitiesAsync<Element>(a => ((a.StructureId == structure.StructureId) && ((a.ElementType?.Name == "Beam2d"))),
                "StaticLoadElements.StaticLoad",
                "StaticPointLoadElements.StaticPointLoad",
                "DynamicLoadElements.DynamicLoad",
                "DynamicPointLoadElements.DynamicPointLoad",
                "MobileLoadElements.MobileLoad",
                "ElementType", "AttributeElements.AttributeType");

            return CreateStructure1(structure, selectedLoadCases, elements, nodes);
        }

        /// <summary>
        /// This function creates a structural structure from the elements of the structure having types "Node" and "Beam2d"
        /// The nodes must have the following attributes:
        /// "X :double", "Y : double", "Horizontal Constraint : bool", "Vertical Constraint :bool", "mass : double"
        /// the Beam2d elements must have the following attributes:
        /// "Noeud1 :int", "Noeud2 :int", "E :double", "S :double", "I :double", "Rho :double"
        /// </summary>
        /// <param name="structure"></param>
        /// <param name="entitiesService"></param>
        /// <returns></returns>
        /// 

        static public Structure CreateStructure1(AliantWebS.Models.Structure structure, Dictionary<LoadCase, double> selectedLoadCases, List<Element> elements, List<Element> nodes)
        {
            List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue)> ChargesStatiqueReparties = new List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue)>();
            List<(Guid id, List<int> noeudInd, int dof, double value, double position)> ChargesStatiquePonctuelles = new List<(Guid id, List<int> noeudInd, int dof, double value, double position)>();
            List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue, Dictionary<double, double> tempFunc)> ChargesDynamiqueReparties = new List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue, Dictionary<double, double> tempFunc)>();
            List<(Guid id, List<int> noeudInd, int dof, double value, double position, Dictionary<double, double> tempFunc)> ChargesDynamiquePonctuelles = new List<(Guid id, List<int> noeudInd, int dof, double value, double position, Dictionary<double, double> tempFunc)>();
            List<(Guid id, List<int> noeudInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL,
                double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)> ChargesMobiles = new List<(Guid id, List<int> noeudInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL, double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)>();



            //if (structure.Elements.Count == 0)
            //{
            //    entitiesService.LoadCollection(structure, "Elements");
            //}
            // List<Guid> elList = structure.Elements.Select(e => e.ElementId).ToList();




            //List<LoadCase> loadCases = new List<LoadCase>();


            Structural.Structure structura = new Structure();








            List<double[]> noeuds = new List<double[]>();
            List<string[]> CL = new List<string[]>();
            List<Guid> guidsNoeuds = new List<Guid>();

            int index = 0;
            foreach (Element node in nodes)
            {
                double x = double.Parse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "X").Value, CultureInfo.InvariantCulture);
                double y = double.Parse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Y").Value, CultureInfo.InvariantCulture);
                noeuds.Add(new double[] { x, y });
                guidsNoeuds.Add(node.ElementId);

                bool success = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Horizontal Constraint 1")?.Value, out bool u);
                if (!success)
                {
                    // Handle the case where the conversion was not successful
                    u = false; // or set a default value
                }

                bool success1 = bool.TryParse(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name == "Vertical Constraint")?.Value, out bool v);
                if (!success1)
                {
                    // Handle the case where the conversion was not successful
                    v = false; // or set a default value
                }
                if (u)
                {
                    CL.Add(new string[] { index.ToString(), "U" });
                }

                if (v)
                {
                    CL.Add(new string[] { index.ToString(), "V" });
                }

                double? mass = Convert.ToDouble(node.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim().ToLowerInvariant() == "mass")?.Value);
                if ((mass != null) && (mass > 0))
                {
                    structura.MasseNodale.Rows.Add(index, mass);
                }
                if (selectedLoadCases != null)
                {
                    AddCharge(node, index, selectedLoadCases, ChargesStatiqueReparties, ChargesStatiquePonctuelles,
    ChargesDynamiqueReparties, ChargesDynamiquePonctuelles, ChargesMobiles);
                }

                index++;
            }

            structura.noeud = noeuds;
            structura.guidsNoeuds = guidsNoeuds;
            structura.CL = CL;

            DataTable element;

            element = Structure.makeElementsDataTable();

            index = 0;
            foreach (Element el in elements)
            {
                string? noeud1 = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Noeud1").Value;
                string? noeud2 = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Noeud2").Value;

                double e = 1E6 * double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "E").Value, CultureInfo.InvariantCulture);
                double s = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "S").Value, CultureInfo.InvariantCulture);
                double i = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "I")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempI) ? tempI : 0;

                //double i = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Iy").Value, CultureInfo.InvariantCulture);
                //double rho = double.Parse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Rho").Value, CultureInfo.InvariantCulture);
                double rho = double.TryParse(el.AttributeElements.FirstOrDefault(ae => ae.AttributeType.Name.Trim() == "Rho")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tempRho) ? tempRho : 1;

                int indexnoeud1 = nodes.FindIndex(x => x.Name == noeud1);
                int indexnoeud2 = nodes.FindIndex(x => x.Name == noeud2);
                element.Rows.Add(indexnoeud1, indexnoeud2, e, 0, s, i, 0, rho, 0, 0, el.ElementId);
                //element.Rows.Add(indexnoeud1, indexnoeud2, e, G, s, iy, iz, rho, b, h, el.ElementId);
                if (selectedLoadCases != null)
                {
                    AddCharge(el, index, selectedLoadCases, ChargesStatiqueReparties, ChargesStatiquePonctuelles,
 ChargesDynamiqueReparties, ChargesDynamiquePonctuelles, ChargesMobiles);
                }


                index++;

            }

            structura.element = element;


            structura.ChargesStatiqueReparties = ChargesStatiqueReparties;
            structura.ChargesStatiquePonctuelles = ChargesStatiquePonctuelles;
            structura.ChargesDynamiqueReparties = ChargesDynamiqueReparties;
            structura.ChargesDynamiquePonctuelles = ChargesDynamiquePonctuelles;
            structura.ChargesMobiles = ChargesMobiles;


            structura.nddl = structura.noeud.Count * 3;

            return structura;

        }


        static private void AddCharge(Element el, int elementIndex, Dictionary<LoadCase, double> selectedLoadCases,
            List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue)> ChargesStatiqueReparties,
        List<(Guid id, List<int> noeudInd, int dof, double value, double position)> ChargesStatiquePonctuelles,
        List<(Guid id, List<int> noeudInd, int dof, double startValue, double endValue, Dictionary<double, double> tempFunc)> ChargesDynamiqueReparties,
        List<(Guid id, List<int> noeudInd, int dof, double value, double position, Dictionary<double, double> tempFunc)> ChargesDynamiquePonctuelles,
        List<(Guid id, List<int> noeudInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL,
            double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)> ChargesMobiles
)
        {
            List<Guid> selectedLoadCasesids = selectedLoadCases.Keys.Select(x => x.LoadCaseId).ToList();

            Dictionary<StaticLoad, double> staticLoads = el.StaticLoadElements.Where(x => selectedLoadCasesids.Contains(x.StaticLoad.LoadCaseId)).
                Select(sle => (sle.StaticLoad, selectedLoadCases[sle.StaticLoad.LoadCase])).
                ToDictionary(x => x.StaticLoad, x => x.Item2);

            Dictionary<StaticPointLoad, double> staticPointLoads = el.StaticPointLoadElements.Where(x => selectedLoadCasesids.Contains(x.StaticPointLoad.LoadCaseId)).Select(sle => (sle.StaticPointLoad, selectedLoadCases[sle.StaticPointLoad.LoadCase])).
    ToDictionary(x => x.StaticPointLoad, x => x.Item2);


            //foreach (var lc in selectedLoadCases)
            //  {
            //LoadCase lc = elc.LoadCase;
            //if (selectedLoadCases != null)
            //{
            //    if (!selectedLoadCases.Keys.Contains(lc)) continue;
            //}
            foreach (StaticLoad sl in staticLoads.Keys)
            {
                int ind = ChargesStatiqueReparties.FindIndex(x => x.id == sl.StaticLoadId);
                if (ind == -1)
                {
                    ChargesStatiqueReparties.Add((sl.StaticLoadId, new List<int>() { elementIndex }, (int)sl.Dof, (sl.StartValue ?? 0) * (staticLoads?[sl] ?? 1), (sl.EndValue ?? 0) * (staticLoads?[sl] ?? 1)));
                }
                else
                {
                    ChargesStatiqueReparties[ind].noeudInd.Add(elementIndex);
                }
            }


            foreach (StaticPointLoad sl in staticPointLoads.Keys)
            {
                int ind = ChargesStatiquePonctuelles.FindIndex(x => x.id == sl.StaticPointLoadId);
                if (ind == -1)
                {
                    ChargesStatiquePonctuelles.Add((sl.StaticPointLoadId, new List<int>() { elementIndex }, (int)sl.Dof, (sl.Value ?? 0) * (staticPointLoads?[sl] ?? 1), sl.ApplicationPointDistance ?? 0));
                }
                else
                {
                    ChargesStatiquePonctuelles[ind].noeudInd.Add(elementIndex);
                }
            }








            //}

        }
        /// <summary>
        /// not completed
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        static Structure CopyStructure(Structure str)
        {
            Structure structure = new Structure();

            structure.element = str.element.Copy();
            structure.CL = new List<string[]>(str.CL);
            structure.noeud = new List<double[]>(str.noeud);

            return structure;

        }





        public delegate Task TestEventHandler(object sender, params string[] messages);

        public event TestEventHandler SimulationProgressing;




        public async Task Simulate(Structural.Structure structuralStructure, Structural.SimulationParameters simulationParameters)

        //  AliantWebS.Models.Structure str, Structure structure, string simulationID, string shortDescription, int fileOpens,
        // int simulationNum, int duree, int samplingFrequency, int multiplicity, int divider, string path)
        {




            var fullPath = Path.Combine(simulationParameters.Path, simulationParameters.SimulationID + ".H5");

            long fileId = Hdf5.CreateFile(fullPath);

            Hdf5.WriteObject(fileId, simulationParameters, "simulationParameters");

            ChunkedDataset<double> FFTMagnitudeTraindDset = null; // new ChunkedDataset<double>("/FFTMagnitudeTrain", fileId);
            ChunkedDataset<double> FFTPhaseTrainDset = null; // new ChunkedDataset<double>("/FFTPhaseTrain", fileId);
            ChunkedDataset<double> ETrainDset = new ChunkedDataset<double>("/ETrain", fileId);
            ChunkedDataset<double> PSDDset = null;// new ChunkedDataset<double>("/PSD", fileId);
            ChunkedDataset<double> MTset = null;// new ChunkedDataset<double>("/MT", fileId);
            ChunkedDataset<double> EigValueset = null;//new ChunkedDataset<double>("/EIGValues", fileId);
            ChunkedDataset<double> EigVectset = null;// new ChunkedDataset<double>("/EIGVect", fileId);
            ChunkedDataset<double> Covset = new ChunkedDataset<double>("/Cov", fileId);




            int elementsNum = structuralStructure.element.Rows.Count;

            double steps = (double)simulationParameters.FileOpens * (double)simulationParameters.SimulationNum / ((double)elementsNum);
            steps = 1 / steps;

            Random rndGlobal;
            if (simulationParameters.GlobalSeed == null)
            {
                rndGlobal = new Random();
            }
            else
            {
                rndGlobal = new Random((int)simulationParameters.GlobalSeed);
            }


            for (int fileOpen = 0; fileOpen < simulationParameters.FileOpens; fileOpen++)
            {

                int globalSeed = rndGlobal.Next();

                ConcurrentBag<Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>>> bagfftpsde = new ConcurrentBag<Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>>>();



                ParallelOptions po = new ParallelOptions();
                po.MaxDegreeOfParallelism = 100;
                Parallel.For(0, simulationParameters.SimulationNum, po, n =>
                {



                    Random rnd = new Random(n + globalSeed);

                    MathNet.Numerics.Distributions.Normal nn = new MathNet.Numerics.Distributions.Normal(0.97, 0.02, rnd);
                    double factor = nn.Sample();

                    //       for (int n = 0; n < simulationNum; n++)


                    Structure Str = new Structure();

                    Str.CL = structuralStructure.CL;
                    Str.noeud = structuralStructure.noeud;
                    Str.element = structuralStructure.element.Copy();
                    Str.MasseNodale = structuralStructure.MasseNodale;

                    double dTemps = 1.0 / simulationParameters.SamplingFrequency;
                    //List<double[]> Vehicle = null;

                    string loadType = "";
                    switch (4)
                    {
                        case 0: loadType = "Traffic1dir"; break;
                        case 1: loadType = "Traffic2dir"; break;
                        case 2: loadType = "Creneau"; break;
                        case 3: loadType = "Dirac"; break;
                        case 4: loadType = "Lacher"; break;

                    }
                    // double m1 = 1 + rnd.NextDouble() * 2;
                    //  double m2 = 1 + rnd.NextDouble() * 3;
                    //  double m3 = 1 + rnd.NextDouble() * 3;

                    //loadType = CreateLoad(m1, m2, m3, Str.CasCharge, Str.ChargeStatique, Str.ChargeMobile, Str.FonctionTemporelle, simulationParameters.Duree, out Vehicle);

                    double[] qt;

                    if (simulationParameters.oneElementAtATime)
                    {
                        int elementIndex = (fileOpen * simulationParameters.SimulationNum + n) % elementsNum;
                        double decr = ((int)((fileOpen * simulationParameters.SimulationNum + n) / elementsNum)) * steps;
                        if (Math.Abs(decr) < 0.001) decr = 0.001;

                        Str.element.Rows[elementIndex]["E"] = Str.element.Rows[elementIndex].Field<double>("E") * decr;


                    }
                    else
                    {
                        int inddd = 0;
                        foreach (DataRow dr in Str.element.Rows)
                        {


                            dr["E"] = dr.Field<double>("E") * rnd.NextDouble();
                            inddd++;
                        }
                    }


                    double[,] qTime = new double[0, 0];
                    Matrix<double>[] covss = null;

                    qTime = StructuralTimeIntegration.Calcul_Dynamique(duree: simulationParameters.Duree, nbReponse: simulationParameters.SamplingFrequency,
                        nbIntegration: simulationParameters.SamplingFrequency * 10, Str.nddl, noeud: Str.noeud, element: Str.element,
                        CL: Str.CL, MasseNodale: Str.MasseNodale,
                       Str.ChargesStatiquePonctuelles, Str.ChargesStatiqueReparties, Str.ChargesDynamiquePonctuelles,
                       Str.ChargesDynamiqueReparties, Str.ChargesMobiles);

                    int maxLag = simulationParameters.SamplingFrequency * 3;//?????
                    covss = SSI.SSI.ConstructCovarianceMatrices2(qTime.Transpose(), maxLag);

                    double[] ValeursPropres = null;
                    double[,] VecteursPropres = null;

                    if (EigValueset != null)
                    {
                        ValeursPropres = StructuralVibration.Calcul_ValeursPropres(Str.element, Str.noeud, Str.MasseNodale, Str.CL, out VecteursPropres);
                    }

                    List<Complex[,]> FFTs = null;

                    if (FFTMagnitudeTraindDset != null)
                    {
                        FFTs = SP.SmoothAndAverageFFT(simulationParameters.SamplingFrequency, simulationParameters.Duree, qTime, simulationParameters.Divider, simulationParameters.Multiplicity);
                    }

                    // for plotting purposes

                    //  ws = new double[FFTs[0].Columns()];

                    //int signalLength = nt * (duree / (divider*multiplicity));
                    // var samplingPeriod = 1.0 / nt;

                    //     for (int i = 0; i < FFTs[0].Columns(); i++)
                    //   {

                    //      ws[i] = (1.0 * i / (signalLength * samplingPeriod));


                    //   }


                    List<List<Complex[,]>> PSDs = null;
                    PSDs = new List<List<Complex[,]>>();

                    if (PSDDset != null)
                    {
                        PSDs = SP.SmoothAndAveragePSD(simulationParameters.SamplingFrequency, simulationParameters.Duree,
                            qTime, simulationParameters.Divider, simulationParameters.Multiplicity);
                    }

                    List<Matrix<double>[]> Covs = new List<Matrix<double>[]>();
                    if (Covset != null)
                    {
                        Covs.Add(covss);

                    }

                    List<double[]> Eigvalues = new List<double[]>();
                    if (EigValueset != null)
                    {
                        for (int k = 0; k < simulationParameters.Multiplicity; k++)
                        {
                            Eigvalues.Add(ValeursPropres);
                        }

                    }

                    List<double[,]> Eigvectors = new List<double[,]>();
                    if (EigVectset != null)
                    {
                        for (int k = 0; k < simulationParameters.Multiplicity; k++)
                        {
                            Eigvectors.Add(VecteursPropres);
                        }

                    }


                    List<double[]> listModulesYoung = new List<double[]>();

                    double[] modulesYoung = new double[Str.element.Rows.Count];

                    int indexR = 0;
                    foreach (DataRow dr in Str.element.Rows)
                    {
                        modulesYoung[indexR] = dr.Field<double>("E");
                        indexR++;
                    }


                    for (int k = 0; k < simulationParameters.Multiplicity; k++)
                    {
                        listModulesYoung.Add(modulesYoung);
                    }

                    Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>> tup =
Tuple.Create(FFTs, PSDs, listModulesYoung, qTime, Covs, Eigvalues, Eigvectors);


                    bagfftpsde.Add(tup);



                    SimulationProgressing?.Invoke(null, (fileOpen * simulationParameters.SimulationNum + n).ToString());

                });

                bagfftpsdeToH5(fileId, fileOpen, simulationParameters.Multiplicity, bagfftpsde,
                             FFTMagnitudeTraindDset: ref FFTMagnitudeTraindDset,
                             ref FFTPhaseTrainDset,
                             ref ETrainDset,
                             ref PSDDset,
                             ref MTset,
                             ref Covset,
                             ref EigValueset,
                             ref EigVectset
                             );



            }

            FFTMagnitudeTraindDset.Flush();
            FFTPhaseTrainDset.Flush();
            ETrainDset.Flush();

            FFTMagnitudeTraindDset.Dispose();
            FFTPhaseTrainDset.Dispose();
            ETrainDset.Dispose();

            Hdf5.CloseFile(fileId);


        }



        static private void bagfftpsdeToH5(long fileId, int fileOpen, int multiplicity,
                   ConcurrentBag<Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>>> bagfftpsde,
                   ref ChunkedDataset<double> FFTMagnitudeTraindDset,
                   ref ChunkedDataset<double> FFTPhaseTrainDset,
                   ref ChunkedDataset<double> ETrainDset,
                   ref ChunkedDataset<double> PSDDset,
                   ref ChunkedDataset<double> MTset,
                   ref ChunkedDataset<double> Covset,
                   ref ChunkedDataset<double> EigValueset,
                   ref ChunkedDataset<double> EigVectset

           )
        {
            List<List<double>> ls1a = new List<List<double>>();
            List<List<double>> ls3a = new List<List<double>>();

            double[,] ea = null;
            if (ETrainDset != null)
            {
                ea = new double[0, bagfftpsde.First().Item3.First().Count()];
            }

            double[,] covm = null;
            if (Covset != null)
            {
                covm = new double[0, bagfftpsde.First().Item5.First().SelectMany(matrix => matrix.ToRowMajorArray()).ToArray().Count()];
            }

            double[,] valProp = null;
            if (EigValueset != null)
            {
                valProp = new double[0, bagfftpsde.First().Item6.First().Count()];
            }


            double[,] vecProp = null;
            if (EigVectset != null)
            {
                vecProp = new double[0, bagfftpsde.First().Item7.First().Flatten().Count()];
            }



            //var vv = bagfftpsde.First().Item2.First();

            //var flatenedPSD = FlattenNestedList(bagfftpsde.First().Item2.First());

            double[][] psd = null;
            if (PSDDset != null)
            {
                psd = new double[0][];
            }


            foreach (var b in bagfftpsde)
            {
                //     sp.Plot(ws, v.Item1[0]);


                for (int k = 0; k < multiplicity; k++)
                {
                    if (ETrainDset != null)
                    {
                        ea = ea.InsertRow(b.Item3[k]);
                    }

                    if (Covset != null)
                    {
                        covm = covm.InsertRow(b.Item5[k].SelectMany(matrix => matrix.ToRowMajorArray()).ToArray());
                    }

                    if (EigValueset != null)
                    {
                        valProp = valProp.InsertRow(b.Item6[k]);
                    }

                    if (EigVectset != null)
                    {
                        vecProp = vecProp.InsertRow(b.Item7[k].Flatten());
                    }




                    if (PSDDset != null)
                    {
                        int currentLength = psd.Length;
                        Array.Resize(ref psd, currentLength + 1);
                        psd[currentLength] = (FlattenNestedList(b.Item2[k]));
                    }

                    if (FFTMagnitudeTraindDset != null)
                    {

                        List<double> smal = new List<double>();
                        List<double> sphl = new List<double>();

                        for (int i = 0; i < b.Item1[k].Rows(); i++)
                        {
                            smal.AddRange(b.Item1[k].Magnitude().GetRow(i));
                            sphl.AddRange(b.Item1[k].Phase().GetRow(i));

                        }

                        ls1a.Add(smal);
                        ls3a.Add(sphl);
                    }

                }
                if (FFTMagnitudeTraindDset != null)
                {
                    int im = ls1a.Count;
                    int jm = ls1a[0].Count;

                    double[,] s1a = new double[ls1a.Count, ls1a[0].Count];
                    double[,] s3a = new double[ls3a.Count, ls3a[0].Count];

                    for (int i = 0; i < im; i++)
                    {
                        for (int j = 0; j < jm; j++)
                        {
                            s1a[i, j] = ls1a[i][j];
                            s3a[i, j] = ls3a[i][j];
                        }
                    }
                }


            }


            //  List<string> psdListString = new List<string>();                
            //  foreach (var v in psdConcurentBag)
            //   {
            //      psdListString.Add(string.Join(",", v.Re()));
            //  }

            /*

            double[,] psdListStringa = new double[0, psdConcurentBag.First().Count()];         
            foreach (var v in psdConcurentBag)
            {
                psdListStringa = psdListStringa.InsertRow(v.Re());
           }
            */





            double[][] output1 = null;

            // create a dataset and append two more datasets to it
            if (PSDDset != null)
            {


            }

            if (fileOpen == 0)
            {
                if (FFTMagnitudeTraindDset != null)
                {
                    //   FFTMagnitudeTraindDset = new ChunkedDataset<double>("/FFTMagnitudeTrain", fileId, s1a);
                    //    FFTPhaseTrainDset = new ChunkedDataset<double>("/FFTPhaseTrain", fileId, s3a);
                }
                if (PSDDset != null)
                {

                    int rows = output1.Length;
                    int cols = output1[0].Length;

                    double[,] multiArray = new double[rows, cols];

                    for (int i = 0; i < rows; i++)
                    {
                        for (int j = 0; j < cols; j++)
                        {
                            multiArray[i, j] = output1[i][j];
                        }
                    }

                    PSDDset = new ChunkedDataset<double>("/PSD", fileId, multiArray);
                }

                if (MTset != null)
                {

                    MTset = new ChunkedDataset<double>("/MT", fileId, bagfftpsde.First().Item4);
                }

                if (ETrainDset != null)
                {
                    ETrainDset = new ChunkedDataset<double>("/ETrain", fileId, ea);
                }



                if (Covset != null)
                {
                    Covset = new ChunkedDataset<double>("/Cov", fileId, covm);
                }

                if (EigValueset != null)
                {
                    EigValueset = new ChunkedDataset<double>("/EIGValues", fileId, valProp);
                }

                if (EigVectset != null)
                {
                    EigVectset = new ChunkedDataset<double>("/EIGVect", fileId, vecProp);
                }

                //            picDset = new ChunkedDataset<double[,,]>("/picTrain", fileId, pic);
            }

            else
            {
                if (FFTMagnitudeTraindDset != null)
                {
                    //   FFTMagnitudeTraindDset.AppendDataset(s1a);
                    //   FFTPhaseTrainDset.AppendDataset(s3a);
                }
                if (ETrainDset != null)
                {
                    ETrainDset.AppendDataset(ea);
                }

                if (Covset != null)
                {
                    Covset.AppendDataset(covm);
                }

                if (EigValueset != null)
                {
                    EigValueset.AppendDataset(valProp);
                }

                if (EigVectset != null)
                {
                    EigVectset.AppendDataset(vecProp);
                }




                if (PSDDset != null)
                {
                    // Create a 2D array with the same dimensions
                    double[,] twoDimensionalArray = new double[output1.Length, output1[0].Length];

                    // Copy the elements from the jagged array to the 2D array
                    for (int i = 0; i < output1.Length; i++)
                    {
                        for (int j = 0; j < output1[i].Length; j++)
                        {
                            twoDimensionalArray[i, j] = output1[i][j];
                        }
                    }



                    PSDDset.AppendDataset(twoDimensionalArray);
                }
                if (MTset != null)
                {

                    MTset.AppendDataset(bagfftpsde.First().Item4);
                }

                //  picDset.AppendDataset(pic);
            }




            // read rows 9 to 22 of the dataset
            /*        ulong begIndex = 8;
                ulong endIndex = 21;
                var dset0 = Hdf5.ReadDataset<double>(fileId, "/test0");
                var dset1 = Hdf5.ReadDataset<double>(fileId, "/test1");
            */


        }

        static private double[] FlattenNestedList(List<Complex[,]> complexList)
        {
            List<double> flattenedList = new List<double>();

            foreach (Complex[,] complexArray in complexList)
            {
                int numRows = complexArray.GetLength(0);
                int numCols = complexArray.GetLength(1);

                for (int row = 0; row < numRows; row++)
                {
                    for (int col = 0; col < numCols; col++)
                    {
                        Complex complexValue = complexArray[row, col];
                        flattenedList.Add(complexValue.Real);
                        flattenedList.Add(complexValue.Imaginary);
                    }
                }
            }

            return flattenedList.ToArray();
        }


        static private double[,] ReadFromCSV(string filePath, int nombreCapteur, char SplitChar)
        {
            double[,] a = new double[0, nombreCapteur * 4];


            var reader = new StreamReader(File.OpenRead(filePath));

            int index = 0;
            var line = reader.ReadLine();
            line = reader.ReadLine();

            while (!reader.EndOfStream)
            {
                line = reader.ReadLine();
                var values = line.Split(SplitChar);
                double[] d = new double[nombreCapteur * 4];
                try
                {
                    for (int i = 0; i < nombreCapteur; i++)
                    {

                        d[i * 4 + 0] = Convert.ToDouble(values[i * 4 + 0]);
                        d[i * 4 + 1] = Convert.ToDouble(values[i * 4 + 1]);
                        d[i * 4 + 2] = Convert.ToDouble(values[i * 4 + 2]);
                        d[i * 4 + 3] = Convert.ToDouble(values[i * 4 + 3]);
                        //   d[i*4 + 4] = Convert.ToDouble(values[i + 4]);
                    }
                    a = a.InsertRow(d);
                    index++;

                }
                catch (Exception ex)
                {
                    int k = 1;
                }

            }

            return a;
        }

        static private double Fetch(double time, int indexCapteur, int indexdll, double[,] qt, int frequenceFichierDonnees)
        {

            double result = 0;

            for (int i = 0; i < qt.GetLength(0); i++)
            {
                if (Math.Abs(time - qt[i, indexCapteur * 4]) < 0.00001)
                {
                    return qt[i, indexCapteur * 4 + indexdll];
                }

                if (time < qt[i, 0])
                {
                    if (i == 0) return qt[i, indexCapteur * 4 + indexdll];


                    double x0 = qt[i - 1, indexCapteur * 4];
                    double x1 = qt[i, indexCapteur * 4];
                    double y0 = qt[i - 1, indexCapteur * 4 + indexdll];
                    double y1 = qt[i, indexCapteur * 4 + indexdll];

                    return result = y0 + (y1 - y0) * (time - x0) / (x1 - x0);
                }
            }

            return result;
        }




        static public void CSVsToH5(List<(string, int)> csvsFilesPaths, string outputH5FilePath, SimulationParameters simPa, int nddls)
        {
            long fileId = Hdf5.CreateFile(outputH5FilePath);

            Hdf5.WriteObject(fileId, simPa, "simulationParameters");

            ChunkedDataset<double> FFTMagnitudeTraindDset = new ChunkedDataset<double>("/FFTMagnitudeTrain", fileId);
            ChunkedDataset<double> FFTPhaseTrainDset = new ChunkedDataset<double>("/FFTPhaseTrain", fileId);
            ChunkedDataset<double> ETrainDset = null;// new ChunkedDataset<double>("/ETrain", fileId);

            ChunkedDataset<double> PSDDset = null;// new ChunkedDataset<double>("/PSD", fileId);
            ChunkedDataset<double> MTset = null;// new ChunkedDataset<double>("/MT", fileId);
            ChunkedDataset<double> EigValueset = null;//new ChunkedDataset<double>("/EIGValues", fileId);
            ChunkedDataset<double> EigVectset = null;// new ChunkedDataset<double>("/EIGVect", fileId);
            ChunkedDataset<double> Covset = new ChunkedDataset<double>("/Cov", fileId);

            //    ChunkedDataset<double> PSDTrainDset = new ChunkedDataset<double>("/PSDTrain", fileId);

            ConcurrentBag<Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>>> bagfftpsde = new ConcurrentBag<Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>>>();



            //int divider = 1;
            //int multiplicity = 1;
            //int duree = 5;
            //int nt = 1000;
            //int nddl = 51;

            int divider = simPa.Divider;
            int multiplicity = simPa.Multiplicity;
            int duree = simPa.Duree;
            int nt = simPa.SamplingFrequency;




            double[,] qTime = new double[duree * nt, nddls];
            Matrix<double>[] covss = null;
            List<double[,]> qts = new List<double[,]>();

            for (int i = 0; i < csvsFilesPaths.Count(); i++)
            {
                qts.Add(ReadFromCSV(csvsFilesPaths[i].Item1, 1, ','));
            }

            for (int i = 0; i < duree * nt; i++)
            {

                for (int j = 0; j < csvsFilesPaths.Count; j++)
                {
                    qTime[i, csvsFilesPaths[j].Item2] = Fetch((double)i * 1000, 0, 3, qts[j], 1000);
                }

            }

            int maxLag = nt * 3;//?????
            covss = SSI.SSI.ConstructCovarianceMatrices2(qTime.Transpose(), maxLag);

            List<Complex[,]> FFTs = null;
            if (FFTMagnitudeTraindDset != null)
            {
                FFTs = SP.SmoothAndAverageFFT(nt, duree, qTime, divider, multiplicity);
            }


            //  ws = new double[FFTs[0].Columns()];

            int signalLength = nt * (duree / (divider * multiplicity));
            var samplingPeriod = 1.0 / nt;

            //     for (int i = 0; i < FFTs[0].Columns(); i++)
            //   {

            //      ws[i] = (1.0 * i / (signalLength * samplingPeriod));


            //   }

            List<List<Complex[,]>> PSDs = null;
            PSDs = new List<List<Complex[,]>>();

            if (PSDDset != null)
            {
                PSDs = SP.SmoothAndAveragePSD(nt, duree,
                    qTime, divider, multiplicity);
            }


            List<Matrix<double>[]> Covs = new List<Matrix<double>[]>();
            if (Covset != null)
            {
                Covs.Add(covss);

            }





            Tuple<List<Complex[,]>, List<List<Complex[,]>>, List<double[]>, double[,], List<Matrix<double>[]>, List<double[]>, List<double[,]>> tup =
Tuple.Create(FFTs, PSDs, new List<double[]>(), qTime, Covs, new List<double[]>(), new List<double[,]>());

            bagfftpsde.Add(tup);


            bagfftpsdeToH5(fileId: fileId, fileOpen: 0, multiplicity: multiplicity, bagfftpsde: bagfftpsde,
             FFTMagnitudeTraindDset: ref FFTMagnitudeTraindDset,
             FFTPhaseTrainDset: ref FFTPhaseTrainDset,
             ETrainDset: ref ETrainDset,
             PSDDset: ref PSDDset,
             MTset: ref MTset,
             Covset: ref Covset,
             EigValueset: ref EigValueset,
             EigVectset: ref EigVectset
             );
            Hdf5.CloseFile(fileId);
        }




        static private string CreateLoad(double m1, double m2, double m3,
         List<string[]> CasCharge,
         List<string[]> ChargeStatique,
         List<string[]> ChargeMobile,
         List<double[,]> FonctionTemporelle, int Duree, out List<double[]> Vehicle)
        {

            double tauxPL = m1 * 0.05;// 0.2;//pas de 0.05
            double vitesse = m2 * 50.0 * 1000 / 3600;//60.0 * 1000 / 3600;pas de 25
            double lambda = m3 * 100.0 / 3600;// 40.0 / 3600;

            string loadType = CasCharge[0][2];
            CreateLoadWorker(loadType, CasCharge, ChargeStatique, ChargeMobile, FonctionTemporelle, Duree, out Vehicle);
            return loadType;
        }

        public static void CreateLoadWorker(string loadType,
         List<string[]> CasCharge,
         List<string[]> ChargeStatique,
         List<string[]> ChargeMobile,
         List<double[,]> FonctionTemporelle,
        int Duree,
        out List<double[]> Vehicle)
        {
            Vehicle = new List<double[]>();

            double vitesse = Convert.ToDouble(ChargeStatique[0][5]);
            double lambda = Convert.ToDouble(ChargeStatique[0][7]);
            double tauxPL = Convert.ToDouble(ChargeStatique[0][8]);
            double bruit = Convert.ToDouble(ChargeStatique[0][9]);
            int tempsCharge = Convert.ToInt32(FonctionTemporelle[0][0, 0]);

            Random rand = new Random();
            double r = rand.NextDouble();

            double t = (double)tempsCharge;
            double P = 10 * 1000;
            double v = 0;
            int aleaDeVitesse = 2;

            switch (loadType)
            {
                case "Traffic1dir":

                    P = 10 * 1000;

                    r = rand.NextDouble();
                    if (r < tauxPL) P = 50 * 1000;
                    if (r < 0.5 * tauxPL) P = 150 * 1000;

                    P *= (1 + bruit * rand.NextDouble());
                    switch (aleaDeVitesse)
                    {
                        case 0:
                            v = vitesse;
                            break;
                        case 1:
                            v = vitesse * (1 + 2 * bruit * rand.NextDouble());
                            break;
                        case 2:
                            v = Math.Max(vitesse + 10 * (-1 + 3 * rand.NextDouble()), 5);
                            break;
                    }
                    Vehicle.Add(new double[] { P, v, t });
                    if (lambda < (1.01 / 3600.0)) break;

                    do
                    {
                        r = rand.NextDouble();

                        t += -Math.Log(1 - r) / lambda;
                        if (t > Duree) break;

                        P = 10 * 1000;
                        r = rand.NextDouble();
                        if (r < tauxPL) P = 50 * 1000;
                        if (r < 0.5 * tauxPL) P = 150 * 1000;
                        P *= (1 + bruit * rand.NextDouble());

                        switch (aleaDeVitesse)
                        {
                            case 0:
                                v = vitesse;
                                break;
                            case 1:
                                v = vitesse * (1 + 2 * bruit * rand.NextDouble());
                                break;
                            case 2:
                                v = Math.Max(vitesse + 10 * (-1 + 3 * rand.NextDouble()), 5);
                                break;
                        }
                        Vehicle.Add(new double[] { P, v, t });
                    } while (t < Duree);
                    break;

                case "Creneau":
                    P = 10 * 1000 * (1 + bruit * rand.NextDouble());
                    v = 1000 / (vitesse * (1 + 2 * bruit * rand.NextDouble()));
                    Vehicle.Add(new double[] { P, v, t });
                    break;
                case "Lacher":
                    P = 10 * 1000;
                    P = 1 * 10; // 1 kg = 10 N
                    v = 0;
                    Vehicle.Add(new double[] { P, v, t });
                    break;
            }
        }





    }
}
