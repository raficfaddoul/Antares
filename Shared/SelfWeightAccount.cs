// =============================================================
// SelfWeightAccount.cs — What a load transmission run actually managed to transmit, and what it did not.
//
// Adding the trusses' own mass makes an unreported failure dangerous in a new way. Before, a truss
// whose mass was unknown only cost a sentence in the study. Now it costs load: every attachment
// point it feeds reads LOWER than the truth, which is the one direction a structural study must
// never be wrong in. So every path that drops load — an element with no supports, a charge whose
// palonnier crosses nothing, an intersection that finds no charpente point, a truss the catalogue
// does not price — is named here instead of vanishing into a console line.
// =============================================================

using System.Collections.Generic;

namespace Helpers
{
    /// <summary>Mass of one truss as the catalogue knows it. A null <see cref="Kg"/> means unknown, never zero.</summary>
    public readonly record struct TrussMass(double? Kg, string Label);

    /// <summary>Bookkeeping for one <c>CalculateCompleteLoadTransmissionWithDetails</c> run.</summary>
    public sealed class SelfWeightAccount
    {
        /// <summary>False when no mass source was supplied, so self weight was not attempted at all.</summary>
        public bool Configured { get; set; }

        /// <summary>Scenic load that reached a mothergrid, kg.</summary>
        public double ScenicTransmittedKg { get; set; }

        /// <summary>Truss mass that reached a support, kg. Below the real total when references are missing.</summary>
        public double SelfWeightTransmittedKg { get; set; }

        /// <summary>References the catalogue does not price, so their mass was left out entirely.</summary>
        public List<string> UnknownMass { get; } = new();

        /// <summary>Elements resting on nothing: their mass could not be transmitted anywhere.</summary>
        public List<string> NoSupport { get; } = new();

        /// <summary>Elements with a single support, which takes the whole mass. Force balances, moment does not.</summary>
        public List<string> SingleSupport { get; } = new();

        /// <summary>Elements too short to carry a distributed mass, or with unusable coordinates.</summary>
        public List<string> Degenerate { get; } = new();

        /// <summary>Scenic loads whose palonnier crosses no mothergrid. These are lost today, silently.</summary>
        public List<string> DroppedCharges { get; } = new();

        /// <summary>Loads on a mothergrid that found no charpente point to land on.</summary>
        public List<string> UnsupportedIntersections { get; } = new();

        /// <summary>Charpente keys the persistence map has no column for, so the load was discarded.</summary>
        public List<string> UnmappedPoints { get; } = new();

        /// <summary>
        /// Palonniers whose recorded hanging decisions could not be read, so every one of their
        /// crossings took the default - hung - whatever the optimiser or a person had decided.
        /// </summary>
        public List<string> UnreadableHookRecords { get; } = new();

        /// <summary>
        /// Palonnier ends hung past the end of the mothergrid recorded for them, on its line - how the
        /// optimiser places an end beyond the outer columns. That mothergrid then carries a cantilever.
        /// </summary>
        public List<string> HangingPointsBeyondMother { get; } = new();

        /// <summary>
        /// Mothergrids with no attachment point under one of their ends. Drawn off the grid, or - when
        /// every mothergrid is here - a wrong Point0 on the event.
        /// </summary>
        public List<string> MotherEndsOffGrid { get; } = new();

        public bool HasReserves =>
            UnknownMass.Count > 0 || NoSupport.Count > 0 || SingleSupport.Count > 0 ||
            Degenerate.Count > 0 || DroppedCharges.Count > 0 ||
            UnsupportedIntersections.Count > 0 || UnmappedPoints.Count > 0 ||
            UnreadableHookRecords.Count > 0 ||
            HangingPointsBeyondMother.Count > 0 || MotherEndsOffGrid.Count > 0;

        /// <summary>One line per kind of reserve, for an operator who has just pressed calculate.</summary>
        /// <param name="includeUnknownMass">False for the report, which has a gap of its own for it.</param>
        public IEnumerable<string> Describe(bool includeUnknownMass = true)
        {
            if (includeUnknownMass && UnknownMass.Count > 0)
                yield return $"Masse inconnue, poids propre non transmis pour {UnknownMass.Count} élément(s) : "
                           + Join(UnknownMass) + $". Complétez {MassColumn}.";

            if (NoSupport.Count > 0)
                yield return $"{NoSupport.Count} élément(s) ne reposent sur aucun appui, leur poids propre "
                           + "n'a pas pu être transmis : " + Join(NoSupport) + ".";

            if (SingleSupport.Count > 0)
                yield return $"{SingleSupport.Count} élément(s) n'ont qu'un seul appui, qui reçoit la totalité "
                           + "de leurs charges et de leur poids propre : " + Join(SingleSupport) + ".";

            if (Degenerate.Count > 0)
                yield return $"Géométrie inexploitable pour {Degenerate.Count} élément(s) : " + Join(Degenerate) + ".";

            if (DroppedCharges.Count > 0)
                yield return $"{DroppedCharges.Count} charge(s) scénique(s) reposent sur un palonnier qui n'est "
                           + "accroché à aucun mother : elles ne sont transmises nulle part : " + Join(DroppedCharges) + ".";

            if (UnsupportedIntersections.Count > 0)
                yield return $"{UnsupportedIntersections.Count} charge(s) sur mother n'ont trouvé aucun point de "
                           + "charpente : " + Join(UnsupportedIntersections) + ". Vérifiez le point 0 de l'événement.";

            if (UnmappedPoints.Count > 0)
                yield return $"Points de charpente sans colonne en base, charge perdue : " + Join(UnmappedPoints) + ".";

            if (UnreadableHookRecords.Count > 0)
                yield return $"Les accrochages enregistrés de {UnreadableHookRecords.Count} palonnier(s) sont "
                           + "illisibles : ils ont été considérés comme accrochés à tous les mothers qu'ils croisent : "
                           + Join(UnreadableHookRecords) + ".";

            if (HangingPointsBeyondMother.Count > 0)
                yield return $"{HangingPointsBeyondMother.Count} extrémité(s) de palonnier sont accrochées "
                           + "au-delà de l'extrémité de leur mother, qui porte donc en porte-à-faux : "
                           + Join(HangingPointsBeyondMother) + ".";

            if (MotherEndsOffGrid.Count > 0)
                yield return $"{MotherEndsOffGrid.Count} mother(s) n'ont pas de point d'accroche sous l'une de "
                           + "leurs extrémités (écart indiqué) : " + Join(MotherEndsOffGrid)
                           + ". Si tous les mothers sont concernés, vérifiez le point 0 de l'événement.";
        }

        // Named here rather than referenced, so this file stays free of the reporting namespace.
        private const string MassColumn = "la colonne « Poids propre » de MotherGrill.xlsx ou de Palonnier.xlsx";

        private static string Join(List<string> items) =>
            items.Count <= 8
                ? string.Join(", ", items)
                : string.Join(", ", items.GetRange(0, 8)) + $" (+{items.Count - 8})";
    }
}
