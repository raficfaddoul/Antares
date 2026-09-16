// =============================================================
// ReportModel.cs — Plain data the Word report is rendered from.
//
// Deliberately free of Entity Framework and of OpenXml: ReportDataBuilder fills these types from the
// database and the scenario's spreadsheets, ReportFigureDrawer draws from them, and WordReportBuilder
// only formats them. That split is what lets the document be checked without a database.
//
// UNITS. Loads are carried in kilograms, exactly as they are stored. The report prints them as daN,
// which is what the existing Rig'Up documents do: rigging practice here treats 1 kg as 1 daN. The
// conversion is therefore deliberately the identity, and is applied nowhere.
//
// AXES. Every coordinate here is in the studies' axes (Helpers.StudyAxes): X along the beams, towards
// point 9, Y across them, towards beam J, in metres from the event's Point0 (E5). The database and the
// calculations keep the grid's axes; ReportDataBuilder and ReportGenerator convert on the way in.
// =============================================================

using RiggingOptimizer.IO;

namespace AliantWebS.Reporting;

/// <summary>Who the study is for and who issued it.</summary>
public sealed class ReportContext
{
    public string ShowName { get; set; } = "";
    public string VenueName { get; set; } = "Antarès";
    public string VenueCity { get; set; } = "Le Mans";

    public DateTime ShowDate { get; set; }
    public DateTime IssueDate { get; set; }

    public string Author { get; set; } = "";
    public string Revision { get; set; } = "V0";
    public string RevisionNote { get; set; } = "Version initiale";

    public CompanyInfo Company { get; set; } = CompanyInfo.RigUp;

    /// <summary>"Antarès – Le Mans", as it appears on the cover and in the page header.</summary>
    public string VenueLine => $"{VenueName} – {VenueCity}";

    /// <summary>
    /// The city in its mid-sentence form: "au Mans", not "au Le Mans". French place names carry their
    /// article, and contracting it is the difference between correct prose and an obvious template.
    /// </summary>
    public string VenueCityLocative => Locative(VenueCity);

    private static string Locative(string city)
    {
        var c = (city ?? "").Trim();
        if (c.Length == 0) return "";

        if (c.StartsWith("Les ", StringComparison.OrdinalIgnoreCase)) return "aux " + c.Substring(4);
        if (c.StartsWith("Le ", StringComparison.OrdinalIgnoreCase)) return "au " + c.Substring(3);
        if (c.StartsWith("La ", StringComparison.OrdinalIgnoreCase)) return "à la " + c.Substring(3);
        if (c.StartsWith("L’", StringComparison.OrdinalIgnoreCase)) return "à l’" + c.Substring(2);

        return "à " + c;
    }
}

/// <summary>The issuing company's identity block, cover footer and page disclaimer.</summary>
public sealed class CompanyInfo
{
    public string Name { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string ContactLine { get; set; } = "";
    public string RegistrationLine { get; set; } = "";
    public string TaxLine { get; set; } = "";
    public string DisclaimerEnglish { get; set; } = "";
    public string DisclaimerFrench { get; set; } = "";

    /// <summary>
    /// Which embedded mark to place on the cover and in the header: "rigup" or "cideco".
    /// </summary>
    public string LogoKey { get; set; } = "rigup";

    /// <summary>
    /// Rig'Up, the issuer of the existing studies. Transcribed from their cover block and footer, so
    /// a generated report is indistinguishable from a hand-written one. This is the default.
    /// Every field is overridable from the "Report:Company" configuration section.
    /// </summary>
    public static CompanyInfo RigUp => new()
    {
        Name = "RIG’UP SARL",
        AddressLine = "4, impasse de la Martinière – ZAC Les Hauts de Couëron 3 - 44220 COUERON",
        ContactLine = "TEL : 02 51 81 02 89   FAX : 09 70 63 14 12",
        RegistrationLine = "SIRET 478 762 503 00033 – RCS NANTES",
        TaxLine = "TVA INTR. FR82 478 762 503 – APE 9002Z – LABEL 459",
        // Reproduced verbatim from the source documents, including the "est la propriété" agreement
        // slip, so the legal wording is not quietly reworded by this generator.
        DisclaimerEnglish =
            "This document and the information contained therein are the exclusive property of RIG’UP®, "
            + "shall not be used, reproduced, copied, for supplying information to others except by prior "
            + "written consent of RIG’UP®.",
        DisclaimerFrench =
            "Ce document et les informations qu’il contient est la propriété de RIG’UP®, il ne peut être "
            + "utilisé, copié ou diffusé pour l’utilisation d’un tiers sans autorisation écrite auprès de RIG’UP®.",
        LogoKey = "rigup",
    };

    /// <summary>
    /// CIDECO, kept available so the issuer can be switched from configuration without a code change.
    /// </summary>
    public static CompanyInfo Cideco => new()
    {
        Name = "CIDECO",
        AddressLine = "2 avenue Blaise Pascal – 63170 AUBIÈRE",
        ContactLine = "contact@cideco.tech – cideco.tech",
        RegistrationLine = "SIRET 827 456 195 00029 – RCS CLERMONT-FERRAND B 827 456 195",
        TaxLine = "TVA INTR. FR81 827 456 195 – APE 7120B",
        DisclaimerEnglish =
            "This document and the information contained therein are the exclusive property of CIDECO, "
            + "shall not be used, reproduced, copied, for supplying information to others except by prior "
            + "written consent of CIDECO.",
        DisclaimerFrench =
            "Ce document et les informations qu’il contient sont la propriété de CIDECO, il ne peut être "
            + "utilisé, copié ou diffusé pour l’utilisation d’un tiers sans autorisation écrite auprès de CIDECO.",
        LogoKey = "cideco",
    };
}

/// <summary>One rigging load from the production's plan.</summary>
/// <param name="X">Along the beams, towards point 9, metres from Point0: the studies' axes.</param>
/// <param name="Y">Across the beams, towards beam J, metres from Point0: the studies' axes.</param>
/// <param name="Located">
/// False when the plan gives the load no position. Its X and Y are then meaningless, and the figures
/// leave it out rather than draw it at the origin, where it would read as a load hung on E5.
/// </param>
/// <param name="Weighed">
/// False when the plan gives the load no weight. Its WeightKg is then 0, which is not a weight: Tableau 1
/// prints an em dash for it, as it does for a position it does not have, rather than a 0 that would read
/// as a charge weighed at nothing, and the study counts it among the checks it could not carry out.
/// </param>
public sealed record ProductionLoad(
    string Name, double X, double Y, double WeightKg, bool Located = true, bool Weighed = true);

/// <summary>
/// The venue's load book: what each attachment point and each beam is allowed to carry. These are
/// venue constants, not production data, and come from the shared TCharpente limit table.
/// </summary>
public sealed class VenueSpec
{
    /// <summary>Beam labels, in the order they are printed: J down to A.</summary>
    public IReadOnlyList<string> Beams { get; set; } = Array.Empty<string>();

    /// <summary>Attachment point numbers along each beam, 1 to 9.</summary>
    public IReadOnlyList<int> Points { get; set; } = Array.Empty<int>();

    /// <summary>Allowed load at one point, keyed "G5". Missing means the load book has no entry.</summary>
    public IReadOnlyDictionary<string, double> PointCapacity { get; set; } =
        new Dictionary<string, double>();

    /// <summary>
    /// Allowed load for a whole beam. This is NOT the sum of its points: the beam is the weaker
    /// constraint, and a plan can respect every point yet overload the beam.
    /// </summary>
    public IReadOnlyDictionary<string, double> BeamCapacity { get; set; } =
        new Dictionary<string, double>();

    public double? CapacityAt(string beam, int point) =>
        PointCapacity.TryGetValue($"{beam}{point}", out var v) ? v : null;

    public double? CapacityOf(string beam) =>
        BeamCapacity.TryGetValue(beam, out var v) ? v : null;
}

/// <summary>One row of the final results table: a beam's allowed loads against the production's.</summary>
public sealed class BeamLoadRow
{
    public string Beam { get; set; } = "";

    /// <summary>Allowed load per point, in point order. Null where the load book has no entry.</summary>
    public IReadOnlyList<double?> Allowed { get; set; } = Array.Empty<double?>();

    /// <summary>Load the production puts on each point, in point order.</summary>
    public IReadOnlyList<double> Applied { get; set; } = Array.Empty<double>();

    public double? AllowedTotal { get; set; }
    public double AppliedTotal { get; set; }

    /// <summary>Indexes of points where the applied load exceeds what is allowed.</summary>
    public IReadOnlyList<int> OverloadedPoints { get; set; } = Array.Empty<int>();

    public bool BeamOverloaded => AllowedTotal is { } cap && AppliedTotal > cap;

    public bool HasProblem => BeamOverloaded || OverloadedPoints.Count > 0;
}

/// <summary>
/// One truss piece - a model at one length - and how many of it the pre-rig uses. A truss assembled
/// from several pieces (5,6 m + 2,8 m) counts once on the line of each of its pieces: that is what the
/// crew takes off the truck and what the inventory stocks and weighs.
/// </summary>
public sealed class TrussLine
{
    /// <summary>Printed label, e.g. "STACCO Acier C500 lg:5,6m".</summary>
    public string Label { get; set; } = "";

    public string Family { get; set; } = "";
    public double LengthM { get; set; }

    /// <summary>How many are taken ("PRIS").</summary>
    public int Count { get; set; }

    /// <summary>Mass of one truss ("POIDS"), kg. Null when the catalogue has no entry.</summary>
    public double? UnitWeightKg { get; set; }

    /// <summary>Row shading, six hex digits, as used in the existing reports.</summary>
    public string? ColorHex { get; set; }

    /// <summary>
    /// Every (ElementType, piece length in cm) this line stands for. Normally one, but a mother and a
    /// palonnier can be the same truss out of the same stock — the catalogue then gives both the
    /// same label and they are printed as a single line, as the existing studies do. A drawing maps
    /// an element back to its line through the key of its first piece.
    /// </summary>
    public List<(string Type, long Cm)> Keys { get; set; } = new();

    public double? TotalWeightKg => UnitWeightKg is { } w ? w * Count : null;
}

/// <summary>A truss assembled from several pieces of its model, as the study lists it under Tableau 2.</summary>
/// <param name="Element">The element's name.</param>
/// <param name="Family">Its model.</param>
/// <param name="Pieces">Its pieces as printed: "5,6 + 2,8 m".</param>
public sealed record AssembledTruss(string Element, string Family, string Pieces);

/// <summary>
/// A truss whose recorded pieces do not describe the truss drawn: they fall short of the length it is drawn
/// over, or they cannot be read at all. Either way it is weighed as something that is not there, and every
/// attachment point it feeds is wrong, so the study cannot conclude until its pieces are corrected.
/// </summary>
/// <param name="Element">The element's name.</param>
/// <param name="Family">Its model.</param>
/// <param name="Pieces">What it is counted as, printed: "4 + 4 m".</param>
/// <param name="PiecesM">Those pieces added up, metres.</param>
/// <param name="DrawnM">The length it is drawn over, metres.</param>
/// <param name="Stored">When the recorded pieces are unreadable, what is stored, as it is; null otherwise.</param>
public sealed record TrussPieceProblem(
    string Element, string Family, string Pieces, double PiecesM, double DrawnM, string? Stored)
{
    /// <summary>The recorded pieces cannot be read; it is counted as one piece of the length in its name.</summary>
    public bool Unreadable => Stored is not null;
}

/// <summary>The pre-rig's bill of materials, piece by piece.</summary>
public sealed class TrussInventory
{
    public IReadOnlyList<TrussLine> Lines { get; set; } = Array.Empty<TrussLine>();

    /// <summary>The elements made of more than one piece, and their pieces.</summary>
    public IReadOnlyList<AssembledTruss> Assembled { get; set; } = Array.Empty<AssembledTruss>();

    /// <summary>
    /// The trusses whose recorded pieces are unreadable, or fall short of the length they are drawn over by
    /// more than the optimiser's tolerance. Each is a verification gap.
    /// </summary>
    public IReadOnlyList<TrussPieceProblem> PieceProblems { get; set; } = Array.Empty<TrussPieceProblem>();

    /// <summary>Sum of count times unit mass over every line that has a catalogued mass.</summary>
    public double TotalMassKg => Lines.Sum(l => l.TotalWeightKg ?? 0);

    /// <summary>True when at least one line had no catalogued mass, so the total is an underestimate.</summary>
    public bool MassIncomplete => Lines.Any(l => l.Count > 0 && l.UnitWeightKg is null);

    /// <summary>
    /// The truss mass the load transmission is given (ReportDataBuilder.MassSource): every element whose
    /// pieces all have a mass, added up. It equals <see cref="TotalMassKg"/> when no mass is missing. When
    /// one is, an element with one unknown piece transmits nothing at all while its known pieces still
    /// count in Tableau 2 - so the stored attachment-point loads are reconciled against this figure,
    /// which counts the same kilograms they do.
    /// </summary>
    public double TransmittedMassKg
    {
        get => _transmittedMassKg ?? TotalMassKg;
        set => _transmittedMassKg = value;
    }

    private double? _transmittedMassKg;
}

/// <summary>Whether a load sits inside the abaque, either centred on a span or out on a cantilever.</summary>
public enum AbaqueLoadCase
{
    Centered,
    Cantilever,

    /// <summary>A mothergrid pulled up by a palonnier's cantilever. No abaque on file covers it.</summary>
    Uplift,
}

/// <summary>
/// Which kind of truss a check is about. The printed families overlap - a mother and a palonnier can
/// both be "C500 ALU" - so the role travels with the check instead of being guessed from the family.
/// </summary>
public enum AbaqueElementRole
{
    Palonnier,
    Mother,
}

/// <summary>How the studies name each role.</summary>
public static class AbaqueRoles
{
    /// <summary>"palonnier" or "mother", as in "sur un palonnier C500 ALU".</summary>
    public static string Noun(this AbaqueElementRole role) =>
        role == AbaqueElementRole.Mother ? "mother" : "palonnier";

    /// <summary>"Palonnier" or "Mother", at the start of a line.</summary>
    public static string Capitalised(this AbaqueElementRole role) =>
        role == AbaqueElementRole.Mother ? "Mother" : "Palonnier";
}

/// <summary>One "the heaviest load on this truss family is X, the abaque allows Y" check.</summary>
public sealed class AbaqueCheck
{
    /// <summary>Heading the check is printed under, e.g. "C500 ACIER".</summary>
    public string Family { get; set; } = "";

    /// <summary>Palonnier or mother. Defaults to palonnier, the only role checked before mothers were.</summary>
    public AbaqueElementRole Role { get; set; } = AbaqueElementRole.Palonnier;

    /// <summary>
    /// The case its family is reported on: the element closest to its limit. The other checks of a
    /// family are further elements over their limit, or beyond their abaque.
    /// </summary>
    public bool IsGoverning { get; set; } = true;

    /// <summary>
    /// Whether it is also the heaviest load of its family. The studies call the governing case "la
    /// charge la plus importante"; that is only true when it is.
    /// </summary>
    public bool IsHeaviest { get; set; } = true;

    public AbaqueLoadCase Case { get; set; }

    /// <summary>Name of the worst-loaded truss, for traceability.</summary>
    public string? WorstElement { get; set; }

    public double AppliedKg { get; set; }
    public double SpanM { get; set; }

    /// <summary>
    /// Where on the truss: the bay between two neighbouring supports - "G3–F3" for a mother, the point
    /// lines "lignes 3–5" for a palonnier - or, for a cantilever, the support it reaches past ("A3",
    /// "ligne 9"). Null where the supports could not be named.
    /// </summary>
    public string? SpanLabel { get; set; }

    /// <summary>
    /// The allowed load: what the loads on the span may add up to (RiggingOptimizer.IO.LoadTable). Null
    /// when there is no value.
    /// </summary>
    public double? AllowedKg { get; set; }

    /// <summary>The length the value applies at: the inventory row read, or for a computed value the span itself.</summary>
    public double? AllowedSpanM { get; set; }

    /// <summary>Where the value comes from: a row of the inventory, computed from its longest row, or none.</summary>
    public AllowedSource Source { get; set; } = AllowedSource.None;

    /// <summary>
    /// The inventory row the value rests on - the row read, or the model's longest row a computed value
    /// starts from: its length and its charge ponctuelle au milieu.
    /// </summary>
    public double? BaseLengthM { get; set; }
    public double? BaseKg { get; set; }

    /// <summary>The own weight per metre a computed value uses, from the longest row's Poids propre.</summary>
    public double? KgPerM { get; set; }

    /// <summary>Why there is no value, in French, as LoadTable gives it. Null when there is one.</summary>
    public string? NoValueReason { get; set; }

    /// <summary>
    /// No value although the model is in the inventory: the span is too long for even a computed value,
    /// or the weight a computed value needs is missing. Distinct from a model the inventory lacks.
    /// </summary>
    public bool BeyondAbaque { get; set; }

    /// <summary>
    /// The rows of this model, in this role's workbook, that could not be read. The value above was read
    /// without them - from the next longer row, or computed past what was only the longest READABLE row - so
    /// it may not be the one the inventory gives. Empty when every row of the model was read.
    /// </summary>
    public IReadOnlyList<SkippedRow> SkippedRows { get; set; } = Array.Empty<SkippedRow>();

    /// <summary>A row of this model could not be read: the value is the readable rows' only, and the study says so.</summary>
    public bool InventoryIncomplete => SkippedRows.Count > 0;

    /// <summary>The value was computed at equal moment, not read: the study says so wherever it prints it.</summary>
    public bool IsComputed => Source == AllowedSource.Computed && AllowedKg is not null;

    public bool Ok => AllowedKg is { } a && AppliedKg <= a;
}

/// <summary>How the study names the inventory rows it could not read.</summary>
public static class UnreadInventoryRows
{
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>"la ligne 7 de Palonnier.xlsx", "les lignes 7 et 9 de Palonnier.xlsx".</summary>
    public static string Describe(IEnumerable<SkippedRow> rows)
    {
        var byFile = rows
            .GroupBy(r => r.File, StringComparer.OrdinalIgnoreCase)
            .Select(g => (File: g.Key, Lines: g.Select(r => r.Line).Distinct().OrderBy(l => l).ToList()))
            .ToList();
        int count = byFile.Sum(f => f.Lines.Count);

        return (count == 1 ? "la ligne " : "les lignes ")
             + string.Join(" et ", byFile.Select(f => $"{Enumerate(f.Lines)} de {f.File}"));
    }

    /// <summary>
    /// "la ligne 7 de Palonnier.xlsx, de ce modèle, n'a pas pu être lue", with a straight apostrophe like
    /// the reasons it is appended to; the Word study makes it typographic.
    /// </summary>
    public static string Clause(IReadOnlyCollection<SkippedRow> rows) =>
        Describe(rows) + ", de ce modèle, "
        + (rows.Select(r => (r.File, r.Line)).Distinct().Count() == 1 ? "n'a pas pu être lue" : "n'ont pas pu être lues");

    /// <summary>"7", "7 et 9", "7, 9 et 11".</summary>
    private static string Enumerate(IReadOnlyList<int> lines) =>
        lines.Count <= 1
            ? string.Join("", lines.Select(l => l.ToString(Fr)))
            : string.Join(", ", lines.Take(lines.Count - 1).Select(l => l.ToString(Fr))) + " et " + lines[^1].ToString(Fr);
}

/// <summary>A crossing where a palonnier is deliberately not hung from the mother it passes over.</summary>
/// <param name="Mother">The mother met there; several, joined, where mothers meet at one point.</param>
/// <param name="ByHand">Unhooked by a person, rather than by the optimiser.</param>
/// <param name="Reason">The optimiser's reason: the load a mother would otherwise have carried.</param>
public sealed record UnhookedCrossing(string Palonnier, string Mother, bool ByHand, string? Reason);

/// <summary>
/// What the study calls each of its three plan figures. One place, because the caption under the figure,
/// the name the drawing carries inside the document, and any reserve that has to name a figure all have
/// to say the same thing - and because a reserve that named a figure by its NUMBER would be wrong the
/// moment an earlier figure is not printed.
/// </summary>
public static class FigureCaptions
{
    public const string ProductionPlan = "Plan de son et lumière de la production";
    public const string PreRig = "Mother + pré rigg";
    public const string Implantation = "Implantation du plan de la production dans la salle";
}

/// <summary>Paths of the images placed in the document. A null path means the figure is skipped.</summary>
public sealed class FigureSet
{
    public string? VenuePhoto { get; set; }
    public string? PointNomenclature { get; set; }
    public string? ProductionPlan { get; set; }
    public string? PreRig { get; set; }
    public string? Implantation { get; set; }
}

/// <summary>Everything the Word writer needs.</summary>
public sealed class RigReport
{
    public ReportContext Context { get; set; } = new();
    public VenueSpec Venue { get; set; } = new();

    public IReadOnlyList<ProductionLoad> Loads { get; set; } = Array.Empty<ProductionLoad>();
    public TrussInventory Trusses { get; set; } = new();
    public IReadOnlyList<BeamLoadRow> BeamLoads { get; set; } = Array.Empty<BeamLoadRow>();
    public IReadOnlyList<AbaqueCheck> AbaqueChecks { get; set; } = Array.Empty<AbaqueCheck>();

    /// <summary>How many palonniers the plan has. The study says where they hang only when there are some.</summary>
    public int PalonnierCount { get; set; }

    /// <summary>Crossings where a palonnier is deliberately not hung from the mother it passes over.</summary>
    public IReadOnlyList<UnhookedCrossing> Unhooked { get; set; } = Array.Empty<UnhookedCrossing>();

    /// <summary>True when a charge hangs on a mother directly rather than through a palonnier.</summary>
    public bool MothersCarryDirectLoads { get; set; }

    public FigureSet Figures { get; set; } = new();

    /// <summary>
    /// Anything the reader must know about how the document was produced: a missing catalogue, an
    /// empty load book, a figure that could not be drawn. Surfaced in the UI and, when it affects the
    /// conclusion, in the document itself.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// False when the loads on the attachment points were never computed, because "Calcul Charpente"
    /// has not been run for this scenario. Distinct from every load being zero, and the difference
    /// decides whether this study is allowed to conclude anything at all.
    /// </summary>
    public bool AppliedLoadsKnown { get; set; } = true;

    /// <summary>
    /// Checks the study could not perform: a capacity that is not in the load book, an abaque that
    /// could not be read. An unperformed check is not a passed check, so each of these blocks the
    /// compliance verdict rather than being silently counted as a pass.
    /// </summary>
    public IReadOnlyList<string> VerificationGaps { get; set; } = Array.Empty<string>();

    public double TotalLoadKg => Loads.Sum(l => l.WeightKg);

    /// <summary>Beams or points carrying more than the load book allows.</summary>
    public IReadOnlyList<BeamLoadRow> Exceedances => BeamLoads.Where(b => b.HasProblem).ToList();

    /// <summary>Trusses loaded beyond their abaque.</summary>
    public IReadOnlyList<AbaqueCheck> AbaqueExceedances =>
        AbaqueChecks.Where(c => c.AllowedKg is not null && !c.Ok).ToList();

    /// <summary>
    /// True when the study had everything it needed to reach a verdict. False means the document must
    /// say so instead of pronouncing either way.
    /// </summary>
    public bool IsConclusive => AppliedLoadsKnown && VerificationGaps.Count == 0;

    /// <summary>At least one constraint is demonstrably breached.</summary>
    public bool HasExceedances => Exceedances.Count > 0 || AbaqueExceedances.Count > 0;

    /// <summary>
    /// True only when every constraint was actually checked AND every check passed. Requiring
    /// <see cref="IsConclusive"/> is the point: without it, a scenario whose loads were never computed
    /// produces empty exceedance lists and would be reported as compliant, which is the worst thing
    /// this document could do.
    /// </summary>
    public bool IsCompliant => IsConclusive && !HasExceedances;
}
