// =============================================================
// ManualWordWriter.cs — The optimiser's manual, as a .docx.
//
// The page and this file print the SAME ManualDocument: the export cannot say something the screen does
// not, because neither of them holds any text of its own. What differs is only what a block looks like.
//
// The document is written for reading away from the application - printed, or sent to an engineer who has
// no login - so it carries a cover, a table of contents Word fills in on opening, and page numbers.
// OpenXml is order-sensitive in ways that produce "unreadable content" rather than an exception, so every
// property list here is built through a helper that emits schema order.
// =============================================================

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AliantWebS.Manual;

public static class ManualWordWriter
{
    // ── Page geometry, in twentieths of a point ──────────────────
    private const int PageWidth = 11910;      // A4 portrait
    private const int PageHeight = 16840;
    private const int MarginSide = 1100;
    private const int MarginTop = 1100;
    private const int MarginBottom = 1100;

    /// <summary>Usable width, which every table grid must add up to.</summary>
    private const int ContentWidth = PageWidth - 2 * MarginSide;

    private const string BodyFont = "Calibri";
    private const string CodeFont = "Consolas";

    private const string NoteShade = "FFF4CE";     // the note box, as the page paints it
    private const string CodeShade = "F2F4F7";
    private const string HeaderShade = "E8EAF0";

    /// <summary>The manual as a Word file, written into <paramref name="stream"/>.</summary>
    public static void Write(ManualDocument manual, Stream stream, DateTime generatedOn)
    {
        using var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);

        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        BuildStyles(main.AddNewPart<StyleDefinitionsPart>());
        var footer = BuildFooter(main);

        AppendCover(body, manual, generatedOn);
        AppendContents(body);

        foreach (var block in manual.Blocks)
            Append(body, block);

        body.AppendChild(SectionProperties(main.GetIdOfPart(footer)));
        main.Document.Save();

        doc.PackageProperties.Title = manual.Title;
        doc.PackageProperties.Subject = "Optimiseur d'implantation — Antarès";
        doc.PackageProperties.Creator = "AliantWebS";
        doc.PackageProperties.Created = generatedOn;
        doc.PackageProperties.Modified = generatedOn;
    }

    /// <summary>The file name the browser is offered, dated so two exports do not collide.</summary>
    public static string FileName(DateTime generatedOn) =>
        $"Manuel-optimiseur-implantation-{generatedOn:yyyy-MM-dd}.docx";

    // =========================================================================
    // Blocks
    // =========================================================================

    private static void Append(Body body, ManualBlock block)
    {
        switch (block.Kind)
        {
            case ManualBlockKind.Title:
                // Printed on the cover already: a second copy here would open the document twice.
                break;

            case ManualBlockKind.Chapter:
                body.AppendChild(Heading(block.Text, "Titre1", pageBreak: true));
                break;

            case ManualBlockKind.Section:
                body.AppendChild(Heading(block.Text, "Titre2", pageBreak: false));
                break;

            case ManualBlockKind.SubSection:
                body.AppendChild(Heading(block.Text, "Titre3", pageBreak: false));
                break;

            case ManualBlockKind.Paragraph:
                body.AppendChild(Body(block.Text));
                break;

            case ManualBlockKind.Bullets:
                foreach (var item in block.Items)
                    body.AppendChild(ListItem("•\t", item));
                break;

            case ManualBlockKind.Steps:
                for (int i = 0; i < block.Items.Count; i++)
                    body.AppendChild(ListItem($"{i + 1}.\t", block.Items[i]));
                break;

            case ManualBlockKind.Note:
                body.AppendChild(Note(block.Text));
                break;

            case ManualBlockKind.Code:
                AppendCode(body, block.Text);
                break;

            case ManualBlockKind.Table:
                if (block.Rows.Count > 0) body.AppendChild(Table(block.Rows));
                break;
        }
    }

    private static Paragraph Heading(string text, string styleId, bool pageBreak)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = styleId });

        // Each chapter starts its page, as a manual read on paper expects.
        if (pageBreak) properties.AppendChild(new PageBreakBefore());

        var paragraph = new Paragraph(properties);
        foreach (var run in ManualDocument.Runs(text)) paragraph.AppendChild(Run(run, 0));
        return paragraph;
    }

    private static Paragraph Body(string text)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corps" },
            new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Both }));

        foreach (var run in ManualDocument.Runs(text)) paragraph.AppendChild(Run(run, 0));
        return paragraph;
    }

    private static Paragraph ListItem(string marker, string text)
    {
        // A hanging indent rather than a numbering definition: the marker is written as text, so the list
        // cannot be renumbered by Word into something the page does not show.
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corps" },
            new SpacingBetweenLines { After = "60", Line = "276", LineRule = LineSpacingRuleValues.Auto },
            new Indentation { Left = "360", Hanging = "360" }));

        paragraph.AppendChild(Run(new ManualRun(marker, false, false, false), 0));
        foreach (var run in ManualDocument.Runs(text)) paragraph.AppendChild(Run(run, 0));
        return paragraph;
    }

    private static Paragraph Note(string text)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corps" },
            new ParagraphBorders(
                new LeftBorder { Val = BorderValues.Single, Size = 18, Color = "E0A800", Space = 8 }),
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = NoteShade },
            new SpacingBetweenLines { Before = "120", After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto },
            new Indentation { Left = "200", Right = "200" }));

        foreach (var run in ManualDocument.Runs(text)) paragraph.AppendChild(Run(run, 0));
        return paragraph;
    }

    private static void AppendCode(Body body, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var paragraph = new Paragraph(new ParagraphProperties(
                new ParagraphStyleId { Val = "Corps" },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeShade },
                new SpacingBetweenLines
                {
                    Before = i == 0 ? "120" : "0",
                    After = i == lines.Length - 1 ? "120" : "0",
                    Line = "240",
                    LineRule = LineSpacingRuleValues.Auto,
                },
                new Indentation { Left = "200", Right = "200" }));

            // Every space kept: these blocks hold formulas and small diagrams that are aligned by hand.
            var run = new Run(new RunProperties(
                new RunFonts { Ascii = CodeFont, HighAnsi = CodeFont, ComplexScript = CodeFont },
                new FontSize { Val = "18" },
                new FontSizeComplexScript { Val = "18" }));
            run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });

            paragraph.AppendChild(run);
            body.AppendChild(paragraph);
        }
    }

    private static Table Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        int columns = Math.Max(1, rows.Max(r => r.Count));
        int width = ContentWidth / columns;

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = ContentWidth.ToString(), Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "B8BEC9" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "B8BEC9" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "B8BEC9" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "B8BEC9" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "B8BEC9" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "B8BEC9" }),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TableCellLeftMargin { Width = 80, Type = TableWidthValues.Dxa },
                new TableCellRightMargin { Width = 80, Type = TableWidthValues.Dxa })));

        var grid = new TableGrid();
        for (int c = 0; c < columns; c++) grid.AppendChild(new GridColumn { Width = width.ToString() });
        table.AppendChild(grid);

        for (int r = 0; r < rows.Count; r++)
        {
            bool header = r == 0;
            var row = new TableRow(new TableRowProperties(new CantSplit()));

            // The header repeats on every page a long table runs over: a lexicon without its columns
            // named is unreadable from page two.
            if (header) row.TableRowProperties!.AppendChild(new TableHeader());

            for (int c = 0; c < columns; c++)
            {
                var cellProperties = new TableCellProperties(
                    new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });

                if (header)
                    cellProperties.AppendChild(new Shading
                    {
                        Val = ShadingPatternValues.Clear,
                        Color = "auto",
                        Fill = HeaderShade,
                    });

                cellProperties.AppendChild(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

                var paragraph = new Paragraph(new ParagraphProperties(
                    new ParagraphStyleId { Val = "Corps" },
                    new SpacingBetweenLines { Before = "40", After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto }));

                var text = c < rows[r].Count ? rows[r][c] : "";
                foreach (var run in ManualDocument.Runs(text)) paragraph.AppendChild(Run(run, header ? 1 : 0));

                row.AppendChild(new TableCell(cellProperties, paragraph));
            }

            table.AppendChild(row);
        }

        return table;
    }

    /// <summary>A run of a line. <paramref name="force"/> 1 makes it bold whatever the author wrote.</summary>
    private static Run Run(ManualRun run, int force)
    {
        var properties = new RunProperties();

        if (run.Code)
            properties.AppendChild(new RunFonts { Ascii = CodeFont, HighAnsi = CodeFont, ComplexScript = CodeFont });

        if (run.Bold || force == 1)
        {
            properties.AppendChild(new Bold());
            properties.AppendChild(new BoldComplexScript());
        }

        if (run.Italic)
        {
            properties.AppendChild(new Italic());
            properties.AppendChild(new ItalicComplexScript());
        }

        if (run.Code)
        {
            properties.AppendChild(new Color { Val = "1F3864" });
            properties.AppendChild(new FontSize { Val = "19" });
            properties.AppendChild(new FontSizeComplexScript { Val = "19" });
        }

        var result = new Run(properties);
        result.AppendChild(new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
        return result;
    }

    // =========================================================================
    // Cover, contents, footer, section
    // =========================================================================

    private static void AppendCover(Body body, ManualDocument manual, DateTime generatedOn)
    {
        body.AppendChild(Centred(manual.Title, size: 44, bold: true, before: 2600, after: 200));
        body.AppendChild(Centred("Optimiseur d'implantation — Antarès", size: 26, bold: false, before: 0, after: 600));
        body.AppendChild(Centred(
            "Document produit par l'application, à partir du code en service. "
            + $"Édition du {generatedOn:dd/MM/yyyy}.",
            size: 20, bold: false, before: 0, after: 0));
    }

    private static Paragraph Centred(string text, int size, bool bold, int before, int after)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corps" },
            new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() },
            new Justification { Val = JustificationValues.Center }));

        var properties = new RunProperties();
        if (bold)
        {
            properties.AppendChild(new Bold());
            properties.AppendChild(new BoldComplexScript());
        }
        properties.AppendChild(new FontSize { Val = size.ToString() });
        properties.AppendChild(new FontSizeComplexScript { Val = size.ToString() });

        var run = new Run(properties);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
        return paragraph;
    }

    private static void AppendContents(Body body)
    {
        var heading = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Titre1" }, new PageBreakBefore()));
        heading.AppendChild(new Run(new Text("Sommaire")));
        body.AppendChild(heading);

        // A real TOC field rather than a list written out: the manual gains a chapter and the contents
        // follow.
        //
        // NOT MARKED DIRTY. A field Word has to update when the document opens is what makes it ask « ce
        // document contient des champs qui peuvent faire référence à d'autres fichiers » - a question this
        // manual has no business raising, since its only field is this one and the page number below, both
        // computed from the document itself. The reader who wants the contents presses Ctrl+A then F9, and
        // the line below says so. The page numbers in the footer need nothing: Word recomputes those every
        // time it lays the document out.
        var toc = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Corps" }));

        toc.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));
        toc.AppendChild(new Run(new FieldCode(" TOC \\o \"1-2\" \\h \\z \\u ")
        {
            Space = SpaceProcessingModeValues.Preserve,
        }));
        toc.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
        toc.AppendChild(new Run(new Text("Pour afficher le sommaire : Ctrl+A, puis F9.")));
        toc.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

        body.AppendChild(toc);
    }

    private static FooterPart BuildFooter(MainDocumentPart main)
    {
        var part = main.AddNewPart<FooterPart>();

        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Corps" },
            new Justification { Val = JustificationValues.Center }));

        var properties = new RunProperties(
            new Color { Val = "6B7280" },
            new FontSize { Val = "18" },
            new FontSizeComplexScript { Val = "18" });

        paragraph.AppendChild(new Run(properties.CloneNode(true), new Text("Manuel de l'optimiseur — page ")
        {
            Space = SpaceProcessingModeValues.Preserve,
        }));
        paragraph.AppendChild(new Run(properties.CloneNode(true),
            new FieldChar { FieldCharType = FieldCharValues.Begin }));
        paragraph.AppendChild(new Run(properties.CloneNode(true),
            new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }));
        paragraph.AppendChild(new Run(properties.CloneNode(true),
            new FieldChar { FieldCharType = FieldCharValues.End }));

        part.Footer = new Footer(paragraph);
        part.Footer.Save();
        return part;
    }

    private static SectionProperties SectionProperties(string footerId) => new(
        new FooterReference { Type = HeaderFooterValues.Default, Id = footerId },
        new PageSize { Width = PageWidth, Height = PageHeight },
        new PageMargin
        {
            Top = MarginTop,
            Right = (uint)MarginSide,
            Bottom = MarginBottom,
            Left = (uint)MarginSide,
            Header = 567,
            Footer = 567,
            Gutter = 0,
        });

    // =========================================================================
    // Styles
    // =========================================================================

    private static void BuildStyles(StyleDefinitionsPart part)
    {
        var styles = new Styles();

        styles.AppendChild(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, ComplexScript = BodyFont },
                new FontSize { Val = "21" },
                new FontSizeComplexScript { Val = "21" },
                new Languages { Val = "fr-FR" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
            StyleName = new StyleName { Val = "Normal" },
            PrimaryStyle = new PrimaryStyle(),
        });

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Corps",
            StyleName = new StyleName { Val = "Corps de texte" },
            BasedOn = new BasedOn { Val = "Normal" },
            PrimaryStyle = new PrimaryStyle(),
        });

        // Named "heading 1" and "heading 2" on purpose: a TOC field built with \o collects by heading style,
        // so the chapters and sections of the manual are what the contents page lists.
        styles.AppendChild(HeadingStyle("Titre1", "heading 1", level: 0, size: 30, colour: "1F3864",
            before: 0, after: 200));
        styles.AppendChild(HeadingStyle("Titre2", "heading 2", level: 1, size: 25, colour: "2E5AAC",
            before: 280, after: 120));
        styles.AppendChild(HeadingStyle("Titre3", "heading 3", level: 2, size: 22, colour: "374151",
            before: 200, after: 80));

        part.Styles = styles;
        part.Styles.Save();
    }

    private static Style HeadingStyle(string id, string name, int level, int size, string colour,
        int before, int after) => new()
    {
        Type = StyleValues.Paragraph,
        StyleId = id,
        StyleName = new StyleName { Val = name },
        BasedOn = new BasedOn { Val = "Normal" },
        NextParagraphStyle = new NextParagraphStyle { Val = "Corps" },
        PrimaryStyle = new PrimaryStyle(),
        StyleParagraphProperties = new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() },
            new OutlineLevel { Val = level }),
        StyleRunProperties = new StyleRunProperties(
            new Bold(),
            new BoldComplexScript(),
            new Color { Val = colour },
            new FontSize { Val = size.ToString() },
            new FontSizeComplexScript { Val = size.ToString() }),
    };
}
