// =============================================================
// ManualDocument.cs — The optimiser's manual, as a document two renderers can print.
//
// The manual is prose, so it lives as prose: a Markdown file embedded in the assembly
// (ScenarioHelpers/Manual/OptimizerManual.md), edited like text rather than like code. It is read twice -
// on the page a developer opens, and in the Word file that page exports - and those two readings must never
// drift apart, so neither of them parses Markdown itself. This class does it once, into blocks, and each
// renderer only decides how a block looks.
//
// The Markdown accepted here is deliberately small: headings, paragraphs, bullets, numbered steps, tables,
// fenced blocks and a quoted note, with bold / italic / code inside a line. Anything else is printed as the
// text it is, which is the safe failure: a document that looks plain, never one that loses a sentence.
// =============================================================

using System.Text;

namespace AliantWebS.Manual;

public enum ManualBlockKind
{
    Title,
    Chapter,
    Section,
    SubSection,
    Paragraph,
    Bullets,
    Steps,
    Table,
    Code,
    Note,
}

/// <summary>A stretch of text inside a line, with the emphasis the author gave it.</summary>
public readonly record struct ManualRun(string Text, bool Bold, bool Italic, bool Code);

/// <summary>
/// One block of the manual. <see cref="Text"/> carries the content of a heading, a paragraph, a note or a
/// fenced block; <see cref="Items"/> the lines of a list; <see cref="Rows"/> the rows of a table, the first
/// being its header.
/// </summary>
public sealed class ManualBlock
{
    public ManualBlockKind Kind { get; init; }
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>Set on chapters and sections: what the page's contents list links to.</summary>
    public string Anchor { get; init; } = "";
}

public sealed class ManualDocument
{
    /// <summary>The resource holding the manual, named as the compiler embeds it.</summary>
    private const string ResourceName = "Shared.ScenarioHelpers.Manual.OptimizerManual.md";

    public string Title { get; }
    public IReadOnlyList<ManualBlock> Blocks { get; }

    /// <summary>Chapters and sections, in order: what the page prints as its contents.</summary>
    public IReadOnlyList<ManualBlock> Contents { get; }

    private ManualDocument(string title, IReadOnlyList<ManualBlock> blocks)
    {
        Title = title;
        Blocks = blocks;
        Contents = blocks
            .Where(b => b.Kind is ManualBlockKind.Chapter or ManualBlockKind.Section)
            .ToList();
    }

    /// <summary>
    /// The manual as it ships. Embedded rather than read from disk so a deployment cannot lose it, and so the
    /// page and the Word export are always the same text.
    /// </summary>
    public static ManualDocument Load()
    {
        var assembly = typeof(ManualDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            // Never throw at a page: an absent resource is a build problem, and saying so on the page is
            // more useful than a circuit error nobody can read.
            return new ManualDocument("Manuel de l'optimiseur", new List<ManualBlock>
            {
                new()
                {
                    Kind = ManualBlockKind.Paragraph,
                    Text = "Le texte du manuel est introuvable dans l'assemblage (ressource « " + ResourceName
                         + " »). Il est embarqué à la compilation : vérifiez que le fichier "
                         + "ScenarioHelpers/Manual/OptimizerManual.md est bien déclaré en EmbeddedResource.",
                },
            });
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the Markdown subset. Public so a test can feed it text without an assembly.</summary>
    public static ManualDocument Parse(string markdown)
    {
        var blocks = new List<ManualBlock>();
        var title = "Manuel de l'optimiseur";

        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // What is being accumulated between blank lines: a paragraph's lines, a list's items, a table's rows.
        var paragraph = new List<string>();
        var bullets = new List<string>();
        var steps = new List<string>();
        var note = new List<string>();
        var table = new List<IReadOnlyList<string>>();
        var code = new List<string>();
        bool inCode = false;
        int anchors = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new ManualBlock { Kind = ManualBlockKind.Paragraph, Text = string.Join(" ", paragraph) });
            paragraph.Clear();
        }

        void FlushBullets()
        {
            if (bullets.Count == 0) return;
            blocks.Add(new ManualBlock { Kind = ManualBlockKind.Bullets, Items = bullets.ToList() });
            bullets.Clear();
        }

        void FlushSteps()
        {
            if (steps.Count == 0) return;
            blocks.Add(new ManualBlock { Kind = ManualBlockKind.Steps, Items = steps.ToList() });
            steps.Clear();
        }

        void FlushNote()
        {
            if (note.Count == 0) return;
            blocks.Add(new ManualBlock { Kind = ManualBlockKind.Note, Text = string.Join(" ", note) });
            note.Clear();
        }

        void FlushTable()
        {
            if (table.Count == 0) return;
            blocks.Add(new ManualBlock { Kind = ManualBlockKind.Table, Rows = table.ToList() });
            table.Clear();
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushBullets();
            FlushSteps();
            FlushNote();
            FlushTable();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            // A fenced block keeps every space it was written with - it holds formulas and small diagrams -
            // so it is read before anything else and nothing inside it is interpreted.
            if (line.TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                if (inCode)
                {
                    blocks.Add(new ManualBlock { Kind = ManualBlockKind.Code, Text = string.Join("\n", code) });
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    FlushAll();
                    inCode = true;
                }
                continue;
            }

            if (inCode)
            {
                code.Add(raw.TrimEnd());
                continue;
            }

            if (line.Trim().Length == 0)
            {
                FlushAll();
                continue;
            }

            if (line.StartsWith("#### ", StringComparison.Ordinal))
            {
                FlushAll();
                blocks.Add(new ManualBlock { Kind = ManualBlockKind.SubSection, Text = line[5..].Trim() });
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushAll();
                anchors++;
                blocks.Add(new ManualBlock
                {
                    Kind = ManualBlockKind.Section,
                    Text = line[4..].Trim(),
                    Anchor = "m" + anchors,
                });
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushAll();
                anchors++;
                blocks.Add(new ManualBlock
                {
                    Kind = ManualBlockKind.Chapter,
                    Text = line[3..].Trim(),
                    Anchor = "m" + anchors,
                });
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushAll();
                title = line[2..].Trim();
                blocks.Add(new ManualBlock { Kind = ManualBlockKind.Title, Text = title });
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal) || line.Trim() == ">")
            {
                FlushParagraph();
                FlushBullets();
                FlushSteps();
                FlushTable();
                note.Add(line.TrimStart('>').Trim());
                continue;
            }

            if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushSteps();
                FlushNote();
                FlushTable();
                bullets.Add(line.TrimStart()[2..].Trim());
                continue;
            }

            if (IsStep(line, out var step))
            {
                FlushParagraph();
                FlushBullets();
                FlushNote();
                FlushTable();
                steps.Add(step);
                continue;
            }

            if (line.TrimStart().StartsWith("|", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushBullets();
                FlushSteps();
                FlushNote();

                var cells = SplitRow(line);

                // The rule under a header row carries no content: it only says the row above it was one.
                // Dropped here so a renderer never has to know about it.
                if (cells.Count > 0 && cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':' or ' ')))
                    continue;

                table.Add(cells);
                continue;
            }

            // A line that continues a list belongs to its last item, not to a paragraph of its own.
            if (raw.StartsWith("  ", StringComparison.Ordinal) && bullets.Count > 0)
            {
                bullets[^1] = bullets[^1] + " " + line.Trim();
                continue;
            }

            if (raw.StartsWith("  ", StringComparison.Ordinal) && steps.Count > 0)
            {
                steps[^1] = steps[^1] + " " + line.Trim();
                continue;
            }

            FlushBullets();
            FlushSteps();
            FlushNote();
            FlushTable();
            paragraph.Add(line.Trim());
        }

        // A fence left open by a typo would otherwise swallow the end of the manual in silence.
        if (inCode && code.Count > 0)
            blocks.Add(new ManualBlock { Kind = ManualBlockKind.Code, Text = string.Join("\n", code) });
        FlushAll();

        return new ManualDocument(title, blocks);
    }

    /// <summary>The fence that opens and closes a verbatim block, spelled once.</summary>
    private static readonly string Fence = new('`', 3);

    /// <summary>"3. Ouvrir le classeur" — a numbered step, whatever number the author typed.</summary>
    private static bool IsStep(string line, out string text)
    {
        text = "";
        var s = line.TrimStart();
        int i = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i == 0 || i + 1 >= s.Length || s[i] != '.' || s[i + 1] != ' ') return false;
        text = s[(i + 2)..].Trim();
        return true;
    }

    private static List<string> SplitRow(string line)
    {
        var s = line.Trim();
        if (s.StartsWith('|')) s = s[1..];
        if (s.EndsWith('|')) s = s[..^1];
        return s.Split('|').Select(c => c.Trim()).ToList();
    }

    /// <summary>
    /// A line cut into the runs a renderer prints: bold, italic and code spans. Shared by the page and the
    /// Word export, so one sentence cannot come out emphasised in one and plain in the other.
    /// </summary>
    public static IReadOnlyList<ManualRun> Runs(string? text)
    {
        var runs = new List<ManualRun>();
        var s = text ?? "";
        var buffer = new StringBuilder();
        bool bold = false, italic = false;

        void Flush()
        {
            if (buffer.Length == 0) return;
            runs.Add(new ManualRun(buffer.ToString(), bold, italic, false));
            buffer.Clear();
        }

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    runs.Add(new ManualRun(s[(i + 1)..end], bold, italic, true));
                    i = end;
                    continue;
                }
            }

            if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }

            if (s[i] == '*')
            {
                Flush();
                italic = !italic;
                continue;
            }

            buffer.Append(s[i]);
        }

        Flush();
        return runs;
    }

    /// <summary>The line without its marks, for a place that cannot carry any - a table of contents.</summary>
    public static string Plain(string? text) => string.Concat(Runs(text).Select(r => r.Text));
}
