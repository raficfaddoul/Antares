// =============================================================
// ReportGenerator.cs — Produces the finished Word study for one scenario.
//
// Ties the three halves together: ReportDataBuilder reads the data, ReportFigureDrawer lays out
// whatever the production did not supply as an image, WordReportBuilder writes the document.
// Registered in DI so a page only has to call GenerateAsync.
//
// EVERY DRAWN FIGURE IS PRODUCED TWICE, from one layout: as a scene, which the document holds as real
// shapes, and as a PNG in the scenario's output folder, which is a downloadable artefact and the plan
// the user opens. They cannot disagree - the same layout answers both - and neither is a copy of the
// other. The production's own plan is not laid out at all: it arrives as a file and stays an image.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AliantWebS.Reporting;

/// <summary>What a generation run produced, including anything the reader should be told.</summary>
public sealed record ReportResult(string FilePath, string FileName, IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
}

public sealed class ReportGenerator
{
    /// <summary>
    /// Base names accepted for the production's own plan drawing, uploaded to the scenario's data
    /// folder. The first one found is used as Figure 4.
    /// </summary>
    private static readonly string[] PlanFileNames =
    {
        "PlanProduction.png", "PlanProduction.jpg", "PlanProduction.jpeg",
        "plan.png", "plan.jpg", "plan.jpeg",
    };

    private readonly EntitiesService _entities;
    private readonly ScenarioStorageService _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(
        EntitiesService entities,
        ScenarioStorageService storage,
        IConfiguration configuration,
        ILogger<ReportGenerator> logger)
    {
        _entities = entities;
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ReportResult> GenerateAsync(
        Event ev,
        EventMpscenario scenario,
        string author,
        string revision = "V0",
        string revisionNote = "Version initiale",
        DateTime? issueDate = null)
    {
        var paths = _storage.GetScenarioPaths(ev, scenario);

        var context = new ReportContext
        {
            ShowName = string.IsNullOrWhiteSpace(ev.Name) ? "(sans nom)" : ev.Name!.Trim(),
            VenueName = _configuration["Report:VenueName"] ?? "Antarès",
            VenueCity = _configuration["Report:VenueCity"] ?? "Le Mans",
            ShowDate = ev.StartDate ?? DateTime.Today,
            IssueDate = issueDate ?? DateTime.Today,
            Author = string.IsNullOrWhiteSpace(author) ? "—" : author.Trim(),
            Revision = string.IsNullOrWhiteSpace(revision) ? "V0" : revision.Trim(),
            RevisionNote = revisionNote,
            Company = ReadCompany(),
        };

        var builder = new ReportDataBuilder(_entities, _storage);
        var report = await builder.BuildAsync(ev, scenario, context);

        // Geometry for the drawings only. Loaded here rather than held on the service, which is scoped
        // and would otherwise carry one scenario's trusses into the next request.
        var elements = await _entities.GetFilteredEntitiesAsync<MotherGridPalonnier>(
            m => m.EventMpscenarioId == scenario.EventMpscenarioId);
        var elementTypes = await _entities.GetAllEntitiesAsync<ElementType>();
        var typeNames = elementTypes.ToDictionary(t => t.ElementTypeId, t => t.Name ?? "");

        // The origin every stored coordinate is relative to, taken as the load transmission takes it, so
        // the figures lay the charpente grid under the trusses where the calculation found it.
        var point0 = (X: ev.Point0X ?? 0, Y: ev.Point0Y ?? 0);

        var warnings = report.Warnings.ToList();
        var (figures, scenes) = BuildFigures(report, paths, elements, typeNames, point0, warnings);
        report.Figures = figures;
        report.Warnings = warnings;

        var fileName = BuildFileName(context);
        var outputPath = Path.Combine(paths.OutputDir, fileName);

        WordReportBuilder.Write(report, outputPath, scenes);
        _logger.LogInformation(
            "Rapport généré : {Path} ({Warnings} réserve(s)).", outputPath, warnings.Count);

        return new ReportResult(outputPath, fileName, warnings);
    }

    /// <summary>
    /// "(2504)ETUDE_IMPLANTATION_ANTARES_LES INTEMPORELS.docx", as the existing studies are named.
    ///
    /// The name is composed through the SAME sanitiser the download endpoint uses to reconstruct it.
    /// Anything looser would write a name on disk that the endpoint then cleans differently, and the
    /// download link would 404 on a report the dialog had just reported as generated. A show called
    /// "MOZART : L'OPERA ROCK" is exactly that case on Linux, where a colon is a legal file name
    /// character.
    /// </summary>
    private static string BuildFileName(ReportContext ctx)
    {
        var prefix = ctx.ShowDate.ToString("ddMM");
        var venue = Segment(ctx.VenueName, 24);

        // Capped so the composed name stays inside the endpoint's own length limit; truncation must
        // happen here, on the show name, rather than there, on the whole name.
        var show = Segment(ctx.ShowName, 70);

        var composed = $"({prefix})ETUDE_IMPLANTATION_{venue}_{show}.docx";
        return ScenarioStorageService.SanitizeFileName(composed)
               ?? $"({prefix})ETUDE_IMPLANTATION.docx";
    }

    private static string Segment(string? value, int maxLength)
    {
        // Accents are stripped only in the file name: the existing studies are named "ANTARES" while
        // the document itself keeps "Antarès".
        var stripped = StripAccents(value ?? "");
        var cleaned = ScenarioStorageService.SanitizeFileName(stripped) ?? "";

        if (cleaned.Length > maxLength)
            cleaned = cleaned.Substring(0, maxLength).Trim().TrimEnd('.', ' ');

        return cleaned.Length == 0 ? "SANS-NOM" : cleaned.ToUpperInvariant();
    }

    private static string StripAccents(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(System.Text.NormalizationForm.FormC).Trim();
    }

    private (FigureSet Figures, FigureScenes Scenes) BuildFigures(
        RigReport report,
        ScenarioPaths paths,
        List<MotherGridPalonnier> elements,
        Dictionary<Guid, string> typeNames,
        (double X, double Y) point0,
        List<string> warnings)
    {
        var figures = new FigureSet
        {
            VenuePhoto = EmbeddedAssets.VenuePhotoPath,
            PointNomenclature = EmbeddedAssets.VenuePointsPath,
        };
        var scenes = new FigureScenes();

        // Figure 4: the production's own plan if one was uploaded, otherwise a plot of its loads.
        figures.ProductionPlan = FindUploadedPlan(paths.DataDir);
        if (figures.ProductionPlan is null && report.Loads.Count > 0)
        {
            (scenes.ProductionPlan, figures.ProductionPlan) = BuildFigure(
                () => ReportFigureDrawer.SceneOfLoadPlan(report.Loads, point0),
                Path.Combine(paths.OutputDir, "figure-plan-charges.png"),
                "plan de charges", warnings);

            // The reserve says what the study actually ended up with, and names the figure instead of
            // numbering it: the charges can all be there and none of them be placed - a plan of charges
            // typed without coordinates - and then there is no such figure to describe at all. The
            // number would be wrong anyway the moment an earlier captioned figure is not printed.
            warnings.Add(
                scenes.ProductionPlan is null
                    ? "Aucun plan de production n'a été fourni, et aucune charge n'a de position : la "
                      + $"figure « {FigureCaptions.ProductionPlan} » ne figure pas dans l'étude. Déposez "
                      + "« PlanProduction.png » dans les fichiers du scénario, ou complétez les "
                      + "coordonnées des charges sur la page Charges."
                    : $"Aucun plan de production n'a été fourni : la figure « {FigureCaptions.ProductionPlan} » "
                      + "est un tracé des charges depuis leurs coordonnées. Déposez « PlanProduction.png » "
                      + "dans les fichiers du scénario pour utiliser le plan de la production.");
        }

        var trusses = BuildFigureTrusses(report, elements, typeNames, warnings);
        if (trusses.Count == 0)
        {
            warnings.Add(elements.Count == 0
                ? "Aucun pont n'est défini pour ce scénario : les figures du pré rigg et de "
                  + "l'implantation sont omises."
                : "Aucun pont n'a de coordonnées complètes : les figures du pré rigg et de "
                  + "l'implantation sont omises.");
            return (figures, scenes);
        }

        (scenes.PreRig, figures.PreRig) = BuildFigure(
            () => ReportFigureDrawer.SceneOfPreRig(trusses, point0),
            Path.Combine(paths.OutputDir, "figure-pre-rigg.png"),
            "pré rigg", warnings);

        (scenes.Implantation, figures.Implantation) = BuildFigure(
            () => ReportFigureDrawer.SceneOfImplantation(trusses, report.Loads, point0),
            Path.Combine(paths.OutputDir, "figure-implantation.png"),
            "implantation", warnings);

        return (figures, scenes);
    }

    /// <summary>
    /// One figure in its two mediums: the scene the document draws as shapes, and the PNG the scenario
    /// keeps beside it. ONE layout, written twice - the framing, the label collision search and the
    /// legend wrap are the expensive part and they are not run again for the picture, and the drawing in
    /// the study cannot disagree with the plan on disk because there is nothing for them to disagree
    /// about.
    ///
    /// A layout with nothing to draw returns neither medium and says nothing: an empty plan is not a
    /// failure. A layout that throws is a reserve, and the study is still produced.
    ///
    /// THE FILE IS THE OPTIONAL HALF. The document needs no disk at all, so a PNG that cannot be written
    /// - a full disk, a folder someone has open - costs the study its downloadable plan and a reserve
    /// saying so, never its figure. Losing a figure that was already laid out because a file could not
    /// be written would be the worse failure by far.
    /// </summary>
    private (FigureScene? Scene, string? File) BuildFigure(
        Func<FigureScene?> layout, string pngPath, string what, List<string> warnings)
    {
        FigureScene? scene;

        try
        {
            scene = layout();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rapport : figure « {What} » non générée.", what);
            warnings.Add($"La figure « {what} » n'a pas pu être générée : {ex.Message}");
            return (null, null);
        }

        if (scene is null) return (null, null);

        try
        {
            return (scene, ReportFigureDrawer.Write(scene, pngPath) ? pngPath : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rapport : image de la figure « {What} » non écrite.", what);
            warnings.Add(
                $"L'image de la figure « {what} » n'a pas pu être écrite ({ex.Message}) : la figure est "
                + "dessinée dans l'étude, mais le fichier PNG n'est pas disponible à côté.");
            return (scene, null);
        }
    }

    /// <summary>
    /// The production's own plan drawing, in the scenario's data folder, under any of the accepted names
    /// and whatever the case of that name.
    ///
    /// THE SCENARIO'S FOLDER AND NOWHERE ELSE — deliberately not through
    /// <see cref="ScenarioStorageService.ResolveInputFile"/>, which every other input goes through. That
    /// resolver falls back to the shared « _common/data », which is right for the inventory workbooks:
    /// MotherGrill.xlsx and Palonnier.xlsx describe the VENUE's stock and one copy serves every scenario.
    /// A production plan is the opposite kind of file. It belongs to one production, « _common » is one
    /// folder for the whole installation, and one image left there - « plan.png » is an accepted name -
    /// would become figure 4 of every study of every event that had not uploaded its own.
    ///
    /// It would do it silently, which is what makes it worth a method of its own: a found plan suppresses
    /// both the figure the study draws from the load coordinates AND the reserve that explains it, and
    /// nothing else in the document says where that picture came from. A study would carry another
    /// production's plan with every table around it correct.
    ///
    /// A scenario with no plan of its own is not a problem: the study plots its charges and says so.
    /// </summary>
    public static string? FindUploadedPlan(string dataDir)
    {
        foreach (var name in PlanFileNames)
        {
            var path = RiggingOptimizer.IO.InputFiles.Find(dataDir, name);
            if (path is not null) return path;
        }
        return null;
    }

    /// <summary>
    /// Re-reads the scenario's trusses purely for drawing, in the studies' axes (Helpers.StudyAxes): the
    /// stored ends are the grid's. Kept separate from the report model so the document does not carry
    /// geometry it never prints.
    /// </summary>
    private static List<FigureTruss> BuildFigureTrusses(
        RigReport report,
        List<MotherGridPalonnier> elements,
        Dictionary<Guid, string> typeNames,
        List<string> warnings)
    {
        // Reuse the catalogue colour already resolved for the composition table, so a truss is the
        // same colour in the drawing and in the table beneath it.
        var byReference = new Dictionary<(string Type, long Cm), TrussLine>();
        foreach (var entry in report.Trusses.Lines)
            foreach (var key in entry.Keys)
                byReference[key] = entry;

        var trusses = new List<FigureTruss>();
        var undrawn = new List<string>();

        foreach (var element in elements)
        {
            // An end with no coordinate has no place on the plan. Drawn at the origin, as it used to be,
            // it reads as a truss hung on E5.
            if (element.StudyX1 is not { } x1 || element.StudyY1 is not { } y1
                || element.StudyX2 is not { } x2 || element.StudyY2 is not { } y2)
            {
                undrawn.Add(string.IsNullOrWhiteSpace(element.Name) ? "(élément sans nom)" : element.Name!.Trim());
                continue;
            }

            // Tableau 2 counts pieces, so a truss is found there by its first piece - the longest, as the
            // pieces are recorded - under the same key the table uses.
            var family = ReportDataBuilder.FamilyOf(typeNames, element);
            byReference.TryGetValue(
                ReportDataBuilder.ReferenceKey(family, ReportDataBuilder.PiecesOf(element)[0]),
                out var line);

            // The legend names the family - the model, as the checks are headed - rather than the label
            // of one of its pieces, which carries a length the family does not have.
            trusses.Add(new FigureTruss(x1, y1, x2, y2, family, line?.ColorHex, element.Famille, family));
        }

        if (undrawn.Count > 0)
            warnings.Add($"{undrawn.Count} élément(s) n'ont pas de coordonnées complètes et ne sont pas dessinés "
                       + "sur les figures : " + string.Join(", ", undrawn) + ".");

        return trusses;
    }

    private CompanyInfo ReadCompany()
    {
        // "Report:Company:Issuer" picks the shipped block; individual keys still override any field.
        var issuer = _configuration["Report:Company:Issuer"];
        var defaults = string.Equals(issuer, "cideco", StringComparison.OrdinalIgnoreCase)
            ? CompanyInfo.Cideco
            : CompanyInfo.RigUp;

        var section = _configuration.GetSection("Report:Company");
        if (!section.Exists()) return defaults;

        return new CompanyInfo
        {
            Name = section["Name"] ?? defaults.Name,
            AddressLine = section["AddressLine"] ?? defaults.AddressLine,
            ContactLine = section["ContactLine"] ?? defaults.ContactLine,
            RegistrationLine = section["RegistrationLine"] ?? defaults.RegistrationLine,
            TaxLine = section["TaxLine"] ?? defaults.TaxLine,
            DisclaimerEnglish = section["DisclaimerEnglish"] ?? defaults.DisclaimerEnglish,
            DisclaimerFrench = section["DisclaimerFrench"] ?? defaults.DisclaimerFrench,
            LogoKey = section["LogoKey"] ?? defaults.LogoKey,
        };
    }
}
