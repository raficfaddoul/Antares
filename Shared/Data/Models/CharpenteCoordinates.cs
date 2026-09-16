using System.Collections.Generic;
using System.Linq;

namespace AliantWebS.Models
{
    /// <summary>
    /// Classe statique contenant les coordonn�es de r�f�rence des points de la charpente
    /// </summary>
    public static class CharpenteCoordinates
    {
        /// <summary>
        /// Dictionnaire des coordonn�es (X, Y) de tous les points de la charpente
        /// Les cl�s sont au format "LetterNumber" (ex: "J9", "I8", etc.)
        /// </summary>
        // Raw surveyed coordinates in MILLIMETRES (as measured on site).
        private static readonly Dictionary<string, (double X, double Y)> _pointsMm = new()
        {
            // Ligne 1 - Y = 15150 (le plus haut)
            { "J1", (0, 15150) },
            { "I1", (3797, 15150) },
            { "H1", (10997, 15150) },
            { "G1", (18697, 15150) },
            { "F1", (25947, 15150) },
            { "E1", (33103, 15150) },
            { "D1", (40247, 15150) },
            { "C1", (47497, 15150) },
            { "B1", (55197, 15150) },
            { "A1", (62397, 15150) },

            // Ligne 2 - Y = 11050
            { "J2", (0, 11050) },
            { "I2", (3797, 11050) },
            { "H2", (10997, 11050) },
            { "G2", (18697, 11050) },
            { "F2", (25947, 11050) },
            { "E2", (33103, 11050) },
            { "D2", (40247, 11050) },
            { "C2", (47497, 11050) },
            { "B2", (55197, 11050) },
            { "A2", (62397, 11050) },

            // Ligne 3 - Y = 7400
            { "J3", (0, 7400) },
            { "I3", (3797, 7400) },
            { "H3", (10997, 7400) },
            { "G3", (18697, 7400) },
            { "F3", (25947, 7400) },
            { "E3", (33103, 7400) },
            { "D3", (40247, 7400) },
            { "C3", (47497, 7400) },
            { "B3", (55197, 7400) },
            { "A3", (62397, 7400) },

            // Ligne 4 - Y = 3700
            { "J4", (0, 3700) },
            { "I4", (3797, 3700) },
            { "H4", (10997, 3700) },
            { "G4", (18697, 3700) },
            { "F4", (25947, 3700) },
            { "E4", (33103, 3700) },
            { "D4", (40247, 3700) },
            { "C4", (47497, 3700) },
            { "B4", (55197, 3700) },
            { "A4", (62397, 3700) },

            // Ligne 5 - Y = 0
            { "J5", (0, 0) },
            { "I5", (3797, 0) },
            { "H5", (10997, 0) },
            { "G5", (18697, 0) },
            { "F5", (25947, 0) },
            { "E5", (33103, 0) },
            { "D5", (40247, 0) },
            { "C5", (47497, 0) },
            { "B5", (55197, 0) },
            { "A5", (62397, 0) },

            // Ligne 6 - Y = -3700
            { "J6", (0, -3700) },
            { "I6", (3797, -3700) },
            { "H6", (10997, -3700) },
            { "G6", (18697, -3700) },
            { "F6", (25947, -3700) },
            { "E6", (33103, -3700) },
            { "D6", (40247, -3700) },
            { "C6", (47497, -3700) },
            { "B6", (55197, -3700) },
            { "A6", (62397, -3700) },

            // Ligne 7 - Y = -7450
            { "J7", (0, -7450) },
            { "I7", (3797, -7450) },
            { "H7", (10997, -7450) },
            { "G7", (18697, -7450) },
            { "F7", (25947, -7450) },
            { "E7", (33103, -7450) },
            { "D7", (40247, -7450) },
            { "C7", (47497, -7450) },
            { "B7", (55197, -7450) },
            { "A7", (62397, -7450) },

            // Ligne 8 - Y = -11050
            { "J8", (0, -11050) },
            { "I8", (3797, -11050) },
            { "H8", (10997, -11050) },
            { "G8", (18697, -11050) },
            { "F8", (25947, -11050) },
            { "E8", (33103, -11050) },
            { "D8", (40247, -11050) },
            { "C8", (47497, -11050) },
            { "B8", (55197, -11050) },
            { "A8", (62397, -11050) },

            // Ligne 9 - Y = -15151 (le plus bas)
            { "J9", (0, -15151) },
            { "I9", (3797, -15151) },
            { "H9", (10997, -15151) },
            { "G9", (18697, -15151) },
            { "F9", (25947, -15151) },
            { "E9", (33103, -15151) },
            { "D9", (40247, -15151) },
            { "C9", (47497, -15151) },
            { "B9", (55197, -15151) },
            { "A9", (62397, -15151) },

            // Ligne 10 - Ajoutez si n�cessaire pour correspondre au mod�le Charpente
            // (si votre base de donn�es a des propri�t�s A10, B10, etc.)
        };

        /// <summary>
        /// Coordonnees (X, Y) des points de la charpente, en METRES — coherent avec
        /// les coordonnees des palonniers / mothergrids / charges (toutes en metres).
        /// </summary>
        public static readonly Dictionary<string, (double X, double Y)> Points =
            _pointsMm.ToDictionary(kv => kv.Key, kv => (kv.Value.X / 1000.0, kv.Value.Y / 1000.0));

        /// <summary>
        /// Obtient les coordonn�es d'un point de charpente par son identifiant
        /// </summary>
        /// <param name="pointId">Identifiant du point (ex: "J9", "I8")</param>
        /// <returns>Tuple contenant les coordonn�es (X, Y) ou null si le point n'existe pas</returns>
        public static (double X, double Y)? GetCoordinates(string pointId)
        {
            if (Points.TryGetValue(pointId, out var coordinates))
            {
                return coordinates;
            }
            return null;
        }

        /// <summary>
        /// V�rifie si un point de charpente existe
        /// </summary>
        /// <param name="pointId">Identifiant du point (ex: "J9", "I8")</param>
        /// <returns>True si le point existe, False sinon</returns>
        public static bool PointExists(string pointId)
        {
            return Points.ContainsKey(pointId);
        }

        /// <summary>
        /// Obtient tous les identifiants de points disponibles
        /// </summary>
        /// <returns>Collection des identifiants de points</returns>
        public static IEnumerable<string> GetAllPointIds()
        {
            return Points.Keys;
        }

        /// <summary>
        /// Obtient tous les points d'une ligne sp�cifique (ex: ligne "9", ligne "8")
        /// </summary>
        /// <param name="lineNumber">Num�ro de la ligne (1-9)</param>
        /// <returns>Dictionnaire des points de la ligne sp�cifi�e</returns>
        public static Dictionary<string, (double X, double Y)> GetPointsByLine(int lineNumber)
        {
            var linePoints = new Dictionary<string, (double X, double Y)>();
            
            foreach (var point in Points)
            {
                if (point.Key.EndsWith(lineNumber.ToString()))
                {
                    linePoints.Add(point.Key, point.Value);
                }
            }
            
            return linePoints;
        }

        /// <summary>
        /// Obtient tous les points d'une colonne sp�cifique (ex: colonne "A", colonne "J")
        /// </summary>
        /// <param name="column">Lettre de la colonne (A-J)</param>
        /// <returns>Dictionnaire des points de la colonne sp�cifi�e</returns>
        public static Dictionary<string, (double X, double Y)> GetPointsByColumn(char column)
        {
            var columnPoints = new Dictionary<string, (double X, double Y)>();
            
            foreach (var point in Points)
            {
                if (point.Key.StartsWith(column.ToString()))
                {
                    columnPoints.Add(point.Key, point.Value);
                }
            }
            
            return columnPoints;
        }
    }
}