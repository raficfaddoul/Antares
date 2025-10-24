using System;
using System.Collections.Generic;
using System.Linq;

namespace AliantWebS.Models
{
    public class VehicleCategory
    {
        public string PropertyName { get; set; }
        public string Label { get; set; }
        public string Color { get; set; }
        public bool IsSelected { get; set; }
    }

    public class ComptageData
    {
        public string Date { get; set; }
        public int ST3T { get; set; }      // <3.5T
        public int ST3T12T { get; set; }   // 3.5T<P<12T
        public int T12TP40T { get; set; }  // 12T<P<40T
        public int GT40T { get; set; }     // >40T
        public int Motos { get; set; }
        public int Velo { get; set; }
        public int Pietons { get; set; }
        public int Autres { get; set; }
    }

    /// <summary>
    /// Modèle de données détaillé pour le comptage de véhicules avec informations individuelles
    /// </summary>
    public class ComptageDataDetailed
    {
        /// <summary>
        /// Horodatage complet de la détection
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Sens de circulation (1 ou 2)
        /// </summary>
        public int Sens { get; set; }

        /// <summary>
        /// Numéro de voie
        /// </summary>
        public int Voie { get; set; }

        /// <summary>
        /// Vitesse du véhicule en km/h
        /// </summary>
        public double Vitesse { get; set; }

        /// <summary>
        /// Poids total du véhicule en tonnes (calculé dynamiquement à partir des essieux)
        /// </summary>
        public double PoidsTotal
        {
            get
            {
                double total = PoidsEssieu1 + PoidsEssieu2;
                if (PoidsEssieu3.HasValue) total += PoidsEssieu3.Value;
                if (PoidsEssieu4.HasValue) total += PoidsEssieu4.Value;
                if (PoidsEssieu5.HasValue) total += PoidsEssieu5.Value;
                if (PoidsEssieu6.HasValue) total += PoidsEssieu6.Value;
                return total;
            }
        }

        /// <summary>
        /// Poids de l'essieu 1 en tonnes
        /// </summary>
        public double PoidsEssieu1 { get; set; }

        /// <summary>
        /// Poids de l'essieu 2 en tonnes
        /// </summary>
        public double PoidsEssieu2 { get; set; }

        /// <summary>
        /// Poids de l'essieu 3 en tonnes (optionnel)
        /// </summary>
        public double? PoidsEssieu3 { get; set; }

        /// <summary>
        /// Poids de l'essieu 4 en tonnes (optionnel)
        /// </summary>
        public double? PoidsEssieu4 { get; set; }

        /// <summary>
        /// Poids de l'essieu 5 en tonnes (optionnel)
        /// </summary>
        public double? PoidsEssieu5 { get; set; }

        /// <summary>
        /// Poids de l'essieu 6 en tonnes (optionnel)
        /// </summary>
        public double? PoidsEssieu6 { get; set; }

        /// <summary>
        /// Distance entre l'essieu 1 et l'essieu 2 en mètres
        /// </summary>
        public double DistanceEssieu1Vers2 { get; set; }

        /// <summary>
        /// Distance entre l'essieu 2 et l'essieu 3 en mètres (optionnel)
        /// </summary>
        public double? DistanceEssieu2Vers3 { get; set; }

        /// <summary>
        /// Distance entre l'essieu 3 et l'essieu 4 en mètres (optionnel)
        /// </summary>
        public double? DistanceEssieu3Vers4 { get; set; }

        /// <summary>
        /// Distance entre l'essieu 4 et l'essieu 5 en mètres (optionnel)
        /// </summary>
        public double? DistanceEssieu4Vers5 { get; set; }

        /// <summary>
        /// Distance entre l'essieu 5 et l'essieu 6 en mètres (optionnel)
        /// </summary>
        public double? DistanceEssieu5Vers6 { get; set; }

        /// <summary>
        /// Longueur du véhicule en mètres
        /// </summary>
        public double? Longueur { get; set; }

        /// <summary>
        /// Largeur du véhicule en mètres
        /// </summary>
        public double? Largeur { get; set; }

        /// <summary>
        /// Hauteur du véhicule en mètres
        /// </summary>
        public double? Hauteur { get; set; }

        /// <summary>
        /// Catégorie du véhicule déterminée automatiquement
        /// </summary>
        public string CategorieVehicule { get; set; } = string.Empty;

        /// <summary>
        /// Nombre d'essieux détectés
        /// </summary>
        public int NombreEssieux { get; set; }

        /// <summary>
        /// Température ambiante au moment de la mesure (optionnel)
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// Identifiant unique de la détection
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Source de la mesure (capteur, station, etc.)
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Indicateur de validité de la mesure
        /// </summary>
        public bool EstValide { get; set; } = true;

        /// <summary>
        /// Commentaires ou notes sur la détection
        /// </summary>
        public string? Commentaires { get; set; }

        /// <summary>
        /// Méthode pour obtenir toutes les distances inter-essieux en mètres
        /// </summary>
        /// <returns>Liste des distances entre essieux consécutifs</returns>
        public List<double> GetDistancesInterEssieux()
        {
            var distances = new List<double>();

            // Distance obligatoire entre essieux 1 et 2
            distances.Add(DistanceEssieu1Vers2);

            // Distances optionnelles selon le nombre d'essieux
            if (DistanceEssieu2Vers3.HasValue)
                distances.Add(DistanceEssieu2Vers3.Value);
            if (DistanceEssieu3Vers4.HasValue)
                distances.Add(DistanceEssieu3Vers4.Value);
            if (DistanceEssieu4Vers5.HasValue)
                distances.Add(DistanceEssieu4Vers5.Value);
            if (DistanceEssieu5Vers6.HasValue)
                distances.Add(DistanceEssieu5Vers6.Value);

            return distances;
        }

        /// <summary>
        /// Méthode pour calculer la longueur totale du véhicule basée sur les distances inter-essieux
        /// </summary>
        /// <returns>Longueur calculée en mètres</returns>
        public double CalculerLongueurParEssieux()
        {
            var distances = GetDistancesInterEssieux();
            return distances.Sum();
        }

        /// <summary>
        /// Méthode pour obtenir la catégorie de poids du véhicule selon les seuils standards
        /// </summary>
        /// <returns>Catégorie de poids</returns>
        public string GetCategoriePoidsStandard()
        {
            return PoidsTotal switch
            {
                < 3.5 => "ST3T",      // <3.5T
                >= 3.5 and < 12 => "ST3T12T",   // 3.5T<P<12T  
                >= 12 and < 40 => "T12TP40T",   // 12T<P<40T
                >= 40 => "GT40T",     // >40T
                _ => "Inconnu"
            };
        }

        /// <summary>
        /// Méthode pour vérifier si le véhicule est en surcharge
        /// </summary>
        /// <param name="limitePoidsAutorisee">Limite de poids autorisée en tonnes</param>
        /// <returns>True si en surcharge</returns>
        public bool EstEnSurcharge(double limitePoidsAutorisee = 40.0)
        {
            return PoidsTotal > limitePoidsAutorisee;
        }

        /// <summary>
        /// Méthode pour obtenir la répartition des poids par essieu en pourcentage
        /// </summary>
        /// <returns>Liste des pourcentages de poids par essieu</returns>
        public List<double> GetRepartitionPoids()
        {
            var repartition = new List<double>();
            
            if (PoidsTotal <= 0) return repartition;

            repartition.Add(Math.Round((PoidsEssieu1 / PoidsTotal) * 100, 2));
            repartition.Add(Math.Round((PoidsEssieu2 / PoidsTotal) * 100, 2));
            
            if (PoidsEssieu3.HasValue)
                repartition.Add(Math.Round((PoidsEssieu3.Value / PoidsTotal) * 100, 2));
            if (PoidsEssieu4.HasValue)
                repartition.Add(Math.Round((PoidsEssieu4.Value / PoidsTotal) * 100, 2));
            if (PoidsEssieu5.HasValue)
                repartition.Add(Math.Round((PoidsEssieu5.Value / PoidsTotal) * 100, 2));
            if (PoidsEssieu6.HasValue)
                repartition.Add(Math.Round((PoidsEssieu6.Value / PoidsTotal) * 100, 2));

            return repartition;
        }

        /// <summary>
        /// Méthode pour analyser la configuration des essieux du véhicule
        /// </summary>
        /// <returns>Informations sur la configuration du véhicule</returns>
        public VehicleConfiguration AnalyserConfiguration()
        {
            var distances = GetDistancesInterEssieux();
            var configuration = new VehicleConfiguration
            {
                NombreEssieux = NombreEssieux,
                DistancesTotale = distances.Sum(),
                DistanceMinimale = distances.Any() ? distances.Min() : 0,
                DistanceMaximale = distances.Any() ? distances.Max() : 0,
                DistanceMoyenne = distances.Any() ? distances.Average() : 0,
                TypeVehicule = DeterminerTypeVehicule(distances)
            };

            return configuration;
        }

        /// <summary>
        /// Méthode privée pour déterminer le type de véhicule basé sur la configuration des essieux
        /// </summary>
        private string DeterminerTypeVehicule(List<double> distances)
        {
            if (NombreEssieux <= 2)
                return "Véhicule léger";
            
            if (NombreEssieux == 3)
                return "Camion";
            
            if (NombreEssieux >= 4 && distances.Any(d => d > 8.0))
                return "Semi-remorque";
            
            if (NombreEssieux >= 5)
                return "Train routier";
            
            return "Poids lourd";
        }

        /// <summary>
        /// Méthode pour valider la cohérence des données incluant les distances
        /// </summary>
        /// <returns>True si les données sont cohérentes</returns>
        public bool ValiderCoherence()
        {
            // Vérifier que la somme des poids des essieux correspond au poids total (±5% de tolérance)
            var sommePoids = PoidsEssieu1 + PoidsEssieu2 + 
                           (PoidsEssieu3 ?? 0) + (PoidsEssieu4 ?? 0) + 
                           (PoidsEssieu5 ?? 0) + (PoidsEssieu6 ?? 0);
            
            var tolerance = PoidsTotal * 0.05; // 5% de tolérance
            
            // Vérifier les distances inter-essieux (doivent être positives et réalistes)
            var distancesValides = DistanceEssieu1Vers2 > 0 && DistanceEssieu1Vers2 < 30 &&
                                  (!DistanceEssieu2Vers3.HasValue || (DistanceEssieu2Vers3.Value > 0 && DistanceEssieu2Vers3.Value < 30)) &&
                                  (!DistanceEssieu3Vers4.HasValue || (DistanceEssieu3Vers4.Value > 0 && DistanceEssieu3Vers4.Value < 30)) &&
                                  (!DistanceEssieu4Vers5.HasValue || (DistanceEssieu4Vers5.Value > 0 && DistanceEssieu4Vers5.Value < 30)) &&
                                  (!DistanceEssieu5Vers6.HasValue || (DistanceEssieu5Vers6.Value > 0 && DistanceEssieu5Vers6.Value < 30));
            
            return Math.Abs(sommePoids - PoidsTotal) <= tolerance &&
                   Vitesse >= 0 && Vitesse <= 200 && // Vitesse réaliste
                   Sens >= 1 && Sens <= 2 && // Sens valide
                   Voie >= 1 && // Voie valide
                   PoidsTotal >= 0 && // Poids positif
                   distancesValides; // Distances valides
        }
    }

    /// <summary>
    /// Modèle pour l'analyse de la configuration des véhicules
    /// </summary>
    public class VehicleConfiguration
    {
        public int NombreEssieux { get; set; }
        public double DistancesTotale { get; set; }
        public double DistanceMinimale { get; set; }
        public double DistanceMaximale { get; set; }
        public double DistanceMoyenne { get; set; }
        public string TypeVehicule { get; set; } = string.Empty;
    }

    /// <summary>
    /// Modèle pour les statistiques agrégées par période
    /// </summary>
    public class ComptageStatistics
    {
        public DateTime Periode { get; set; }
        public int TotalVehicules { get; set; }
        public double VitesseMoyenne { get; set; }
        public double PoidsMoyen { get; set; }
        public double PoidsMaximum { get; set; }
        public int VehiculesSens1 { get; set; }
        public int VehiculesSens2 { get; set; }
        public int VehiculesEnSurcharge { get; set; }
        public Dictionary<string, int> RepartitionParCategorie { get; set; } = new();
        public double DistanceMoyenneInterEssieux { get; set; }
        public Dictionary<string, int> RepartitionParTypeVehicule { get; set; } = new();
    }
}