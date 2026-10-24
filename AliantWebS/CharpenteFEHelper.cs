using AliantWebS.Models;
using AliantWebS.Pages.InventoryElementPages;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using static AliantWebS.Pages.Inventory.StructuresInfoComponent;
using static DiffSharp.TensorOp;

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

    public class ComponentTaux
    {
        public Guid ElementId { get; set; }
        public long? BimId { get; set; }
        public string Name { get; set; }
        public double Sigma { get; set; }
        public double Tau { get; set; }
        public double Taux { get; set; }
    }

    /// <summary>
    /// Bilan des charges ajoutées à chargeNodale par CharpenteFEHelper.AjouterChargesComplementaires,
    /// pour le compte rendu.
    /// </summary>
    public class ChargesComplementaires
    {
        public double ChargesAppliquees { get; set; } // Σ charges nodales reçues (grille), avant poids propre et neige, kg
        public bool PoidsPropre { get; set; }
        public bool Neige { get; set; }
        public double Q { get; set; }                 // charge uniforme du poids propre, kg/m²
        public double Q1 { get; set; }                // surcharge, activée avec le poids propre, kg/m²
        public double EpaisseurNeige { get; set; }    // épaisseur de neige, cm
        public double S { get; set; }                 // charge de neige = 2 kg/m² par cm, kg/m²
        public double PoidsElements { get; set; }     // Σ Rho × S × L des éléments, kg
        public double ChargeQ { get; set; }           // Σ q × surface affectée, kg
        public double ChargeQ1 { get; set; }          // Σ q1 × surface affectée, kg
        public double ChargeNeige { get; set; }       // Σ s × surface affectée, kg
        public double SurfaceTotale { get; set; }     // Σ surfaces affectées, m² (projection horizontale)
        public int NoeudsAvecSurface { get; set; }
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
        public static async Task<(List<BeamTaux>,List<ComponentTaux>)> ComputeAsync(
            Structure structure,
            Dictionary<LoadCase, double> selectedLoadCases,
            Dictionary<Guid, (int dof, double value)> chargeNodale,
            EntitiesService entitiesService)
        {
            var result = new List<BeamTaux>();
            var componentResult = new List<ComponentTaux>();

            if (structure == null) return (result, componentResult);

            // --- identical to Calcul(): build the structural model with the given nodal loads ---
            Structural.Structure charpenteAntares = Structural.Simulation.CreateStructure3D(
                structure, selectedLoadCases, chargeNodale, entitiesService);

            var nodes = await entitiesService.GetFilteredEntitiesAsync<Element>(
                a => a.StructureId == structure.StructureId && a.ElementType != null && a.ElementType.Name == "Noeud3d");
            var elements = await entitiesService.GetFilteredEntitiesAsync<Element>(
                a => a.StructureId == structure.StructureId && a.ElementType != null 
                && (a.ElementType.Name == "Beam3d" || a.ElementType.Name == "Elem3d")  
                && a.Structural == 1);

            double[] q = new double[charpenteAntares.nddl];
            double[] Fe = new double[6*charpenteAntares.element.Rows.Count];

            Structural.StructuralStatic.StructureVerification(charpenteAntares, q, Fe);
            List<string> uniteStr = new List<string>() {
                "Poutre B-", "Poutre F-",
                "Poutre 2-", "Poutre 3-", "Poutre 4-", "Poutre 5-", "Poutre 6-", "Poutre 7-", "Poutre 8-", "Poutre 9-", "Poutre 10-",
                "Membrure B-", "Membrure F-", "Membrure 2-", "Membrure 3-", "Membrure 9-", "Membrure 10-",
                "Ame B-", "Ame F-", "Ame 2-", "Ame 3-", "Ame 9-", "Ame 10-"
                  };
            List<(Guid, long?, string?, double, double, double)> effortMax = new System.Collections.Generic.List<(Guid, long?, string, double, double, double)>();
            for (int i = 0; i < uniteStr.Count; i++) effortMax.Add((Guid.Empty, 0, "", 0, 0, 0));

            string sa = "";
            int nbelem = -1;
            double sigma = 0, tau = 0, sigmaMax = 20, tauMax = 15;
            foreach (DataRow elem in charpenteAntares.element.Rows)
            {

                nbelem++;
                Guid id = elem.Field<Guid>("Guid");
                var beam = elements.FirstOrDefault(x => x.ElementId == id);

                bool trouve = uniteStr.Any(s => beam.Name.Contains(s));
                if (!trouve) continue;

                double b = elem.Field<double>("b");
                double h = elem.Field<double>("h");
                double S, Iy, Iz, J;
                if (b > 0 && h > 0)
                {
                    sigma = Fe[0 + 6 * nbelem] / (b * h) + 6 * Fe[4 + 6 * nbelem] / (b * h * h);// + 6 * Fe[5 + 6 * nbelem] / (h * b * b);
                    tau = 1.5 * Fe[2 + 6 * nbelem] / (b * h);// + 1.5 * Fe[1 + 6 * nbelem] / (b * h);
                    sigmaMax = 20;
                    tauMax = 15;
                }
                else
                {
                    S = elem.Field<double>("S");
                    Iy = elem.Field<double>("Iy");
                    Iz = elem.Field<double>("Iz");

                    sigma = Fe[0 + 6 * nbelem] / S + Fe[4 + 6 * nbelem] * (0.145 / 2) / Iy + Fe[5 + 6 * nbelem] * (0.145 / 2) / Iz;
                    tau = 1.5 * Fe[1 + 6 * nbelem] / S + 1.5 * Fe[2 + 6 * nbelem] / S;
                    sigmaMax = 355 / 1.2;
                    tauMax = 0.6 * 355 / 2;
                }

                //sigma /= 1000;
                //tau /= 10000;
                double Taux = Math.Max(sigma / sigmaMax, tau / tauMax);
                sa += nbelem.ToString() + ";"  + Fe[0 + 6 * nbelem].ToString() + ";" + Fe[1 + 6 * nbelem].ToString() + ";" + Fe[2 + 6 * nbelem].ToString() + ";" + Fe[4 + 6 * nbelem].ToString() + ";" + Fe[5 + 6 * nbelem].ToString() + ";" + sigma.ToString() + ";" + tau.ToString() + ";" + Taux.ToString() + "\n";
                if (Taux>1)
                {
                    int cr = 0;
                }
               result.Add(new BeamTaux
                {
                    ElementId = id,
                    BimId = beam?.BimId,
                    Name = beam?.Name,
                    Sigma = sigma,
                    Tau = tau,
                    Taux = Taux,
                });

                int idx = uniteStr.FindIndex(s => beam.Name.Contains(s));
                if (idx >= 0)
                {
                    var t = effortMax[idx];
                    if (Taux > t.Item6)
                    {
                        t.Item1 = id;
                        t.Item2 = beam?.BimId;
                        t.Item3 = uniteStr[idx].Substring(0, uniteStr[idx].Length - 1) + " (" + beam.Name + ")";
                        t.Item4 = sigma;
                        t.Item5 = tau;
                        t.Item6 = Taux;
                    }
                    effortMax[idx] = t;
                }
            }
            foreach(var u in effortMax)
                componentResult.Add(new ComponentTaux
                {
                    ElementId = u.Item1,
                    BimId = u.Item2,
                    Name = u.Item3,
                    Sigma = u.Item4,
                    Tau = u.Item5,
                    Taux = u.Item6,
                });

  

            return (result, componentResult);
        }

        // Poids de la neige : 2 kg/m² par cm d'épaisseur.
        public const double KgParM2ParCmNeige = 2;

        // Matériaux des éléments bois, seuls à porter la toiture pour le calcul des surfaces affectées.
        private static readonly string[] MateriauxBois = { "GL30h", "C24" };

        // Deux positions en plan plus proches que cette tolérance (m) sont considérées comme le même point.
        private const double ToleranceGeometrie = 0.05;

        // Surfaces affectées par structure, calculées une fois pour toutes : la géométrie ne change pas
        // d'un calcul à l'autre. Appeler InvaliderSurfacesAffectees après une modification des nœuds.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IReadOnlyDictionary<Guid, double>> surfacesParStructure = new();

        public static void InvaliderSurfacesAffectees(Guid structureId) => surfacesParStructure.TryRemove(structureId, out _);

        private static double Attr(Element e, string nom) =>
            double.TryParse(e.AttributeElements.FirstOrDefault(ae => ae.AttributeType?.Name?.Trim() == nom)?.Value,
                            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static string AttrTexte(Element e, string nom) =>
            e.AttributeElements.FirstOrDefault(ae => ae.AttributeType?.Name?.Trim() == nom)?.Value?.Trim();

        /// <summary>
        /// Surface de toiture affectée à chaque nœud (m², projection horizontale), calculée une fois par
        /// structure puis gardée en cache.
        ///   - Nœuds de toiture : les extrémités des éléments bois (GL30h, C24) ; pour une même position
        ///     en plan, seul le nœud le plus haut est gardé.
        ///   - Contour de la toiture : l'enveloppe convexe de ces nœuds en plan.
        ///   - Partage des distances : chaque point du contour revient au nœud le plus proche ; la limite
        ///     entre deux nœuds voisins est la médiatrice de leur segment (cellules de Voronoï découpées
        ///     par le contour). Sur une grille régulière, on retrouve les demi-distances de part et d'autre
        ///     (et les demi- ou quarts de cellule en rive et en angle) ; aux angles non orthogonaux et sur
        ///     les nœuds de rive reliés à un seul élément, la surface suit la géométrie réelle.
        /// La somme des surfaces est exactement l'aire du contour.
        /// </summary>
        public static IReadOnlyDictionary<Guid, double> SurfacesAffectees(Guid structureId, List<Element> elements, List<Element> nodes) =>
            surfacesParStructure.GetOrAdd(structureId, _ => CalculerSurfacesAffectees(elements, nodes));

        private static Dictionary<Guid, double> CalculerSurfacesAffectees(List<Element> elements, List<Element> nodes)
        {
            var noeudsParNom = nodes.Where(n => n.Name != null)
                                    .GroupBy(n => n.Name)
                                    .ToDictionary(g => g.Key, g => g.First());

            // ---- Nœuds des éléments bois ----
            var noeudsBois = new HashSet<Guid>();
            foreach (var el in elements)
            {
                string mat = AttrTexte(el, "Matériau") ?? "";
                if (!MateriauxBois.Any(m => mat.Contains(m))) continue;

                foreach (var attr in new[] { "Noeud1", "Noeud2" })
                {
                    string nom = AttrTexte(el, attr);
                    if (nom != null && noeudsParNom.TryGetValue(nom, out var nd)) noeudsBois.Add(nd.ElementId);
                }
            }

            long Cle(double v) => (long)Math.Round(v / ToleranceGeometrie);

            var toiture = nodes
                .Where(n => noeudsBois.Contains(n.ElementId))
                .Select(n => (Id: n.ElementId, X: Attr(n, "X"), Y: Attr(n, "Y"), Z: Attr(n, "Z")))
                .GroupBy(n => (Cle(n.X), Cle(n.Y)))
                .Select(g => g.OrderByDescending(n => n.Z).First())
                .ToList();

            var surfaces = new Dictionary<Guid, double>();
            var contour = EnveloppeConvexe(toiture.Select(n => (n.X, n.Y)).ToList());
            if (contour.Count < 3) return surfaces;   // nœuds alignés : pas de surface

            foreach (var p in toiture)
            {
                var cellule = contour;
                foreach (var o in toiture)
                {
                    if (o.Id == p.Id) continue;

                    // Garder le côté de p de la médiatrice de [p, o] : (x - m)·(o - p) <= 0.
                    double nx = o.X - p.X, ny = o.Y - p.Y;
                    double c = nx * (p.X + o.X) / 2 + ny * (p.Y + o.Y) / 2;
                    cellule = DecouperDemiPlan(cellule, nx, ny, c);
                    if (cellule.Count < 3) break;
                }

                double aire = Aire(cellule);
                if (aire > 0) surfaces[p.Id] = aire;
            }
            return surfaces;
        }

        /// <summary>Enveloppe convexe (chaîne monotone d'Andrew), sens trigonométrique, sans points alignés.</summary>
        private static List<(double X, double Y)> EnveloppeConvexe(List<(double X, double Y)> points)
        {
            var pts = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (pts.Count < 3) return pts;

            static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
                (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

            var h = new List<(double X, double Y)>();
            foreach (var p in pts)                                   // chaîne inférieure
            {
                while (h.Count >= 2 && Cross(h[^2], h[^1], p) <= 1e-12) h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            int bas = h.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--)                 // chaîne supérieure
            {
                while (h.Count >= bas && Cross(h[^2], h[^1], pts[i]) <= 1e-12) h.RemoveAt(h.Count - 1);
                h.Add(pts[i]);
            }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        /// <summary>Polygone convexe ∩ demi-plan { nx·x + ny·y <= c } (Sutherland–Hodgman sur une arête).</summary>
        private static List<(double X, double Y)> DecouperDemiPlan(List<(double X, double Y)> poly, double nx, double ny, double c)
        {
            var res = new List<(double X, double Y)>(poly.Count + 1);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                double da = nx * a.X + ny * a.Y - c;
                double db = nx * b.X + ny * b.Y - c;

                if (da <= 0) res.Add(a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0))
                {
                    double t = da / (da - db);
                    res.Add((a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
                }
            }
            return res;
        }

        /// <summary>Aire d'un polygone (formule du lacet), m².</summary>
        private static double Aire(List<(double X, double Y)> poly)
        {
            double s = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                s += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(s) / 2;
        }

        /// <summary>
        /// Ajoute à chargeNodale (clé = nœud Noeud3d) les charges verticales :
        ///   - poids propre : Rho × S × L de chaque élément, moitié sur chacun de ses deux nœuds,
        ///                    plus (q + q1) × surface affectée de chaque nœud de toiture (q1 : surcharge,
        ///                    activée par le même interrupteur) ;
        ///   - neige        : s × surface affectée de chaque nœud de toiture, s = 2 kg/m² par cm d'épaisseur.
        /// Unités : tout en kg, comme la grille — Rho en kg/m³, S en m², L en m ; q et q1 en kg/m² ;
        /// épaisseur de neige en cm.
        /// Les éléments et les nœuds doivent être chargés avec "AttributeElements.AttributeType".
        /// </summary>
        public static ChargesComplementaires AjouterChargesComplementaires(
            Dictionary<Guid, (int dof, double value)> chargeNodale,
            Guid structureId,
            List<Element> elements,
            List<Element> nodes,
            bool poidsPropre, double q, double q1,
            bool neige, double epaisseurNeige)
        {
            double s = epaisseurNeige * KgParM2ParCmNeige;   // kg/m²
            var bilan = new ChargesComplementaires
            {
                // Relevé avant tout ajout : ce sont les charges réellement appliquées au modèle, hors poids propre et neige.
                ChargesAppliquees = chargeNodale.Values.Sum(c => c.value),
                PoidsPropre = poidsPropre, Neige = neige, Q = q, Q1 = q1, EpaisseurNeige = epaisseurNeige, S = s
            };
            if (!poidsPropre && !neige) return bilan;

            // dof 2 : vertical, même convention que GetChargeNodale.
            void Ajouter(Guid noeud, double p)
            {
                if (p == 0) return;
                chargeNodale[noeud] = chargeNodale.TryGetValue(noeud, out var c) ? (c.dof, c.value + p) : (2, p);
            }

            var noeudsParNom = nodes.Where(n => n.Name != null)
                                    .GroupBy(n => n.Name)
                                    .ToDictionary(g => g.Key, g => g.First());

            // ---- Poids des éléments : Rho × S × L ----
            if (poidsPropre)
            {
                foreach (var el in elements)
                {
                    string n1 = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType?.Name?.Trim() == "Noeud1")?.Value;
                    string n2 = el.AttributeElements.FirstOrDefault(ae => ae.AttributeType?.Name?.Trim() == "Noeud2")?.Value;
                    if (n1 == null || n2 == null
                        || !noeudsParNom.TryGetValue(n1, out var nd1) || !noeudsParNom.TryGetValue(n2, out var nd2))
                        continue;

                    double rho = Attr(el, "Rho");
                    string mat =el.AttributeElements.FirstOrDefault(ae => ae.AttributeType?.Name?.Trim() == "Matériau")?.Value.ToString();
                    if (rho<= 0) 
                    {
                        if (mat.Contains("S235") || mat.Contains("S355")) rho = 7850;
                        if (mat.Contains("C24") || mat.Contains("GL30h")) rho = 450;
                        if (mat.Contains("C25/30") || mat.Contains("C30/35")) rho = 2500;
                    }
                    double section = Attr(el, "S");
                    if (section <= 0) section = Attr(el, "b") * Attr(el, "h");   // sections rectangulaires (bois)

                    double dx = Attr(nd2, "X") - Attr(nd1, "X");
                    double dy = Attr(nd2, "Y") - Attr(nd1, "Y");
                    double dz = Attr(nd2, "Z") - Attr(nd1, "Z");
                    double longueur = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                    double poids = rho * section * longueur;
                    if (poids <= 0) continue;
                    
                    Ajouter(nd1.ElementId, -poids / 2);
                    Ajouter(nd2.ElementId, -poids / 2);
                    bilan.PoidsElements += poids;
                }
            }

            // ---- Charges surfaciques : q (poids propre) et s (neige) × surface affectée ----
            //string sn = ""; // pour vérification
            foreach (var (noeud, surface) in SurfacesAffectees(structureId, elements, nodes))
            {
                bilan.NoeudsAvecSurface++;
                bilan.SurfaceTotale += surface;

                if (poidsPropre)
                {
                    Ajouter(noeud, -(q + q1) * surface);
                    bilan.ChargeQ += q * surface;
                    bilan.ChargeQ1 += q1 * surface;
                }
                if (neige)
                {
                    Ajouter(noeud, -s * surface);
                    bilan.ChargeNeige += s * surface;
                }
                // Pour vérification des surfaces
                //string nomNoeud = nodes.Where(n => n.ElementId == noeud).FirstOrDefault().Name;
                //sn += nomNoeud + ";" + surface.ToString("N2")+"\n";
            }

            return bilan;
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
