// =============================================================
// WordReportBuilder.cs — Renders a RigReport as the "Implantation RIGG" study.
//
// The sample documents this reproduces came through a PDF-to-Word conversion: no header part, page
// numbers typed as literal text, and pagination held together by hand-counted empty paragraphs. That
// is fine for a document written once, and fatal for a generator, where the number of beams, trusses
// and loads changes with every production.
//
// So the look is reproduced but the mechanics are not: a real header part with PAGE and NUMPAGES
// fields, real page breaks, real heading numbering, and TOC / SEQ fields Word refreshes on open.
// A production with three more beams simply reflows.
//
// OpenXml is order-sensitive in ways that produce "unreadable content" rather than an exception, so
// every property list below is built through helpers that emit schema order.
//
// THE PLAN FIGURES ARE NOT PICTURES. Where the study draws a figure itself it hands this a FigureScene
// and the figure is written as real shapes (FigureDrawingMl): crisp at any zoom and in print at any
// format, its beam letters and load names selectable, searchable and readable by a test, and lettered
// by Word rather than by a font file the container has to carry. ImageParagraph remains for what
// genuinely is an image — the venue photograph, the nomenclature diagram, and the production's own
// plan when one was uploaded. Both go into the same box, and everything around them — the captions,
// the numbering, the sentences that introduce them — is the same either way.
// =============================================================

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace AliantWebS.Reporting;

/// <summary>
/// The figures the document draws rather than embeds, each as the scene its layout produced. It sits
/// here rather than in the report model beside FigureSet on purpose: the model carries what the study
/// says, and a scene is not that — it is this writer's own input, and no other reader of a RigReport
/// has any use for it. A null scene falls back to the image the FigureSet names, which is what the
/// production's own plan always is: it arrives as a file, and nothing here laid it out.
/// </summary>
public sealed class FigureScenes
{
    public FigureScene? ProductionPlan { get; set; }
    public FigureScene? PreRig { get; set; }
    public FigureScene? Implantation { get; set; }
}

public static class WordReportBuilder
{
    // ── Page geometry, in twentieths of a point ──────────────────
    private const int PageWidth = 11910;      // A4 portrait
    private const int PageHeight = 16840;
    private const int MarginLeft = 720;
    private const int MarginRight = 620;
    private const int MarginTopBody = 2100;   // room for the banner in the header
    private const int MarginTopCover = 680;
    private const int MarginBottom = 1300;
    private const int HeaderDistance = 454;
    private const int FooterDistance = 567;

    /// <summary>Usable width, which every table grid must add up to.</summary>
    private const int ContentWidth = PageWidth - MarginLeft - MarginRight;   // 10570

    // The generated plans are drawn as the studies draw the hall, portrait (ReportFigureDrawer), so their
    // box is as tall as a page allows beside a heading and a caption. An image is fitted into it keeping
    // its proportions, so an uploaded landscape plan still fills the width.
    private const long FigureWidthEmu = 5859213;
    private const long FigureHeightEmu = 7200000;

    // ── Fonts and sizes, in half-points ──────────────────────────
    private const string BodyFont = "Arial";
    private const string TitleFont = "Arial Black";
    private const string TocFont = "Calibri";

    private static readonly System.Globalization.CultureInfo Fr =
        System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    private static int _drawingId = 1000;

    /// <summary>Writes the document. Overwrites <paramref name="outputPath"/> if it exists.</summary>
    /// <param name="scenes">
    /// The figures the study drew for itself, to be written as shapes. Omitted — as the tests that
    /// only care about the text omit it — every figure falls back to the image the FigureSet names.
    /// </param>
    public static void Write(RigReport report, string outputPath, FigureScenes? scenes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        BuildStyles(main.AddNewPart<StyleDefinitionsPart>());
        BuildNumbering(main.AddNewPart<NumberingDefinitionsPart>());

        // NOTHING IN THIS DOCUMENT ASKS WORD TO REFRESH A FIELD ON OPENING, and that is the whole point.
        // Word answers any such request with a modal question - « ce document contient des champs qui
        // peuvent faire référence à d'autres fichiers » - which tells the reader of a signed structural
        // study that it might be pulling figures from somewhere else. It never did: every field in it is a
        // TOC, a SEQ caption number or a PAGE count, all computed from the document itself.
        //
        // It was UpdateFieldsOnOpen here at first; removing it was not enough, because marking each field
        // dirty asks Word for the very same thing and raises the very same question. Measured, not assumed:
        // the same study with every w:dirty stripped opens silently.
        //
        // So each kind of field is made to be right WITHOUT an update:
        //   - PAGE and NUMPAGES are recomputed by Word whenever it lays the document out, dirty or not;
        //   - the SEQ caption numbers are counted here, at the end of this method, and written as each
        //     field's cached result - so they are correct on opening, and still real fields;
        //   - the three TOCs cannot be: their page numbers are Word's own layout. They carry a line saying
        //     to refresh, and the reader who wants them presses Ctrl+A then F9. That is the one thing this
        //     costs, and it costs it once.
        var settings = main.AddNewPart<DocumentSettingsPart>();
        settings.Settings = new Settings(
            new DecimalSymbol { Val = "," },
            new ListSeparator { Val = ";" });

        var coverFooter = BuildCoverFooter(main, report.Context);
        var bodyHeader = BuildBanner(main, report.Context);
        var bodyFooter = BuildDisclaimerFooter(main, report.Context);

        // ── Page 1: cover ────────────────────────────────────────
        AppendCover(body, main, report.Context);
        body.AppendChild(SectionBreakParagraph(
            main.GetIdOfPart(coverFooter), headerId: null, isCover: true));

        // ── Everything else shares the banner header ─────────────
        AppendRevisionPage(body, report);
        AppendPageBreak(body);

        AppendTableOfContents(body);
        AppendPageBreak(body);

        AppendFigureAndTableLists(body);
        AppendPageBreak(body);

        AppendPreliminaries(body, report);
        AppendVenuePresentation(body, main, report);
        AppendImplantation(body, main, report, scenes);
        AppendConclusion(body, report);

        // The caption numbers, now that it is known which figures and tables the document really ended up
        // with: a figure that was not drawn takes no number, and the next one does not skip one.
        NumberCaptions(body);

        // Final section properties: the last child of Body, carrying the banner and disclaimer.
        body.AppendChild(BuildSectionProperties(
            footerId: main.GetIdOfPart(bodyFooter),
            headerId: main.GetIdOfPart(bodyHeader),
            isCover: false));

        main.Document.Save();

        doc.PackageProperties.Creator = report.Context.Author;
        doc.PackageProperties.Title = $"Implantation RIGG – {report.Context.ShowName}";
        doc.PackageProperties.Subject = $"{report.Context.VenueLine} – {Date(report.Context.ShowDate)}";
        doc.PackageProperties.Created = report.Context.IssueDate;
        doc.PackageProperties.Modified = report.Context.IssueDate;
    }

    // =========================================================================
    // Styles
    // =========================================================================

    private static void BuildStyles(StyleDefinitionsPart part)
    {
        var styles = new Styles();

        styles.AppendChild(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, ComplexScript = BodyFont },
                new FontSize { Val = "22" },
                new FontSizeComplexScript { Val = "22" },
                new Languages { Val = "fr-FR" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new WidowControl { Val = false },
                new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }))));

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
            StyleName = new StyleName { Val = "Normal" },
            PrimaryStyle = new PrimaryStyle(),
        });

        // Heading 1 — Arial Black 16 pt, underlined, as in the source documents.
        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Titre1",
            StyleName = new StyleName { Val = "heading 1" },
            BasedOn = new BasedOn { Val = "Normal" },
            NextParagraphStyle = new NextParagraphStyle { Val = "Corpsdetexte" },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "240", After = "120" },
                new OutlineLevel { Val = 0 }),
            StyleRunProperties = new StyleRunProperties(
                new RunFonts { Ascii = TitleFont, HighAnsi = TitleFont, ComplexScript = TitleFont },
                new FontSize { Val = "32" },
                new FontSizeComplexScript { Val = "32" },
                new Underline { Val = UnderlineValues.Single, Color = "000000" }),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Titre2",
            StyleName = new StyleName { Val = "heading 2" },
            BasedOn = new BasedOn { Val = "Normal" },
            NextParagraphStyle = new NextParagraphStyle { Val = "Corpsdetexte" },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "180", After = "80" },
                new OutlineLevel { Val = 1 }),
            StyleRunProperties = new StyleRunProperties(
                new Bold(), new BoldComplexScript(),
                new FontSize { Val = "26" }, new FontSizeComplexScript { Val = "26" }),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Titre3",
            StyleName = new StyleName { Val = "heading 3" },
            BasedOn = new BasedOn { Val = "Normal" },
            NextParagraphStyle = new NextParagraphStyle { Val = "Corpsdetexte" },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "140", After = "60" },
                new OutlineLevel { Val = 2 }),
            StyleRunProperties = new StyleRunProperties(
                new Bold(), new BoldComplexScript(), new Italic(), new ItalicComplexScript(),
                new FontSize { Val = "22" }, new FontSizeComplexScript { Val = "22" }),
        });

        // Deliberately NOT named "heading N". A TOC field built with \o collects by heading STYLE, so
        // a front-matter banner using Titre1 lands in the contents however its outline level is
        // overridden. Giving it its own style, and an explicit outline level only where an entry is
        // wanted, is what lets the figure lists appear while the contents page itself does not.
        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "TitreSection",
            CustomStyle = true,
            StyleName = new StyleName { Val = "Titre de section" },
            BasedOn = new BasedOn { Val = "Normal" },
            NextParagraphStyle = new NextParagraphStyle { Val = "Corpsdetexte" },
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "240" }),
            StyleRunProperties = new StyleRunProperties(
                new RunFonts { Ascii = TitleFont, HighAnsi = TitleFont, ComplexScript = TitleFont },
                new FontSize { Val = "32" },
                new FontSizeComplexScript { Val = "32" }),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Titre",
            StyleName = new StyleName { Val = "Title" },
            BasedOn = new BasedOn { Val = "Normal" },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = new StyleParagraphProperties(
                new Justification { Val = JustificationValues.Center }),
            StyleRunProperties = new StyleRunProperties(
                new Bold(), new BoldComplexScript(),
                new FontSize { Val = "72" }, new FontSizeComplexScript { Val = "72" }),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Corpsdetexte",
            StyleName = new StyleName { Val = "Body Text" },
            BasedOn = new BasedOn { Val = "Normal" },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "60", After = "60" },
                new Indentation { Left = "340" },
                new Justification { Val = JustificationValues.Both }),
            StyleRunProperties = new StyleRunProperties(
                new FontSize { Val = "24" }, new FontSizeComplexScript { Val = "24" }),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "TableParagraph",
            CustomStyle = true,
            StyleName = new StyleName { Val = "Table Paragraph" },
            BasedOn = new BasedOn { Val = "Normal" },
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "40", After = "40" },
                new Justification { Val = JustificationValues.Center }),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Legende",
            CustomStyle = true,
            StyleName = new StyleName { Val = "caption" },
            BasedOn = new BasedOn { Val = "Normal" },
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "120", After = "180" },
                new Justification { Val = JustificationValues.Center }),
            StyleRunProperties = new StyleRunProperties(
                new Bold(), new BoldComplexScript(),
                new FontSize { Val = "20" }, new FontSizeComplexScript { Val = "20" }),
        });

        // TOC levels. Word writes into these when it builds the field, so they must exist up front.
        foreach (var (id, name, indent, bold, italic) in new[]
        {
            ("TM1", "toc 1", 0, true, false),
            ("TM2", "toc 2", 220, false, false),
            ("TM3", "toc 3", 440, false, true),
        })
        {
            var runProps = new StyleRunProperties(
                new RunFonts { Ascii = TocFont, HighAnsi = TocFont, ComplexScript = TocFont });
            if (bold) runProps.AppendChild(new Bold());
            if (italic) runProps.AppendChild(new Italic());
            runProps.AppendChild(new FontSize { Val = "20" });
            runProps.AppendChild(new FontSizeComplexScript { Val = "20" });

            styles.AppendChild(new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = id,
                StyleName = new StyleName { Val = name },
                BasedOn = new BasedOn { Val = "Normal" },
                StyleParagraphProperties = new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "60", After = "0" },
                    new Indentation { Left = indent.ToString() }),
                StyleRunProperties = runProps,
            });
        }

        // Zero cell margins, which every table's per-cell indentation assumes.
        styles.AppendChild(new Style
        {
            Type = StyleValues.Table,
            StyleId = "TableNormal",
            CustomStyle = true,
            StyleName = new StyleName { Val = "Table Normal" },
            StyleTableProperties = new StyleTableProperties(
                new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
                new TableCellMarginDefault(
                    new TopMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 0, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = 0, Type = TableWidthValues.Dxa })),
        });

        part.Styles = styles;
        part.Styles.Save();
    }

    // =========================================================================
    // Numbering
    // =========================================================================

    private const int NumIdHeadings = 2;
    private const int NumIdBullets = 1;

    private static void BuildNumbering(NumberingDefinitionsPart part)
    {
        // Word rejects the file if any NumberingInstance precedes an AbstractNum, so both abstract
        // definitions are appended before either instance.
        var numbering = new Numbering();

        var bullets = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "-" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "700", Hanging = "340" }),
                new NumberingSymbolRunProperties(
                    new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, Hint = FontTypeHintValues.Default },
                    new FontSize { Val = "24" }))
            { LevelIndex = 0 })
        { AbstractNumberId = 0 };

        var headings = new AbstractNum { AbstractNumberId = 1 };
        headings.AppendChild(new MultiLevelType { Val = MultiLevelValues.Multilevel });

        for (var level = 0; level < 3; level++)
        {
            var text = level switch { 0 => "%1", 1 => "%1.%2", _ => "%1.%2.%3" };

            // Hanging must equal the step, not exceed the indent, or the number is pushed into the
            // left margin and the page border clips it.
            const int hanging = 432;
            var left = hanging + level * 288;

            headings.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = text },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = left.ToString(), Hanging = hanging.ToString() }))
            { LevelIndex = level });
        }

        // Symbol bullet under a level-3 heading, used for the truss families in 3.3.1.
        headings.AppendChild(new Level(
            new NumberingFormat { Val = NumberFormatValues.Bullet },
            new LevelText { Val = "" },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation { Left = "1000", Hanging = "360" }),
            new NumberingSymbolRunProperties(
                new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol", Hint = FontTypeHintValues.Default },
                new FontSize { Val = "24" }))
        { LevelIndex = 3 });

        numbering.AppendChild(bullets);
        numbering.AppendChild(headings);
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = NumIdBullets });
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = NumIdHeadings });

        part.Numbering = numbering;
        part.Numbering.Save();
    }

    // =========================================================================
    // Sections, header and footers
    // =========================================================================

    private static SectionProperties BuildSectionProperties(string? footerId, string? headerId, bool isCover)
    {
        var sect = new SectionProperties();

        // Schema order is fixed here: references, type, size, margins, borders, numbering, columns.
        if (headerId is not null)
            sect.AppendChild(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId });
        if (footerId is not null)
            sect.AppendChild(new FooterReference { Type = HeaderFooterValues.Default, Id = footerId });

        sect.AppendChild(new PageSize { Width = PageWidth, Height = PageHeight });
        sect.AppendChild(new PageMargin
        {
            Top = isCover ? MarginTopCover : MarginTopBody,
            Right = (uint)MarginRight,
            Bottom = MarginBottom,
            Left = (uint)MarginLeft,
            Header = (uint)HeaderDistance,
            Footer = (uint)FooterDistance,
            Gutter = 0,
        });

        // The grey frame around every page. Size is in eighths of a point, Space in points.
        sect.AppendChild(new PageBorders(
            new TopBorder { Val = BorderValues.Single, Size = 8, Space = 24, Color = "404040" },
            new LeftBorder { Val = BorderValues.Single, Size = 8, Space = 24, Color = "404040" },
            new BottomBorder { Val = BorderValues.Single, Size = 8, Space = 24, Color = "404040" },
            new RightBorder { Val = BorderValues.Single, Size = 8, Space = 24, Color = "404040" })
        { OffsetFrom = PageBorderOffsetValues.Page });

        if (isCover) sect.AppendChild(new PageNumberType { Start = 1 });

        sect.AppendChild(new Columns { Space = "720" });
        return sect;
    }

    private static Paragraph SectionBreakParagraph(string? footerId, string? headerId, bool isCover) =>
        new(new ParagraphProperties(BuildSectionProperties(footerId, headerId, isCover)));

    /// <summary>The banner repeated at the top of every page after the cover.</summary>
    private static HeaderPart BuildBanner(MainDocumentPart main, ReportContext ctx)
    {
        var part = main.AddNewPart<HeaderPart>();

        int[] widths = { 2129, 5670, 951, 1820 };
        var table = NewTable(widths, indent: 0, borderSize: 4);

        var row = new TableRow();

        // Logo cell.
        var logoCell = NewCell(widths[0]);
        logoCell.RemoveAllChildren<Paragraph>();
        // 597340 x 598931 EMU is what the source studies place here. The box is square, and
        // ImageParagraph scales into it, so a wordmark and a roundel both sit correctly.
        var logo = EmbeddedAssets.HeaderLogoPath(ctx.Company.LogoKey);
        if (logo is not null)
        {
            logoCell.AppendChild(ImageParagraph(main, part, logo, 700000, 600000,
                JustificationValues.Center));
        }
        else
        {
            logoCell.AppendChild(CellText(ctx.Company.Name, bold: true, size: 24));
        }
        row.AppendChild(logoCell);

        // Title cell: three centred lines.
        var titleCell = NewCell(widths[1]);
        titleCell.RemoveAllChildren<Paragraph>();
        titleCell.AppendChild(CellText($"Implantation RIGG – Production {ctx.ShowName}", size: 24));
        titleCell.AppendChild(CellText(ctx.VenueLine, size: 24));
        titleCell.AppendChild(CellText(Date(ctx.ShowDate), size: 24));
        row.AppendChild(titleCell);

        // Labels.
        var labelCell = NewCell(widths[2]);
        labelCell.RemoveAllChildren<Paragraph>();
        labelCell.AppendChild(CellText("Date :", size: 20, align: JustificationValues.Left));
        labelCell.AppendChild(CellText("Page :", size: 20, align: JustificationValues.Left));
        row.AppendChild(labelCell);

        // Values: issue date, then live page numbers.
        var valueCell = NewCell(widths[3]);
        valueCell.RemoveAllChildren<Paragraph>();
        valueCell.AppendChild(CellText(Date(ctx.IssueDate), size: 20, align: JustificationValues.Left));

        var pageLine = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "TableParagraph" },
            new Justification { Val = JustificationValues.Left }));
        foreach (var r in FieldRuns(" PAGE   \\* MERGEFORMAT ", "1", size: 20)) pageLine.AppendChild(r);
        pageLine.AppendChild(TextRun("/", size: 20));
        foreach (var r in FieldRuns(" NUMPAGES   \\* MERGEFORMAT ", "1", size: 20)) pageLine.AppendChild(r);
        valueCell.AppendChild(pageLine);
        row.AppendChild(valueCell);

        table.AppendChild(row);

        part.Header = new Header(table, new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { After = "0", Line = "20", LineRule = LineSpacingRuleValues.Exact })));
        part.Header.Save();
        return part;
    }

    /// <summary>Cover footer: the company identity block.</summary>
    private static FooterPart BuildCoverFooter(MainDocumentPart main, ReportContext ctx)
    {
        var part = main.AddNewPart<FooterPart>();
        var c = ctx.Company;

        var footer = new Footer();
        footer.AppendChild(RuleParagraph());
        footer.AppendChild(CenteredSmall(c.Name, bold: true, size: 20));
        footer.AppendChild(CenteredSmall(c.AddressLine, size: 18));
        footer.AppendChild(CenteredSmall(c.ContactLine, size: 18));
        footer.AppendChild(CenteredSmall(c.RegistrationLine, size: 18));
        footer.AppendChild(CenteredSmall(c.TaxLine, size: 18));

        part.Footer = footer;
        part.Footer.Save();
        return part;
    }

    /// <summary>Body footer: the two-language confidentiality notice.</summary>
    private static FooterPart BuildDisclaimerFooter(MainDocumentPart main, ReportContext ctx)
    {
        var part = main.AddNewPart<FooterPart>();

        var footer = new Footer();
        footer.AppendChild(RuleParagraph());
        footer.AppendChild(CenteredSmall(ctx.Company.DisclaimerEnglish, size: 18, italic: true));
        footer.AppendChild(CenteredSmall(ctx.Company.DisclaimerFrench, size: 18));

        part.Footer = footer;
        part.Footer.Save();
        return part;
    }

    private static Paragraph RuleParagraph() =>
        new(new ParagraphProperties(
            new ParagraphBorders(new TopBorder
            { Val = BorderValues.Single, Size = 4, Space = 1, Color = "000000" }),
            new SpacingBetweenLines { Before = "0", After = "40", Line = "20", LineRule = LineSpacingRuleValues.Exact }));

    private static Paragraph CenteredSmall(string text, int size, bool bold = false, bool italic = false)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "0" },
            new Justification { Val = JustificationValues.Center }));
        p.AppendChild(TextRun(text, size: size, bold: bold, italic: italic));
        return p;
    }

    // =========================================================================
    // Cover
    // =========================================================================

    private static void AppendCover(Body body, MainDocumentPart main, ReportContext ctx)
    {
        // The cover mark is boxed at the size the source studies give it. A tall roundel and a wide
        // wordmark have very different proportions, so the box is generous in both directions and the
        // image is scaled to fit inside it rather than stretched to fill it.
        var logo = EmbeddedAssets.LogoPath(ctx.Company.LogoKey);
        if (logo is not null)
        {
            body.AppendChild(Spacer(400));
            body.AppendChild(ImageParagraph(main, main, logo, 3313430, 3300000, JustificationValues.Center));
        }

        var venueLogo = EmbeddedAssets.VenueLogoPath;
        if (venueLogo is not null)
        {
            body.AppendChild(Spacer(200));
            body.AppendChild(ImageParagraph(main, main, venueLogo, 2882075, 574167, JustificationValues.Center));
        }

        body.AppendChild(Spacer(1400));

        var title = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Titre" }));
        title.AppendChild(TextRun("Implantation RIGG", size: 72, bold: true));
        body.AppendChild(title);

        body.AppendChild(Spacer(400));

        body.AppendChild(CoverLine("Spectacle : ", ctx.ShowName));
        body.AppendChild(CoverLine("Lieu : ", ctx.VenueLine));
        body.AppendChild(CoverLine("Date : ", Date(ctx.ShowDate)));
    }

    /// <summary>A cover line whose label is underlined and whose value is not.</summary>
    private static Paragraph CoverLine(string label, string value)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "60", After = "60" },
            new Justification { Val = JustificationValues.Center }));

        p.AppendChild(new Run(
            RunProps(size: 48, bold: true, underline: true),
            new Text(label) { Space = SpaceProcessingModeValues.Preserve }));
        p.AppendChild(TextRun(value, size: 48, bold: true));
        return p;
    }

    // =========================================================================
    // Front matter
    // =========================================================================

    private static void AppendRevisionPage(Body body, RigReport report)
    {
        body.AppendChild(BannerHeading("PAGE DES INDICES", inToc: false));

        int[] widths = Distribute(ContentWidth - 700, 4);
        var table = NewTable(widths, indent: 350, borderSize: 4);

        table.AppendChild(HeaderRow(widths, new[] { "Révision", "Date", "Auteur", "Modifications" },
            font: TitleFont, size: 20, shade: null));

        var ctx = report.Context;
        table.AppendChild(DataRow(widths, new[]
        {
            ctx.Revision, Date(ctx.IssueDate), ctx.Author, ctx.RevisionNote,
        }, size: 20));

        body.AppendChild(table);
    }

    private static void AppendTableOfContents(Body body)
    {
        body.AppendChild(BannerHeading("TABLE DES MATIERES", inToc: false));

        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "120", After = "120" }));
        foreach (var r in FieldRuns(" TOC \\o \"1-3\" \\h \\z \\u ",
                     "Pour afficher la table des matières : Ctrl+A, puis F9.", size: 20))
        {
            p.AppendChild(r);
        }
        body.AppendChild(p);
    }

    private static void AppendFigureAndTableLists(Body body)
    {
        body.AppendChild(BannerHeading("LISTE DES ILLUSTRATIONS"));
        body.AppendChild(TocFieldParagraph(" TOC \\h \\z \\c \"Figure\" "));

        body.AppendChild(Spacer(400));

        body.AppendChild(BannerHeading("LISTE DES TABLEAUX"));
        body.AppendChild(TocFieldParagraph(" TOC \\h \\z \\c \"Tableau\" "));
    }

    private static Paragraph TocFieldParagraph(string instruction)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "120", After = "120" }));
        foreach (var r in FieldRuns(instruction, "Pour afficher cette liste : Ctrl+A, puis F9.", size: 20))
            p.AppendChild(r);
        return p;
    }

    /// <summary>
    /// An unnumbered Arial Black heading for the front-matter pages. <paramref name="inToc"/> is false
    /// for the contents page itself and the revision page, which the existing reports do not list.
    /// </summary>
    private static Paragraph BannerHeading(string text, bool inToc = true)
    {
        // Schema order inside pPr puts pBdr before spacing; reversing them produces a file Word
        // reports as unreadable rather than a validation exception at write time.
        var props = new ParagraphProperties(
            new ParagraphStyleId { Val = "TitreSection" },
            new ParagraphBorders(new BottomBorder
            { Val = BorderValues.Single, Size = 8, Space = 4, Color = "000000" }),
            new SpacingBetweenLines { Before = "0", After = "240" });

        // The TOC field carries \u, so an explicit outline level is what pulls a non-heading paragraph
        // into the contents. Without it the heading simply does not appear there.
        if (inToc) props.AppendChild(new OutlineLevel { Val = 0 });

        var p = new Paragraph(props);

        p.AppendChild(new Run(new Text(text)));
        return p;
    }

    // =========================================================================
    // 1 Préliminaires, 2 Présentation de la salle
    // =========================================================================

    private static void AppendPreliminaries(Body body, RigReport report)
    {
        var ctx = report.Context;

        body.AppendChild(Heading(1, "PRELIMINAIRES"));

        body.AppendChild(Heading(2, "Objet de l’étude"));
        body.AppendChild(BodyText(
            $"Le présent document constitue le rapport de l’étude de l’implantation du spectacle "
            + $"{ctx.ShowName} dans la salle {ctx.VenueName} {ctx.VenueCityLocative} le {Date(ctx.ShowDate)}."));

        body.AppendChild(Heading(2, "Documents de référence"));
        body.AppendChild(BodyText("Pour cette étude, les documents de référence sont les suivants :"));
        body.AppendChild(Bullet("Plan de lumière et de son de la production"));
        body.AppendChild(Bullet("Cahier des charges de la salle"));
    }

    private static void AppendVenuePresentation(Body body, MainDocumentPart main, RigReport report)
    {
        var venue = report.Venue;

        body.AppendChild(Heading(1, "PRESENTATION DE LA SALLE"));

        body.AppendChild(Heading(2, "Système d’accroche"));
        body.AppendChild(BodyText(
            $"La salle est composée de {venue.Beams.Count} poutres en bois traversant la salle dans sa "
            + $"largeur, numérotées de {venue.Beams.Last()} à {venue.Beams.First()}. Sur ces poutres en "
            + $"bois, {venue.Points.Count} points d’accroche ont été définis et forment avec les poutres "
            + $"des points d’accroche nommés {venue.Beams.Last()}{venue.Points.First()} à "
            + $"{venue.Beams.First()}{venue.Points.Last()}."));

        if (report.Figures.VenuePhoto is { } photo)
        {
            body.AppendChild(ImageParagraph(main, main, photo, 5644081, 2629662, JustificationValues.Center));
            body.AppendChild(Caption("Figure",
                $"Salle {report.Context.VenueName} {report.Context.VenueCityLocative}"));
        }

        body.AppendChild(Heading(2, "Nomination des éléments d’accroche"));
        body.AppendChild(BodyText("Les poutres et les appuis sont donnés dans le schéma suivant."));

        if (report.Figures.PointNomenclature is { } nomenclature)
        {
            body.AppendChild(ImageParagraph(main, main, nomenclature, 3950455, 5678424, JustificationValues.Center));
            body.AppendChild(Caption("Figure", "Nomination des poutres et des points d’accroche de la salle"));
        }

        body.AppendChild(BodyText(
            "Pour avoir des points d’accroche entre deux poutres, des palonniers sont installés entre les "
            + "points d’accroche. Des ponts treillis C500 sont d’abord installés entre les points "
            + "d’accroche pour former des lignes allant de la face vers le lointain, qu’on appellera "
            + "mother gril. Par-dessus ce mother, d’autres ponts seront superposés."));

        body.AppendChild(Heading(2, "Cahier des charges"));
        body.AppendChild(BodyText(
            "Le cahier des charges de la salle nous donne des capacités de charge aux points d’accroche "
            + "définis précédemment et une capacité de charge pour chaque poutre."));

        if (venue.PointCapacity.Count > 0)
        {
            // Ten beam blocks do not share a page with anything else without splitting badly.
            AppendPageBreak(body);
            body.AppendChild(CapacityTable(report));
            body.AppendChild(Caption("Figure", "Cahier des charges de la salle"));
        }
        else
        {
            body.AppendChild(BodyText(
                "Le cahier des charges n’est pas renseigné dans la base : les capacités admissibles ne "
                + "peuvent pas être reportées ici."));
        }
    }

    /// <summary>The black-header capacity table, one two-row block per beam.</summary>
    private static Table CapacityTable(RigReport report)
    {
        var venue = report.Venue;
        var pointCount = venue.Points.Count;

        var widths = BeamTableWidths(pointCount);
        var table = NewTable(widths, indent: 350, borderSize: 8);

        foreach (var beam in venue.Beams)
        {
            var headers = venue.Points.Select(p => $"{beam}{p}").ToList();
            headers.Add($"TOTAL POUTRE {beam}");
            table.AppendChild(BlackHeaderRow(widths, headers));

            var values = venue.Points
                .Select(p => venue.CapacityAt(beam, p) is { } c ? Num(c) : "—")
                .ToList();
            values.Add(venue.CapacityOf(beam) is { } t ? Num(t) : "—");
            table.AppendChild(DataRow(widths, values, size: 24, bold: true));

            table.AppendChild(SpacerRow(widths));
        }

        return table;
    }

    // =========================================================================
    // 3 Implantation dans la salle
    // =========================================================================

    private static void AppendImplantation(
        Body body, MainDocumentPart main, RigReport report, FigureScenes? scenes)
    {
        body.AppendChild(Heading(1, "IMPLANTATION DANS LA SALLE"));

        // ── 3.1 Plan de charges ──────────────────────────────────
        body.AppendChild(Heading(2, "Plan de charges de la production"));

        // Each sentence introduces what actually follows it. Announced unconditionally, "le plan de
        // charges est le suivant :" over a missing figure, or over a table that is only printed when
        // there are loads, leaves the reader looking for something the document never shows.
        //
        // Shapes where the study plotted the loads itself, and the uploaded plan where the production
        // supplied one - that one is a picture, and stays a picture.
        const string planCaption = FigureCaptions.ProductionPlan;
        if (FigureParagraph(main, scenes?.ProductionPlan, report.Figures.ProductionPlan, planCaption) is { } plan)
        {
            body.AppendChild(BodyText("Le plan de charges de la production est le suivant :"));
            body.AppendChild(plan);
            body.AppendChild(Caption("Figure", planCaption));
        }

        if (report.Loads.Count > 0)
        {
            body.AppendChild(BodyText("Les charges de la production sont les suivantes :"));
            body.AppendChild(LoadsTable(report));
            body.AppendChild(Caption("Tableau", "Charges de la production"));
        }
        else
        {
            body.AppendChild(BodyText(
                "Aucune charge n’est enregistrée dans le plan de charges de la production : cette étude "
                + "ne porte sur aucune charge."));
        }

        // ── 3.2 Implantation ─────────────────────────────────────
        body.AppendChild(Heading(2, "Implantation dans la salle"));
        body.AppendChild(Heading(3, "Mother + pré rigg"));

        const string preRigCaption = FigureCaptions.PreRig;
        if (FigureParagraph(main, scenes?.PreRig, report.Figures.PreRig, preRigCaption) is { } preRig)
        {
            // The sentence announces the figure, so it is written only where the figure is. Without it
            // the section is carried by the composition table below, which announces itself.
            body.AppendChild(BodyText("Le pré rigg pour cette implantation sera le suivant."));
            body.AppendChild(preRig);
            body.AppendChild(Caption("Figure", preRigCaption));
        }

        if (report.Trusses.Lines.Count > 0)
        {
            body.AppendChild(BodyText("La composition de ce pré rigg, pièce par pièce, est la suivante :"));
            body.AppendChild(TrussTable(report.Trusses));
            body.AppendChild(Caption("Tableau", "Ponts pour le mother et le pré rigg"));

            var mass = report.Trusses.TotalMassKg;
            // The trusses are transmitted to the attachment points, so this mass is already inside
            // the loads of the next table. Say so: the reader otherwise reconciles the two by adding
            // the pre rigg a second time.
            body.AppendChild(BodyText(
                !report.Trusses.MassIncomplete
                    ? report.AppliedLoadsKnown
                        ? $"Le pré rigg a une masse de {Num(mass)} daN, comprise dans les charges aux "
                          + "points d’accroche données plus loin."
                        : $"Le pré rigg a une masse de {Num(mass)} daN."
                    : mass > 0
                        ? $"Le pré rigg a une masse d’au moins {Num(mass)} daN. Cette valeur est "
                          + "incomplète : la masse de certaines références de pont est inconnue. "
                          + "Le poids propre de ces références n’est transmis à aucun point d’accroche, "
                          + "de sorte que les charges données plus loin sont sous-estimées."
                        // Nothing at all was catalogued. Printing "au moins 0 daN" states a
                        // mass, and zero is not one.
                        : "La masse du pré rigg n’a pas pu être établie : aucune des références de "
                          + "pont utilisées n’a de poids propre dans MotherGrill.xlsx ou Palonnier.xlsx. "
                          + "Aucun poids propre de pont n’est donc transmis aux points d’accroche."));

            // Tableau 2 counts pieces; which trusses are made of several, and of which, only this list
            // says. Where their joints fall is not recorded, and is not imposed.
            if (report.Trusses.Assembled.Count > 0)
            {
                body.AppendChild(BodyText(
                    "Les ponts suivants sont assemblés de plusieurs pièces de leur modèle, dont les jonctions "
                    + "peuvent se trouver en tout point de leur longueur :"));
                foreach (var assembled in report.Trusses.Assembled)
                    body.AppendChild(Bullet($"« {assembled.Element} » ({assembled.Family}) : {assembled.Pieces}."));
            }
        }

        // Where the palonniers hang. Every load on the attachment points follows from it, and a crossing
        // left unhooked on purpose is exactly what a reader of the plan could not otherwise know.
        if (report.PalonnierCount > 0) AppendHangingNote(body, report);

        const string implantationCaption = FigureCaptions.Implantation;
        if (FigureParagraph(main, scenes?.Implantation, report.Figures.Implantation, implantationCaption)
            is { } implantation)
        {
            // This section is the figure and nothing else: without it, a heading and a sentence saying
            // the plan "sera implanté de cette manière" would stand over an empty page.
            body.AppendChild(Heading(3, "Production"));
            body.AppendChild(BodyText(
                "Après ajustement des accroches, le plan de la production dans la salle sera implanté de "
                + "cette manière :"));
            body.AppendChild(implantation);
            body.AppendChild(Caption("Figure", implantationCaption));
        }

        // ── 3.3 Résultats ────────────────────────────────────────
        body.AppendChild(Heading(2, "Résultats de l’étude de charges"));

        // Mothers first, as the implantation section presents them ("Mother + pré rigg"). The note on
        // what an abaque states is printed once, ahead of whichever section comes first.
        var abaqueNotePrinted = false;
        foreach (var (role, heading) in new[]
                 {
                     (AbaqueElementRole.Mother, "Mother"),
                     (AbaqueElementRole.Palonnier, "Pré rigg"),
                 })
        {
            if (!report.AbaqueChecks.Any(c => c.Role == role)) continue;

            body.AppendChild(Heading(3, heading));
            AppendAbaqueChecks(body, report, role, printNote: !abaqueNotePrinted);
            abaqueNotePrinted = true;
        }

        body.AppendChild(Heading(3, "Points d’accroche de la salle"));

        if (report.BeamLoads.Count == 0)
        {
            // Announcing "le tableau suivant" over nothing sends the reader looking for a table the
            // study does not contain. Say instead what is missing, which is also why it is not here.
            body.AppendChild(BodyText(
                "Aucune charge n’a été calculée sur les points d’accroche de la salle : voir les réserves "
                + "en fin de document."));
        }
        else
        {
            body.AppendChild(BodyText(
                "Le tableau suivant donne la charge sur les points d’accroche de la salle avec leur "
                + "capacité maximale."));
            body.AppendChild(BeamResultsTable(report));
            body.AppendChild(Caption("Tableau", "Charge par point d’accroche"));

            // A proven exceedance is stated FIRST. It is a fact about the numbers in the table
            // above, and it stays true whether or not the study as a whole is conclusive - saying
            // only "could not be compared" over a table that visibly exceeds would bury it.
            body.AppendChild(BodyText(
                report.Exceedances.Count > 0
                    ? "Les capacités de charge ne sont pas respectées sur les poutres suivantes : "
                      + string.Join(", ", report.Exceedances.Select(e => e.Beam)) + "."
                      + (report.IsConclusive
                            ? ""
                            : " D’autres points n’ont par ailleurs pas pu être vérifiés : voir les "
                              + "réserves en fin de document.")
                    : !report.IsConclusive
                        ? "Les charges appliquées n’ont pas pu être comparées au cahier des charges : "
                          + "voir les réserves en fin de document."
                        : "Les capacités de charge des points d’accroche et des poutres en bois sont respectées."));
        }
    }

    private static void AppendAbaqueChecks(Body body, RigReport report, AbaqueElementRole role, bool printNote)
    {
        var checks = report.AbaqueChecks.Where(c => c.Role == role).ToList();
        if (checks.Count == 0) return;

        var noun = role.Noun();
        var mother = role == AbaqueElementRole.Mother;

        // The two numbers on this page come from different conventions and a reader will otherwise
        // try to reconcile them: the inventory states what may be HUNG on a truss, while the attachment
        // point loads earlier in the study include the trusses' own weight. And a value past the longest
        // length the inventory gives rests on an assumption about the joints that the engineer signing
        // the study must accept - stated whether or not a printed case uses one, since a case that
        // passed, and is therefore not printed, may.
        if (printNote)
        {
            body.AppendChild(BodyText(
                "Les charges ponctuelles au milieu données par l’inventaire sont des charges utiles, hors "
                + "poids propre du pont : les valeurs comparées ci-dessous sont donc les seules charges "
                + "appliquées. Le poids propre des ponts est en revanche compris dans les charges aux points "
                + "d’accroche."));
            body.AppendChild(BodyText(
                "Au-delà de la plus grande longueur que l’inventaire donne pour un modèle, la charge admissible "
                + "est calculée à moment égal à partir de cette longueur, jusqu’à " + ExtrapolationWords()
                + " celle-ci, en admettant que les jonctions entre les pièces d’un pont assemblé transmettent "
                + "la totalité du moment. Cette hypothèse est acceptée par l’ingénieur signataire de l’étude."));
        }

        // Say what the applied load is made of, and how it is checked. A mother's comes from the
        // palonniers hung on it and from any charge hung on the mother itself; a mother line is checked
        // bay by bay between the beams it rests on, a palonnier span by span between its hanging points.
        if (mother)
        {
            body.AppendChild(BodyText(
                "La charge appliquée à un mother est celle que lui transmettent les palonniers qui y "
                + "sont accrochés : leur part des charges de la production et de leur propre poids"
                + (report.MothersCarryDirectLoads
                    ? ", ainsi que les charges accrochées directement au mother."
                    : ".")));
            body.AppendChild(BodyText(
                "Un mother est vérifié travée par travée : la somme des charges situées entre deux poutres "
                + "voisines est comparée à la charge ponctuelle au milieu que l’inventaire admet à la longueur "
                + "immédiatement supérieure à la travée."));
        }
        else
        {
            body.AppendChild(BodyText(
                "Un palonnier est vérifié travée par travée : la somme des charges situées entre deux "
                + "points d’accroche voisins est comparée à la charge ponctuelle au milieu que l’inventaire "
                + "admet à la longueur immédiatement supérieure à cette travée."));
        }

        foreach (var family in checks.GroupBy(c => c.Family, StringComparer.OrdinalIgnoreCase))
        {
            body.AppendChild(SymbolBullet(family.Key));

            foreach (var check in family)
            {
                var span = Dec(check.SpanM);
                var applied = Num(check.AppliedKg);
                var name = string.IsNullOrWhiteSpace(check.WorstElement) ? "" : $" « {check.WorstElement!.Trim()} »";
                var bay = string.IsNullOrWhiteSpace(check.SpanLabel) ? "" : $", travée {check.SpanLabel}";

                if (check.Case == AbaqueLoadCase.Uplift)
                {
                    body.AppendChild(BodyText(
                        $"Le {noun}{name} ({family.Key}) est soulevé de {applied} daN par un palonnier en "
                        + "porte-à-faux. Aucune valeur de l’inventaire ne couvre ce cas : il n’a pas pu être vérifié."));
                    continue;
                }

                // Only the load beyond the outermost support is in cantilever, and that is what is stated -
                // never the whole load on the truss.
                if (check.Case == AbaqueLoadCase.Cantilever)
                {
                    var known = !string.IsNullOrWhiteSpace(check.SpanLabel);
                    var beyond = mother
                        ? (known ? $"au-delà du point {check.SpanLabel}" : "au-delà de sa dernière poutre")
                        : (known ? $"au-delà de son point d’accroche de la {check.SpanLabel}"
                                 : "au-delà de son dernier point d’accroche");
                    body.AppendChild(BodyText(
                        $"Le {noun}{name} ({family.Key}) porte {applied} daN en déport, jusqu’à {span} m {beyond}. "
                        + "L’inventaire ne donne aucune charge admissible en déport : cette valeur n’a pas pu "
                        + "être vérifiée."));
                    continue;
                }

                // How the case is introduced. The studies write "la charge la plus importante", which is
                // only true when the governing case - the one closest to its limit - is also the heaviest
                // load of its family. Otherwise the sentence names the truss and says why it is the one.
                // The bay is named wherever it is known: a mother line has several.
                var lead = !check.IsGoverning
                    ? $"Le {noun}{name} porte également {applied} daN sur une longueur de {span} m{bay}."
                    : check.IsHeaviest
                        ? $"La charge la plus importante sur un {noun} {family.Key} est de {applied} daN "
                          + $"sur une longueur de {span} m{Where(name, check.SpanLabel)}."
                        : $"La charge la plus défavorable sur un {noun} {family.Key}, rapportée à la charge "
                          + $"admissible, est celle du {noun}{name}{bay} : {applied} daN sur une longueur de {span} m.";

                body.AppendChild(BodyText(lead + " " + AllowedSentence(check)));
            }
        }

        var exceeded = report.AbaqueExceedances.Where(c => c.Role == role).ToList();
        // A value read without one of its model's rows is not the inventory's value: never "correctement
        // dimensionné" on the back of it either.
        var unverified = checks.Where(c => c.AllowedKg is null || c.InventoryIncomplete).ToList();

        body.AppendChild(BodyText(
            exceeded.Count > 0
                ? (mother ? "Certains mothers" : "Certains palonniers du pré rigg")
                  + " dépassent la charge admissible donnée par l’inventaire : "
                  + string.Join(", ", exceeded.Select(c => c.Family).Distinct()) + "."
                : unverified.Count > 0
                    // Never "correctement dimensionné" on the back of a check that could not run.
                    ? $"Le dimensionnement des {(mother ? "mothers" : "palonniers")} n’a pas pu être entièrement "
                      + "vérifié pour : "
                      + string.Join(", ", unverified.Select(c => c.Family).Distinct()) + "."
                    : mother
                        ? "L’ensemble des mothers utilisé pour ce plan est correctement dimensionné pour "
                          + "cette implantation."
                        : "L’ensemble des palonniers du pré rigg utilisé pour ce plan est correctement "
                          + "dimensionné pour cette implantation."));
    }

    /// <summary>
    /// Where the palonniers hang: on every mother they cross, save the crossings left unhooked - by the
    /// optimiser because a mother would have gone past its capacity, or by hand - each named.
    /// </summary>
    private static void AppendHangingNote(Body body, RigReport report)
    {
        const string rule = "Chaque palonnier est accroché à tous les mothers qu’il croise, et chaque charge "
                          + "est reprise par les deux points d’accroche les plus proches, de part et d’autre";

        if (report.Unhooked.Count == 0)
        {
            body.AppendChild(BodyText(rule + "."));
            return;
        }

        body.AppendChild(BodyText(rule + ", à l’exception des croisements suivants, où le palonnier n’est "
                                  + "pas accroché au mother :"));

        foreach (var u in report.Unhooked)
        {
            body.AppendChild(Bullet(
                $"palonnier « {u.Palonnier} », mother « {u.Mother} » : "
                + (u.ByHand
                    ? "décroché à la main"
                    : "décroché par l’optimiseur" + (string.IsNullOrWhiteSpace(u.Reason) ? "" : $", {u.Reason}"))
                + "."));
        }
    }

    /// <summary>
    /// Production loads with the position the plan gives them: two loads to a row, each printed as
    /// name, X, Y and weight. The coordinates are in the studies' axes, like every other coordinate
    /// of the report, so a load can be found on the implantation drawing from this table alone.
    /// </summary>
    private static Table LoadsTable(RigReport report)
    {
        const int blocks = 2;        // loads printed side by side on one row
        const int perBlock = 4;      // name, X, Y, weight
        const int coordWidth = 1000;
        const int weightWidth = 1435;

        var gridWidth = ContentWidth - 700;
        var widths = new int[blocks * perBlock];
        for (var b = 0; b < blocks; b++)
        {
            // The name takes what the figures leave: it is the only column whose content varies in length.
            widths[b * perBlock] = gridWidth / blocks - 2 * coordWidth - weightWidth;
            widths[b * perBlock + 1] = coordWidth;
            widths[b * perBlock + 2] = coordWidth;
            widths[b * perBlock + 3] = weightWidth;
        }

        // The block width is an integer division, so the last column takes back whatever it dropped:
        // this grid has to add up to the same width as the other tables of the study.
        widths[^1] += gridWidth - widths.Sum();

        var table = NewTable(widths, indent: 350, borderSize: 4);
        var loads = report.Loads;

        // Headed once, over both blocks, as the hand-written studies head this table.
        var headers = new List<string>();
        for (var b = 0; b < blocks; b++)
            headers.AddRange(new[] { "Moteur", "X (m)", "Y (m)", "Charge (daN)" });
        table.AppendChild(HeaderRow(widths, headers, font: BodyFont, size: 20, shade: "D9D9D9"));

        for (var i = 0; i < loads.Count; i += blocks)
        {
            var cells = new List<string>();
            for (var b = 0; b < blocks; b++)
            {
                var index = i + b;
                if (index >= loads.Count)
                {
                    // The row is padded to the full grid, or DataRow reads it as a row with a gutter.
                    for (var c = 0; c < perBlock; c++) cells.Add("");
                    continue;
                }

                // A load the plan does not place carries X = Y = 0, which is not a position: an em dash,
                // as the beam table prints for a value it does not have, rather than "0,00", which would
                // read as a load hung on E5. A load the plan does not weigh carries 0 daN, which is not a
                // weight either, and this is the table the crew reads to know what hangs where.
                var load = loads[index];
                cells.Add(load.Name);
                cells.Add(load.Located ? Metres(load.X) : "—");
                cells.Add(load.Located ? Metres(load.Y) : "—");
                cells.Add(load.Weighed ? Num(load.WeightKg) : "—");
            }
            table.AppendChild(DataRow(widths, cells, size: 20));
        }

        // Total across the last block, matching how the existing reports close this table: TOTAL under
        // the coordinates, its value under the weights. It adds up the weights that are known; a charge
        // whose weight nobody entered is a verification gap, not a silent addend of 0 daN.
        var totalCells = new List<string>();
        for (var c = 0; c < (blocks - 1) * perBlock; c++) totalCells.Add("");
        totalCells.Add("");
        totalCells.Add("");
        totalCells.Add("TOTAL");
        totalCells.Add(Num(report.TotalLoadKg));
        table.AppendChild(DataRow(widths, totalCells, size: 20, bold: true));

        return table;
    }

    private static Table TrussTable(TrussInventory inventory)
    {
        int[] widths = { ContentWidth - 700 - 2000 - 2000, 2000, 2000 };
        var table = NewTable(widths, indent: 350, borderSize: 4);

        table.AppendChild(HeaderRow(widths, new[] { "", "PRIS", "POIDS" }, font: BodyFont, size: 24, shade: null));

        foreach (var line in inventory.Lines)
        {
            var values = new[]
            {
                line.Label,
                line.Count.ToString(),
                line.UnitWeightKg is { } w ? Num(w) : "—",
            };
            table.AppendChild(DataRow(widths, values, size: 24, shade: line.ColorHex));
        }

        return table;
    }

    /// <summary>Per-beam results: allowed loads over applied loads, one three-row block per beam.</summary>
    private static Table BeamResultsTable(RigReport report)
    {
        var pointCount = report.Venue.Points.Count;
        var widths = BeamTableWidths(pointCount);
        var table = NewTable(widths, indent: 350, borderSize: 8);

        foreach (var beam in report.BeamLoads)
        {
            var headers = report.Venue.Points.Select(p => $"{beam.Beam}{p}").ToList();
            headers.Add($"TOTAL POUTRE {beam.Beam}");
            table.AppendChild(BlackHeaderRow(widths, headers));

            var allowed = beam.Allowed.Select(a => a is { } v ? Num(v) : "—").ToList();
            allowed.Add(beam.AllowedTotal is { } t ? Num(t) : "—");
            // keepNext so the allowed row stays with the applied row underneath it.
            table.AppendChild(DataRow(widths, allowed, size: 24, bold: true, keepNext: true));

            // An em dash, not 0: a table of zeros reads as "nothing is hung here", which is exactly
            // the wrong conclusion when the loads were simply never calculated.
            var applied = report.AppliedLoadsKnown
                ? beam.Applied.Select(Num).ToList()
                : beam.Applied.Select(_ => "—").ToList();
            applied.Add(report.AppliedLoadsKnown ? Num(beam.AppliedTotal) : "—");

            // An overloaded cell is tinted red so it cannot be read past.
            var shades = new string?[applied.Count];
            for (var i = 0; i < shades.Length; i++) shades[i] = "D9D9D9";
            foreach (var i in beam.OverloadedPoints) shades[i] = "F4B8B8";
            if (beam.BeamOverloaded) shades[^1] = "F4B8B8";

            table.AppendChild(DataRow(widths, applied, size: 20, bold: true, shadePerCell: shades));
            table.AppendChild(SpacerRow(widths));
        }

        return table;
    }

    private static int[] BeamTableWidths(int pointCount)
    {
        var totalWidth = ContentWidth - 700;
        var totalColumn = 2300;
        var gutter = 200;
        var pointWidth = (totalWidth - totalColumn - gutter) / pointCount;

        var widths = new int[pointCount + 2];
        for (var i = 0; i < pointCount; i++) widths[i] = pointWidth;
        widths[pointCount] = gutter;
        widths[pointCount + 1] = totalWidth - pointWidth * pointCount - gutter;
        return widths;
    }

    // =========================================================================
    // 4 Conclusion
    // =========================================================================

    private static void AppendConclusion(Body body, RigReport report)
    {
        body.AppendChild(Heading(1, "CONCLUSION"));

        // Three outcomes, not two. An unperformed check is not a passed check, so a study that
        // could not compare the loads against the load book says exactly that instead of
        // pronouncing on a plan it never verified.
        if (!report.IsConclusive)
        {
            body.AppendChild(BodyText(
                "Cette étude n’est pas concluante : certaines vérifications n’ont pas pu être "
                + "effectuées faute de données. Le plan de lumière et de son de la production "
                + $"{report.Context.ShowName} ne peut donc être déclaré ni conforme ni non conforme "
                + "au cahier des charges de la salle en l’état."));

            body.AppendChild(BodyText("Vérifications non effectuées :"));
            foreach (var gap in report.VerificationGaps) body.AppendChild(Bullet(gap));

            if (report.HasExceedances)
            {
                body.AppendChild(BodyText(
                    "Les vérifications qui ont pu être menées font en outre apparaître les "
                    + "dépassements suivants :"));
                AppendExceedanceList(body, report);
            }

            body.AppendChild(BodyText(
                "Les données manquantes doivent être complétées, puis l’étude rééditée, avant toute "
                + "exploitation de ce document."));
        }
        else if (report.IsCompliant)
        {
            body.AppendChild(BodyText(
                "Les éléments structurels de la salle ont été vérifiés selon le cahier des charges. Le "
                + $"plan de lumière et de son de la production {report.Context.ShowName} respecte donc le "
                + "cahier des charges de la salle."));
        }
        else
        {
            body.AppendChild(BodyText(
                "Les éléments structurels de la salle ont été vérifiés selon le cahier des charges. Le "
                + $"plan de lumière et de son de la production {report.Context.ShowName} ne respecte pas "
                + "le cahier des charges de la salle sur les points suivants :"));

            AppendExceedanceList(body, report);

            body.AppendChild(BodyText(
                "L’implantation doit être reprise avant validation."));
        }

        if (report.Warnings.Count > 0)
        {
            body.AppendChild(Heading(2, "Réserves sur les données"));
            body.AppendChild(BodyText(
                "Les points suivants limitent la portée de cette étude et doivent être levés avant "
                + "exploitation du document :"));
            foreach (var warning in report.Warnings) body.AppendChild(Bullet(warning));
        }
    }

    /// <summary>The bullet list of everything demonstrably over its limit.</summary>
    private static void AppendExceedanceList(Body body, RigReport report)
    {
        foreach (var beam in report.Exceedances)
        {
            if (beam.BeamOverloaded)
            {
                body.AppendChild(Bullet(
                    $"Poutre {beam.Beam} : {Num(beam.AppliedTotal)} daN appliqués pour "
                    + $"{Num(beam.AllowedTotal ?? 0)} daN admissibles."));
            }

            foreach (var index in beam.OverloadedPoints)
            {
                body.AppendChild(Bullet(
                    $"Point {beam.Beam}{report.Venue.Points[index]} : {Num(beam.Applied[index])} daN "
                    + $"appliqués pour {Num(beam.Allowed[index] ?? 0)} daN admissibles."));
            }
        }

        foreach (var check in report.AbaqueExceedances)
        {
            // Which truss and which bay, so the reader can find it on the plan, and whether the allowed
            // value was read from the inventory or computed from it.
            body.AppendChild(Bullet(
                $"{check.Role.Capitalised()} {check.Family}"
                + (string.IsNullOrWhiteSpace(check.WorstElement) ? "" : $" « {check.WorstElement!.Trim()} »")
                + (string.IsNullOrWhiteSpace(check.SpanLabel) ? "" : $", travée {check.SpanLabel}")
                + $" : {Num(check.AppliedKg)} daN sur {Dec(check.SpanM)} m pour "
                + $"{Num(check.AllowedKg ?? 0)} daN admissibles"
                + (check.IsComputed ? " (valeur calculée)" : "")
                + (check.InventoryIncomplete
                    ? $" ({Typographic(UnreadInventoryRows.Clause(check.SkippedRows))})"
                    : "")
                + "."));
        }
    }

    // =========================================================================
    // Paragraph and run helpers
    // =========================================================================

    private static Paragraph Heading(int level, string text)
    {
        var style = level switch { 1 => "Titre1", 2 => "Titre2", _ => "Titre3" };

        var props = new ParagraphProperties(
            new ParagraphStyleId { Val = style },
            new NumberingProperties(
                new NumberingLevelReference { Val = level - 1 },
                new NumberingId { Val = NumIdHeadings }));

        if (level == 1)
        {
            props.AppendChild(new ParagraphBorders(new BottomBorder
            { Val = BorderValues.Single, Size = 8, Space = 4, Color = "000000" }));
        }

        var p = new Paragraph(props);
        p.AppendChild(new Run(new Text(text)));
        return p;
    }

    private static Paragraph BodyText(string text)
    {
        var p = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Corpsdetexte" }));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph Bullet(string text)
    {
        var p = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corpsdetexte" },
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = NumIdBullets }),
            new Justification { Val = JustificationValues.Left }));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph SymbolBullet(string text)
    {
        var p = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corpsdetexte" },
            new NumberingProperties(
                new NumberingLevelReference { Val = 3 },
                new NumberingId { Val = NumIdHeadings }),
            new Justification { Val = JustificationValues.Left }));
        p.AppendChild(TextRun(text, size: 24, bold: true));
        return p;
    }

    /// <summary>A numbered caption. The number is a SEQ field so the lists stay in step.</summary>
    private static Paragraph Caption(string kind, string text)
    {
        var p = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Legende" }));

        p.AppendChild(TextRun($"{kind} ", size: 20, bold: true));
        foreach (var r in FieldRuns($" SEQ {kind} \\* ARABIC ", "1", size: 20, bold: true))
            p.AppendChild(r);
        p.AppendChild(TextRun($" : {text}", size: 20, bold: true));

        return p;
    }

    /// <summary>
    /// Counts the SEQ caption fields in the order they appear and writes each one's number into its own
    /// cached result, so « Figure 3 » reads as Figure 3 in a document nobody has refreshed.
    ///
    /// Done at the end rather than as each caption is written, because what a study contains is decided as
    /// it is written: a figure whose scene could not be laid out takes no number at all, and numbering as
    /// we went would leave a gap where it would have been.
    /// </summary>
    private static void NumberCaptions(Body body)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var code = paragraph.Descendants<FieldCode>()
                .FirstOrDefault(c => c.Text.Contains("SEQ ", StringComparison.Ordinal));
            if (code is null) continue;

            // " SEQ Figure \\* ARABIC " - the kind is the word after SEQ.
            var parts = code.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var seq = Array.IndexOf(parts, "SEQ");
            if (seq < 0 || seq + 1 >= parts.Length) continue;

            var kind = parts[seq + 1];
            counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;

            // The cached result is whatever sits between the separator and the end of the field.
            var runs = paragraph.Elements<Run>().ToList();
            var separator = runs.FindIndex(r => r.Descendants<FieldChar>()
                .Any(f => f.FieldCharType?.Value == FieldCharValues.Separate));
            var end = runs.FindIndex(r => r.Descendants<FieldChar>()
                .Any(f => f.FieldCharType?.Value == FieldCharValues.End));
            if (separator < 0 || end <= separator) continue;

            foreach (var text in runs.Skip(separator + 1).Take(end - separator - 1)
                         .SelectMany(r => r.Elements<Text>()))
            {
                text.Text = counts[kind].ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    private static Paragraph Spacer(int twips) =>
        new(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "0", Line = twips.ToString(), LineRule = LineSpacingRuleValues.Exact }));

    private static void AppendPageBreak(Body body) =>
        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

    private static RunProperties RunProps(
        int size, bool bold = false, bool italic = false, bool underline = false,
        string? color = null, string? font = null)
    {
        // Built in schema order: fonts, bold, italic, colour, size, underline.
        var props = new RunProperties();
        if (font is not null)
            props.AppendChild(new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font });
        if (bold) { props.AppendChild(new Bold()); props.AppendChild(new BoldComplexScript()); }
        if (italic) { props.AppendChild(new Italic()); props.AppendChild(new ItalicComplexScript()); }
        if (color is not null) props.AppendChild(new Color { Val = color });
        props.AppendChild(new FontSize { Val = size.ToString() });
        props.AppendChild(new FontSizeComplexScript { Val = size.ToString() });
        if (underline) props.AppendChild(new Underline { Val = UnderlineValues.Single });
        return props;
    }

    private static Run TextRun(
        string text, int size, bool bold = false, bool italic = false,
        string? color = null, string? font = null) =>
        new(RunProps(size, bold, italic, color: color, font: font),
            new Text(text) { Space = SpaceProcessingModeValues.Preserve });

    /// <summary>
    /// The five runs that make one field: begin, the code, the separator, the cached result, end.
    ///
    /// NOT MARKED DIRTY, deliberately - see the note in <see cref="Write"/>. A dirty field is one Word must
    /// update when the document is opened, and updating fields at open is what raises « ce document
    /// contient des champs qui peuvent faire référence à d'autres fichiers ». So the cached text is not a
    /// fallback here: it is what the reader sees, and it has to be right.
    /// </summary>
    private static IEnumerable<Run> FieldRuns(string instruction, string cached, int size, bool bold = false)
    {
        yield return new Run(RunProps(size, bold),
            new FieldChar { FieldCharType = FieldCharValues.Begin });

        yield return new Run(RunProps(size, bold),
            new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve });

        yield return new Run(RunProps(size, bold),
            new FieldChar { FieldCharType = FieldCharValues.Separate });

        yield return new Run(RunProps(size, bold),
            new Text(cached) { Space = SpaceProcessingModeValues.Preserve });

        yield return new Run(RunProps(size, bold),
            new FieldChar { FieldCharType = FieldCharValues.End });
    }

    // =========================================================================
    // Table helpers
    // =========================================================================

    private static Table NewTable(int[] widths, int indent, int borderSize)
    {
        var borders = new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = (uint)borderSize, Space = 0, Color = "000000" },
            new LeftBorder { Val = BorderValues.Single, Size = (uint)borderSize, Space = 0, Color = "000000" },
            new BottomBorder { Val = BorderValues.Single, Size = (uint)borderSize, Space = 0, Color = "000000" },
            new RightBorder { Val = BorderValues.Single, Size = (uint)borderSize, Space = 0, Color = "000000" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = (uint)borderSize, Space = 0, Color = "000000" },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = (uint)borderSize, Space = 0, Color = "000000" });

        var props = new TableProperties(
            new TableStyle { Val = "TableNormal" },
            new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto },
            new TableIndentation { Width = indent, Type = TableWidthUnitValues.Dxa },
            borders,
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableLook { Val = "01E0", FirstRow = true, LastRow = true, FirstColumn = true, LastColumn = true });

        var grid = new TableGrid();
        foreach (var w in widths) grid.AppendChild(new GridColumn { Width = w.ToString() });

        return new Table(props, grid);
    }

    private static TableCell NewCell(int width, string? shade = null, bool noRightBorder = false)
    {
        // TableCellProperties order: width, span, merge, borders, shading.
        var props = new TableCellProperties(
            new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });

        if (noRightBorder)
            props.AppendChild(new TableCellBorders(new RightBorder { Val = BorderValues.Nil }));

        if (shade is not null)
        {
            props.AppendChild(new Shading
            { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shade });
        }

        props.AppendChild(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        // A cell without a paragraph corrupts the document, so one is always present.
        return new TableCell(props, new Paragraph());
    }

    private static Paragraph CellText(
        string text, int size = 22, bool bold = false, string? color = null,
        string? font = null, JustificationValues? align = null, bool keepNext = false)
    {
        var props = new ParagraphProperties(new ParagraphStyleId { Val = "TableParagraph" });

        // keepNext on a row's paragraphs is what stops Word splitting a beam block so that its black
        // header sits at the foot of one page and its values at the head of the next.
        if (keepNext) props.AppendChild(new KeepNext());
        if (align is { } a) props.AppendChild(new Justification { Val = a });

        var p = new Paragraph(props);
        if (text.Length > 0) p.AppendChild(TextRun(text, size, bold, color: color, font: font));
        return p;
    }

    private static TableRow HeaderRow(
        int[] widths, IReadOnlyList<string> texts, string font, int size, string? shade)
    {
        var row = new TableRow(new TableRowProperties(new CantSplit()));
        for (var i = 0; i < widths.Length; i++)
        {
            var cell = NewCell(widths[i], shade);
            cell.RemoveAllChildren<Paragraph>();
            cell.AppendChild(CellText(i < texts.Count ? texts[i] : "", size, bold: true, font: font,
                keepNext: true));
            row.AppendChild(cell);
        }
        return row;
    }

    /// <summary>Black band with white bold text: the beam header used by both load tables.</summary>
    private static TableRow BlackHeaderRow(int[] widths, IReadOnlyList<string> texts)
    {
        var row = new TableRow(new TableRowProperties(new CantSplit()));
        var gutterIndex = widths.Length - 2;

        for (var i = 0; i < widths.Length; i++)
        {
            // The narrow column before the total is a visual gutter, left unshaded.
            var isGutter = i == gutterIndex;
            var cell = NewCell(widths[i], isGutter ? null : "000000");
            cell.RemoveAllChildren<Paragraph>();

            var text = isGutter ? "" : texts[i < gutterIndex ? i : texts.Count - 1];
            cell.AppendChild(CellText(text, size: 24, bold: true, color: "FFFFFF", keepNext: true));
            row.AppendChild(cell);
        }
        return row;
    }

    private static TableRow DataRow(
        int[] widths, IReadOnlyList<string> texts, int size,
        bool bold = false, string? shade = null, string?[]? shadePerCell = null,
        bool keepNext = false)
    {
        var row = new TableRow(new TableRowProperties(new CantSplit()));
        var gutterIndex = widths.Length - 2;
        var hasGutter = widths.Length == texts.Count + 1;

        for (var i = 0; i < widths.Length; i++)
        {
            var isGutter = hasGutter && i == gutterIndex;
            var textIndex = !hasGutter ? i : (i < gutterIndex ? i : texts.Count - 1);

            var cellShade = isGutter
                ? null
                : shadePerCell is not null && textIndex < shadePerCell.Length
                    ? shadePerCell[textIndex]
                    : shade;

            var cell = NewCell(widths[i], cellShade);
            cell.RemoveAllChildren<Paragraph>();
            cell.AppendChild(CellText(
                isGutter || textIndex >= texts.Count ? "" : texts[textIndex], size, bold,
                keepNext: keepNext));
            row.AppendChild(cell);
        }
        return row;
    }

    /// <summary>A full-width blank row separating two beam blocks, borders suppressed.</summary>
    private static TableRow SpacerRow(int[] widths)
    {
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Width = widths.Sum().ToString(), Type = TableWidthUnitValues.Dxa },
                new GridSpan { Val = widths.Length },
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.Nil },
                    new LeftBorder { Val = BorderValues.Nil },
                    new BottomBorder { Val = BorderValues.Nil },
                    new RightBorder { Val = BorderValues.Nil })),
            new Paragraph(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "0", Line = "120", LineRule = LineSpacingRuleValues.Exact })));

        return new TableRow(cell);
    }

    private static int[] Distribute(int total, int columns)
    {
        var widths = new int[columns];
        for (var i = 0; i < columns; i++) widths[i] = total / columns;
        widths[^1] = total - widths[0] * (columns - 1);
        return widths;
    }

    // =========================================================================
    // Figures
    // =========================================================================

    /// <summary>
    /// One of the study's plan figures: shapes where the layout handed over a scene, the image the
    /// FigureSet names where it did not, and nothing at all - not even the sentence that introduces
    /// it, which is the caller's test - where there is neither.
    /// </summary>
    private static Paragraph? FigureParagraph(
        MainDocumentPart main, FigureScene? scene, string? imagePath, string name)
    {
        if (scene is not null) return DrawnParagraph(scene, name);

        if (imagePath is not null)
            return ImageParagraph(main, main, imagePath, FigureWidthEmu, FigureHeightEmu, JustificationValues.Center);

        return null;
    }

    /// <summary>
    /// A scene placed inline in its own centred paragraph, fitted into the very box an image would
    /// have been fitted into and with its proportions kept, so a figure occupies the page as it always
    /// did whichever medium it arrived in.
    /// </summary>
    private static Paragraph DrawnParagraph(FigureScene scene, string name)
    {
        var p = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "TableParagraph" },
            new SpacingBetweenLines { Before = "120", After = "60" },
            new Justification { Val = JustificationValues.Center }));

        p.AppendChild(new Run(
            FigureDrawingMl.Build(scene, FigureWidthEmu, FigureHeightEmu, name, NextDrawingId)));

        return p;
    }

    /// <summary>
    /// The next id for a drawing or for one of the shapes inside one. Every wp:docPr and wps:cNvPr id
    /// must be unique across the document or Word repairs the file, so pictures and shapes draw from
    /// this one series.
    /// </summary>
    private static uint NextDrawingId() => (uint)System.Threading.Interlocked.Increment(ref _drawingId);

    /// <summary>
    /// Places an image inline in its own centred paragraph. Both the container part and the main part
    /// are taken because a header hosts its own image part, not the document's.
    /// </summary>
    private static Paragraph ImageParagraph(
        MainDocumentPart main, OpenXmlPartContainer container,
        string imagePath, long maxWidthEmu, long maxHeightEmu, JustificationValues align)
    {
        // Scaled into the box rather than stretched to fill it. Every generated figure carries a
        // printed scale bar, so distorting one axis makes distances measured off the drawing wrong.
        var (widthEmu, heightEmu) = FitToBox(imagePath, maxWidthEmu, maxHeightEmu);

        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var type = extension is ".jpg" or ".jpeg" ? ImagePartType.Jpeg : ImagePartType.Png;

        ImagePart part = container switch
        {
            HeaderPart header => header.AddImagePart(type),
            FooterPart footer => footer.AddImagePart(type),
            _ => main.AddImagePart(type),
        };

        using (var stream = File.OpenRead(imagePath)) part.FeedData(stream);
        var relationshipId = container.GetIdOfPart(part);

        var id = NextDrawingId();
        var name = $"figure{id}";

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = id, Name = name },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });

        var p = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "TableParagraph" },
            new SpacingBetweenLines { Before = "120", After = "60" },
            new Justification { Val = align }));
        p.AppendChild(new Run(drawing));
        return p;
    }

    /// <summary>
    /// The largest size that fits inside the box while keeping the image's own proportions. Falls back
    /// to the box itself when the image cannot be decoded, which is no worse than the fixed size it
    /// would otherwise have used.
    /// </summary>
    private static (long Width, long Height) FitToBox(string path, long maxWidth, long maxHeight)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is { Info: { Width: > 0, Height: > 0 } info })
            {
                var scale = Math.Min((double)maxWidth / info.Width, (double)maxHeight / info.Height);
                var width = (long)Math.Round(info.Width * scale);
                var height = (long)Math.Round(info.Height * scale);
                if (width > 0 && height > 0) return (width, height);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rapport : dimensions de « {Path.GetFileName(path)} » illisibles ({ex.Message}).");
        }

        return (maxWidth, maxHeight);
    }

    // =========================================================================
    // Formatting
    // =========================================================================

    /// <summary>
    /// "9 000" — French grouping with a plain space, as the source documents use. Rounded through
    /// <see cref="Rounded"/>, so that a value just under zero never prints as "-0".
    /// </summary>
    private static string Num(double value) =>
        Rounded(value).ToString("#,##0", Fr).Replace(' ', ' ').Replace(' ', ' ');

    /// <summary>
    /// A value rounded to the unit it is printed in, with the sign of zero dropped. Math.Round keeps
    /// that sign and .NET prints it, so an attachment point left with -0,4 daN of residual uplift would
    /// read "-0" daN — a minus sign in front of a load, over a table the crew rigs from. <see cref="Metres"/>
    /// guards the same case, as does ReportDataBuilder.StudyPoint.
    /// </summary>
    private static double Rounded(double value)
    {
        var rounded = Math.Round(value);
        return rounded == 0 ? 0.0 : rounded;
    }

    /// <summary>"7,4" — one decimal, comma separator.</summary>
    private static string Dec(double value) => value.ToString("0.##", Fr);

    /// <summary>
    /// "1,20" — two decimals, the format every other coordinate of the report is printed in
    /// (ReportDataBuilder.StudyPoint). A reflected axis turns a 0 into -0, which would print as
    /// "-0,00", so anything under half a centimetre is printed as a plain 0.
    /// </summary>
    private static string Metres(double value) =>
        (Math.Abs(value) < 0.005 ? 0.0 : value).ToString("0.00", Fr);

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Fr);

    // =========================================================================
    // Allowed loads
    // =========================================================================

    /// <summary>
    /// What the inventory allows for a check, and where that figure comes from: a row read, a value
    /// computed at equal moment past the model's longest row, or why there is none.
    /// </summary>
    private static string AllowedSentence(AbaqueCheck check)
    {
        if (check.AllowedKg is { } allowed)
        {
            // A row of this model could not be read: the value comes from the other rows, and neither "the
            // longest length of the inventory" nor "the inventory" says which rows those are. The sentence says
            // it, and names the missing row; the study also counts it among the checks not carried out.
            var incomplete = check.InventoryIncomplete;
            var unread = incomplete
                ? " " + Capitalise(Typographic(UnreadInventoryRows.Clause(check.SkippedRows)))
                  + " : cette valeur ne tient compte que des lignes lisibles et reste à vérifier."
                : "";

            if (check.Source == global::RiggingOptimizer.IO.AllowedSource.Computed)
            {
                var basis = check.BaseLengthM is { } baseLength && check.BaseKg is { } baseKg
                    ? $" ({Dec(baseLength)} m, {Num(baseKg)} daN)"
                    : "";
                var perMetre = check.KgPerM is { } p ? $", avec p = {Dec(p)} daN/m" : "";
                return (incomplete
                           ? "Cette portée dépasse la plus grande longueur lisible de l’inventaire pour ce modèle"
                           : "Cette portée dépasse la plus grande longueur de l’inventaire pour ce modèle")
                     + $"{basis} : la charge ponctuelle centrée admise, {Num(allowed)} daN, en est déduite à "
                     + $"moment égal (M = P·L/4 + p·L²/8{perMetre})." + unread;
            }

            var source = incomplete ? "D’après les lignes lisibles de l’inventaire" : "D’après l’inventaire";
            return (check.AllowedSpanM is { } rowLength
                       ? $"{source}, une charge ponctuelle centrée de {Num(allowed)} daN est autorisée "
                         + $"sur une longueur de {Dec(rowLength)} m."
                       : $"{source}, une charge ponctuelle centrée de {Num(allowed)} daN est autorisée.")
                 + unread;
        }

        var reason = string.IsNullOrWhiteSpace(check.NoValueReason)
            ? "Aucune charge admissible n’est disponible"
            : Capitalise(Typographic(check.NoValueReason!.Trim()));
        return $"{reason} : la charge n’a pas pu être vérifiée.";
    }

    /// <summary>" (« name », travée G3–F3)": which truss and which bay, wherever either is known.</summary>
    private static string Where(string quotedName, string? bay)
    {
        var parts = new List<string>();
        if (quotedName.Trim().Length > 0) parts.Add(quotedName.Trim());
        if (!string.IsNullOrWhiteSpace(bay)) parts.Add($"travée {bay}");
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }

    /// <summary>How far past its longest row a model's value is computed, in words: "deux fois".</summary>
    private static string ExtrapolationWords()
    {
        var limit = global::RiggingOptimizer.IO.LoadTable.ExtrapolationLimit;
        return Math.Abs(limit - 2.0) < 1e-9 ? "deux fois" : $"{Dec(limit)} fois";
    }

    /// <summary>The study's typographic apostrophe in a text written with a straight one.</summary>
    private static string Typographic(string text) => text.Replace('\'', '’');

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Fr) + text.Substring(1);
}
