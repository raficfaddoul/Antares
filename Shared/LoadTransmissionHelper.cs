using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Helpers
{
    /// <summary>
    /// Helper class pour calculer la transmission des charges des palonniers aux mothergrids,
    /// puis des mothergrids vers les points de la charpente (J9, I9, etc.)
    /// </summary>
    public class LoadTransmissionHelper
    {
        /// <summary>
        /// Action pour afficher des alertes - peut être configurée par le code appelant
        ///  CHANGÉ: Func<string, Task> au lieu de Action<string> pour supporter async
        /// </summary>
        public Func<string, Task> AlertAction { get; set; }

        // Ou en paramètre de classe
        public double CharpenteAlignmentTolerance { get; set; } = 0.5; // metres (was 500 mm)

        // Event origin (Point0) in absolute charpente metres. Palonnier/mothergrid/charge
        // coordinates are stored relative to this; it is added before matching against the
        // (absolute) CharpenteCoordinates. Set from Layout.selectedEvent — EVENT-DEPENDENT.
        public double Point0X { get; set; } = 0;
        public double Point0Y { get; set; } = 0;

        /// <summary>
        /// Mass of one truss, resolved by the caller, which owns the catalogue and the file paths.
        /// LEAVING THIS NULL MEANS "do not transmit self weight at all" - it never means zero. A
        /// truss whose mass is merely unknown must be named in <see cref="Account"/>, because a
        /// missing mass now makes every attachment point it feeds read LOWER than the truth.
        /// </summary>
        public Func<MotherGridPalonnier, TrussMass>? TrussMassOf { get; set; }

        /// <summary>What the last run transmitted, and everything it could not. Reset on each run.</summary>
        public SelfWeightAccount Account { get; private set; } = new();

        /// <summary>
        /// What each mothergrid carried in the last run, point by point: the hanging points of the
        /// palonniers on it, each with its load - their share of the scenic loads and of their own
        /// weight, negative where a palonnier's cantilever lifts it - and any charge placed on the
        /// mothergrid itself. The mothergrid's own mass is not in it: that is not hung on it.
        /// </summary>
        public Dictionary<Guid, List<(double X, double Y, double Load)>> PointLoadsOnMothers { get; private set; } = new();

        // A mothergrid's charpente supports, resolved once per mothergrid per run, so its loads and
        // its own weight land on the same points and any shortfall is reported once.
        private readonly Dictionary<Guid, List<(string Name, double S)>> _supports = new();

        private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>
        /// Trouve les mothergrids qui supportent le palonnier, en tenant compte de la position de la charge
        /// Retourne les intersections de part et d'autre de la charge, ou les plus proches si en porte-à-faux
        ///  SIMPLIFICATION: Utiliser directement chargeMP.X au lieu de recalculer les positions
        /// </summary>
        private List<(MotherGridPalonnier MotherGrid, double IntersectionX, double IntersectionY, double S, bool PorteAFaux, double Charge)>
            FindSupportingMotherGrids(ChargeMp chargeMP, MotherGridPalonnier palonnier, List<MotherGridPalonnier> motherGrids)
        {
            //  SIMPLIFICATION: Utiliser directement la distance le long du palonnier
            double chargeDistanceAlongPalonnier = chargeMP.X ?? 0;
            double chargeValue = chargeMP.Charge ?? 0;

            // A palonnier hangs from every mothergrid it crosses, save where the optimiser or a person
            // recorded otherwise (PalonnierHanging). Resolved once per palonnier and shared with the
            // self-weight pass, so its loads and its own weight rest on exactly the same supports. Each
            // load goes to the two that bracket it, by the lever rule; the porte-à-faux branch handles
            // a load hung beyond the outermost one.
            var crossings = HangingPointsOf(palonnier, motherGrids);

            var intersections = crossings
                .Select(c => (c.MotherGrid, c.X, c.Y,
                              DistanceAlongPalonnier: c.S,
                              //  Distance relative à la charge (positif = après, négatif = avant)
                              RelativeToCharge: c.S - chargeDistanceAlongPalonnier))
                .ToList();

            if (intersections.Count == 0)
                return new List<(MotherGridPalonnier, double, double, double, bool, double)>();

            // Séparer les intersections avant et après la charge
            var beforeCharge = intersections.Where(i => i.RelativeToCharge < 0).OrderByDescending(i => i.DistanceAlongPalonnier).ToList();
            var afterCharge = intersections.Where(i => i.RelativeToCharge >= 0).OrderBy(i => i.DistanceAlongPalonnier).ToList();

            var result = new List<(MotherGridPalonnier, double, double, double, bool, double)>();

            // Cas 1: Il y a des intersections de part et d'autre (CAS NORMAL)
            if (beforeCharge.Any() && afterCharge.Any())
            {
                var closest1 = beforeCharge.First(); // Plus proche AVANT la charge
                var closest2 = afterCharge.First();  // Plus proche APRÈS la charge

                result.Add((closest1.MotherGrid, closest1.X, closest1.Y, closest1.DistanceAlongPalonnier, false, chargeValue));
                result.Add((closest2.MotherGrid, closest2.X, closest2.Y, closest2.DistanceAlongPalonnier, false, chargeValue));

                return result;
            }

            // Cas 2: PORTE-À-FAUX - toutes les intersections sont du même côté
            var allIntersections = beforeCharge.Any() ? beforeCharge : afterCharge;
            var twoClosest = allIntersections.OrderBy(i => Math.Abs(i.RelativeToCharge)).Take(2).ToList();

            foreach (var intersection in twoClosest)
            {
                result.Add((intersection.MotherGrid, intersection.X, intersection.Y, intersection.DistanceAlongPalonnier, true, chargeValue));
            }

            return result;
        }




        /// <summary>How far a palonnier and a mothergrid may miss each other and still meet: <see cref="PalonnierHanging.ToleranceM"/>.</summary>
        public const double HangingToleranceM = PalonnierHanging.ToleranceM;

        // Where each palonnier hangs, resolved once per palonnier per run. Shared by the scenic loads and
        // the palonnier's own weight so the two always rest on exactly the same supports, and so each
        // anomaly is reported once rather than once per charge.
        private readonly Dictionary<Guid, HangingResolution> _hanging = new();
        private readonly Dictionary<Guid, List<(MotherGridPalonnier MotherGrid, double X, double Y, double S)>> _hangingPoints = new();

        /// <summary>Where each palonnier with a load or a mass hung in the last run: every crossing, hooked or not.</summary>
        public IReadOnlyDictionary<Guid, HangingResolution> Hanging => _hanging;

        /// <summary>
        /// The mothergrids a palonnier hangs from, in order along it: every one it crosses, except where
        /// the optimiser or a person recorded otherwise. <see cref="PalonnierHanging.Resolve"/> is the one
        /// reading of that rule; the report and the plan page use it too.
        /// </summary>
        private List<(MotherGridPalonnier MotherGrid, double X, double Y, double S)> HangingPointsOf(
            MotherGridPalonnier palonnier, List<MotherGridPalonnier> motherGrids)
        {
            if (_hangingPoints.TryGetValue(palonnier.MotherGridPalonnierId, out var cached))
                return cached;

            var resolution = PalonnierHanging.Resolve(palonnier, motherGrids);
            string who = Describe(palonnier, "");

            if (!resolution.RecordsReadable) Account.UnreadableHookRecords.Add(who);

            // The optimiser's placement of an end beyond the outer columns: the mothergrid carries it in
            // cantilever, which no abaque on file covers.
            foreach (var crossing in resolution.Crossings.Where(c => c.BeyondMother))
            {
                Account.HangingPointsBeyondMother.Add(
                    $"{who} (extrémité {crossing.End}, {crossing.HungFrom!.PastEndM.ToString("0.00", Fr)} m "
                    + $"au-delà de {Describe(crossing.HungFrom.Mother, "")})");
            }

            var points = resolution.Supports
                .Select(h => (MotherGrid: h.Mother, h.X, h.Y, h.S))
                .ToList();

            int distinct = SelfWeightDistribution.DistinctSupports(points.Select(pt => pt.S).ToList());
            if (distinct == 0) Account.NoSupport.Add(who);
            else if (distinct == 1) Account.SingleSupport.Add(who);

            _hanging[palonnier.MotherGridPalonnierId] = resolution;
            _hangingPoints[palonnier.MotherGridPalonnierId] = points;
            return points;
        }

        /// <summary>
        /// The charpente points a mothergrid rests on, ordered along it, as distances from (X1, Y1).
        /// </summary>
        private List<(string Name, double S)> CharpenteSupportsAlong(MotherGridPalonnier motherGrid) =>
            CharpenteSupportsOf(motherGrid, Point0X, Point0Y, CharpenteAlignmentTolerance);

        /// <summary>
        /// The charpente points an element rests on - every point within <paramref name="tolerance"/> of its
        /// line, up to that far past its ends - ordered along it, as distances from (X1, Y1). One reading,
        /// shared by the load transmission and the study's bay-by-bay check of a mother line.
        /// </summary>
        public static List<(string Name, double S)> CharpenteSupportsOf(
            MotherGridPalonnier motherGrid, double point0X, double point0Y, double tolerance = 0.5)
        {
            var supports = new List<(string Name, double S)>();

            if (motherGrid.X1 == null || motherGrid.Y1 == null || motherGrid.X2 == null || motherGrid.Y2 == null)
                return supports;

            // Element coordinates are relative to the event's Point0; CharpenteCoordinates is
            // absolute. Same shift the per-intersection query makes.
            double ax = (double)motherGrid.X1 + point0X, ay = (double)motherGrid.Y1 + point0Y;
            double bx = (double)motherGrid.X2 + point0X, by = (double)motherGrid.Y2 + point0Y;

            double dx = bx - ax, dy = by - ay;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001) return supports;

            double dirX = dx / length, dirY = dy / length;

            foreach (var point in CharpenteCoordinates.Points)
            {
                double px = point.Value.X - ax, py = point.Value.Y - ay;

                double along = px * dirX + py * dirY;
                double offset = Math.Abs(px * dirY - py * dirX);

                if (offset > tolerance) continue;

                // Keep the PROJECTED position rather than clamping it into [0, length]: a support a
                // few millimetres past an endpoint is Point0 rounding, and clamping would quietly
                // turn a real overhang into a flush end. The band matches the perpendicular
                // tolerance, which is deliberately loose - a mistyped Point0 must not silently
                // reduce every mothergrid to a single support carrying all of its mass. It cannot
                // let a stranger in either: the next charpente point in line is at least 3,6 m away.
                if (along < -tolerance || along > length + tolerance)
                    continue;

                supports.Add((point.Key, along));
            }

            supports.Sort((a, b) => a.S.CompareTo(b.S));
            return supports;
        }

        /// <summary>
        /// Adds each palonnier's own mass to the mothergrids it hangs from, span by span. Called between
        /// the two existing stages, so the mass then travels to the charpente by exactly the path the
        /// scenic loads sitting on the same palonnier travel.
        /// </summary>
        private void AddPalonnierSelfWeights(
            List<MotherGridPalonnier> palonniers,
            List<MotherGridPalonnier> motherGrids,
            Dictionary<Guid, List<(double X, double Y, double Load)>> perIntersection)
        {
            foreach (var palonnier in palonniers)
            {
                var mass = TrussMassOf!(palonnier);
                string who = Describe(palonnier, mass.Label);

                if (mass.Kg is not { } kg || kg <= 0) { Account.UnknownMass.Add(who); continue; }

                double length = LengthOf(palonnier);
                if (length < 0.001) { Account.Degenerate.Add(who); continue; }

                // The same hanging points its loads use. A shortfall - no support, or only one - was
                // reported once, when they were resolved.
                var crossings = HangingPointsOf(palonnier, motherGrids);
                if (crossings.Count == 0) continue;

                var positions = crossings.Select(c => c.S).ToList();
                var reactions = SelfWeightDistribution.Reactions(positions, length, kg);

                for (int i = 0; i < crossings.Count; i++)
                {
                    AddOrAccumulateIntersectionLoad(
                        perIntersection,
                        crossings[i].MotherGrid.MotherGridPalonnierId,
                        crossings[i].X, crossings[i].Y,
                        reactions[i]);
                }

                Account.SelfWeightTransmittedKg += kg;
            }
        }

        /// <summary>
        /// Adds each mothergrid's own mass straight to the charpente points it rests on. A mothergrid
        /// sits on the roof, so unlike a palonnier's mass this does not pass through stage 2 - it is
        /// already a support reaction, not a load looking for one.
        /// </summary>
        private void AddMotherGridSelfWeights(
            List<MotherGridPalonnier> motherGrids,
            Dictionary<string, double> charpenteLoads)
        {
            foreach (var motherGrid in motherGrids)
            {
                var mass = TrussMassOf!(motherGrid);
                string who = Describe(motherGrid, mass.Label);

                if (mass.Kg is not { } kg || kg <= 0) { Account.UnknownMass.Add(who); continue; }

                double length = LengthOf(motherGrid);
                if (length < 0.001) { Account.Degenerate.Add(who); continue; }

                // The same supports its loads use. A shortfall was reported once, when they were resolved.
                var supports = SupportsOf(motherGrid);
                if (supports.Count == 0) continue;

                var positions = supports.Select(pt => pt.S).ToList();
                var reactions = SelfWeightDistribution.Reactions(positions, length, kg);

                for (int i = 0; i < supports.Count; i++)
                {
                    charpenteLoads[supports[i].Name] =
                        charpenteLoads.GetValueOrDefault(supports[i].Name) + reactions[i];
                }

                Account.SelfWeightTransmittedKg += kg;
            }
        }

        /// <summary>Length of the segment an element is drawn over, metres.</summary>
        private static double LengthOf(MotherGridPalonnier element)
        {
            double dx = (element.X2 ?? 0) - (element.X1 ?? 0);
            double dy = (element.Y2 ?? 0) - (element.Y1 ?? 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Names an element for a reserve line: its own name where it has one.</summary>
        private static string Describe(MotherGridPalonnier element, string label) =>
            string.IsNullOrWhiteSpace(element.Name)
                ? (string.IsNullOrWhiteSpace(label) ? "(élément sans nom)" : label)
                : element.Name!.Trim();

        /// <summary>
        /// Valide qu'une valeur float est acceptable pour la base de données SQL Server
        /// </summary>
        private double? ValidateFloat(double value)
        {
            if (double.IsNaN(value))
            {
                AlertAction?.Invoke($"⚠️ ERREUR: Valeur NaN détectée. Remplacée par null.");
                return null;
            }

            if (double.IsInfinity(value))
            {
                AlertAction?.Invoke($"⚠️ ERREUR: Valeur infinie détectée: {value}. Remplacée par null.");
                return null;
            }

            return value;
        }

        /// <summary>
        /// Division sécurisée qui évite les divisions par zéro et les résultats invalides
        /// </summary>
        private double SafeDivide(double numerator, double denominator, string context = "")
        {
            if (Math.Abs(denominator) < 1e-10)
            {
                AlertAction?.Invoke($"⚠️ ATTENTION: Division par zéro évitée dans {context}. Résultat: 0");
                return 0;
            }

            double result = numerator / denominator;

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                AlertAction?.Invoke($"⚠️ ERREUR: Résultat de division invalide dans {context} ({numerator}/{denominator}). Résultat: 0");
                return 0;
            }

            return result;
        }

        /// <summary>
        /// Applique les charges calculées à l'objet Charpente
        /// </summary>
        public void ApplyLoadsToCharpente(Charpente charpente, Dictionary<string, double> charpenteLoads)
        {
            //  MAPPING COMPLET basé sur les propriétés RÉELLES du modèle Charpente
            var propertyMap = new Dictionary<string, Action<double>>
            {
                // Ligne 10
                { "A10", val => charpente.A10 = ValidateFloat(val) }, { "B10", val => charpente.B10 = ValidateFloat(val) },
                { "C10", val => charpente.C10 = ValidateFloat(val) }, { "D10", val => charpente.D10 = ValidateFloat(val) },
                { "E10", val => charpente.E10 = ValidateFloat(val) }, { "F10", val => charpente.F10 = ValidateFloat(val) },
                { "G10", val => charpente.G10 = ValidateFloat(val) }, { "H10", val => charpente.H10 = ValidateFloat(val) },
                { "I10", val => charpente.I10 = ValidateFloat(val) },
                { "J10", val => charpente.J10 = ValidateFloat(val) },

                // Ligne 9
                { "A9", val => charpente.A9 = ValidateFloat(val) }, { "B9", val => charpente.B9 = ValidateFloat(val) },
                { "C9", val => charpente.C9 = ValidateFloat(val) }, { "D9", val => charpente.D9 = ValidateFloat(val) },
                { "E9", val => charpente.E9 = ValidateFloat(val) }, { "F9", val => charpente.F9 = ValidateFloat(val) },
                { "G9", val => charpente.G9 = ValidateFloat(val) }, { "H9", val => charpente.H9 = ValidateFloat(val) },
                { "I9", val => charpente.I9 = ValidateFloat(val) },
                { "J9", val => charpente.J9 = ValidateFloat(val) },

                // Ligne 8
                { "A8", val => charpente.A8 = ValidateFloat(val) }, { "B8", val => charpente.B8 = ValidateFloat(val) },
                { "C8", val => charpente.C8 = ValidateFloat(val) }, { "D8", val => charpente.D8 = ValidateFloat(val) },
                { "E8", val => charpente.E8 = ValidateFloat(val) }, { "F8", val => charpente.F8 = ValidateFloat(val) },
                { "G8", val => charpente.G8 = ValidateFloat(val) }, { "H8", val => charpente.H8 = ValidateFloat(val) },
                { "I8", val => charpente.I8 = ValidateFloat(val) }, { "J8", val => charpente.J8 = ValidateFloat(val) },

                // Ligne 7
                { "A7", val => charpente.A7 = ValidateFloat(val) }, { "B7", val => charpente.B7 = ValidateFloat(val) },
                { "C7", val => charpente.C7 = ValidateFloat(val) }, { "D7", val => charpente.D7 = ValidateFloat(val) },
                { "E7", val => charpente.E7 = ValidateFloat(val) }, { "F7", val => charpente.F7 = ValidateFloat(val) },
                { "G7", val => charpente.G7 = ValidateFloat(val) }, { "H7", val => charpente.H7 = ValidateFloat(val) },
                { "I7", val => charpente.I7 = ValidateFloat(val) }, { "J7", val => charpente.J7 = ValidateFloat(val) },

                // Ligne 6
                { "A6", val => charpente.A6 = ValidateFloat(val) }, { "B6", val => charpente.B6 = ValidateFloat(val) },
                { "C6", val => charpente.C6 = ValidateFloat(val) }, { "D6", val => charpente.D6 = ValidateFloat(val) },
                { "E6", val => charpente.E6 = ValidateFloat(val) }, { "F6", val => charpente.F6 = ValidateFloat(val) },
                { "G6", val => charpente.G6 = ValidateFloat(val) }, { "H6", val => charpente.H6 = ValidateFloat(val) },
                { "I6", val => charpente.I6 = ValidateFloat(val) }, { "J6", val => charpente.J6 = ValidateFloat(val) },

                // Ligne 5
                { "A5", val => charpente.A5 = ValidateFloat(val) }, { "B5", val => charpente.B5 = ValidateFloat(val) },
                { "C5", val => charpente.C5 = ValidateFloat(val) }, { "D5", val => charpente.D5 = ValidateFloat(val) },
                { "E5", val => charpente.E5 = ValidateFloat(val) }, { "F5", val => charpente.F5 = ValidateFloat(val) },
                { "G5", val => charpente.G5 = ValidateFloat(val) }, { "H5", val => charpente.H5 = ValidateFloat(val) },
                { "I5", val => charpente.I5 = ValidateFloat(val) }, { "J5", val => charpente.J5 = ValidateFloat(val) },

                // Ligne 4
                { "A4", val => charpente.A4 = ValidateFloat(val) }, { "B4", val => charpente.B4 = ValidateFloat(val) },
                { "C4", val => charpente.C4 = ValidateFloat(val) }, { "D4", val => charpente.D4 = ValidateFloat(val) },
                { "E4", val => charpente.E4 = ValidateFloat(val) }, { "F4", val => charpente.F4 = ValidateFloat(val) },
                { "G4", val => charpente.G4 = ValidateFloat(val) }, { "H4", val => charpente.H4 = ValidateFloat(val) },
                { "I4", val => charpente.I4 = ValidateFloat(val) }, { "J4", val => charpente.J4 = ValidateFloat(val) },

                // Ligne 3
                { "A3", val => charpente.A3 = ValidateFloat(val) }, { "B3", val => charpente.B3 = ValidateFloat(val) },
                { "C3", val => charpente.C3 = ValidateFloat(val) }, { "D3", val => charpente.D3 = ValidateFloat(val) }, // ⚠️ Le fameux D3 qui causait l'erreur
                { "E3", val => charpente.E3 = ValidateFloat(val) }, { "F3", val => charpente.F3 = ValidateFloat(val) },
                { "G3", val => charpente.G3 = ValidateFloat(val) }, { "H3", val => charpente.H3 = ValidateFloat(val) },
                { "I3", val => charpente.I3 = ValidateFloat(val) }, { "J3", val => charpente.J3 = ValidateFloat(val) },

                // Ligne 2
                { "A2", val => charpente.A2 = ValidateFloat(val) }, { "B2", val => charpente.B2 = ValidateFloat(val) },
                { "C2", val => charpente.C2 = ValidateFloat(val) }, { "D2", val => charpente.D2 = ValidateFloat(val) },
                { "E2", val => charpente.E2 = ValidateFloat(val) }, { "F2", val => charpente.F2 = ValidateFloat(val) },
                { "G2", val => charpente.G2 = ValidateFloat(val) }, { "H2", val => charpente.H2 = ValidateFloat(val) },
                { "I2", val => charpente.I2 = ValidateFloat(val) }, { "J2", val => charpente.J2 = ValidateFloat(val) },

                // Ligne 1
                { "A1", val => charpente.A1 = ValidateFloat(val) }, { "B1", val => charpente.B1 = ValidateFloat(val) },
                { "C1", val => charpente.C1 = ValidateFloat(val) }, { "D1", val => charpente.D1 = ValidateFloat(val) },
                { "E1", val => charpente.E1 = ValidateFloat(val) }, { "F1", val => charpente.F1 = ValidateFloat(val) },
                { "G1", val => charpente.G1 = ValidateFloat(val) }, { "H1", val => charpente.H1 = ValidateFloat(val) },
                { "I1", val => charpente.I1 = ValidateFloat(val) }, { "J1", val => charpente.J1 = ValidateFloat(val) }
            };

            // Clear EVERY node first, so loads from a previous calc don't linger on nodes
            // that receive no charge this time (each CalculCharpente is a full recompute).
            foreach (var setter in propertyMap.Values)
                setter(0);

            foreach (var load in charpenteLoads)
            {
                if (propertyMap.ContainsKey(load.Key))
                {
                    AlertAction?.Invoke($"📝 Appliquant charge {load.Value:F6} au point {load.Key}");
                    propertyMap[load.Key](load.Value);
                }
                else
                {
                    // The load is discarded here, so this has to be nameable rather than a console
                    // line: I9 sat in this branch until it was added to the map above, and every
                    // load routed to it was lost without trace.
                    Account.UnmappedPoints.Add(load.Key);
                    AlertAction?.Invoke($"⚠️ ATTENTION: Point de charpente '{load.Key}' non trouvé dans le mapping des propriétés");
                }
            }
        }

        /// <summary>A mothergrid's charpente supports, once per run. Shortfalls are reported here, once.</summary>
        private List<(string Name, double S)> SupportsOf(MotherGridPalonnier motherGrid)
        {
            if (_supports.TryGetValue(motherGrid.MotherGridPalonnierId, out var cached))
                return cached;

            var supports = CharpenteSupportsAlong(motherGrid);
            string who = Describe(motherGrid, "");

            if (supports.Count == 0)
            {
                Account.NoSupport.Add(who);
            }
            else
            {
                if (SelfWeightDistribution.DistinctSupports(supports.Select(pt => pt.S).ToList()) == 1)
                    Account.SingleSupport.Add(who);

                // A mothergrid rests on an attachment point at each end: the optimiser draws it from one
                // column to the next. An end with no point under it is a mothergrid drawn off the grid
                // - or, when every mothergrid is affected, a wrong Point0 on the event, which would
                // otherwise send loads to whichever charpente points happen to line up.
                double length = LengthOf(motherGrid);
                double off = Math.Max(
                    supports.Min(pt => Math.Abs(pt.S)),
                    supports.Min(pt => Math.Abs(pt.S - length)));
                if (off > HangingToleranceM)
                    Account.MotherEndsOffGrid.Add($"{who} ({off.ToString("0.00", Fr)} m)");
            }

            _supports[motherGrid.MotherGridPalonnierId] = supports;
            return supports;
        }

        /// <summary>Distance of a point along a mothergrid from (X1, Y1); both in the element frame.</summary>
        private static double AlongMother(MotherGridPalonnier motherGrid, double x, double y)
        {
            double x1 = motherGrid.X1 ?? 0, y1 = motherGrid.Y1 ?? 0;
            double dx = (motherGrid.X2 ?? 0) - x1, dy = (motherGrid.Y2 ?? 0) - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-9 ? 0 : ((x - x1) * dx + (y - y1) * dy) / len;
        }

        /// <summary>
        /// How a point load at <paramref name="s"/> along a mothergrid divides between the points it
        /// rests on. Shares sum to one. Within 10 mm of a point it all goes there; between two, by the
        /// lever rule; past the outermost, by the same affine rule on the two nearest - which is the
        /// porte-à-faux formula: more than all of it on the near point, uplift on the far one.
        /// </summary>
        private static List<(string Name, double Share)> PointLoadReactions(List<(string Name, double S)> supports, double s)
        {
            const double OnPointM = 0.010;   // the 10 mm this chain has always used for "on a point"

            var byDistance = supports.OrderBy(pt => Math.Abs(pt.S - s)).ToList();
            if (byDistance.Count == 1 || Math.Abs(byDistance[0].S - s) < OnPointM)
                return new List<(string, double)> { (byDistance[0].Name, 1.0) };

            var left = supports.Where(pt => pt.S <= s).OrderByDescending(pt => pt.S).FirstOrDefault();
            var right = supports.Where(pt => pt.S > s).OrderBy(pt => pt.S).FirstOrDefault();

            var (a, b) = left.Name is not null && right.Name is not null
                ? (left, right)
                : (byDistance[0], byDistance[1]);

            double span = b.S - a.S;
            if (Math.Abs(span) < 1e-9)
                return new List<(string, double)> { (a.Name, 0.5), (b.Name, 0.5) };

            return new List<(string, double)> { (a.Name, (b.S - s) / span), (b.Name, (s - a.S) / span) };
        }

        /// <summary>The point <paramref name="s"/> metres along an element from (X1, Y1); null for unusable geometry.</summary>
        private static (double X, double Y)? PointOnElement(MotherGridPalonnier element, double s)
        {
            if (element.X1 == null || element.Y1 == null || element.X2 == null || element.Y2 == null) return null;
            double dx = (double)element.X2 - (double)element.X1, dy = (double)element.Y2 - (double)element.Y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) return null;
            return ((double)element.X1 + s * dx / len, (double)element.Y1 + s * dy / len);
        }

        /// <summary>
        /// Version avec diagnostics détaillés - utilise le nouveau pipeline per-intersection
        /// </summary>
        public (Dictionary<Guid, (double Load, List<(double X, double Y)> IntersectionPoints)> MotherGridLoads,
                Dictionary<string, double> CharpenteLoads)
            CalculateCompleteLoadTransmissionWithDetails(
                List<ChargeMp> chargesMPs,
                List<MotherGridPalonnier> allMotherGridsAndPalonniers)
        {
            var palonniers = allMotherGridsAndPalonniers.Where(mg => !mg.Famille).ToList();
            var motherGrids = allMotherGridsAndPalonniers.Where(mg => mg.Famille).ToList();

            // A fresh account per run: this method is called repeatedly on the same instance. The
            // hanging points go with it, since their anomalies are recorded in the account.
            Account = new SelfWeightAccount { Configured = TrussMassOf is not null };
            _hanging.Clear();
            _hangingPoints.Clear();
            _supports.Clear();

            // Utiliser le nouveau pipeline per-intersection (source unique de vérité)
            var perIntersection = CalculateAllChargesToMotherGridsLoadsPerIntersection(chargesMPs, palonniers, motherGrids);

            // The trusses carry themselves as well as the production. A palonnier's mass joins the
            // scenic loads on the mothergrids BEFORE stage 2, so it reaches the charpente by the
            // same path they do; a mothergrid rests on the roof directly, so its mass is added
            // after stage 2. Together these make the attachment points total the scenic loads plus
            // the pre rigg, which is the identity the hand-written studies reconcile to.
            if (TrussMassOf is not null)
                AddPalonnierSelfWeights(palonniers, motherGrids, perIntersection);

            var charpenteLoads = CalculateMotherGridsToCharpenteFromPerIntersection(perIntersection, motherGrids);

            if (TrussMassOf is not null)
                AddMotherGridSelfWeights(motherGrids, charpenteLoads);

            // Convertir au format legacy pour la compatibilité des diagnostics
            var motherGridLoadsWithIntersections = new Dictionary<Guid, (double Load, List<(double X, double Y)> IntersectionPoints)>();
            foreach (var kvp in perIntersection)
            {
                double totalLoad = kvp.Value.Sum(p => p.Load);
                var points = kvp.Value.Select(p => (p.X, p.Y)).Distinct().ToList();
                motherGridLoadsWithIntersections[kvp.Key] = (totalLoad, points);
            }

            PointLoadsOnMothers = perIntersection;
            return (motherGridLoadsWithIntersections, charpenteLoads);
        }


        // Helper to accumulate loads per (X,Y) on a given mothergrid
        private static void AddOrAccumulateIntersectionLoad(
            Dictionary<Guid, List<(double X, double Y, double Load)>> dict,
            Guid motherGridId, double x, double y, double load, double mergeTolerance = 1e-3)
        {
            if (Math.Abs(load) < 1e-9) return; // do not add zero-noise

            if (!dict.TryGetValue(motherGridId, out var list))
            {
                list = new List<(double X, double Y, double Load)>();
                dict[motherGridId] = list;
            }

            // Merge with existing point if same coordinates (within tol)
            for (int i = 0; i < list.Count; i++)
            {
                var (px, py, pl) = list[i];
                if (Math.Abs(px - x) <= mergeTolerance && Math.Abs(py - y) <= mergeTolerance)
                {
                    list[i] = (px, py, pl + load);
                    return;
                }
            }

            list.Add((x, y, load));
        }

        /// <summary>
        /// Nouveau pipeline (étape 1): calcule, pour chaque mothergrid, la liste des intersections
        /// avec leur charge déjà répartie (porte-à-faux/normal) depuis le palonnier.
        /// Aucune division par le nombre d'intersections ne sera nécessaire ensuite.
        /// </summary>
        public Dictionary<Guid, List<(double X, double Y, double Load)>>
            CalculateAllChargesToMotherGridsLoadsPerIntersection(
                List<ChargeMp> chargesMPs,
                List<MotherGridPalonnier> palonniers,
                List<MotherGridPalonnier> motherGrids)
        {
            var perIntersection = new Dictionary<Guid, List<(double X, double Y, double Load)>>();

            // Public entry point in its own right: never reuse hanging points from another run.
            _hanging.Clear();
            _hangingPoints.Clear();

            foreach (var chargeMP in chargesMPs)
            {
                var palonnier = chargeMP.MotherGridPalonnier;
                if (palonnier == null) continue;

                double chargeValue = chargeMP.Charge ?? 0;

                // A charge placed on a mothergrid itself - the UI allows it - hangs on that mothergrid
                // at its position along it, with no palonnier in between. It used to be treated as a
                // palonnier looking for mothergrids to hang from, found none, and was dropped.
                if (palonnier.Famille)
                {
                    if (PointOnElement(palonnier, chargeMP.X ?? 0) is { } at)
                    {
                        AddOrAccumulateIntersectionLoad(perIntersection, palonnier.MotherGridPalonnierId, at.X, at.Y, chargeValue);
                        Account.ScenicTransmittedKg += chargeValue;
                    }
                    else
                    {
                        Account.DroppedCharges.Add(
                            string.IsNullOrWhiteSpace(chargeMP.Name) ? "(charge sans nom)" : chargeMP.Name!.Trim());
                    }
                    continue;
                }

                var supportingGrids = FindSupportingMotherGrids(chargeMP,palonnier, motherGrids);
                if (supportingGrids.Count == 0)
                {
                    // A palonnier crossing no mothergrid takes its loads with it. Silent until now.
                    Account.DroppedCharges.Add(
                        string.IsNullOrWhiteSpace(chargeMP.Name) ? "(charge sans nom)" : chargeMP.Name!.Trim());
                    continue;
                }

                if (supportingGrids.Count == 1)
                {
                    var sg = supportingGrids[0];
                    var mgId = sg.MotherGrid.MotherGridPalonnierId;
                    AddOrAccumulateIntersectionLoad(perIntersection, mgId, sg.IntersectionX, sg.IntersectionY, chargeValue);
                    Account.ScenicTransmittedKg += chargeValue;
                    continue;
                }

                double chargeDistanceAlongPalonnier = chargeMP.X ?? 0; // Position de la charge le long du palonnier

                // Where the palonnier hangs, as distances along it: the S each hanging point was found at,
                // which is what the optimiser sized the mothers with. Not worked out again from the hanging
                // point, which is moved onto the mother's drawn segment: for a biais palonnier hung a few
                // centimetres past a mother's end, re-projecting it shifted the lever arms and moved load
                // from one mother to the other.
                double s1 = supportingGrids[0].S;
                double s2 = supportingGrids[1].S;

                double loadTo1, loadTo2;

                if (supportingGrids[0].PorteAFaux)
                {
                    //  PAS BESOIN DE SWAP ! Les valeurs absolues gèrent tout automatiquement

                    double cantilever = Math.Abs(chargeDistanceAlongPalonnier - s1);
                    double span = Math.Abs(s2 - s1);

                    //  ATTENTION: Il faut identifier QUEL mothergrid est le plus proche pour attribuer correctement !
                    // On garde la logique, mais on l'utilise APRÈS le calcul des charges

                    double loadClosest, loadFarthest;

                    if (span < 1e-9)
                    {
                        // Two mothergrids crossing the palonnier at the same point: no lever between
                        // them, so they share. Without this the two SafeDivide calls below both
                        // return 0 and the ENTIRE charge disappears. Stage 2 already halves in the
                        // same situation, and one file must not hold two conventions, one of which
                        // destroys load. It is reachable: a palonnier whose x falls on a column line
                        // crosses the mothergrids of both adjacent bays at the same coordinate.
                        loadClosest = chargeValue / 2.0;
                        loadFarthest = chargeValue / 2.0;
                    }
                    else
                    {
                        loadClosest = chargeValue * SafeDivide(span + cantilever, span, "cantilever grid closest");
                        loadFarthest = -chargeValue * SafeDivide(cantilever, span, "cantilever grid farthest");
                    }

                    // Déterminer lequel est le plus proche
                    double dist1 = Math.Abs(chargeDistanceAlongPalonnier - s1);
                    double dist2 = Math.Abs(chargeDistanceAlongPalonnier - s2);

                    if (dist1 < dist2)
                    {
                        // s1 est plus proche
                        loadTo1 = loadClosest;
                        loadTo2 = loadFarthest;
                    }
                    else
                    {
                        // s2 est plus proche
                        loadTo1 = loadFarthest;
                        loadTo2 = loadClosest;
                    }
                }
                else
                {
                    //  CAS NORMAL: Répartition inversement proportionnelle
                    double d1 = Math.Abs(chargeDistanceAlongPalonnier - s1);
                    double d2 = Math.Abs(chargeDistanceAlongPalonnier - s2);
                    double total = d1 + d2;

                    loadTo1 = chargeValue * SafeDivide(d2, total, "normal load distribution");
                    loadTo2 = chargeValue * SafeDivide(d1, total, "normal load distribution");

                }

                // Accumuler
                var sg1 = supportingGrids[0];
                var sg2 = supportingGrids[1];
                AddOrAccumulateIntersectionLoad(perIntersection, sg1.MotherGrid.MotherGridPalonnierId, sg1.IntersectionX, sg1.IntersectionY, loadTo1);
                AddOrAccumulateIntersectionLoad(perIntersection, sg2.MotherGrid.MotherGridPalonnierId, sg2.IntersectionX, sg2.IntersectionY, loadTo2);

                // The pair always sums to the charge, cantilever and uplift included.
                Account.ScenicTransmittedKg += chargeValue;
            }

            return perIntersection;
        }

        /// <summary>
        /// Stage 2: each load on a mothergrid goes to the charpente points that mothergrid rests on -
        /// the two bracketing it, by the lever rule, or, past the outermost, the two nearest, by the
        /// porte-à-faux rule. It used to take the two nearest charpente points on the row instead,
        /// whether or not the mothergrid rests on them: past a mothergrid's end that sent load to the
        /// next column, and with a wrong Point0 it moved every load onto other beams without a word.
        /// For a load between a one-bay mothergrid's two points - everything the optimiser produces -
        /// the two rules give the same answer.
        /// </summary>
        public Dictionary<string, double> CalculateMotherGridsToCharpenteFromPerIntersection(
            Dictionary<Guid, List<(double X, double Y, double Load)>> motherGridIntersectionLoads,
            List<MotherGridPalonnier> motherGrids)
        {
            var charpenteLoads = new Dictionary<string, double>();

            // Public entry point in its own right: never reuse supports from another run.
            _supports.Clear();

            foreach (var kvp in motherGridIntersectionLoads)
            {
                var motherGrid = motherGrids.FirstOrDefault(mg => mg.MotherGridPalonnierId == kvp.Key);

                foreach (var (ix, iy, iload) in kvp.Value)
                {
                    if (Math.Abs(iload) < 1e-9) continue;

                    // Printed in the studies' axes, as every coordinate a person reads (StudyAxes), in fr-FR.
                    var (sx, sy) = StudyAxes.ToStudy(ix, iy);
                    string at = $"({sx.ToString("0.00", Fr)} ; {sy.ToString("0.00", Fr)})";
                    string where = motherGrid is null || string.IsNullOrWhiteSpace(motherGrid.Name)
                        ? at
                        : $"{motherGrid.Name!.Trim()} {at}";

                    var supports = motherGrid is null
                        ? new List<(string Name, double S)>()
                        : SupportsOf(motherGrid);

                    if (supports.Count == 0)
                    {
                        // Nothing to land on. The usual cause is a wrong Point0 on the event, which
                        // puts every element off the charpente grid.
                        Account.UnsupportedIntersections.Add(where);
                        continue;
                    }

                    double along = AlongMother(motherGrid!, ix, iy);
                    foreach (var (name, share) in PointLoadReactions(supports, along))
                        charpenteLoads[name] = charpenteLoads.GetValueOrDefault(name) + iload * share;
                }
            }

            return charpenteLoads;
        }
    }
}
