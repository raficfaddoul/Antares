using AliantWebS.Manual;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// The optimiser's manual and the small Markdown it is written in. The text is read twice - on the page a
/// developer opens and in the Word file that page exports - and neither reading parses Markdown itself, so
/// everything either of them can get wrong is decided here. The parser is pinned on a sample that uses every
/// block it knows, and then the manual as it really ships is read: a manual that shows a reader its own
/// markup, or that loses a sentence between the two readings, is worse than no manual.
/// </summary>
public class ManualTests
{
    /// <summary>The fence that opens and closes a verbatim block, spelled once as the author types it.</summary>
    private const string Fence = "```";

    /// <summary>
    /// One sample using every block the parser knows, written the way an author writes it: headings on four
    /// levels, a paragraph over two lines, bullets with a wrapped item, numbered steps, a table with its
    /// rule row, a quoted note, a fenced block and emphasis inside a line.
    /// </summary>
    private static readonly string Sample = string.Join("\n", new[]
    {
        "# Titre du manuel",
        "",
        "## Chapitre 1 — Le début",
        "Un paragraphe écrit",
        "sur deux lignes.",
        "",
        "### 1.1 Une section",
        "#### Une sous-section",
        "- premier point",
        "- deuxième point",
        "  qui continue à la ligne",
        "",
        "1. première étape",
        "2. deuxième étape",
        "",
        "| Colonne A | Colonne B |",
        "|---|---|",
        "| a1 | b1 |",
        "| a2 | b2 |",
        "",
        "> Une note que le lecteur",
        "> ne doit pas manquer.",
        "",
        Fence,
        "  M = P.L/4 + p.L²/8",
        "     aligné à la main",
        Fence,
        "",
        "Un mot **en gras**, un mot *en italique* et du `code` en ligne.",
    });

    /// <summary>The sample read once. A parsed document is immutable, so every test may share it.</summary>
    private static readonly ManualDocument Parsed = ManualDocument.Parse(Sample);

    /// <summary>The manual as it ships, taken from the assembly exactly as the page takes it.</summary>
    private static readonly ManualDocument Shipped = ManualDocument.Load();

    // ── The Markdown subset the parser accepts ───────────────────────────────

    [Fact]
    public void The_title_is_the_single_hash_heading()
    {
        Assert.Equal("Titre du manuel", Parsed.Title);
        Assert.Contains(Parsed.Blocks, b => b.Kind == ManualBlockKind.Title);
    }

    [Fact]
    public void A_chapter_a_section_and_a_sub_section_are_told_apart()
    {
        // Three heading levels, three kinds: the Word export gives each its own style and the page its own
        // size, so a chapter read as a section would print in the wrong place in both.
        var kinds = Parsed.Blocks.Select(b => b.Kind).ToList();
        Assert.Contains(ManualBlockKind.Chapter, kinds);
        Assert.Contains(ManualBlockKind.Section, kinds);
        Assert.Contains(ManualBlockKind.SubSection, kinds);
    }

    [Fact]
    public void Two_lines_of_one_paragraph_become_one_paragraph()
    {
        // Prose is wrapped in the .md file for the person who edits it. Those line breaks are not the
        // author's sentences and must not become paragraphs of their own.
        var paragraphs = Parsed.Blocks
            .Where(b => b.Kind == ManualBlockKind.Paragraph)
            .Select(b => b.Text)
            .ToList();

        Assert.True(paragraphs.Contains("Un paragraphe écrit sur deux lignes."),
            "deux lignes séparées par un retour doivent former un seul paragraphe ; lus : "
            + string.Join(" | ", paragraphs));
    }

    [Fact]
    public void Bullets_keep_their_items_and_a_wrapped_line_joins_the_item_above()
    {
        var bullets = Assert.Single(Parsed.Blocks.Where(b => b.Kind == ManualBlockKind.Bullets));

        Assert.Equal(2, bullets.Items.Count);
        Assert.Equal("premier point", bullets.Items[0]);

        // An indented continuation belongs to the item above it, never to a paragraph of its own.
        Assert.Equal("deuxième point qui continue à la ligne", bullets.Items[1]);
    }

    [Fact]
    public void Numbered_steps_are_a_list_of_their_own()
    {
        // A procedure is not a bullet list: the reader follows it in order, and the numbers are printed by
        // the renderer, so the author's own numbering is dropped here.
        var steps = Assert.Single(Parsed.Blocks.Where(b => b.Kind == ManualBlockKind.Steps));
        Assert.Equal(new[] { "première étape", "deuxième étape" }, steps.Items);
    }

    [Fact]
    public void A_table_keeps_its_header_and_its_rows_and_drops_the_rule_under_the_header()
    {
        var table = Assert.Single(Parsed.Blocks.Where(b => b.Kind == ManualBlockKind.Table));

        // Header + two rows. The |---|---| rule carries no content - it only says the row above it was a
        // header - so a renderer must never be handed it as data.
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Colonne A", table.Rows[0][0]);
        Assert.Equal("b2", table.Rows[2][1]);
    }

    [Fact]
    public void A_note_joins_its_lines()
    {
        var note = Assert.Single(Parsed.Blocks.Where(b => b.Kind == ManualBlockKind.Note));
        Assert.Equal("Une note que le lecteur ne doit pas manquer.", note.Text);
    }

    [Fact]
    public void A_fenced_block_keeps_every_space_it_was_written_with()
    {
        // Fenced blocks hold formulas and small diagrams aligned by hand; trimming them would print
        // nonsense on both the page and the Word file.
        var code = Assert.Single(Parsed.Blocks.Where(b => b.Kind == ManualBlockKind.Code));

        Assert.StartsWith("  M = P.L/4", code.Text);
        Assert.Contains("\n     aligné", code.Text);
    }

    [Fact]
    public void Bold_italic_and_code_are_read_inside_a_line()
    {
        var runs = ManualDocument.Runs("Un mot **en gras**, un mot *en italique* et du `code` en ligne.");
        var shown = string.Join(" | ", runs.Select(r =>
            $"{r.Text}[{(r.Bold ? "b" : "")}{(r.Italic ? "i" : "")}{(r.Code ? "c" : "")}]"));

        Assert.True(runs.Any(r => r.Bold && r.Text == "en gras"), "le gras n'est pas lu : " + shown);
        Assert.True(runs.Any(r => r.Italic && r.Text == "en italique"), "l'italique n'est pas lu : " + shown);
        Assert.True(runs.Any(r => r.Code && r.Text == "code"), "le code en ligne n'est pas lu : " + shown);
    }

    [Fact]
    public void The_emphasis_marks_themselves_are_never_printed()
    {
        // Plain() is what a place that can carry no emphasis prints - a table of contents, a bookmark - so
        // a mark that survives it reaches the reader as the star or the backtick it is.
        var plain = ManualDocument.Plain("Un **gras** et du `code`");

        Assert.DoesNotContain("*", plain);
        Assert.DoesNotContain("`", plain);
        Assert.Equal("Un gras et du code", plain);
    }

    [Fact]
    public void The_contents_list_holds_the_chapters_and_the_sections_in_order()
    {
        // Sub-sections are deliberately out: the contents would be longer than the chapter it lists.
        Assert.Equal(2, Parsed.Contents.Count);
        Assert.Equal(ManualBlockKind.Chapter, Parsed.Contents[0].Kind);
        Assert.Equal(ManualBlockKind.Section, Parsed.Contents[1].Kind);

        Assert.All(Parsed.Contents, c => Assert.False(string.IsNullOrEmpty(c.Anchor),
            $"« {c.Text} » n'a pas d'ancre : le sommaire de la page ne peut pas y renvoyer"));
    }

    [Fact]
    public void An_unclosed_fenced_block_still_yields_its_lines()
    {
        // A fence the author forgot to close must not swallow the rest of the manual in silence: the
        // failure a reader can see is a block printed verbatim, never a page that stops mid-chapter.
        var unclosed = ManualDocument.Parse("## Chapitre\n\n" + Fence + "\nune ligne\n");

        Assert.True(unclosed.Blocks.Any(b => b.Kind == ManualBlockKind.Code && b.Text.Contains("une ligne")),
            "une clôture de bloc oubliée ne doit pas faire disparaître le texte qui suit");
    }

    // ── The manual as it ships ───────────────────────────────────────────────

    [Fact]
    public void The_embedded_manual_is_found()
    {
        // The .md is an EmbeddedResource of Shared: a rename of the file or of its folder changes the
        // resource name, and the page then shows its own "introuvable" paragraph instead of the manual.
        var lost = Shipped.Blocks.FirstOrDefault(b => b.Text.Contains("introuvable dans l'assemblage"));

        Assert.True(lost is null,
            "le manuel n'est plus embarqué dans Shared.dll (EmbeddedResource ScenarioHelpers/Manual/*.md) : "
            + lost?.Text);
    }

    [Fact]
    public void The_embedded_manual_has_chapters()
    {
        var chapters = Shipped.Blocks.Count(b => b.Kind == ManualBlockKind.Chapter);

        Assert.True(chapters >= 1,
            $"le manuel ne contient aucun chapitre (« ## … ») : {Shipped.Blocks.Count} bloc(s) lu(s)");
    }

    [Fact]
    public void No_paragraph_carries_markup_the_parser_did_not_understand()
    {
        // Nothing must reach a reader as raw Markdown. A stray heading mark, a table pipe, a link or a bit
        // of HTML in a finished paragraph means the author and the parser disagree about the format, and
        // the safe failure - printing the line as it is - has already happened.
        var strays = Shipped.Blocks
            .Where(b => b.Kind is ManualBlockKind.Paragraph or ManualBlockKind.Note)
            .Where(b => b.Text.StartsWith('#') || b.Text.StartsWith('|') || b.Text.Contains("](")
                        || b.Text.Contains("<br") || b.Text.Contains("&nbsp"))
            .Select(b => b.Text.Length > 90 ? b.Text[..90] + "…" : b.Text)
            .ToList();

        Assert.True(strays.Count == 0,
            $"{strays.Count} paragraphe(s) portent une syntaxe que le parseur ne comprend pas : "
            + string.Join(" || ", strays.Take(5)));
    }

    [Fact]
    public void Every_code_span_in_the_manual_is_closed()
    {
        // An odd number of backticks on a line is an unclosed span: the page prints the backtick, and the
        // Word export prints the rest of the sentence in the code font.
        var unbalanced = Shipped.Blocks
            .Where(b => b.Kind is ManualBlockKind.Paragraph or ManualBlockKind.Note or ManualBlockKind.Bullets)
            .SelectMany(b => b.Items.Count > 0 ? b.Items : new List<string> { b.Text })
            .Where(t => t.Count(c => c == '`') % 2 == 1)
            .ToList();

        Assert.True(unbalanced.Count == 0,
            "ligne(s) avec un ` non refermé : " + string.Join(" || ", unbalanced.Take(3)));
    }
}
