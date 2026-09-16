using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// Result of the charpente FE solve for one Beam3d element.
    /// </summary>
    public class BeamTaux
    {
        public Guid ElementId { get; set; }
        public long? BimId { get; set; }
        public string Name { get; set; }
        public double Sigma { get; set; }
        public double Tau { get; set; }
        public double Taux { get; set; }   // utilisation ratio = max(sigma/20, tau/15)
    }

    /// <summary>
    /// The charpente finite-element solve — extracted verbatim from Charpente.razor's Calcul()
    /// so it is run from exactly one place. Both callers use the SAME model + SAME taux formula;
    /// only the nodal loads (chargeNodale) differ:
    ///   - Charpente.razor      → calculated loads  (GetChargeNodale)
    ///   - SurveillanceAntares  → measured loads     (max manille weight over a period)
    /// </summary>
    public static class CharpenteFEHelper
    {
        public static async Task<List<BeamTaux>> ComputeAsync(
            Structure structure,
            Dictionary<LoadCase, double> selectedLoadCases,
            Dictionary<Guid, (int dof, double value)> chargeNodale,
            EntitiesService entitiesService)
        {
            var result = new List<BeamTaux>();
            if (structure == null) return result;

            // --- identical to Calcul(): build the structural model with the given nodal loads ---
            Structural.Structure charpenteAntares = Structural.Simulation.CreateStructure3D(
                structure, selectedLoadCases, chargeNodale, entitiesService);

            var nodes = await entitiesService.GetFilteredEntitiesAsync<Element>(
                a => a.StructureId == structure.StructureId && a.ElementType != null && a.ElementType.Name == "Noeud3d");
            var elements = await entitiesService.GetFilteredEntitiesAsync<Element>(
                a => a.StructureId == structure.StructureId && a.ElementType != null && a.ElementType.Name == "Beam3d");

            double[] q = new double[6 * nodes.Count];
            double[] Fe = new double[6 * elements.Count];

            Structural.StructuralStatic.StructureVerification(charpenteAntares, q, Fe);

            int nbelem = -1;
            foreach (DataRow elem in charpenteAntares.element.Rows)
            {
                nbelem++;
                double b = elem.Field<double>("b");
                double h = elem.Field<double>("h");

                double sigma = Fe[0 + 6 * nbelem] / (b * h) + 6 * Fe[3 + 6 * nbelem] / (b * h * h) + 6 * Fe[4 + 6 * nbelem] / (h * b * b);
                double tau = 1.5 * Fe[1 + 6 * nbelem] / (b * h) + 1.5 * Fe[2 + 6 * nbelem] / (b * h);
                sigma /= 100000;
                tau /= 10000;
                Guid id = elem.Field<Guid>("Guid");
                var beam = elements.FirstOrDefault(x => x.ElementId == id);

                result.Add(new BeamTaux
                {
                    ElementId = id,
                    BimId = beam?.BimId,
                    Name = beam?.Name ?? nbelem.ToString(),
                    Sigma = sigma,
                    Tau = tau,
                    Taux = Math.Max(sigma / 20, tau / 15)/100,
                });
            }

            return result;
        }
    }

    /// <summary>
    /// Maps a taux de travail (0..1 utilisation ratio) to a green→red colour, and builds the
    /// BIMComponent colour map (BimIds grouped by colour). Shared by the Modèle 3D page and the
    /// SurveillanceAntares 3D tab so the colour scale lives in one place.
    /// </summary>
    public static class TauxColor
    {
        // 0 = green, 1+ = red. Quantized to 20 levels to keep the number of colour groups small.
        public static (int R, int G, int B) ToColor(double taux)
        {
            double t = Math.Round(Math.Clamp(taux, 0.0, 1.0) * 20) / 20.0;
            int r, g;
            if (t < 0.5) { r = (int)(255 * (t / 0.5)); g = 255; }
            else { r = 255; g = (int)(255 * (1 - (t - 0.5) / 0.5)); }
            return (r, g, 0);
        }

        public static Dictionary<int, (List<long> EntityIds, (int R, int G, int B) Color)> BuildColorMapping(
            Dictionary<long, double> tauxByBimId)
        {
            var buckets = new Dictionary<(int R, int G, int B), List<long>>();
            foreach (var kv in tauxByBimId)
            {
                var c = ToColor(kv.Value);
                if (!buckets.TryGetValue(c, out var list)) { list = new List<long>(); buckets[c] = list; }
                list.Add(kv.Key);
            }

            var mapping = new Dictionary<int, (List<long> EntityIds, (int R, int G, int B) Color)>();
            int i = 0;
            foreach (var kv in buckets)
                mapping[i++] = (kv.Value, kv.Key);
            return mapping;
        }
    }
}
