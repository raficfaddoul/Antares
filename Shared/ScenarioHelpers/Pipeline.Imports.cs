// =============================================================
// Pipeline.Imports.cs — Reading a workbook into the pages.
//
// The placement no longer reads a workbook: the plan is what the Charges page holds. These three are how a
// workbook gets INTO a page - the scenic loads, the drawn elements, and the loads of one palonnier - and
// they are the only remaining callers of the Excel readers.
// =============================================================

using AliantWebS.Data;
using AliantWebS.Models;
using Helpers;
using Microsoft.EntityFrameworkCore;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;

namespace RiggingOptimizer;

public static partial class Pipeline
{
    /// <summary>
    /// What ChargesScenique.Name can hold (nvarchar(50)). An imported name longer than this is left out and
    /// named in the warnings: inserted, it would fail the whole batch with SQL's own English message and
    /// leave the rows stuck in the context for the next save to trip over.
    /// </summary>
    private const int MaxChargeNameLength = 50;

    // ── Réutilisable depuis l'UI ──────────────────────────────────
    /// <param name="warnings">
    /// What the reader noticed and read anyway - a header row skipped, columns read in the fixed order -
    /// and the rows this import left out, in French. Optional so that an existing caller keeps compiling;
    /// the page passes a list and shows it.
    /// </param>
    public static Task<List<ChargesScenique>> ImportChargesSceniquesFromStream(
        Stream stream,
        Guid eventMpscenarioId,
        IEnumerable<string?> existingNames,
        List<string>? warnings = null)
    {
        var charges = ExcelLoader.LoadCharges(stream, warnings);

        // Trimmed and ignoring case, the way the Charges page refuses a duplicate and the way
        // GenerateLayout matches the plan. Compared literally, an imported « l1 » lands beside a typed
        // « L1 » and every later generation then refuses the whole plan for two charges of one name.
        var existing = existingNames
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<ChargesScenique>();
        var kept = new List<string>();     // already on the page, so not imported
        var tooLong = new List<string>();  // longer than the column holds

        foreach (var c in charges)
        {
            var name = c.Name.Trim();

            if (name.Length > MaxChargeNameLength)
            {
                tooLong.Add(name);
                continue;
            }

            if (existing.Contains(name))
            {
                kept.Add(name);
                continue;
            }

            result.Add(new ChargesScenique
            {
                ChargesSceniqueId = Guid.NewGuid(),
                EventMpscenarioId = eventMpscenarioId,
                Name              = name,
                X                 = c.X,
                Y                 = c.Y,
                Charge            = c.Weight,
            });
        }

        // The page's rows are left as they are - whether a re-import should overwrite them is the owner's
        // decision, not ours - but never in silence: since the page IS the plan of charges, a position or
        // a weight corrected in the workbook that changes nothing here would build the study on the old
        // figures, and nothing downstream could notice.
        if (kept.Count > 0)
            warnings?.Add($"{kept.Count} charge(s) du fichier portent un nom déjà présent sur la page : "
                          + $"{NamedRows(kept)}. Les valeurs de la page ont été conservées ; corrigez-les "
                          + "sur la page, ou supprimez ces lignes avant de réimporter.");

        if (tooLong.Count > 0)
            warnings?.Add($"{tooLong.Count} charge(s) non importée(s), nom de plus de "
                          + $"{MaxChargeNameLength} caractères : {NamedRows(tooLong)}.");

        return Task.FromResult(result);
    }

    // ── Réutilisable depuis l'UI ──────────────────────────────────
    public static Task<List<MotherGridPalonnier>> ImportMotherGridPalonniersFromStream(
        Stream stream,
        Guid eventMpscenarioId,
        bool famille,
        IEnumerable<ElementType> elementTypes,
        IEnumerable<string> existingNames,
        out List<string> warnings)  // ← unknown ElementType names, and what the reader noticed
    {
        // Built before the file is read, because the reader adds its own notes to it: a header row it
        // skipped, columns it read in the fixed order, a row whose coordinates it could not read.
        warnings = new List<string>();
        var rows = ExcelLoader.LoadMotherGridPalonniers(stream, warnings);

        // Trimmed and ignoring case: a name is a name everywhere in this application, and the pages
        // compare them that way.
        var existing = existingNames
            .Select(n => (n ?? string.Empty).Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var etList = elementTypes.ToList();

        var result = new List<MotherGridPalonnier>();

        foreach (var r in rows.Where(r => !existing.Contains(r.Name.Trim())))
        {
            var et = etList.FirstOrDefault(x =>
                x.Name.Equals(r.ElementTypeName, StringComparison.OrdinalIgnoreCase));

            if (et == null)
            {
                // ── Skip row and warn instead of silently using wrong type ──
                warnings.Add($"Row '{r.Name}': ElementType '{r.ElementTypeName}' not found — skipped.");
                continue;
            }

            // Column G: the pieces, "5,6+2,8". Unreadable is said, never guessed.
            string? composition = null;
            if (r.Composition is not null)
            {
                var pieces = TrussComposition.ParseText(r.Composition);
                if (pieces is null)
                    warnings.Add($"Row '{r.Name}': pieces '{r.Composition}' unreadable — ignored.");
                else
                    composition = TrussComposition.Serialize(pieces);
            }

            result.Add(new MotherGridPalonnier
            {
                MotherGridPalonnierId = Guid.NewGuid(),
                EventMpscenarioId     = eventMpscenarioId,
                Famille               = famille,
                Name                  = r.Name,
                ElementTypeId         = et.ElementTypeId,
                X1                    = r.X1,
                Y1                    = r.Y1,
                X2                    = r.X2,
                Y2                    = r.Y2,
                Composition           = composition,
            });
        }

        return Task.FromResult(result);
    }

    // ── Réutilisable depuis l'UI ──────────────────────────────────
    public static Task<List<ChargeMp>> ImportChargeMpsFromStream(
        Stream stream,
        MotherGridPalonnier palonnier,
        IEnumerable<string> existingNames,
        out List<string> warnings,   // ← rows left out, and what the reader noticed
        int startNumero = 1)
    {
        // Built before the file is read: the reader adds its own notes to this same list - a header row it
        // skipped, columns it read in the fixed order - so the page shows them with the skipped rows.
        warnings = new List<string>();
        var charges = ExcelLoader.LoadCharges(stream, warnings);

        double dx  = (palonnier.X2 ?? 0) - (palonnier.X1 ?? 0);
        double dy  = (palonnier.Y2 ?? 0) - (palonnier.Y1 ?? 0);
        double len = Math.Sqrt(dx * dx + dy * dy);

        // Trimmed and ignoring case: a name is a name everywhere in this application, and the pages
        // compare them that way.
        var existing = existingNames
            .Select(n => (n ?? string.Empty).Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ChargeMp>();

        foreach (var (c, j) in charges
            .Where(c => !existing.Contains(c.Name.Trim()))
            .Select((c, j) => (c, j)))
        {
            double distAlongBar = len > 0
                ? ((c.X - (palonnier.X1 ?? 0)) * dx + (c.Y - (palonnier.Y1 ?? 0)) * dy) / len
                : 0;

            // ── Warn if charge falls outside the bar bounds ───────
            if (distAlongBar < 0 || distAlongBar > len)
            {
                warnings.Add($"Row '{c.Name}': distAlongBar={distAlongBar:F2}m is outside bar length [{0}, {len:F2}m] — skipped.");
                continue;
            }

            result.Add(new ChargeMp
            {
                ChargeMpid            = Guid.NewGuid(),
                MotherGridPalonnierId = palonnier.MotherGridPalonnierId,
                Name                  = c.Name,
                Numero                = startNumero + j,
                X                     = distAlongBar,
                Charge                = c.Weight,
            });
        }

        return Task.FromResult(result);
    }
}
