// =============================================================
// PlanDxfExporter.cs — The plan as a DXF drawing, for CAD.
//
// The file is written in the studies' axes (Helpers.StudyAxes), in METRES from the event's Point0 (E5 at
// Antarès): X along the beams towards point 9, Y across them towards J - the coordinates the studies print
// and the pages show. In CAD that is X to the right and Y up, which is the studies' Figure 2: beams
// horizontal, J at the top. It is NOT the screen's view, which is turned a quarter from it (PlanView); the
// file's numbers are the same either way, a charge at X = 5 m is 5 m towards point 9 in both.
//
// Everything comes from PlanData, already in the stored frame (the grid's axes, metres from Point0), and is
// turned into the studies' axes by StudyAxes.ToStudy alone - except the sensors, whose X/Y attributes are
// absolute millimetres and go through PlanView.StudyMetres. The background picture is laid turned a quarter
// clockwise (PlanView.ImageInStudyMetres, ImageRotationDeg): that is the file's frame, not a view choice.
//
// One layer per kind of thing, so CAD can hide any of them: BACKGROUND, MOTHERGRID, PALONNIERS, CHARGES,
// PAL_LOADS, INTERSECTIONS, SENSORS, ORIGIN, and DRAWING_DATA, a block of metadata below the picture's
// footprint. A DXF cannot embed a picture, only name it: the page ships the .jpg beside the .dxf.
//
// Moved out of SVGComponent.ExportDxf on 2026-09-17, unchanged: the same document, entities, texts and
// order. The text of both was compared over every option set, several plans, Point0s and cultures, and was
// identical but for the creation and update times netDxf stamps in the header ($TDCREATE, $TDUPDATE and
// their UTC forms), which differ between any two exports. The file name, the download and the journal entry
// stay with the page.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Helpers;

namespace AliantWebS.Plan;

/// <summary>Which layers a DXF export draws. The drawing data is always written.</summary>
public sealed class PlanDxfOptions
{
    public bool MotherGrids { get; init; } = true;
    public bool Palonniers { get; init; } = true;

    /// <summary>The scenic loads (ChargesScenique).</summary>
    public bool Charges { get; init; } = true;

    /// <summary>The loads on palonniers, and on the mothers they hang from directly (ChargeMp).</summary>
    public bool PalonnierLoads { get; init; } = true;

    public bool Intersections { get; init; } = true;
    public bool Sensors { get; init; } = true;

    /// <summary>Point0 and the two axes.</summary>
    public bool Origin { get; init; } = true;

    /// <summary>The hall's picture, referenced by its file name (PlanView.BackgroundImage).</summary>
    public bool BackgroundImage { get; init; } = false;
}

/// <summary>What the DRAWING_DATA layer says about the export. A name left empty is not printed.</summary>
public sealed class PlanDxfInfo
{
    /// <summary>The page the export was launched from.</summary>
    public string? ModuleName { get; init; }

    public string? StructureName { get; init; }
    public string? EventName { get; init; }
    public string? ScenarioName { get; init; }
    public Guid? EventId { get; init; }
    public Guid? ScenarioId { get; init; }

    /// <summary>Printed as « Date d'export », to the minute.</summary>
    public DateTime ExportedAt { get; init; }
}

public static class PlanDxfExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// The DXF text (AutoCAD 2000, metres, studies' axes from Point0) of the plan, with the layers the options keep.
    /// The loads' values are printed in the current culture, as the page always printed them.
    /// </summary>
    public static string Write(PlanData data, PlanDxfOptions options, PlanDxfInfo info)
    {
        const string imgFile = PlanView.BackgroundImage;

        // Two conventions feed into the file:
        //   • palonniers / mothergrids / charges / intersections : the stored frame, METRES from Point0
        //   • sensors (X/Y attributes)                            : the grid's axes, absolute MILLIMETRES
        // These bring each into the studies' axes.
        static (double X, double Y) Stored(double gridXm, double gridYm) => StudyAxes.ToStudy(gridXm, gridYm);
        (double X, double Y) Absolute(double gridXmm, double gridYmm) =>
            PlanView.StudyMetres(gridXmm, gridYmm, data.Point0X, data.Point0Y);

        // The picture, turned: where the corner that was its lower left lands, and its width (running down,
        // towards A) and height (running right, towards point 9), in metres.
        var (imgX, imgY, imgW, imgH) = PlanView.ImageInStudyMetres(data.Point0X, data.Point0Y);

        // netDxf writes the file. Hand-authoring the DXF skeleton (handles, AcDb subclass markers,
        // symbol tables, block records and the raster-image dictionary chain) proved too easy to get
        // subtly wrong — the library owns all of that and emits a valid document.
        var doc = new netDxf.DxfDocument(netDxf.Header.DxfVersion.AutoCad2000);
        doc.DrawingVariables.InsUnits = netDxf.Units.DrawingUnits.Meters;

        netDxf.Tables.Layer L(string name, short aci)
        {
            var layer = new netDxf.Tables.Layer(name) { Color = new netDxf.AciColor(aci) };
            doc.Layers.Add(layer);
            return layer;
        }

        // Created in this order whatever is exported, so every file has the same table of layers.
        var layBackground    = L("BACKGROUND",    8);
        var layMotherGrid    = L("MOTHERGRID",    1);
        var layPalonniers    = L("PALONNIERS",    5);
        var layCharges       = L("CHARGES",       3);
        var layPalLoads      = L("PAL_LOADS",     6);
        var layIntersections = L("INTERSECTIONS", 1);
        var laySensors       = L("SENSORS",       4);
        var layOrigin        = L("ORIGIN",        7);
        var layDrawingData   = L("DRAWING_DATA",  2);

        void AddLine(netDxf.Tables.Layer layer, double x1, double y1, double x2, double y2)
            => doc.Entities.Add(new netDxf.Entities.Line(
                   new netDxf.Vector2(x1, y1), new netDxf.Vector2(x2, y2)) { Layer = layer });

        void AddCircle(netDxf.Tables.Layer layer, double cx, double cy, double r)
            => doc.Entities.Add(new netDxf.Entities.Circle(new netDxf.Vector2(cx, cy), r) { Layer = layer });

        void AddText(netDxf.Tables.Layer layer, double x, double y, double height, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            // DXF stores one value per line, so strip newlines out of names.
            var clean = text.Replace("\r", " ").Replace("\n", " ");
            doc.Entities.Add(new netDxf.Entities.Text(clean, new netDxf.Vector2(x, y), height) { Layer = layer });
        }

        // Background raster. DXF cannot embed image data — the entity only stores a path — so the
        // page saves the .jpg next to the .dxf.
        if (options.BackgroundImage)
        {
            // Its true size in pixels, and pixels per centimetre: CAD lays the file's pixels out by these.
            var imgDef = new netDxf.Objects.ImageDefinition(
                imgFile, imgFile,
                PlanView.ImageWidthPx, PlanView.ImageWidthPx / (imgW * 100.0),
                PlanView.ImageHeightPx, PlanView.ImageHeightPx / (imgH * 100.0),
                netDxf.Units.ImageResolutionUnits.Centimeters);

            // A quarter turn clockwise: its width runs down towards A and its height right towards point 9,
            // from the corner that was its lower left.
            doc.Entities.Add(new netDxf.Entities.Image(imgDef, new netDxf.Vector2(imgX, imgY), imgW, imgH)
            {
                Layer = layBackground,
                Rotation = PlanView.ImageRotationDeg,
            });
        }

        // Origin axes
        if (options.Origin)
        {
            // Point0 is the origin: X along the beams towards point 9, Y across them towards J.
            AddLine(layOrigin, 0, 0, 2, 0);
            AddLine(layOrigin, 0, 0, 0, 2);
            AddText(layOrigin, 2.1,  0,    0.3, "X");
            AddText(layOrigin, 0,    2.3,  0.3, "Y");
            AddText(layOrigin, 0.2, -0.5,  0.2, "(0,0)");
        }

        // Mother Grids
        if (options.MotherGrids)
            foreach (var m in data.MotherGrids)
            {
                if (!PlanData.TryEnds(m, out var ends)) continue;
                var (x1, y1) = Stored(ends.X1, ends.Y1);
                var (x2, y2) = Stored(ends.X2, ends.Y2);
                AddLine(layMotherGrid, x1, y1, x2, y2);
                AddText(layMotherGrid, (x1 + x2) / 2, (y1 + y2) / 2, 0.15, m.Name);
            }

        // Palonniers
        if (options.Palonniers)
            foreach (var p in data.Palonniers)
            {
                if (!PlanData.TryEnds(p, out var ends)) continue;
                var (x1, y1) = Stored(ends.X1, ends.Y1);
                var (x2, y2) = Stored(ends.X2, ends.Y2);
                AddLine(layPalonniers, x1, y1, x2, y2);
                AddText(layPalonniers, (x1 + x2) / 2, (y1 + y2) / 2, 0.15, p.Name);
            }

        // Charges Scéniques
        if (options.Charges)
            foreach (var c in data.ChargesSceniques)
            {
                if (c.X == null || c.Y == null || c.Charge == null) continue;
                var (cx, cy) = Stored((double)c.X, (double)c.Y);
                AddCircle(layCharges, cx, cy, 0.1);
                AddText(layCharges, cx + 0.15, cy, 0.12, $"{c.Name ?? "Charge"}: {c.Charge:F1}");
            }

        // Palonnier Loads - on a palonnier, or on a mother line the charge hangs from directly. A load of unknown
        // weight is still placed, with nothing after its name.
        if (options.PalonnierLoads)
            foreach (var charge in data.PalonnierLoads)
            {
                if (!data.TryCarriedPosition(charge, out _, out var gx, out var gy)) continue;
                var (cx, cy) = Stored(gx, gy);
                AddCircle(layPalLoads, cx, cy, 0.08);
                AddText(layPalLoads, cx + 0.12, cy, 0.1, $"{charge.Name ?? "Load"}: {charge.Charge:F1}");
            }

        // Intersections
        if (options.Intersections)
            foreach (var kvp in data.IntersectionPoints)
                foreach (var pt in kvp.Value)
                    AddCircle(layIntersections, Stored(pt.X, pt.Y).X, Stored(pt.X, pt.Y).Y, 0.12);

        // Sensors — their X/Y attributes are absolute MILLIMETRES, unlike the event-relative metres
        // used above, so they need converting.
        if (options.Sensors)
            foreach (var sensor in data.Sensors)
            {
                if (!PlanData.TrySensorAbsoluteMm(sensor, out var sxMm, out var syMm)) continue;
                var (sx, sy) = Absolute(sxMm, syMm);
                AddCircle(laySensors, sx, sy, 0.1);
                AddText(laySensors, sx + 0.15, sy, 0.12, sensor.Name ?? "Sensor");
            }

        // ── Drawing data ──────────────────────────────────────────────────────────────────
        // A titled block of metadata on its own layer, laid out below the plan so it never
        // overlaps the geometry. Freeze or turn off DRAWING_DATA in CAD to hide it.
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.ModuleName))    lines.Add($"Module : {info.ModuleName}");
            if (!string.IsNullOrWhiteSpace(info.StructureName)) lines.Add($"Structure : {info.StructureName}");
            if (!string.IsNullOrWhiteSpace(info.EventName))     lines.Add($"Événement : {info.EventName}");
            if (info.EventId.HasValue && info.EventId != Guid.Empty) lines.Add($"ID événement : {info.EventId}");
            if (!string.IsNullOrWhiteSpace(info.ScenarioName))  lines.Add($"Scénario : {info.ScenarioName}");
            if (info.ScenarioId.HasValue && info.ScenarioId != Guid.Empty) lines.Add($"ID scénario : {info.ScenarioId}");
            lines.Add($"Date d'export : {info.ExportedAt.ToString("dd/MM/yyyy HH:mm", Inv)}");
            lines.Add("Unités : mètres");
            // The origin is whatever Point0 the calling page passes, so say which one: naming E5
            // unconditionally told the reader the wrong origin on every page that passes none.
            lines.Add((data.Point0X == 0 && data.Point0Y == 0
                          ? "Axes : ceux des études — aucun Point0 défini, origine au (0,000 ; 0,000) du plan"
                          : $"Axes : ceux des études — origine au Point0 ({data.Point0X.ToString("0.000", Fr)} ; "
                            + $"{data.Point0Y.ToString("0.000", Fr)})")
                      + ", X le long des poutres vers le point 9, Y en travers des poutres vers la poutre J");

            var exported = new List<string>();
            if (options.BackgroundImage)  exported.Add("fond de plan");
            if (options.MotherGrids)      exported.Add("mothergrids");
            if (options.Palonniers)       exported.Add("palonniers");
            if (options.Charges)          exported.Add("charges scéniques");
            if (options.PalonnierLoads)   exported.Add("charges palonniers");
            if (options.Intersections)    exported.Add("intersections");
            if (options.Sensors)          exported.Add("capteurs");
            if (options.Origin)           exported.Add("origine");
            if (exported.Count > 0) lines.Add($"Calques exportés : {string.Join(", ", exported)}");

            const double titleH = 0.6, lineH = 0.35, gap = 0.55;
            double tx = imgX;                  // aligned with the picture's footprint, drawn or not
            double ty = imgY - imgW - 1.2;     // starting below it: its width runs down from (imgX, imgY)
            AddText(layDrawingData, tx, ty, titleH, "DONNÉES DU DESSIN");
            ty -= gap;
            AddLine(layDrawingData, tx, ty + 0.15, tx + 25, ty + 0.15);
            foreach (var line in lines)
            {
                ty -= gap;
                AddText(layDrawingData, tx, ty, lineH, line);
            }
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return reader.ReadToEnd();
    }
}
