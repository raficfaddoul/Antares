using System.Buffers.Binary;
using AliantWebS.Reporting;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

namespace Shared.Tests.Report;

/// <summary>
/// The study's three figures as the document now holds them: real shapes, not pictures.
///
/// These are the rules a picture could never be asked. What a figure SHOWS is WordStudyTests' subject and
/// ReportFigureDrawer's: the same geometry, the same axes, the same labels, the same legend. What is asked
/// here is of the medium - that the beam letters are text the document carries rather than pixels somebody
/// would have to read off a screenshot, that nothing is drawn outside the frame the figure declares, that
/// no two drawings claim the same id, and that the package still validates. A figure emitted out of schema
/// order does not throw: it opens as « contenu illisible », which is why the validator runs here.
///
/// The documents are written into a folder of the system temp directory, once for the whole class, and
/// removed afterwards. Nothing here touches a database or a scenario.
/// </summary>
public class FigureTests : IClassFixture<FigureTests.Drawn>
{
    private readonly Drawn _drawn;

    public FigureTests(Drawn drawn) => _drawn = drawn;

    // ═══════════════════════════════════════════════════════════════
    // Shapes, not pictures
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void The_three_figures_of_the_study_are_drawings_not_pictures()
    {
        using var doc = _drawn.Open();

        foreach (var name in Drawn.FigureNames)
        {
            var figure = Figure(doc, name);
            Assert.True(figure is not null, $"la figure « {name} » n'est pas dans le document");

            Assert.True(figure!.Descendants<WPG.WordprocessingGroup>().Count() == 1,
                $"la figure « {name} » ne porte pas un groupe de formes : ce n'est pas un dessin");

            // a:blip is the reference to an image part. A figure that still carries one is still a picture,
            // whatever else the document gained beside it.
            Assert.True(!figure.Descendants<A.Blip>().Any(),
                $"la figure « {name} » renvoie encore à une image");
        }
    }

    [Fact]
    public void Drawing_the_figures_adds_no_image_to_the_package()
    {
        // The same study written twice - once with the three scenes, once with nothing at all - must hold
        // the same images. Whatever the cover and the banner embed, the figures embed nothing: that is the
        // weight the document stops carrying, and the reason its text stays crisp at any zoom.
        using var drawn = _drawn.Open();
        using var plain = _drawn.OpenPlain();

        int withFigures = ImageCount(drawn), without = ImageCount(plain);
        Assert.True(withFigures == without,
            $"le document dessiné contient {withFigures} image(s) contre {without} pour le même document "
            + "sans figures : les figures sont encore embarquées comme images");

        Assert.True(GroupCount(plain) == 0,
            "le document sans scènes ne devrait porter aucun groupe de formes");
        Assert.True(GroupCount(drawn) == Drawn.FigureNames.Length,
            $"{GroupCount(drawn)} figure(s) dessinée(s) au lieu de {Drawn.FigureNames.Length}");
    }

    // ═══════════════════════════════════════════════════════════════
    // The package Word opens
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Word_would_open_it_the_package_validates()
    {
        // Every shape is assembled by hand, and OpenXml is order-sensitive in ways that produce a document
        // Word offers to repair rather than an exception: a:spPr whose children are transposed, wps:bodyPr
        // put before the text box it must follow. Nothing but the schema catches those.
        using var doc = _drawn.Open();

        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();

        Assert.True(errors.Count == 0,
            $"{errors.Count} erreur(s) de schéma OOXML : "
            + string.Join(" || ", errors.Take(8).Select(e => $"{e.Description} @ {e.Path?.XPath}")));
    }

    // ═══════════════════════════════════════════════════════════════
    // The figures' text is in the document
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void The_beam_letters_and_the_point_numbers_are_text_in_the_document()
    {
        // The charpente the figure frames, named. A reader searching for E5 in the study finds the figure;
        // with a picture there was nothing to find, and no test could tell a plan drawn in the studies'
        // axes from one drawn upside down.
        var texts = _drawn.TextsOf(Drawn.ImplantationName);

        Assert.True(texts.Contains("E"),
            "la lettre de la poutre E, qui porte l'origine Point0, n'est pas un texte de la figure");
        Assert.True(texts.Contains("5"),
            "le numéro du point 5, qui porte l'origine Point0, n'est pas un texte de la figure");
    }

    [Fact]
    public void The_axes_of_the_studies_are_text_in_the_document()
    {
        // X towards point 9 and Y towards beam J, from the origin at Point0: the two letters at the ends of
        // the arrows are what say which axes the plan is drawn in.
        var texts = _drawn.TextsOf(Drawn.ImplantationName);

        Assert.True(texts.Contains("X"), "l'axe X n'est pas nommé dans la figure");
        Assert.True(texts.Contains("Y"), "l'axe Y n'est pas nommé dans la figure");
    }

    [Fact]
    public void A_loads_name_and_its_weight_are_one_selectable_label()
    {
        // Name and charge in one label, as the layout places them, with the two spaces that separate them.
        // A test can now read off the figure what until today only the table could be asked.
        var texts = _drawn.TextsOf(Drawn.ImplantationName);

        Assert.True(texts.Contains("MOTEUR AVANT  500 daN"),
            "la charge et son poids ne sont pas un texte de la figure : "
            + string.Join(" | ", texts.Where(t => t.Contains("daN"))));
    }

    [Fact]
    public void A_charge_nobody_weighed_shows_an_em_dash_in_the_figure_never_zero_daN()
    {
        // The rule Tableau 1 obeys, obeyed by the drawing as well - and now readable there. A charge
        // weighed at nothing and a charge nobody weighed are different facts.
        var texts = _drawn.TextsOf(Drawn.ImplantationName);

        Assert.True(texts.Contains("NON PESÉ  — daN"),
            "une charge non pesée doit porter un tiret cadratin dans la figure : "
            + string.Join(" | ", texts.Where(t => t.Contains("daN"))));
        Assert.True(!texts.Contains("NON PESÉ  0 daN"),
            "la figure annonce 0 daN pour une charge que personne n'a pesée");
    }

    [Fact]
    public void Every_label_the_layout_placed_reaches_the_document_as_text()
    {
        // The whole of it, not a sample: the caption, the beam letters, the point numbers, the load names,
        // the axes, the scale bar and the legend. The scene is what the layout decided; the document must
        // carry exactly that, nothing dropped and nothing invented.
        foreach (var (name, scene) in _drawn.Scenes)
        {
            var placed = scene.Primitives.OfType<FigureLabel>().Select(l => l.Text).ToList();
            var written = _drawn.TextsOf(name);

            // Taken one for one: a label placed twice must be written twice, and what is left over at the
            // end is text the document carries that the layout never asked for.
            var pool = new List<string>(written);
            var missing = new List<string>();
            foreach (var text in placed)
                if (!pool.Remove(text)) missing.Add(text);

            Assert.True(missing.Count == 0,
                $"la figure « {name} » ne porte pas comme texte : {string.Join(" | ", missing.Take(6))}");
            Assert.True(pool.Count == 0,
                $"la figure « {name} » porte un texte que la mise en page n'a pas placé : "
                + string.Join(" | ", pool.Take(6)));
        }
    }

    [Fact]
    public void A_load_the_plan_does_not_place_is_not_drawn_and_is_not_named()
    {
        // (0,00 ; 0,00) is Point0 - E5 - which is a real place a load can hang. A load whose position
        // nobody knows must leave the drawing altogether: drawn at the origin it would read as a load hung
        // on E5, and named beside it, as a load somebody located. Both figures that carry loads are asked.
        foreach (var name in new[] { Drawn.PlanName, Drawn.ImplantationName })
        {
            var texts = _drawn.TextsOf(name);

            Assert.True(!texts.Any(t => t.Contains(Drawn.UnplacedLoad)),
                $"« {Drawn.UnplacedLoad} », que le plan ne situe pas, est nommée dans « {name} »");

            // The control: the loads the plan does place are there, so the absence above comes from the
            // missing position and not from the figure having dropped every label.
            Assert.True(texts.Any(t => t.StartsWith("MOTEUR AVANT")),
                $"les charges situées doivent, elles, être nommées dans « {name} »");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Where the shapes are
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Every_shape_sits_inside_its_figures_box()
    {
        // A figure declares its size once, in wp:extent, and the page is laid out around that. A shape at a
        // negative offset, or one reaching past the extent, is drawn over the text of the study - and a
        // whole figure at negative offsets is what a mistaken coordinate system looks like.
        //
        // A LABEL'S BOX IS PADDED ON PURPOSE - made wider and taller than the glyphs the layout measured, so
        // that Word never has to wrap or clip them - and no ink is ever put in that padding. So a text box
        // is allowed to hang its own box's height past an edge; everything that draws is not.
        using var doc = _drawn.Open();

        foreach (var name in Drawn.FigureNames)
        {
            var figure = Figure(doc, name);
            Assert.True(figure is not null, $"la figure « {name} » n'est pas dans le document");

            var extent = figure!.Descendants<DW.Extent>().Single();
            long boxWidth = extent.Cx!.Value, boxHeight = extent.Cy!.Value;

            foreach (var shape in figure.Descendants<WPS.WordprocessingShape>())
            {
                var transform = shape.Descendants<A.Transform2D>().First();
                long x = transform.Offset!.X!.Value, y = transform.Offset!.Y!.Value;
                long cx = transform.Extents!.Cx!.Value, cy = transform.Extents!.Cy!.Value;

                var label = shape.GetFirstChild<WPS.NonVisualDrawingProperties>()?.Name?.Value ?? "?";
                var slack = shape.GetFirstChild<WPS.TextBoxInfo2>() is not null ? cy : Rounding;

                Assert.True(x >= -slack && y >= -slack,
                    $"« {label} » de la figure « {name} » est placée en dehors du cadre, à ({x} ; {y}) EMU");
                Assert.True(cx >= 0 && cy >= 0,
                    $"« {label} » de la figure « {name} » a une étendue négative ({cx} × {cy}) EMU");
                Assert.True(x + cx <= boxWidth + slack && y + cy <= boxHeight + slack,
                    $"« {label} » de la figure « {name} » déborde du cadre : elle finit à "
                    + $"({x + cx} ; {y + cy}) EMU pour un cadre de ({boxWidth} ; {boxHeight}) EMU");
            }
        }
    }

    [Fact]
    public void A_figures_group_scales_nothing_so_its_labels_stay_on_their_anchors()
    {
        // A group whose child extent differs from its extent is a group Word scales - and it scales the
        // shapes but NOT the text inside them, so every label would drift off the point it names while the
        // figure still looked plausible. The fit happens once, before the shapes are written; the group
        // must be the identity. This is the one rule here whose breach the reader would never notice.
        using var doc = _drawn.Open();

        foreach (var name in Drawn.FigureNames)
        {
            var figure = Figure(doc, name);
            Assert.True(figure is not null, $"la figure « {name} » n'est pas dans le document");

            var extent = figure!.Descendants<DW.Extent>().Single();
            var group = figure.Descendants<A.TransformGroup>().Single();

            Assert.True(group.Offset!.X!.Value == 0 && group.Offset!.Y!.Value == 0,
                $"le groupe de « {name} » n'est pas à l'origine de son cadre");
            Assert.True(group.ChildOffset!.X!.Value == 0 && group.ChildOffset!.Y!.Value == 0,
                $"les formes de « {name} » sont décalées dans le groupe");
            Assert.True(
                group.Extents!.Cx!.Value == group.ChildExtents!.Cx!.Value
                && group.Extents!.Cy!.Value == group.ChildExtents!.Cy!.Value,
                $"le groupe de « {name} » redimensionne ses formes : les textes ne suivraient pas");
            Assert.True(
                group.Extents!.Cx!.Value == extent.Cx!.Value
                && group.Extents!.Cy!.Value == extent.Cy!.Value,
                $"le groupe de « {name} » n'occupe pas le cadre déclaré par wp:extent");
        }
    }

    [Fact]
    public void No_two_drawings_of_the_document_share_an_id()
    {
        // wp:docPr names a drawing to the whole document, and the body, the headers and the footers all
        // name into the same series: a repeated one is one of the ways Word answers « contenu illisible »
        // rather than with an error. A figure takes one, whatever it holds, and the cover's pictures and
        // the banner take theirs from the same counter.
        using var doc = _drawn.Open();

        var ids = DocPrIds(doc);
        var repeated = ids.GroupBy(i => i.Id).Where(g => g.Count() > 1).ToList();

        Assert.True(repeated.Count == 0,
            $"{repeated.Count} identifiant(s) de dessin en double : "
            + string.Join(" | ", repeated.Take(5).Select(g =>
                $"{g.Key} dans {string.Join(" et ", g.Select(i => i.Where).Distinct())}")));
    }

    [Fact]
    public void No_two_shapes_of_one_drawing_share_an_id()
    {
        // Inside a drawing the ids name the shapes to one another, so what has to be unique is unique
        // within the drawing - and a figure of four hundred shapes takes four hundred ids, which is
        // exactly the kind of pressure a per-drawing counter would fail under. It is a different series
        // from wp:docPr's: Word itself writes pic:cNvPr id="0" in every picture it embeds, so counting
        // the two together would call a correct document broken.
        using var doc = _drawn.Open();

        int shapes = 0;

        foreach (var (where, drawing) in Drawings(doc))
        {
            var ids = drawing.Descendants<WPS.NonVisualDrawingProperties>()
                .Where(p => p.Id is not null).Select(p => p.Id!.Value)
                .Concat(drawing.Descendants<PIC.NonVisualDrawingProperties>()
                    .Where(p => p.Id is not null).Select(p => p.Id!.Value))
                .ToList();
            shapes += ids.Count;

            var repeated = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            Assert.True(repeated.Count == 0,
                $"dessin « {where} » : {repeated.Count} identifiant(s) de forme en double "
                + $"({string.Join(", ", repeated.Take(5))})");
        }

        // The control: a document whose figures were not emitted at all would have no duplicate either.
        Assert.True(shapes > 100,
            $"seulement {shapes} forme(s) identifiée(s) : les figures ne sont pas dans le document");
    }

    // ═══════════════════════════════════════════════════════════════
    // What the study says it is about to show
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_study_with_no_figure_announces_none()
    {
        // Every lead-in belongs INSIDE the test that writes what it announces. A sentence saying the
        // pre-rig "sera le suivant" over a page with no figure on it sends a reader hunting for a
        // drawing the document does not contain, and a heading on its own reads as a figure that failed
        // to print rather than as one that was never drawn.
        using var plain = _drawn.OpenPlain();
        var text = plain.MainDocumentPart!.Document.Body!.InnerText;

        foreach (var announcement in new[]
                 {
                     "Le pré rigg pour cette implantation sera le suivant",
                     "sera implanté de cette manière",
                     "Le plan de charges de la production est le suivant",
                 })
        {
            Assert.True(!text.Contains(announcement),
                $"l'étude sans figure annonce quand même « {announcement} »");
        }
    }

    [Fact]
    public void A_study_with_no_attachment_point_load_does_not_announce_a_table()
    {
        // « Le tableau suivant donne la charge sur les points d'accroche » over no table at all is the
        // same failure in the section that matters most: the reader is told the figures they came for
        // are just below.
        using var plain = _drawn.OpenPlain();
        var text = plain.MainDocumentPart!.Document.Body!.InnerText;

        Assert.True(!text.Contains("Le tableau suivant donne la charge"),
            "l'étude annonce le tableau des points d'accroche alors qu'elle n'en contient aucun");
        Assert.Contains("Aucune charge n’a été calculée sur les points d’accroche", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // A figure with nothing hung on it, and the files beside the study
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_pre_rig_carrying_no_load_at_all_is_still_drawn_and_the_study_still_written()
    {
        // A pre-rig is put up before the production's charges are known, and that study is written too.
        // The figure is then the trusses and the hall alone - it must still be a figure, and it must not
        // announce a charge of any kind.
        var file = new FileInfo(_drawn.NoLoadDocxPath);
        Assert.True(file.Exists && file.Length > 4000,
            $"l'étude sans charge n'a pas été écrite : {file.FullName}");

        using var doc = WordprocessingDocument.Open(_drawn.NoLoadDocxPath, false);

        var figure = Figure(doc, Drawn.PreRigName);
        Assert.True(figure is not null, "le pré rigg sans charge n'est pas dessiné");
        Assert.True(figure!.Descendants<WPG.WordprocessingGroup>().Count() == 1,
            "le pré rigg sans charge n'est pas un groupe de formes");

        var texts = TextsOf(figure!);
        Assert.True(texts.Contains("E"), "la figure sans charge ne porte plus la charpente");
        Assert.True(!texts.Any(t => t.Contains("daN")),
            $"la figure sans charge annonce une charge : {string.Join(" | ", texts.Where(t => t.Contains("daN")))}");

        // And the study around it says so, rather than announcing a table it does not print.
        Assert.Contains(
            doc.MainDocumentPart!.Document.Body!.Descendants<W.Paragraph>().Select(p => p.InnerText),
            t => t.Contains("Aucune charge n’est enregistrée dans le plan de charges"));
    }

    [Fact]
    public void The_PNG_files_are_still_written_beside_the_study()
    {
        // The scenario keeps the figures in its output folder, and the plan of charges is one the user
        // downloads. The document stopped embedding them; nothing stopped producing them, and they are
        // still the 1 600 px the folder has always held - eight pixels to the millimetre of a 200 mm page.
        Assert.True(_drawn.PreRigDrawn, "figure 5, le pré rigg, doit être écrite en PNG");
        Assert.True(_drawn.ImplantationDrawn, "figure 6, l'implantation, doit être écrite en PNG");
        Assert.True(_drawn.LoadPlanDrawn, "figure 4, le plan de charges, doit être écrit en PNG");

        foreach (var path in _drawn.PngPaths)
        {
            Assert.True(File.Exists(path), $"aucun fichier PNG à {path}");

            var width = PngWidth(path);
            Assert.True(width == 1600,
                $"{Path.GetFileName(path)} fait {width} px de large au lieu de 1 600");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Reading a document
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// EMU of slack allowed on a shape that draws ink. A corner is rounded from its own millimetre and its
    /// box from another, so two lengths that meet on paper can end a unit apart; an EMU is a
    /// thirty-six-thousandth of a millimetre, and nothing is visible at that size.
    /// </summary>
    private const long Rounding = 2;

    /// <summary>The drawing whose wp:docPr carries this name, which is the caption the builder gave it.</summary>
    private static W.Drawing? Figure(WordprocessingDocument doc, string name) =>
        doc.MainDocumentPart!.Document.Body!.Descendants<W.Drawing>()
            .FirstOrDefault(d => d.Descendants<DW.DocProperties>().Any(p => p.Name?.Value == name));

    /// <summary>Every text a figure's shapes carry, one entry per label, in the order they are drawn.</summary>
    private static List<string> TextsOf(W.Drawing figure) =>
        figure.Descendants<WPS.WordprocessingShape>()
            .Select(s => s.InnerText)
            .Where(t => t.Length > 0)
            .ToList();

    private static int GroupCount(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document.Body!.Descendants<WPG.WordprocessingGroup>().Count();

    /// <summary>Every image the package holds, wherever it hangs: the body's, the headers', the footers'.</summary>
    private static int ImageCount(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart!;
        var containers = new List<OpenXmlPartContainer> { main };
        containers.AddRange(main.HeaderParts);
        containers.AddRange(main.FooterParts);

        return containers.Sum(c => c.Parts.Count(p => p.OpenXmlPart is ImagePart));
    }

    /// <summary>
    /// Every wp:docPr of the document, with where it was found: the one id that has to be unique across
    /// the body, the headers and the footers at once.
    /// </summary>
    private static List<(string Where, uint Id)> DocPrIds(WordprocessingDocument doc) =>
        Roots(doc)
            .SelectMany(r => r.Root.Descendants<DW.DocProperties>()
                .Where(p => p.Id is not null)
                .Select(p => (r.Where, p.Id!.Value)))
            .ToList();

    /// <summary>Every drawing of the document, named by its wp:docPr where it has one.</summary>
    private static List<(string Where, W.Drawing Drawing)> Drawings(WordprocessingDocument doc) =>
        Roots(doc)
            .SelectMany(r => r.Root.Descendants<W.Drawing>()
                .Select(d => (
                    d.Descendants<DW.DocProperties>().FirstOrDefault()?.Name?.Value ?? r.Where,
                    d)))
            .ToList();

    /// <summary>The body and every header and footer: a drawing lives in any of them.</summary>
    private static List<(string Where, OpenXmlElement Root)> Roots(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart!;
        var roots = new List<(string Where, OpenXmlElement? Root)> { ("le corps", main.Document.Body) };
        foreach (var header in main.HeaderParts) roots.Add(("un en-tête", header.Header));
        foreach (var footer in main.FooterParts) roots.Add(("un pied de page", footer.Footer));

        return roots.Where(r => r.Root is not null).Select(r => (r.Where, r.Root!)).ToList();
    }

    /// <summary>The width a PNG declares in its header, which is the only part of it worth reading here.</summary>
    private static int PngWidth(string path)
    {
        var head = new byte[24];
        using (var stream = File.OpenRead(path)) stream.ReadExactly(head, 0, head.Length);

        Assert.True(head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G',
            $"{Path.GetFileName(path)} n'est pas un PNG");

        return BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16, 4));
    }

    // ═══════════════════════════════════════════════════════════════
    // The pre-rig, its scenes and the documents: built once, read by every test
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// One pre-rig laid out around Point0, its three scenes, the PNGs of those scenes, and the documents
    /// written from them. Shared by the whole class because writing a .docx is the expensive part; every
    /// test only reads it, so no test can affect another.
    /// </summary>
    public sealed class Drawn : IDisposable
    {
        // The names the builder gives the drawings, which is how a figure is found in the document.
        public const string PlanName = "Plan de son et lumière de la production";
        public const string PreRigName = "Mother + pré rigg";
        public const string ImplantationName = "Implantation du plan de la production dans la salle";

        public static readonly string[] FigureNames = { PlanName, PreRigName, ImplantationName };

        /// <summary>The charge the plan gives no position, which no figure may draw or name.</summary>
        public const string UnplacedLoad = "SANS POSITION";

        // The pre-rig, in the studies' axes, metres from Point0 = E5. The beams are horizontal lines of
        // constant Y - E is the origin's own, F and D its neighbours - and the point lines cross them at a
        // constant X. Everything here straddles the origin on purpose: the axes are drawn only when it is
        // inside the frame, and it is the beam letter and the point number the tests read back.
        private const double BeamF = 7.156, BeamE = 0, BeamD = -7.144;
        private const double Point3 = -7.4, Point4 = -3.7, Point6 = 3.7, Point7 = 7.45;

        private readonly string _temp = Path.Combine(
            Path.GetTempPath(), nameof(FigureTests), Guid.NewGuid().ToString("N"));

        /// <summary>The three scenes, by the name the document knows each figure under.</summary>
        public readonly List<(string Name, FigureScene Scene)> Scenes = new();

        /// <summary>The study whose three figures are drawn, and the same study with no figure at all.</summary>
        public readonly string DocxPath, PlainDocxPath, NoLoadDocxPath;

        public readonly string[] PngPaths;
        public readonly bool PreRigDrawn, ImplantationDrawn, LoadPlanDrawn;

        private readonly Dictionary<string, List<string>> _texts = new();

        public Drawn()
        {
            Directory.CreateDirectory(_temp);

            // ── The pre-rig and the production's charges ──────────
            var trusses = new List<FigureTruss>
            {
                new(Point3, BeamE, Point7, BeamE, "S500", null, IsMother: true, Label: "S500"),
                new(Point3, BeamF, Point7, BeamF, "S500", null, IsMother: true, Label: "S500"),
                new(Point4, BeamD, Point4, BeamF, "P300", null, IsMother: false, Label: "P300"),
                new(Point6, BeamD, Point6, BeamF, "P300", null, IsMother: false, Label: "P300"),
            };

            var loads = new List<ProductionLoad>
            {
                new("MOTEUR AVANT", Point4, 3.5, 500),
                new("MOTEUR ARRIÈRE", Point6, 3.5, 350),
                new("NON PESÉ", Point6, -3.5, 0, Weighed: false),
                new(UnplacedLoad, 0, 0, 120, Located: false),
            };

            // ── The scenes, and the PNGs of the same scenes ───────
            // Point0 is left to its default, E5, so the origin of the studies' axes is the venue's own.
            var plan = ReportFigureDrawer.SceneOfLoadPlan(loads)!;
            var preRig = ReportFigureDrawer.SceneOfPreRig(trusses)!;
            var implantation = ReportFigureDrawer.SceneOfImplantation(trusses, loads)!;

            Scenes.Add((PlanName, plan));
            Scenes.Add((PreRigName, preRig));
            Scenes.Add((ImplantationName, implantation));

            var planPng = Path.Combine(_temp, "figure-plan-charges.png");
            var preRigPng = Path.Combine(_temp, "figure-pre-rigg.png");
            var implantationPng = Path.Combine(_temp, "figure-implantation.png");
            PngPaths = new[] { planPng, preRigPng, implantationPng };

            LoadPlanDrawn = ReportFigureDrawer.DrawLoadPlan(loads, planPng);
            PreRigDrawn = ReportFigureDrawer.DrawPreRig(trusses, preRigPng);
            ImplantationDrawn = ReportFigureDrawer.DrawImplantation(trusses, loads, implantationPng);

            // ── The documents ────────────────────────────────────
            var context = new ReportContext
            {
                ShowName = "ESSAI FIGURES",
                ShowDate = new DateTime(2026, 9, 20),
                IssueDate = new DateTime(2026, 9, 12),
                Author = "Shared.Tests",
            };
            var venue = new VenueSpec { Beams = ReportDataBuilder.Beams, Points = ReportDataBuilder.Points };

            RigReport Report(IReadOnlyList<ProductionLoad> charges) => new()
            {
                Context = context,
                Venue = venue,
                Loads = charges,
                AppliedLoadsKnown = true,
                PalonnierCount = 2,
            };

            // No FigureSet anywhere in these: a figure that appears here appears because a scene was
            // handed over, never because a file was found on disk.
            DocxPath = Write("etude-figures.docx", Report(loads), new FigureScenes
            {
                ProductionPlan = plan,
                PreRig = preRig,
                Implantation = implantation,
            });

            // The control for what a drawn figure costs the package: the same study, no scenes.
            PlainDocxPath = Write("etude-sans-figures.docx", Report(loads), scenes: null);

            // A pre-rig put up before anybody weighed anything.
            NoLoadDocxPath = Write(
                "etude-sans-charge.docx",
                Report(Array.Empty<ProductionLoad>()),
                new FigureScenes { PreRig = ReportFigureDrawer.SceneOfPreRig(trusses)! });

            // Read back once. A figure that is not in the document at all leaves no text rather than
            // failing the fixture, so it is the rule about that figure that reports it and not every rule.
            using (var doc = Open())
            {
                foreach (var (name, _) in Scenes)
                {
                    _texts[name] = Figure(doc, name) is { } figure
                        ? FigureTests.TextsOf(figure)
                        : new List<string>();
                }
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(_temp, recursive: true); }
            catch (IOException) { /* a file still held open must not fail a passing test */ }
        }

        /// <summary>The study with the three drawn figures, opened read-only and afresh for each rule.</summary>
        public WordprocessingDocument Open() => WordprocessingDocument.Open(DocxPath, false);

        /// <summary>The same study with no figure at all: what the figures are measured against.</summary>
        public WordprocessingDocument OpenPlain() => WordprocessingDocument.Open(PlainDocxPath, false);

        /// <summary>Every text one of the three figures carries, read back once from the written document.</summary>
        public List<string> TextsOf(string name) => _texts[name];

        private string Write(string fileName, RigReport report, FigureScenes? scenes)
        {
            var path = Path.Combine(_temp, fileName);
            WordReportBuilder.Write(report, path, scenes);
            return path;
        }
    }
}
