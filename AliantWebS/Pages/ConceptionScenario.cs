// =============================================================
// ConceptionScenario.cs — Which scenario a conception page shows, and the event it belongs to.
//
// The pages under AntaresConceptionLayout (Charges Scéniques, Mother Grid, Charpente, Modèle 3D) carry their scenario in
// their address. Until 2026-09-18 the layout never read it: the browser's Back after a switch of the dropdown, Back past a
// change of event, or a download link (which left the NavigationManager on the file's address, so the dropdown stopped
// navigating) left the dropdown on one scenario and the page on another - and « Calcul Charpente » wrote the page's loads
// into the dropdown's Charpente row, weighed with the dropdown's inventory.
//
// The address is the truth. The layout follows it (ConceptionRoute), and what calculates or draws for the scenario on
// screen resolves it from the page's own id (ConceptionScenario.ResolveAsync), never from the dropdown.
// =============================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Pages;

public static class ConceptionRoute
{
    /// <summary>The conception pages, as their routes name them: "{page}/{id}". The id is a scenario's, except that
    /// Charges Scéniques is also opened with its event's (Events, EventForm).</summary>
    public static readonly IReadOnlyList<string> Pages = new[] { "ChargesSceniques", "MotherGrid", "Charpente", "Model3D" };

    /// <summary>
    /// "MotherGrid/{guid}", relative to the application's base, any case, query and fragment ignored → ("MotherGrid",
    /// guid), the page named as <see cref="Pages"/> spells it. False for any other path - MotherGridComponent/…,
    /// ScenarioFiles/Download?…, a page without its id.
    /// </summary>
    public static bool TryParse(string? baseRelativePath, out string page, out Guid id)
    {
        page = "";
        id = Guid.Empty;
        if (string.IsNullOrEmpty(baseRelativePath)) return false;

        string path = baseRelativePath;
        int end = path.IndexOfAny(new[] { '?', '#' });
        if (end >= 0) path = path[..end];

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2) return false;

        string? name = Pages.FirstOrDefault(p => string.Equals(p, segments[0], StringComparison.OrdinalIgnoreCase));
        if (name is null || !Guid.TryParse(segments[1], out id)) return false;

        page = name;
        return true;
    }

    /// <summary>The same, for an address as the NavigationManager hands them out - absolute (a link, Back) or relative to
    /// the base (NavigateTo). False outside the application.</summary>
    public static bool TryParse(NavigationManager navigation, string location, out string page, out Guid id)
    {
        page = "";
        id = Guid.Empty;
        string relative;
        try
        {
            relative = navigation.ToBaseRelativePath(navigation.ToAbsoluteUri(location).AbsoluteUri);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return TryParse(relative, out page, out id);
    }
}

/// <summary>A scenario and the event it belongs to.</summary>
public sealed record ConceptionScenario(Event Event, EventMpscenario Scenario)
{
    /// <summary>
    /// The scenario of this id and its event. The layout's own objects when it lists that scenario under the event it
    /// shows - no query, and the very names its folders were resolved with; otherwise read from the database through a
    /// context of its own, without tracking (the circuit's shared context may be busy with the page, and these copies are
    /// only ever read). Null for Guid.Empty, or when the scenario or its event no longer exists.
    /// </summary>
    public static async Task<ConceptionScenario?> ResolveAsync(EntitiesService db, AntaresConceptionLayout? layout, Guid scenarioId)
    {
        if (scenarioId == Guid.Empty) return null;

        var listed = layout?.ScenarioList.FirstOrDefault(s => s.EventMpscenarioId == scenarioId);
        if (listed != null && layout!.selectedEvent is { } shown && shown.EventId == listed.EventId)
            return new ConceptionScenario(shown, listed);

        await using var context = db.CreateOwnedContext();
        var scenario = await context.EventMpscenarios.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EventMpscenarioId == scenarioId);
        if (scenario is null) return null;
        var ev = await context.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == scenario.EventId);
        return ev is null ? null : new ConceptionScenario(ev, scenario);
    }

    /// <summary>
    /// What an id of a conception page's address names, read from the database through a context of its own: a
    /// scenario (its event, and itself), an event (itself, no scenario) - Charges Scéniques is opened with an event's id
    /// -, or nothing (null): a scenario deleted since.
    /// </summary>
    public static async Task<(Guid EventId, Guid? ScenarioId)?> LookUpAsync(EntitiesService db, Guid id)
    {
        await using var context = db.CreateOwnedContext();
        var eventOfScenario = await context.EventMpscenarios.AsNoTracking()
            .Where(s => s.EventMpscenarioId == id)
            .Select(s => (Guid?)s.EventId)
            .FirstOrDefaultAsync();
        if (eventOfScenario is { } eventId) return (eventId, id);
        if (await context.Events.AsNoTracking().AnyAsync(e => e.EventId == id)) return (id, null);
        return null;
    }
}
