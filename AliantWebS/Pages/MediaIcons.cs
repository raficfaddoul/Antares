using System.Collections.Generic;
using Microsoft.AspNetCore.Components;

namespace AliantWebS.Pages
{
    /// <summary>
    /// The line icons of the camera and the media gallery, drawn inline as SVG so they stay sharp at any size and
    /// take the colour of the text around them (no icon font to load, none of the three the app mixes).
    /// Names are fixed here; nothing a user types ever reaches this markup.
    /// </summary>
    public static class MediaIcons
    {
        private static readonly Dictionary<string, string> Paths = new()
        {
            ["close"] = "<path d=\"M18 6 6 18M6 6l12 12\"/>",
            ["flash"] = "<path d=\"M13 2 4 14h7l-1 8 9-12h-7l1-8z\"/>",
            ["flash-off"] = "<path d=\"M13 2 9.6 6.4M7.4 9.2 4 14h7l-1 8 4.2-5.6M16.4 13.2 20 10h-7l1-8\"/><path d=\"M3 3l18 18\"/>",
            ["timer"] = "<circle cx=\"12\" cy=\"13\" r=\"8\"/><path d=\"M12 9v4l2.5 2M9.5 2h5\"/>",
            ["grid"] = "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><path d=\"M3 9h18M3 15h18M9 3v18M15 3v18\"/>",
            ["stamp"] = "<rect x=\"3\" y=\"4\" width=\"18\" height=\"17\" rx=\"2\"/><path d=\"M16 2v4M8 2v4M3 10h18M7 14h5M7 17.5h8\"/>",
            ["sliders"] = "<path d=\"M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6\"/>",
            ["flip"] = "<path d=\"M20 7h-3l-2-3H9L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2z\"/><path d=\"M8.5 13a3.5 3.5 0 0 1 6-2.4M15.5 13a3.5 3.5 0 0 1-6 2.4\"/><path d=\"M14.6 8.6v2.1h-2.1M9.4 17.4v-2.1h2.1\"/>",
            ["image"] = "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><circle cx=\"9\" cy=\"9\" r=\"2\"/><path d=\"m21 15-3.1-3.1a2 2 0 0 0-2.8 0L6 21\"/>",
            ["image-plus"] = "<path d=\"M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7\"/><path d=\"M16 5h6M19 2v6\"/><circle cx=\"9\" cy=\"9\" r=\"2\"/><path d=\"m21 15-3.1-3.1a2 2 0 0 0-2.8 0L6 21\"/>",
            ["camera"] = "<path d=\"M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z\"/><circle cx=\"12\" cy=\"13\" r=\"3.5\"/>",
            ["camera-off"] = "<path d=\"M3 3l18 18M9.5 4h5L17 7h3a2 2 0 0 1 2 2v8.5M20 20H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h3\"/><path d=\"M14.1 15.1A3.5 3.5 0 0 1 9.4 10.4\"/>",
            ["video"] = "<path d=\"m22 8-6 4 6 4V8z\"/><rect x=\"2\" y=\"6\" width=\"14\" height=\"12\" rx=\"2\"/>",
            ["trash"] = "<path d=\"M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M10 11v6M14 11v6\"/>",
            ["markup"] = "<path d=\"M12 20h9\"/><path d=\"M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z\"/>",
            ["check"] = "<path d=\"M20 6 9 17l-5-5\"/>",
            ["chevron-left"] = "<path d=\"m15 18-6-6 6-6\"/>",
            ["chevron-right"] = "<path d=\"m9 18 6-6-6-6\"/>",
            ["back"] = "<path d=\"M19 12H5M12 19l-7-7 7-7\"/>",
            ["undo"] = "<path d=\"M3 7v6h6\"/><path d=\"M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13\"/>",
            ["redo"] = "<path d=\"M21 7v6h-6\"/><path d=\"M3 17a9 9 0 0 1 9-9 9 9 0 0 1 6 2.3L21 13\"/>",
            ["tool-pen"] = "<path d=\"M3 18c2.5-3.5 4-9 7-9s1.5 7.5 4.5 7.5S19 12 21 10\"/>",
            ["tool-arrow"] = "<path d=\"M5 19 19 5M9 5h10v10\"/>",
            ["tool-rect"] = "<rect x=\"4\" y=\"5\" width=\"16\" height=\"14\" rx=\"1.5\"/>",
            ["tool-ellipse"] = "<ellipse cx=\"12\" cy=\"12\" rx=\"9\" ry=\"7\"/>",
            ["tool-text"] = "<path d=\"M4 7V4h16v3M9 20h6M12 4v16\"/>",
            ["eraser"] = "<path d=\"m7 21-4.3-4.3a1 1 0 0 1 0-1.4l10-10a1 1 0 0 1 1.4 0l5.6 5.6a1 1 0 0 1 0 1.4L11 21M22 21H7M5 11l9 9\"/>",
            ["alert"] = "<path d=\"M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z\"/><path d=\"M12 9v4M12 17h.01\"/>",
            ["lock"] = "<rect x=\"4\" y=\"11\" width=\"16\" height=\"10\" rx=\"2\"/><path d=\"M8 11V7a4 4 0 0 1 8 0v4\"/>",
            ["pin"] = "<path d=\"M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0z\"/><circle cx=\"12\" cy=\"10\" r=\"3\"/>",
            ["mic"] = "<path d=\"M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z\"/><path d=\"M19 10v2a7 7 0 0 1-14 0v-2M12 19v3\"/>",
            ["sound"] = "<path d=\"M11 5 6 9H2v6h4l5 4V5z\"/><path d=\"M15.5 8.5a5 5 0 0 1 0 7M19 5a10 10 0 0 1 0 14\"/>",
            ["phone"] = "<rect x=\"5\" y=\"2\" width=\"14\" height=\"20\" rx=\"2\"/><path d=\"M12 18h.01\"/>",
            ["refresh"] = "<path d=\"M21 12a9 9 0 1 1-2.6-6.4L21 8\"/><path d=\"M21 3v5h-5\"/>",
            ["upload"] = "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12\"/>",
            ["download"] = "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3\"/>",
            ["expand"] = "<path d=\"M8 3H5a2 2 0 0 0-2 2v3M21 8V5a2 2 0 0 0-2-2h-3M3 16v3a2 2 0 0 0 2 2h3M16 21h3a2 2 0 0 0 2-2v-3\"/>",
            ["keyboard"] = "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><path d=\"M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10\"/>",
            ["play"] = "<path d=\"M7 4.5v15a1 1 0 0 0 1.5.9l12-7.5a1 1 0 0 0 0-1.8l-12-7.5A1 1 0 0 0 7 4.5z\"/>",
            ["flag"] = "<path d=\"M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1zM4 22v-7\"/>",
            ["plus"] = "<path d=\"M12 5v14M5 12h14\"/>",
            ["zoom-in"] = "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m21 21-4.3-4.3M11 8v6M8 11h6\"/>",
            ["zoom-out"] = "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m21 21-4.3-4.3M8 11h6\"/>",
            ["info"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M12 16v-4M12 8h.01\"/>",
        };

        /// <summary>The icon as inline SVG markup. An unknown name gives an empty icon rather than an error, so a
        /// typo shows as a missing glyph, not a broken page.</summary>
        public static MarkupString Svg(string name, string cssClass = "media-ico")
        {
            Paths.TryGetValue(name, out var body);
            return new MarkupString(
                $"<svg class=\"{cssClass}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" " +
                $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{body}</svg>");
        }
    }
}
