using AliantWebS.Manual;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// The manual as the page exports it: a Word file a person opens, prints and hands to someone who will never
/// see the application. Nothing else in the build opens that file, so it is written here once and read back
/// - a .docx Word refuses, or one that quietly drops a chapter or a table, would only be discovered by the
/// reader it was written for.
/// </summary>
public class ManualWordTests : IClassFixture<ManualWordTests.Export>
{
    private readonly Export _export;

    public ManualWordTests(Export export) => _export = export;

    /// <summary>
    /// The manual written to disk once for the whole class, into a folder of its own under the system temp
    /// folder, and deleted with it. Nothing here is modified afterwards: every test opens the file read-only,
    /// so the tests may run in any order.
    /// </summary>
    public sealed class Export : IDisposable
    {
        /// <summary>A fixed date, so two runs write the same bytes and a diff means a real change.</summary>
        private static readonly DateTime GeneratedOn = new(2026, 9, 12);

        /// <summary>Named after the test class, made unique so two runs never share or delete one file.</summary>
        private static string ClassFolder => Path.Combine(Path.GetTempPath(), nameof(ManualWordTests));

        public string Folder { get; }
        public string DocxPath { get; }

        /// <summary>The manual the file was written from: what the file is checked against.</summary>
        public ManualDocument Manual { get; } = ManualDocument.Load();

        public Export()
        {
            Folder = Path.Combine(ClassFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            DocxPath = Path.Combine(Folder, "manuel-optimiseur.docx");

            using var file = File.Create(DocxPath);
            ManualWordWriter.Write(Manual, file, GeneratedOn);
        }

        /// <summary>The written file, opened read-only and afresh for each rule that reads it.</summary>
        public WordprocessingDocument Open() => WordprocessingDocument.Open(DocxPath, false);

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // And the class's folder itself, when this was the last run using it. DirectoryNotFoundException
            // is an IOException, so the first clause already covers a folder another run has removed.
            try { Directory.Delete(ClassFolder); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Body BodyOf(WordprocessingDocument doc) => doc.MainDocumentPart!.Document.Body!;

    private static List<string> ParagraphTexts(WordprocessingDocument doc) =>
        BodyOf(doc).Descendants<Paragraph>().Select(p => p.InnerText).ToList();

    [Fact]
    public void The_export_writes_a_file()
    {
        var file = new FileInfo(_export.DocxPath);

        Assert.True(file.Exists, $"rien n'a été écrit dans {_export.DocxPath}");
        Assert.True(file.Length > 4000,
            $"le fichier ne fait que {file.Length} octets : un manuel complet en fait bien davantage");
    }

    [Fact]
    public void Word_would_open_it_the_package_validates()
    {
        // The writer builds the OOXML by hand. A package Word refuses opens on nothing at all, which is
        // the one failure a reader cannot work around.
        using var doc = _export.Open();

        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();

        Assert.True(errors.Count == 0,
            $"{errors.Count} erreur(s) de schéma OOXML : "
            + string.Join(" || ", errors.Take(8).Select(e => $"{e.Description} @ {e.Path?.XPath}")));
    }

    [Fact]
    public void The_cover_carries_the_title()
    {
        using var doc = _export.Open();

        Assert.True(ParagraphTexts(doc).Any(t => t.Contains(_export.Manual.Title)),
            $"la page de garde ne porte pas le titre du manuel : « {_export.Manual.Title} »");
    }

    [Fact]
    public void A_table_of_contents_field_is_there()
    {
        // Word fills the contents itself, from the headings, when the reader updates the fields. Without
        // the TOC field there is nothing to fill, and a manual of this length needs one.
        using var doc = _export.Open();

        Assert.True(BodyOf(doc).Descendants<FieldCode>().Any(f => f.Text.Contains("TOC")),
            "aucun champ TOC : le document n'aura pas de sommaire");
    }

    [Fact]
    public void Page_numbers_are_in_the_footer()
    {
        // The manual is meant to be printed and referred to aloud; unnumbered pages cannot be quoted.
        using var doc = _export.Open();

        Assert.True(
            doc.MainDocumentPart!.FooterParts.Any(f =>
                f.Footer.Descendants<FieldCode>().Any(c => c.Text.Contains("PAGE"))),
            "aucun champ PAGE dans un pied de page : le manuel imprimé n'est pas paginé");
    }

    [Fact]
    public void Every_chapter_is_a_heading_in_the_Word_file()
    {
        // A chapter written as body text is invisible to the contents field and to Word's navigation pane:
        // the chapter is in the file, and the reader still cannot find it.
        using var doc = _export.Open();

        var headings = BodyOf(doc).Descendants<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value is "Titre1" or "Titre2")
            .Select(p => p.InnerText)
            .ToList();

        var absent = _export.Manual.Blocks
            .Where(b => b.Kind == ManualBlockKind.Chapter)
            .Select(b => ManualDocument.Plain(b.Text))
            .Where(chapter => !headings.Any(h => h.Contains(chapter)))
            .ToList();

        Assert.True(absent.Count == 0,
            $"{absent.Count} chapitre(s) ne sont pas des titres dans le .docx, donc absents du sommaire : "
            + string.Join(" | ", absent.Take(3)));
    }

    [Fact]
    public void Every_paragraph_of_the_manual_reaches_the_Word_file()
    {
        // What the page shows and what the file prints must be the same sentences. Only the substantial
        // paragraphs are checked, on their opening words: a renderer is free to split a sentence into runs,
        // but never free to leave it out.
        using var doc = _export.Open();
        var docText = string.Join("\n", ParagraphTexts(doc));

        var missing = _export.Manual.Blocks
            .Where(b => b.Kind == ManualBlockKind.Paragraph)
            .Select(b => ManualDocument.Plain(b.Text))
            .Where(t => t.Length > 40 && !docText.Contains(t[..40]))
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} paragraphe(s) du manuel n'arrivent pas dans le .docx : "
            + string.Join(" || ", missing.Take(3).Select(m => m[..Math.Min(80, m.Length)])));
    }

    [Fact]
    public void Every_table_of_the_manual_reaches_the_Word_file()
    {
        // The tables carry the figures a rigger reads off the manual - one missing table is a rule lost.
        using var doc = _export.Open();

        var inFile = BodyOf(doc).Descendants<Table>().Count();
        var inManual = _export.Manual.Blocks.Count(b => b.Kind == ManualBlockKind.Table);

        Assert.True(inFile == inManual,
            $"{inFile} tableau(x) dans le fichier, {inManual} dans le manuel");
    }
}
