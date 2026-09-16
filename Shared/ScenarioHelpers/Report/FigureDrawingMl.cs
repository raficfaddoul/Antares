// =============================================================
// FigureDrawingMl.cs — Reads a FigureScene as shapes the document itself holds.
//
// The scene's other reader is the PNG writer at the foot of ReportFigureDrawer. That one paints
// pixels; this one writes a w:drawing — a group of real shapes, which stays sharp at any zoom and in
// print at any format, whose text a reader can select and a test can read, which weighs a fraction of
// a picture of the same figure, and which Word letters with its own fonts instead of the container
// having to carry font files. Nothing here decides anything about the figure: the scene already says
// what is drawn, in what order, and this walks it.
//
// EMU, AND ONE SCALE. A scene is millimetres; OpenXml is English Metric Units, 36 000 to the
// millimetre, whole numbers. The figure is also fitted into the box the study gives its plates, so
// every length below is multiplied by ONE scale on its way out — coordinates, stroke widths and text
// sizes alike — and rounded to whole EMU at the last moment.
//
// THE GROUP TRICK. The group's a:ext and a:chExt are deliberately the SAME. A group whose child
// extent differs from its extent is a group Word scales, and it scales the shapes but NOT the text
// inside them, so every label would drift off its anchor as the figure grew or shrank. Equal extents
// make the group's coordinates the shapes' own; the fit has already happened, once, in the scale
// above. Change that and the figures still draw — but the labels will lie.
//
// IDS ARE THE DOCUMENT'S. A wp:docPr id has to be unique across the body, the headers and the footers
// at once - a repeated one is one of the ways Word answers with "contenu illisible" rather than with
// an error - and a wps:cNvPr id has to be unique among the shapes of its own drawing, which for a
// figure of four hundred shapes is the harder of the two. Both are satisfied at once by taking every
// id from the one allocator the caller already uses for its pictures, so nothing here has to reason
// about which series it is in.
//
// ORDER. a:spPr takes its children in schema order — a:xfrm, the geometry, the fill, then a:ln — and
// wps:wsp ends with wps:bodyPr, after the text box and never before it. Both are the kind of mistake
// that produces an unreadable file instead of an exception, so everything goes through the one
// Shape() below rather than being assembled at each call site.
//
// FILL AND LINE ARE NEVER LEFT OUT. A shape that states neither inherits Word's theme defaults — an
// accent-coloured fill with a darker outline — so a disc meant to be white would come out blue. Every
// shape here says a:noFill where it means nothing.
// =============================================================

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

namespace AliantWebS.Reporting;

public static class FigureDrawingMl
{
    /// <summary>English Metric Units to the millimetre: the document's unit against the scene's.</summary>
    public const double EmuPerMillimetre = 36000d;

    /// <summary>The graphic data a Word 2010 shape group is carried under.</summary>
    private const string GroupUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";

    /// <summary>The study's own font, so a figure's text is the text of the pages around it.</summary>
    // The family the layout measured with. Naming a different one here would letter the document in a
    // font the label's box was not sized for.
    private const string LabelFont = FigureFonts.Family;

    // Vertical metrics, as fractions of the em. A text box can be placed but never pinned to a
    // baseline, so the baseline the scene gives is turned into a box through these. They are Arial's
    // own - the font Word will letter the label with - which is what makes them the right numbers
    // here rather than the raster's.
    private const double Ascent = 0.905;
    private const double Descent = 0.212;
    private const double CapHeight = 0.716;

    /// <summary>
    /// The scale the layout measures text at — eight pixels to the millimetre, as ReportFigureDrawer
    /// measures — kept so a width computed here is the width the layout computed.
    /// </summary>
    private const float MeasureScale = 8f;

    // The same two faces the layout measured with, resolved once. See FigureFonts.
    private static readonly SKTypeface RegularFace =
        SKTypeface.FromFamilyName(FigureFonts.Family, SKFontStyle.Normal) ?? SKTypeface.Default;

    private static readonly SKTypeface BoldFace =
        SKTypeface.FromFamilyName(FigureFonts.Family, SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>
    /// The scene as an inline w:drawing, fitted into the box without distorting it.
    /// </summary>
    /// <param name="name">
    /// What Word calls the drawing, in its selection pane and as the figure's alternative text: the
    /// caption, so a reader who cannot see the figure is told which figure it is.
    /// </param>
    /// <param name="nextId">
    /// The document's drawing-id allocator. Every shape takes one, so a figure of four hundred
    /// primitives consumes four hundred ids; they only have to be unique, not tidy.
    /// </param>
    public static Drawing Build(
        FigureScene scene, long maxWidthEmu, long maxHeightEmu, string name, Func<uint> nextId)
    {
        var scale = FitScale(scene, maxWidthEmu, maxHeightEmu);
        var width = Math.Max(1, Emu(scene.Width, scale));
        var height = Math.Max(1, Emu(scene.Height, scale));

        var group = new WPG.WordprocessingGroup(
            new WPG.NonVisualGroupDrawingShapeProperties(),
            new WPG.GroupShapeProperties(
                new A.TransformGroup(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = width, Cy = height },
                    new A.ChildOffset { X = 0L, Y = 0L },
                    new A.ChildExtents { Cx = width, Cy = height })));

        // The scene clears its page before it draws: the first shape is that clearing. On a white page
        // under a white background it shows nothing, which is exactly what it is for — a figure laid
        // over a shaded paragraph would otherwise have the shading between its lines.
        group.AppendChild(Shape(
            nextId, "Fond", 0, 0, width, height,
            Geometry(A.ShapeTypeValues.Rectangle), Fill(scene.Background), NoLine()));

        // In the scene's own order, which is paint order: first drawn is furthest back, and a group
        // stacks its shapes the same way.
        foreach (var primitive in scene.Primitives)
            foreach (var shape in Shapes(primitive, scale, nextId))
                group.AppendChild(shape);

        var id = nextId();

        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = width, Cy = height },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name, Description = name },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(group) { Uri = GroupUri }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });
    }

    /// <summary>
    /// The one factor everything is drawn at: the largest that keeps the whole scene inside the box
    /// with its proportions intact. A figure carries a printed scale bar, so stretching one axis would
    /// make a distance measured off the drawing wrong.
    /// </summary>
    private static double FitScale(FigureScene scene, long maxWidthEmu, long maxHeightEmu)
    {
        // A scene of no size cannot be fitted into anything. Drawn at its own millimetres it is at
        // least visible, and nothing downstream has to guard against a zero.
        if (scene.Width <= 0 || scene.Height <= 0) return 1;

        return Math.Min(
            maxWidthEmu / (scene.Width * EmuPerMillimetre),
            maxHeightEmu / (scene.Height * EmuPerMillimetre));
    }

    // ── One primitive, one or two shapes ──────────────────────────────────────────────────────

    private static IEnumerable<WPS.WordprocessingShape> Shapes(
        FigurePrimitive primitive, double scale, Func<uint> nextId)
    {
        switch (primitive)
        {
            case FigureLine line:
                yield return LineShape(line, scale, nextId);
                break;

            case FigureCircle circle:
                yield return CircleShape(circle, scale, nextId);
                break;

            case FigureRect rect:
                yield return RectShape(rect, scale, nextId);
                break;

            case FigurePolyline polyline when polyline.Points.Count > 1:
                yield return PolylineShape(polyline, scale, nextId);
                break;

            case FigurePolygon polygon when polygon.Points.Count > 0:
                yield return PolygonShape(polygon, scale, nextId);
                break;

            case FigureLabel label:
                // The halo first: it is what the text is read against, so it goes under it.
                if (HaloShape(label, scale, nextId) is { } halo) yield return halo;
                yield return LabelShape(label, scale, nextId);
                break;
        }
    }

    private static WPS.WordprocessingShape LineShape(FigureLine line, double scale, Func<uint> nextId)
    {
        long x1 = Emu(line.X1, scale), y1 = Emu(line.Y1, scale);
        long x2 = Emu(line.X2, scale), y2 = Emu(line.Y2, scale);

        var outline = new A.Outline(Fill(line.Color))
        {
            Width = Stroke(line.Width, scale),
            CapType = line.RoundCap ? A.LineCapValues.Round : A.LineCapValues.Flat,
        };

        // The scene's dash is 1,5 mm drawn and 1 mm blank; "dash" is the preset nearest to it. Nothing
        // in the figures is dashed today, so no figure turns on the difference.
        if (line.Dashed) outline.AppendChild(new A.PresetDash { Val = A.PresetLineDashValues.Dash });

        // prst="line" runs from the top-left corner of its box to the bottom-right one; the other two
        // diagonals are that same line flipped. So the box is the ends' bounding box — never negative,
        // which an a:ext refuses — and the flips say which way round the segment actually goes.
        return Shape(
            nextId, "Trait",
            Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1),
            Geometry(A.ShapeTypeValues.Line), new A.NoFill(), outline,
            flipH: x2 < x1, flipV: y2 < y1);
    }

    private static WPS.WordprocessingShape CircleShape(FigureCircle circle, double scale, Func<uint> nextId)
    {
        var radius = Emu(circle.Radius, scale);

        return Shape(
            nextId, "Disque",
            Emu(circle.Cx, scale) - radius, Emu(circle.Cy, scale) - radius, 2 * radius, 2 * radius,
            Geometry(A.ShapeTypeValues.Ellipse),
            circle.Fill is { } fill ? (OpenXmlElement)Fill(fill) : new A.NoFill(),
            circle.Outline is { } outline
                ? new A.Outline(Fill(outline)) { Width = Stroke(circle.OutlineWidth, scale) }
                : NoLine());
    }

    private static WPS.WordprocessingShape RectShape(FigureRect rect, double scale, Func<uint> nextId)
    {
        long width = Emu(rect.Width, scale), height = Emu(rect.Height, scale);

        return Shape(
            nextId, "Cadre",
            Emu(rect.X, scale), Emu(rect.Y, scale), width, height,
            rect.CornerRadius > 0
                ? RoundedGeometry(Emu(rect.CornerRadius, scale), width, height)
                : Geometry(A.ShapeTypeValues.Rectangle),
            rect.Fill is { } fill ? (OpenXmlElement)Fill(fill) : new A.NoFill(),
            rect.Outline is { } outline
                ? new A.Outline(Fill(outline)) { Width = Stroke(rect.OutlineWidth, scale) }
                : NoLine());
    }

    /// <summary>
    /// The one shape no preset draws: the solid head of an axis arrow. a:custGeom carries a path in a
    /// coordinate space of its own, so the space is taken to be the polygon's bounding box and the
    /// points are written relative to its corner — which is also where the shape is placed.
    /// </summary>
    private static WPS.WordprocessingShape PolygonShape(FigurePolygon polygon, double scale, Func<uint> nextId)
    {
        var xs = polygon.Points.Select(p => Emu(p.X, scale)).ToList();
        var ys = polygon.Points.Select(p => Emu(p.Y, scale)).ToList();

        long x = xs.Min(), y = ys.Min();
        long width = Math.Max(1, xs.Max() - x), height = Math.Max(1, ys.Max() - y);

        var path = new A.Path { Width = width, Height = height };
        path.AppendChild(new A.MoveTo(Point(xs[0] - x, ys[0] - y)));
        for (var i = 1; i < xs.Count; i++)
            path.AppendChild(new A.LineTo(Point(xs[i] - x, ys[i] - y)));
        path.AppendChild(new A.CloseShapePath());

        // The four empty lists are what a custom geometry is expected to open with, in this order;
        // this shape has no adjustments, no guides, no handles and no connection sites.
        var geometry = new A.CustomGeometry(
            new A.AdjustValueList(),
            new A.ShapeGuideList(),
            new A.AdjustHandleList(),
            new A.ConnectionSiteList(),
            new A.PathList(path));

        return Shape(nextId, "Polygone", x, y, width, height, geometry, Fill(polygon.Fill), NoLine());
    }

    /// <summary>
    /// An open run of segments — the trusses' web — as one shape. Same custom geometry as the polygon
    /// above, with two differences that are the whole point: the path is not closed, and it is declared
    /// fill="none" so Word strokes it instead of filling the area the zig-zag encloses. A filled one
    /// would paint the truss's web solid, which is the opposite of what it draws.
    /// </summary>
    private static WPS.WordprocessingShape PolylineShape(FigurePolyline line, double scale, Func<uint> nextId)
    {
        var xs = line.Points.Select(p => Emu(p.X, scale)).ToList();
        var ys = line.Points.Select(p => Emu(p.Y, scale)).ToList();

        long x = xs.Min(), y = ys.Min();

        // A run that is perfectly straight in one direction has no extent in the other, and an a:ext of
        // zero is a shape Word drops. One EMU is a thirty-six-thousandth of a millimetre: invisible.
        long width = Math.Max(1, xs.Max() - x), height = Math.Max(1, ys.Max() - y);

        var path = new A.Path { Width = width, Height = height, Fill = A.PathFillModeValues.None };
        path.AppendChild(new A.MoveTo(Point(xs[0] - x, ys[0] - y)));
        for (var i = 1; i < xs.Count; i++)
            path.AppendChild(new A.LineTo(Point(xs[i] - x, ys[i] - y)));

        var geometry = new A.CustomGeometry(
            new A.AdjustValueList(),
            new A.ShapeGuideList(),
            new A.AdjustHandleList(),
            new A.ConnectionSiteList(),
            new A.PathList(path));

        var outline = new A.Outline(Fill(line.Color))
        {
            Width = Stroke(line.Width, scale),
            CapType = line.RoundCap ? A.LineCapValues.Round : A.LineCapValues.Flat,
        };

        // Round joins for the same reason as the round caps: a zig-zag mitred at its turns grows little
        // spikes past the chord it turns on.
        if (line.RoundCap) outline.AppendChild(new A.Round());

        return Shape(nextId, "Treillis", x, y, width, height, geometry, new A.NoFill(), outline);
    }

    // ── Text ──────────────────────────────────────────────────────────────────────────────────
    // A label becomes a text box: a rectangle with neither fill nor line, holding one paragraph of one
    // run. That is what makes the beam letters and the load names selectable, searchable, and readable
    // by a test — which no test could do with a picture of them.
    //
    // A HALO IS A PLATE HERE. The raster draws the halo as a thick outline of the very same glyphs,
    // under their fill. A text box cannot do that: a run has no stroke. So the halo becomes what it
    // reads as — a plain filled rounded plate of the label's own measured size, laid under the text —
    // which is what keeps a load's name legible where it crosses a truss.

    private static WPS.WordprocessingShape LabelShape(FigureLabel label, double scale, Func<uint> nextId)
    {
        var place = Place(label);

        // The box is CENTRED on the middle of the line rather than hung from its top: centring
        // survives a line height a little different from the one assumed here, where a top anchor
        // would shift the text by the whole of that error.
        var middle = place.Baseline - (Ascent - Descent) / 2 * place.Em;

        // Wide and tall enough that Word never has to wrap or clip what the layout measured. Wrapping
        // is off as well, and both matter: the alignment below is read from the box's own edges, so an
        // overflowing label stays anchored where the layout anchored it.
        var boxWidth = place.Width + 2 * place.Em;
        var boxHeight = 2 * place.Em;

        var left = label.HAlign switch
        {
            FigureHAlign.Center => label.X - boxWidth / 2,
            FigureHAlign.Right => label.X - boxWidth,
            _ => label.X,
        };

        return Shape(
            nextId, "Texte",
            Emu(left, scale), Emu(middle - boxHeight / 2, scale),
            Emu(boxWidth, scale), Emu(boxHeight, scale),
            Geometry(A.ShapeTypeValues.Rectangle), new A.NoFill(), NoLine(),
            text: TextBox(label, HalfPoints(label.Size, scale)));
    }

    private static WPS.WordprocessingShape? HaloShape(FigureLabel label, double scale, Func<uint> nextId)
    {
        if (label.Halo is not { } halo) return null;

        var place = Place(label);
        var pad = Math.Max(label.HaloWidth, 0);

        // The plate covers the glyphs, not the line box: from the cap height above the baseline to the
        // descenders below it, and the halo's own width beyond on every side — which is how far the
        // raster's stroked outline reaches around the same letters.
        var top = place.Baseline - CapHeight * place.Em - pad;
        var bottom = place.Baseline + Descent * place.Em + pad;

        long width = Emu(place.Width + 2 * pad, scale), height = Emu(bottom - top, scale);

        return Shape(
            nextId, "Halo",
            Emu(place.Left - pad, scale), Emu(top, scale), width, height,
            RoundedGeometry(Emu(pad, scale), width, height), Fill(halo), NoLine());
    }

    /// <summary>Where a label's text actually lands, in the scene's millimetres.</summary>
    private readonly record struct LabelPlacement(double Left, double Width, double Baseline, double Em);

    private static LabelPlacement Place(FigureLabel label)
    {
        var width = Measure(label.Text, label.Size, label.Bold);

        var left = label.HAlign switch
        {
            FigureHAlign.Center => label.X - width / 2,
            FigureHAlign.Right => label.X - width,
            _ => label.X,
        };

        // The raster draws from the baseline and moves it by the font's metrics for the other anchors.
        // The same arithmetic, so a label sits where the layout meant to put it.
        var baseline = label.VAlign switch
        {
            FigureVAlign.Top => label.Y + Ascent * label.Size,
            FigureVAlign.Middle => label.Y + (Ascent - Descent) / 2 * label.Size,
            FigureVAlign.Bottom => label.Y - Descent * label.Size,
            _ => label.Y,
        };

        return new LabelPlacement(left, width, baseline, label.Size);
    }

    private static WPS.TextBoxInfo2 TextBox(FigureLabel label, int halfPoints)
    {
        var size = halfPoints.ToString(CultureInfo.InvariantCulture);

        // Run properties in schema order: fonts, weight, proofing, colour, then size.
        var runProperties = new RunProperties(
            new RunFonts { Ascii = LabelFont, HighAnsi = LabelFont, ComplexScript = LabelFont });

        if (label.Bold)
        {
            runProperties.AppendChild(new Bold());
            runProperties.AppendChild(new BoldComplexScript());
        }

        // A beam letter is not a misspelling: nothing in a figure deserves a red underline under it.
        runProperties.AppendChild(new NoProof());
        runProperties.AppendChild(new Color { Val = FigureColors.HexOf(label.Color) });
        runProperties.AppendChild(new FontSize { Val = size });
        runProperties.AppendChild(new FontSizeComplexScript { Val = size });

        // The paragraph mark is a character too, and with automatic line spacing the line is as tall as
        // the TALLEST character in it. Left unstated it takes the document's own size - eleven points,
        // where a beam letter is seven - so the line grows, and a block centred in its box carries the
        // glyphs down with it by a fraction of a millimetre that no PNG shows. It is given the run's own
        // size and font for that reason, not for tidiness.
        var markProperties = (RunProperties)runProperties.CloneNode(true);

        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines
                {
                    Before = "0",
                    After = "0",
                    Line = "240",
                    LineRule = LineSpacingRuleValues.Auto,
                },
                new Justification { Val = Align(label.HAlign) })
            {
                ParagraphMarkRunProperties = new ParagraphMarkRunProperties(
                    markProperties.ChildElements.Select(c => c.CloneNode(true)).ToArray()),
            },
            new Run(
                runProperties,
                // The load labels carry two spaces between the name and the charge; a run that does
                // not preserve its spaces would close them up.
                new Text(label.Text) { Space = SpaceProcessingModeValues.Preserve }));

        return new WPS.TextBoxInfo2(new TextBoxContent(paragraph));
    }

    /// <summary>
    /// The body of a text shape: no insets and no autofit, so the text sits where the scene put it and
    /// Word neither pads the box nor resizes it. Overflow rather than clip, and no wrapping, so a
    /// label Word letters a shade wider than it was measured still reads whole.
    /// </summary>
    private static WPS.TextBodyProperties LabelBody() =>
        new(new A.NoAutoFit())
        {
            LeftInset = 0,
            TopInset = 0,
            RightInset = 0,
            BottomInset = 0,
            Wrap = A.TextWrappingValues.None,
            VerticalOverflow = A.TextVerticalOverflowValues.Overflow,
            HorizontalOverflow = A.TextHorizontalOverflowValues.Overflow,
            Anchor = A.TextAnchoringTypeValues.Center,
            AnchorCenter = false,
        };

    private static JustificationValues Align(FigureHAlign align) => align switch
    {
        FigureHAlign.Center => JustificationValues.Center,
        FigureHAlign.Right => JustificationValues.Right,
        _ => JustificationValues.Left,
    };

    /// <summary>
    /// How wide the text is, in the scene's millimetres, measured the way the layout measured it: the
    /// same engine, at the same eight pixels to the millimetre (ReportFigureDrawer). Word letters the
    /// label in Arial and will differ by a hair; nothing here has to be exact to the glyph, only true
    /// to what the layout believed it was placing.
    /// </summary>
    private static double Measure(string text, double size, bool bold)
    {
        var face = bold ? BoldFace : RegularFace;

        using var font = new SKPaint
        {
            TextSize = (float)(size * MeasureScale),
            Typeface = face,
            FakeBoldText = bold && !face.IsBold,
        };

        return font.MeasureText(text) / MeasureScale;
    }

    // ── The pieces every shape is made of ─────────────────────────────────────────────────────

    private static WPS.WordprocessingShape Shape(
        Func<uint> nextId, string name,
        long x, long y, long width, long height,
        OpenXmlElement geometry, OpenXmlElement fill, A.Outline line,
        bool flipH = false, bool flipV = false,
        WPS.TextBoxInfo2? text = null)
    {
        var transform = new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = width, Cy = height });

        if (flipH) transform.HorizontalFlip = true;
        if (flipV) transform.VerticalFlip = true;

        var id = nextId();
        var nonVisual = new WPS.NonVisualDrawingShapeProperties();
        if (text is not null) nonVisual.TextBox = true;

        var shape = new WPS.WordprocessingShape(
            // The name is what Word shows in its selection pane; only the id has to be unique.
            new WPS.NonVisualDrawingProperties { Id = id, Name = $"{name} {id}" },
            nonVisual,
            new WPS.ShapeProperties(transform, geometry, fill, line));

        if (text is not null) shape.AppendChild(text);

        // wps:bodyPr closes every shape, text or not, and comes after the text box: the schema wants
        // it last, and a shape that puts it first is a document Word offers to repair.
        shape.AppendChild(text is null ? new WPS.TextBodyProperties() : LabelBody());

        return shape;
    }

    private static A.PresetGeometry Geometry(A.ShapeTypeValues preset) =>
        new(new A.AdjustValueList()) { Preset = preset };

    /// <summary>
    /// prst="roundRect" with the corner the scene asked for. Its adjustment is not a length but the
    /// share of THE SHORTER SIDE the corner eats, in hundred-thousandths — the preset reads it as
    /// <c>x1 = ss * adj / 100000</c> — which is why the box's size has to be known to write it: a beam
    /// letter's 0,625 mm corner on a 4,25 mm box is 14 706.
    ///
    /// The preset pins adj at 50 000, where the corner has eaten half the shorter side and the box is a
    /// stadium; asking for more is not refused, it is silently taken as that.
    /// </summary>
    private static A.PresetGeometry RoundedGeometry(long radius, long width, long height)
    {
        var shorter = (double)Math.Min(width, height);
        var share = shorter > 0 ? Math.Clamp(radius / shorter, 0, 0.5) : 0;

        return new A.PresetGeometry(
            new A.AdjustValueList(
                new A.ShapeGuide
                {
                    Name = "adj",
                    Formula = $"val {Math.Round(share * 100000).ToString("0", CultureInfo.InvariantCulture)}",
                }))
        { Preset = A.ShapeTypeValues.RoundRectangle };
    }

    /// <summary>
    /// A solid fill of one of the scene's ARGB colours. OpenXml states a colour as six hex digits and
    /// carries its opacity separately, as a percentage in hundred-thousandths.
    /// </summary>
    private static A.SolidFill Fill(uint colour)
    {
        var rgb = new A.RgbColorModelHex { Val = FigureColors.HexOf(colour) };

        var alpha = FigureColors.AlphaOf(colour);
        if (alpha < 255)
            rgb.AppendChild(new A.Alpha { Val = (int)Math.Round(alpha * 100000.0 / 255) });

        return new A.SolidFill(rgb);
    }

    /// <summary>An explicit absence of a line, which is not the same thing as saying nothing.</summary>
    private static A.Outline NoLine() => new(new A.NoFill());

    private static A.Point Point(long x, long y) =>
        new()
        {
            X = x.ToString(CultureInfo.InvariantCulture),
            Y = y.ToString(CultureInfo.InvariantCulture),
        };

    private static long Emu(double millimetres, double scale) =>
        (long)Math.Round(millimetres * EmuPerMillimetre * scale, MidpointRounding.AwayFromZero);

    /// <summary>A stroke width, in EMU as a:ln takes it, and never negative.</summary>
    private static int Stroke(double millimetres, double scale) =>
        (int)Math.Max(0, Math.Round(millimetres * EmuPerMillimetre * scale));

    /// <summary>
    /// A text size in half-points, which is how w:sz states one. The scene gives the em in millimetres
    /// — 72 points to the inch, 25,4 millimetres to it — and the fit scale applies to text exactly as
    /// it applies to everything else, because the group does no scaling of its own.
    /// </summary>
    private static int HalfPoints(double millimetres, double scale) =>
        Math.Max(2, (int)Math.Round(millimetres * scale * 2 * 72 / 25.4));
}
