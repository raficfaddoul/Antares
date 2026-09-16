// =============================================================
// FigureScene.cs — What a report figure is made of, before anybody decides how to draw it.
//
// The figures used to exist only as pixels: one file decided the layout and painted it in the same
// breath, so a figure could only ever be a picture. A picture is the wrong medium for a study - it
// blurs when the reader zooms, it carries its own fonts, it weighs more than the document it sits in,
// and no test can read the beam letters out of it. The layout is now decided once, into a scene, and
// rendered twice: to PNG for the scenario's output folder, and to DrawingML for the Word file. Two
// renderers, one truth.
//
// The scene is deliberately dumb. It holds no geometry of the hall, no axes, no idea that Y grows
// towards beam J: ReportFigureDrawer has already resolved all of that. A renderer walks the
// primitives in order and puts them on a page - it flips nothing, computes nothing, decides nothing.
//
// MILLIMETRES, everywhere, with no exceptions: every coordinate, radius, stroke width and text size
// below is a length on paper. The page runs left to right and TOP TO BOTTOM - x grows rightwards, y
// grows DOWNWARDS from the top-left corner - which is how both a canvas and a DrawingML drawing are
// laid out, so neither renderer has to invert anything. A renderer that must fit the scene into a box
// scales it uniformly; the millimetres are the drawing's own, natural size.
//
// COLOURS are a plain 32-bit ARGB value, 0xAARRGGBB, because it is the one encoding a canvas and an
// OpenXml colour element can both be built from without either renderer owning a colour type.
// =============================================================

namespace AliantWebS.Reporting;

/// <summary>Where a label sits horizontally with respect to its anchor point.</summary>
public enum FigureHAlign
{
    /// <summary>The anchor is the left edge of the text.</summary>
    Left,

    /// <summary>The anchor is the middle of the text: the renderer measures and centres it.</summary>
    Center,

    /// <summary>The anchor is the right edge of the text.</summary>
    Right,
}

/// <summary>Where a label sits vertically with respect to its anchor point.</summary>
public enum FigureVAlign
{
    /// <summary>The anchor is the top of the text's line.</summary>
    Top,

    /// <summary>The anchor is the middle of the text's line.</summary>
    Middle,

    /// <summary>
    /// The anchor is the baseline the glyphs sit on - where a text engine actually places them, which
    /// is what the layout computes. A renderer that can only position a box turns this into a top by
    /// subtracting the font's ascent.
    /// </summary>
    Baseline,

    /// <summary>The anchor is the bottom of the text's line.</summary>
    Bottom,
}

/// <summary>One thing to draw. The renderer switches on the concrete type; there is nothing else to it.</summary>
public abstract record FigurePrimitive;

/// <summary>
/// A straight segment from (X1, Y1) to (X2, Y2), <paramref name="Width"/> millimetres thick.
/// </summary>
/// <param name="RoundCap">
/// True rounds the ends, as the trusses are drawn: a truss is a beam, not a hairline, and a square end
/// on a thick coloured segment reads as a fitting that is not there.
/// </param>
public sealed record FigureLine(
    double X1, double Y1, double X2, double Y2,
    double Width, uint Color, bool Dashed = false, bool RoundCap = false) : FigurePrimitive;

/// <summary>
/// A disc centred on (Cx, Cy) with a radius in millimetres. A dot is simply a circle with a fill and
/// no outline; an attachment point is one with both. A null fill draws nothing but the outline.
/// </summary>
public sealed record FigureCircle(
    double Cx, double Cy, double Radius,
    uint? Fill, uint? Outline = null, double OutlineWidth = 0) : FigurePrimitive;

/// <summary>
/// A rectangle whose top-left corner is (X, Y), sized in millimetres. A non-zero
/// <paramref name="CornerRadius"/> rounds the corners, as the beam letters' boxes are rounded.
/// </summary>
public sealed record FigureRect(
    double X, double Y, double Width, double Height,
    uint? Fill, uint? Outline = null, double OutlineWidth = 0, double CornerRadius = 0) : FigurePrimitive;

/// <summary>
/// A filled closed shape. The figures need exactly one thing that is neither a segment, a disc nor a
/// box - the solid head of an axis arrow - and a renderer draws it truthfully from its three points
/// rather than approximating it with a line ending.
/// </summary>
public sealed record FigurePolygon(
    IReadOnlyList<(double X, double Y)> Points, uint Fill) : FigurePrimitive;

/// <summary>
/// An open run of segments, stroked and never filled. It exists for the trusses: their web is drawn as
/// two zig-zags between the chords, and a zig-zag of forty panels written as forty separate segments
/// would be forty shapes in the Word file instead of one. Same drawing, a fraction of the document.
/// </summary>
/// <param name="RoundCap">
/// Rounds the ends and the corners of the run, so a zig-zag has no notches at its turns.
/// </param>
public sealed record FigurePolyline(
    IReadOnlyList<(double X, double Y)> Points,
    double Width, uint Color, bool RoundCap = false) : FigurePrimitive;

/// <summary>
/// A single line of text placed about (X, Y) according to its alignments, at a size given in
/// millimetres (the height of the em, as a text engine takes it).
/// </summary>
/// <param name="Halo">
/// An optional colour drawn as a thick outline under the glyphs, which is how the load labels stay
/// readable where they cross a truss. Null - the usual case - draws the text alone.
/// </param>
public sealed record FigureLabel(
    string Text, double X, double Y,
    FigureHAlign HAlign, FigureVAlign VAlign,
    double Size, bool Bold, uint Color,
    uint? Halo = null, double HaloWidth = 0) : FigurePrimitive;

/// <summary>
/// A whole figure: a page of the given size in millimetres, a background to clear it with, and the
/// primitives IN PAINT ORDER - first drawn is furthest back. The order is the layout's business, not
/// the renderer's: it is how the load labels end up over the trusses and the trusses over the grid.
/// </summary>
public sealed record FigureScene(
    double Width, double Height, uint Background, IReadOnlyList<FigurePrimitive> Primitives);

/// <summary>
/// The little that a renderer needs to take an ARGB value apart. Kept here, beside the primitives, so
/// the PNG renderer and the DrawingML renderer read a colour the same way.
/// </summary>
/// <summary>
/// The typeface a figure is lettered in. It lives here, beside the colours, for the same reason: the
/// LAYOUT measures every label with it - to place it, to size its halo, to decide whether two labels
/// collide and where the legend wraps - and BOTH renderers letter with it. A layout that measured one
/// family while a renderer drew another would place every label by a width that is not the width drawn,
/// and the error would grow with the length of the text: long load names would be the ones to collide.
///
/// Arial, because the study's body text is Arial and a figure lettered in something else reads as a
/// picture pasted in. On a machine without it - the Linux container, where fonts-liberation supplies
/// the metrically identical Liberation Sans - the substitute is what gets measured, which is why that
/// package is installed rather than left to chance.
/// </summary>
public static class FigureFonts
{
    public const string Family = "Arial";
}

public static class FigureColors
{
    /// <summary>An opaque colour from its three channels.</summary>
    public static uint Rgb(byte red, byte green, byte blue) =>
        0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;

    public static byte AlphaOf(uint color) => (byte)((color >> 24) & 0xFF);

    public static byte RedOf(uint color) => (byte)((color >> 16) & 0xFF);

    public static byte GreenOf(uint color) => (byte)((color >> 8) & 0xFF);

    public static byte BlueOf(uint color) => (byte)(color & 0xFF);

    /// <summary>
    /// The six hex digits "RRGGBB", which is the only form an OpenXml colour takes; the alpha channel
    /// is carried separately there, as a percentage of opacity.
    /// </summary>
    public static string HexOf(uint color) => (color & 0x00FFFFFFu).ToString("X6");
}
