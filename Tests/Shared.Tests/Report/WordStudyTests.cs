using AliantWebS.Models;
using AliantWebS.Reporting;
using DocumentFormat.OpenXml.Packaging;
using Helpers;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Shared.Tests.Report;

/// <summary>
/// The study as it is handed over: written to a .docx and read back paragraph by paragraph.
///
/// These are the sentences a rigger and a signing engineer read, so they are pinned word for word. A study
/// that prints a figure without saying where it comes from, that prints 0 daN for a charge nobody weighed,
/// or that says "correctement dimensionné" about a check it could not carry out, is worse than no study:
/// it is a document that looks like a verification and is not one.
///
/// The document is written into a folder of the system temp directory, once for the whole class, and
/// removed afterwards. Nothing here touches a database or a scenario.
/// </summary>
public class WordStudyTests : IClassFixture<WordStudyTests.Study>
{
    private readonly Study _study;

    public WordStudyTests(Study study) => _study = study;

    // ═══════════════════════════════════════════════════════════════
    // The figures
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void The_three_figures_of_the_study_are_drawn_in_the_studies_axes()
    {
        // Figures 4, 5 and 6 of the hand-written studies. The drawer is given coordinates already converted
        // to the studies' axes, and Point0 places the charpente grid under them.
        Assert.True(_study.PreRigDrawn, "figure 5, le pré rigg, doit être dessinée");
        Assert.True(_study.ImplantationDrawn, "figure 6, l'implantation, doit être dessinée");
        Assert.True(_study.LoadPlanDrawn, "figure 4, le plan de charges, doit être dessinée");
    }

    // ═══════════════════════════════════════════════════════════════
    // Opening the document
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Word_opens_the_study_without_asking_about_other_files()
    {
        // The study once carried UpdateFieldsOnOpen, so that its contents page and page counts were right
        // on opening. Word answers that setting with a modal question - « ce document contient des champs
        // qui peuvent faire référence à d'autres fichiers » - which tells the reader of a signed structural
        // study that it might be pulling figures from somewhere else. It never did: every field in it is
        // computed from the document itself. The refresh is asked for field by field instead.
        using var doc = WordprocessingDocument.Open(_study.DocxPath, false);

        var settings = doc.MainDocumentPart!.DocumentSettingsPart?.Settings;
        Assert.NotNull(settings);
        Assert.Empty(settings!.Descendants<W.UpdateFieldsOnOpen>());
    }

    [Fact]
    public void No_field_asks_Word_to_refresh_itself_when_the_document_is_opened()
    {
        // Removing UpdateFieldsOnOpen was not enough: marking each field dirty asks Word for the same
        // thing - update these on opening - and raises the same modal question. Measured, not reasoned:
        // the same study with every w:dirty stripped opens silently. So no field carries it, in the body
        // or in a header, and the cached results below are what the reader actually sees.
        using var doc = WordprocessingDocument.Open(_study.DocxPath, false);
        var main = doc.MainDocumentPart!;

        var roots = new List<DocumentFormat.OpenXml.OpenXmlElement> { main.Document };
        roots.AddRange(main.HeaderParts.Select(h => (DocumentFormat.OpenXml.OpenXmlElement)h.Header));
        roots.AddRange(main.FooterParts.Select(f => (DocumentFormat.OpenXml.OpenXmlElement)f.Footer));

        var begins = roots
            .SelectMany(r => r.Descendants<W.FieldChar>())
            .Where(f => f.FieldCharType is not null
                        && f.FieldCharType.Value == W.FieldCharValues.Begin)
            .ToList();

        Assert.NotEmpty(begins);
        Assert.All(begins, b => Assert.True(b.Dirty?.Value != true,
            "aucun champ ne doit être marqué « dirty » : Word demanderait alors s'il peut les mettre à jour"));
    }

    [Fact]
    public void Every_caption_carries_its_own_number_without_being_refreshed()
    {
        // The consequence of the rule above, and the reason it costs nothing: the SEQ fields are counted
        // when the document is written and their number is stored as the field's own result. A reader who
        // never refreshes anything still reads « Figure 1 », « Figure 2 », « Tableau 1 » - in order, with
        // no gap where a figure was not drawn.
        using var doc = WordprocessingDocument.Open(_study.DocxPath, false);

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var paragraph in doc.MainDocumentPart!.Document.Body!.Descendants<W.Paragraph>())
        {
            var code = paragraph.Descendants<W.FieldCode>()
                .FirstOrDefault(c => c.Text.Contains("SEQ ", StringComparison.Ordinal));
            if (code is null) continue;

            var kind = code.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            seen[kind] = seen.TryGetValue(kind, out var n) ? n + 1 : 1;

            var runs = paragraph.Elements<W.Run>().ToList();
            var separator = runs.FindIndex(r => r.Descendants<W.FieldChar>()
                .Any(f => f.FieldCharType?.Value == W.FieldCharValues.Separate));
            var end = runs.FindIndex(r => r.Descendants<W.FieldChar>()
                .Any(f => f.FieldCharType?.Value == W.FieldCharValues.End));

            var cached = string.Concat(runs.Skip(separator + 1).Take(end - separator - 1)
                .SelectMany(r => r.Elements<W.Text>()).Select(t => t.Text));

            Assert.True(cached == seen[kind].ToString(),
                $"la légende « {kind} » nº {seen[kind]} porte le numéro « {cached} »");
        }

        Assert.True(seen.ContainsKey("Figure") && seen.ContainsKey("Tableau"),
            "l'étude n'a ni figure ni tableau numéroté : la règle ne prouve rien");
        Assert.True(seen["Figure"] > 1, $"une seule figure numérotée ({seen["Figure"]}) : la règle ne prouve rien");
    }

    [Fact]
    public void A_table_of_contents_says_how_to_fill_itself()
    {
        // The one thing not asking for a refresh costs: a TOC's page numbers are Word's own layout and
        // cannot be computed here. So the field's cached result is the instruction, rather than a blank
        // that reads as a contents page that failed to print.
        using var doc = WordprocessingDocument.Open(_study.DocxPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("Ctrl+A", text);
        Assert.Contains("F9", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // Tableau 1: the production's loads
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Tableau_1_gives_every_load_a_position_not_only_a_weight()
    {
        // A load list without coordinates cannot be checked against the plan by the person reading it.
        Assert.Contains("Moteur", _study.AllText);
        Assert.Contains("X (m)", _study.AllText);
        Assert.Contains("Y (m)", _study.AllText);
        Assert.Contains("Charge (daN)", _study.AllText);
        Assert.Contains("TOTAL", _study.AllText);

        Assert.All(_study.Loads, l =>
        {
            Assert.Contains(Study.M2(l.X), _study.AllText);
            Assert.Contains(Study.M2(l.Y), _study.AllText);
        });
    }

    [Fact]
    public void A_load_the_plan_does_not_place_prints_an_em_dash_never_a_position_on_E5()
    {
        var text = _study.Write("etude-charge-sans-position.docx", new RigReport
        {
            Context = _study.Context,
            Venue = _study.Venue,
            Loads = new List<ProductionLoad> { new("SANS", 0, 0, 120, Located: false) },
        });

        // (0,00 ; 0,00) is Point0 - E5 - which is a real place a load can hang. Printing it for a load whose
        // position nobody knows would put that load on a beam it was never on.
        Assert.Contains("—", text);
        Assert.DoesNotContain("0,00", text);
    }

    [Fact]
    public void A_load_nobody_weighed_prints_an_em_dash_never_zero_daN()
    {
        var text = _study.Write("etude-charge-sans-poids.docx", new RigReport
        {
            Context = _study.Context,
            Venue = _study.Venue,
            Loads = new List<ProductionLoad>
            {
                new("PESANT", 1.0, 2.0, 250),
                new("SANS POIDS", 3.0, 4.0, 0, Weighed: false),
            },
        });

        // A charge weighed at nothing and a charge nobody weighed are different facts, and the second one is
        // a check the study did not carry out.
        Assert.Contains("—", text);
        Assert.Contains("250", text);
        Assert.DoesNotContain("0", text);
    }

    [Fact]
    public void With_no_charge_at_all_the_study_says_so_instead_of_announcing_an_empty_table()
    {
        var text = _study.Write("etude-plan-vide.docx", new RigReport { Context = _study.Context, Venue = _study.Venue });

        Assert.Contains(text, t => t.Contains("Aucune charge n’est enregistrée dans le plan de charges"));
        Assert.DoesNotContain(text, t => t.Contains("Les charges de la production sont les suivantes"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Tableau 2 and the sentences the verification prints
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void The_study_says_how_a_mother_is_verified_before_it_verifies_one()
    {
        Assert.Contains(_study.Text, t => t.Contains(
            "Un mother est vérifié travée par travée : la somme des charges situées entre deux poutres voisines "
            + "est comparée à la charge ponctuelle au milieu que l’inventaire admet à la longueur immédiatement "
            + "supérieure à la travée."));
    }

    [Fact]
    public void A_value_read_from_a_row_says_which_length_it_was_read_at()
    {
        Assert.Contains(_study.Text, t => t.Contains(
            "D’après l’inventaire, une charge ponctuelle centrée de 2 286 daN est autorisée sur une longueur de 8,4 m."));
    }

    [Fact]
    public void A_computed_value_shows_the_row_it_starts_from_and_the_moment_it_keeps_equal()
    {
        // The reader must be able to redo the arithmetic: the row, its load, the own weight per metre, and
        // the equation. A number with no derivation cannot be signed for.
        Assert.Contains(_study.Text, t => t.Contains(
            "Cette portée dépasse la plus grande longueur de l’inventaire pour ce modèle (4 m, 900 daN) : la charge "
            + "ponctuelle centrée admise, 454 daN, en est déduite à moment égal (M = P·L/4 + p·L²/8, avec "
            + "p = 12,5 daN/m)."));
    }

    [Fact]
    public void A_span_nobody_can_justify_is_reported_as_unverified_not_as_passed()
    {
        Assert.Contains(_study.Text, t => t.Contains(
            "Portée de plus de 2 fois la plus grande longueur de l’inventaire (4 m) : la charge n’a pas pu être "
            + "vérifiée."));
    }

    [Fact]
    public void The_study_names_the_assumption_the_signing_engineer_has_to_accept()
    {
        Assert.Contains(_study.Text, t => t.Contains("hypothèse est acceptée par l’ingénieur signataire"));
    }

    [Fact]
    public void An_exceedance_is_listed_with_its_bay_and_says_when_its_value_was_computed()
    {
        Assert.Contains(_study.Text, t => t.Contains("travée F3–E3"));
        Assert.Contains(_study.Text, t => t.Contains("(valeur calculée)"));
    }

    [Fact]
    public void Tableau_2_lists_the_pieces_each_assembled_truss_is_made_of()
    {
        // What the crew has to build on the floor, under the table that counts the pieces.
        Assert.Contains(_study.Text, t => t.Contains("« S500 L=22,4m #1 » (S500) : 4 × 5,6 m."));
    }

    // ═══════════════════════════════════════════════════════════════
    // The study written from an inventory a row of which could not be read
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A_computed_value_never_claims_the_whole_inventory_when_a_row_was_not_read()
    {
        Assert.Contains(_study.UnreadText, t => t.Contains(
            "Cette portée dépasse la plus grande longueur lisible de l’inventaire pour ce modèle (4 m, 900 daN)"));
        Assert.DoesNotContain(_study.UnreadText, t => t.Contains(
            "Cette portée dépasse la plus grande longueur de l’inventaire"));
    }

    [Fact]
    public void The_row_that_could_not_be_read_is_named_right_after_the_value_it_affects()
    {
        // Next to the figure, not in an appendix: it is that figure the reader has to distrust.
        Assert.Contains(_study.UnreadText, t => t.Contains(
            "La ligne 3 de Palonnier.xlsx, de ce modèle, n’a pas pu être lue : cette valeur ne tient compte que "
            + "des lignes lisibles et reste à vérifier."));
    }

    [Fact]
    public void A_value_read_from_a_row_is_qualified_too_and_names_every_unread_row_of_its_model()
    {
        Assert.Contains(_study.UnreadText, t => t.Contains(
            "D’après les lignes lisibles de l’inventaire, une charge ponctuelle centrée de 2 286 daN est autorisée "
            + "sur une longueur de 8,4 m. Les lignes 4 et 5 de MotherGrill.xlsx, de ce modèle, n’ont pas pu être lues"));
    }

    [Fact]
    public void A_missing_value_is_qualified_by_the_row_that_could_not_be_read()
    {
        Assert.Contains(_study.UnreadText, t => t.Contains(
            "Portée de plus de 2 fois la plus grande longueur lisible de l’inventaire (4 m) ; la ligne 3 de "
            + "Palonnier.xlsx, de ce modèle, n’a pas pu être lue : la charge n’a pas pu être vérifiée."));
    }

    [Fact]
    public void An_exceedance_bullet_says_when_its_value_rests_on_an_unread_inventory()
    {
        Assert.Contains(_study.UnreadText, t => t.Contains("(la ligne 3 de Palonnier.xlsx, de ce modèle, n’a pas pu être lue)."));
    }

    [Fact]
    public void A_study_with_an_unread_inventory_row_never_concludes()
    {
        // An unperformed check is not a passed check. This is the sentence the whole document exists to
        // refuse to print when it cannot be justified.
        Assert.DoesNotContain(_study.UnreadText, t => t.Contains("correctement dimensionné"));
        Assert.Contains(_study.UnreadText, t => t.Contains("Cette étude n’est pas concluante"));
    }

    [Fact]
    public void A_check_that_passes_with_every_row_read_does_conclude()
    {
        // The control for the rule above: the refusal has to come from the unread row, not from the study
        // being unable to conclude at all.
        var text = _study.WritePassingStudy("etude-passe.docx", unread: false);

        Assert.Contains(text, t => t.Contains(
            "L’ensemble des palonniers du pré rigg utilisé pour ce plan est correctement dimensionné"));
    }

    [Fact]
    public void A_check_that_passes_without_one_of_its_rows_is_not_fully_verified()
    {
        var text = _study.WritePassingStudy("etude-passe-illisible.docx", unread: true);

        Assert.Contains(text, t => t.Contains("Le dimensionnement des palonniers n’a pas pu être entièrement vérifié pour : P300."));
        Assert.DoesNotContain(text, t => t.Contains("correctement dimensionné"));
    }

    // ═══════════════════════════════════════════════════════════════
    // The scenario, the figures and the documents: built once, read by every test
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// One pre-rig, its figures and the study written from it. Shared by the whole class because writing a
    /// .docx is the expensive part; every test only reads it, so no test can affect another.
    /// </summary>
    public sealed class Study : IDisposable
    {
        // The venue's axes, relative to Point0 = E5.
        private const double P0X = 33.103, P0Y = 0;
        private const double G = 18.697 - P0X, F = 25.947 - P0X, E = 0, D = 40.247 - P0X;
        private const double Row1 = 15.15, Row3 = 7.4, Row5 = 0, Row6 = -3.7;

        private readonly string _temp = Path.Combine(
            Path.GetTempPath(), nameof(WordStudyTests), Guid.NewGuid().ToString("N"));

        public readonly ReportContext Context;
        public readonly VenueSpec Venue;
        public readonly List<ProductionLoad> Loads;
        public readonly TrussInventory TrussBill;

        public readonly bool PreRigDrawn, ImplantationDrawn, LoadPlanDrawn;

        /// <summary>The study from the verification onwards: the sentences the check prints.</summary>
        public readonly List<string> Text;

        /// <summary>Every paragraph of the study, so that one table cell is one entry.</summary>
        public readonly List<string> AllText;

        /// <summary>The same study, written from an inventory whose Palonnier.xlsx line 3 could not be read.</summary>
        public readonly List<string> UnreadText;

        /// <summary>The study on disk, for the rules that are about the file rather than its words.</summary>
        public readonly string DocxPath;

        private readonly IReadOnlyList<SkippedRow> _unread;
        private readonly string _palonnierName;

        public Study()
        {
            Directory.CreateDirectory(_temp);

            // ── The pre-rig, built in code ────────────────────────
            var s500 = Guid.NewGuid();
            var p300 = Guid.NewGuid();
            var typeNames = new Dictionary<Guid, string> { [s500] = "S500", [p300] = "P300" };

            var motherRows = new List<BarSpec>
            {
                new() { Type = "S500", Length = 2.8, StockInit = 10, Stock = 10, Capacity = 3000, WeightKg = 80 },
                new() { Type = "S500", Length = 5.6, StockInit = 10, Stock = 10, Capacity = 2500, WeightKg = 160 },
                new() { Type = "S500", Length = 8.399999999999999, StockInit = 0, Stock = 0, Capacity = 2286, WeightKg = 240 },
            };
            var palonnierRows = new List<BarSpec>
            {
                new() { Type = "P300", Length = 4.0, StockInit = 6, Stock = 6, Capacity = 900, WeightKg = 50 },
            };
            var motherTable = LoadTable.From(motherRows);
            var palonnierTable = LoadTable.From(palonnierRows);
            var catalog = TrussCatalog.FromInventory(motherRows.Concat(palonnierRows), new List<string>());

            var mother = El("S500 L=22,4m #1", s500, true, G, Row3, D, Row3, "[560,560,560,560]");
            var palonnier = El("P300 L=12,0m #1", p300, false, -10.8, Row3, -10.8, Row6, "[400,400,400]");
            var longPalonnier = El("P300 L=16,0m #2", p300, false, -5.0, Row1, -5.0, Row5, "[400,400,400,400]");
            var oldMother = El("S500 L=8,4m #9", s500, true, F, -11.05, E, -11.05, null);
            var unreadableComposition = El("S500 L=2,8m #7", s500, true, -1.0, -15.151, 1.0, -15.151, "oops");
            var partial = El("S500 L=15,6m #5", s500, true, D, 3.7, D + 15.0, 3.7, "[999,560]");
            var elements = new List<MotherGridPalonnier>
            {
                mother, palonnier, longPalonnier, oldMother, unreadableComposition, partial,
            };
            _palonnierName = palonnier.Name!;

            var warnings = new List<string>();
            TrussBill = ReportDataBuilder.BuildTrussInventory(elements, typeNames, catalog, warnings);

            var chargeMps = new List<ChargeMp>
            {
                Charge("T1", palonnier, 3.0, 500),
                Charge("T2", palonnier, 5.0, 400),
                Charge("T3", palonnier, 9.0, 300),
                Charge("T4", longPalonnier, 5.0, 200),
            };
            var hangingPoints = new Dictionary<Guid, IReadOnlyList<double>>
            {
                [palonnier.MotherGridPalonnierId] = new List<double> { 0, 7.4, 11.1 },
                [longPalonnier.MotherGridPalonnierId] = new List<double> { 0, 15.15 },
            };
            var pointLoads = new Dictionary<Guid, List<(double X, double Y, double Load)>>
            {
                [mother.MotherGridPalonnierId] = new()
                {
                    (-10.8, Row3, 1500),
                    (-3.5, Row3, 2500),         // F3–E3, over 2 286
                    (E + 0.005, Row3, 999),
                    (3.6, Row3, 800),
                    (5.0, Row3, 700),
                    (D + 0.4, Row3, 300),       // cantilever past D3
                    (4.0, Row3, -120),          // uplift
                },
            };

            var palonnierChecks = ReportDataBuilder.BuildAbaqueChecks(
                elements, typeNames, chargeMps, palonnierTable, warnings, hangingPoints, point0Y: P0Y);
            var motherChecks = ReportDataBuilder.BuildMotherAbaqueChecks(
                elements, typeNames, pointLoads, motherTable, warnings, P0X, P0Y);

            // ── The loads and the trusses, in the studies' axes ───
            Loads = chargeMps.Select(c =>
            {
                var e = c.MotherGridPalonnier;
                double length = Math.Sqrt(Math.Pow(e.X2!.Value - e.X1!.Value, 2) + Math.Pow(e.Y2!.Value - e.Y1!.Value, 2));
                double gx = e.X1!.Value + c.X!.Value * (e.X2!.Value - e.X1!.Value) / length;
                double gy = e.Y1!.Value + c.X!.Value * (e.Y2!.Value - e.Y1!.Value) / length;
                var (sx, sy) = StudyAxes.ToStudy(gx, gy);
                return new ProductionLoad(c.Name!, sx, sy, c.Charge!.Value);
            }).ToList();
            var trusses = elements
                .Select(e => new FigureTruss(
                    e.StudyX1!.Value, e.StudyY1!.Value, e.StudyX2!.Value, e.StudyY2!.Value,
                    typeNames[e.ElementTypeId], null, e.Famille, typeNames[e.ElementTypeId]))
                .ToList();

            var preRig = Path.Combine(_temp, "figure-pre-rigg.png");
            var implantation = Path.Combine(_temp, "figure-implantation.png");
            var plan = Path.Combine(_temp, "figure-plan-charges.png");
            PreRigDrawn = ReportFigureDrawer.DrawPreRig(trusses, preRig, (P0X, P0Y));
            ImplantationDrawn = ReportFigureDrawer.DrawImplantation(trusses, Loads, implantation, (P0X, P0Y));
            LoadPlanDrawn = ReportFigureDrawer.DrawLoadPlan(Loads, plan);

            // ── The study ─────────────────────────────────────────
            Context = new ReportContext
            {
                ShowName = "ESSAI",
                ShowDate = new DateTime(2026, 9, 20),
                IssueDate = new DateTime(2026, 9, 11),
                Author = "Shared.Tests",
            };
            Venue = new VenueSpec { Beams = ReportDataBuilder.Beams, Points = ReportDataBuilder.Points };

            var report = new RigReport
            {
                Context = Context,
                Venue = Venue,
                Loads = Loads,
                Trusses = TrussBill,
                AbaqueChecks = Ordered(palonnierChecks.Concat(motherChecks)),
                Warnings = warnings,
                AppliedLoadsKnown = true,
                VerificationGaps = new List<string> { "(exemple de réserve)" },
                PalonnierCount = 2,
                Figures = new FigureSet { PreRig = preRig, Implantation = implantation, ProductionPlan = plan },
            };
            var path = Path.Combine(_temp, "etude.docx");
            WordReportBuilder.Write(report, path);
            DocxPath = path;

            AllText = Paragraphs(path);
            Text = AllText.SkipWhile(t => !t.StartsWith("La composition de ce pré rigg")).ToList();

            // ── The same study, one inventory row unread ──────────
            // The rows InventoryFiles.Load skips in the venue's own workbooks; reading the workbooks is
            // TrussInventoryTests' subject, what the study says about them is this one's.
            _unread = new List<SkippedRow>
            {
                new("MotherGrill.xlsx", true, "S500", 7.5, 4, "charge ponctuelle au milieu illisible"),
                new("MotherGrill.xlsx", true, "S500", null, 5, "longueur illisible"),
                new("Palonnier.xlsx", false, "P300", 6.0, 3, "charge ponctuelle au milieu illisible"),
            };
            var unreadReport = new RigReport
            {
                Context = Context,
                Venue = Venue,
                Loads = Loads,
                Trusses = TrussBill,
                AbaqueChecks = Ordered(
                    ReportDataBuilder.BuildAbaqueChecks(elements, typeNames, chargeMps, palonnierTable,
                        new List<string>(), hangingPoints, point0Y: P0Y, skippedRows: _unread.ToList())
                    .Concat(
                        ReportDataBuilder.BuildMotherAbaqueChecks(elements, typeNames, pointLoads, motherTable,
                            new List<string>(), P0X, P0Y, skippedRows: _unread.ToList()))),
                AppliedLoadsKnown = true,
                VerificationGaps = ReportDataBuilder.SkippedRowGaps(_unread, elements, typeNames),
                PalonnierCount = 2,
            };
            UnreadText = Write("etude-lignes-illisibles.docx", unreadReport);
        }

        public void Dispose()
        {
            try { Directory.Delete(_temp, recursive: true); }
            catch (IOException) { /* a file still held open must not fail a passing test */ }
        }

        /// <summary>Writes a study into this class's temp folder and reads every paragraph back.</summary>
        public List<string> Write(string fileName, RigReport report)
        {
            var path = Path.Combine(_temp, fileName);
            WordReportBuilder.Write(report, path);
            return Paragraphs(path);
        }

        /// <summary>
        /// A study whose only check passes, with every row of the inventory read or with one of them unread:
        /// the two sides of the rule that a check made without part of the inventory is not a passed check.
        /// </summary>
        public List<string> WritePassingStudy(string fileName, bool unread) =>
            Write(fileName, new RigReport
            {
                Context = Context,
                Venue = Venue,
                Loads = Loads,
                Trusses = TrussBill,
                AbaqueChecks = new List<AbaqueCheck>
                {
                    new()
                    {
                        Family = "P300", Role = AbaqueElementRole.Palonnier, Case = AbaqueLoadCase.Centered,
                        WorstElement = _palonnierName,
                        AppliedKg = 100, SpanM = 3.7, AllowedKg = 900, AllowedSpanM = 4,
                        Source = AllowedSource.Table, BaseLengthM = 4, BaseKg = 900,
                        SkippedRows = unread
                            ? _unread.Where(r => !r.Mothers && r.Model == "P300").ToList()
                            : Array.Empty<SkippedRow>(),
                    },
                },
                AppliedLoadsKnown = true,
                PalonnierCount = 2,
            });

        /// <summary>A coordinate as Tableau 1 prints it, so a test compares the same string the cell holds.</summary>
        public static string M2(double v) =>
            (Math.Abs(v) < 0.005 ? 0.0 : v).ToString("0.00", ReportDataBuilder.Fr);

        /// <summary>The checks in the order the study prints them: by role, then family, then case.</summary>
        private static List<AbaqueCheck> Ordered(IEnumerable<AbaqueCheck> checks) => checks
            .OrderBy(c => c.Role)
            .ThenBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Case)
            .ToList();

        /// <summary>Every non-empty paragraph of a document, in order: one table cell is one entry.</summary>
        private static List<string> Paragraphs(string path)
        {
            var found = new List<string>();
            using var doc = WordprocessingDocument.Open(path, false);
            foreach (var p in doc.MainDocumentPart!.Document.Body!.Descendants<W.Paragraph>())
                if (p.InnerText.Trim().Length > 0)
                    found.Add(p.InnerText);
            return found;
        }

        private static MotherGridPalonnier El(
            string name, Guid type, bool mother, double x1, double y1, double x2, double y2, string? composition) => new()
        {
            MotherGridPalonnierId = Guid.NewGuid(), ElementTypeId = type, Famille = mother, Name = name,
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Composition = composition,
        };

        private static ChargeMp Charge(string name, MotherGridPalonnier on, double x, double kg) => new()
        {
            ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = on.MotherGridPalonnierId,
            MotherGridPalonnier = on, Name = name, X = x, Charge = kg,
        };
    }
}
