using AliantWebS.Data;
using Xunit;
using static AliantWebS.Data.UncheckedAlarms;

namespace Shared.Tests.Monitoring;

/// <summary>
/// A reading whose calibration cannot be had is stored raw but never compared with its threshold (Rafic, 2026-10-22).
/// What the operators are told follows the batches: it starts with the first batch that leaves a threshold unchecked,
/// keeps its start time while it lasts, and ends with the first batch that checks everything. The journal is written on
/// the start and the end only (ReportsService), so a long outage is two rows, not one per reading.
/// </summary>
public class UncheckedAlarmsTests
{
    static readonly DateTime T0 = new(2026, 10, 22, 9, 0, 0);
    const string Unreadable = "Les coefficients d'étalonnage (C1/C0) n'ont pas pu être lus : délai dépassé";
    const string Unusable = "Étalonnage inutilisable — Manille 2 / weight : C1_weight = « abc » n'est pas un nombre";

    static (string Reading, string Reason)[] Readings(string reason, params string[] readings) =>
        readings.Select(r => (r, reason)).ToArray();

    static readonly (string, string)[] None = Array.Empty<(string, string)>();

    [Fact]
    public void It_starts_keeps_its_start_while_it_lasts_and_ends_when_every_threshold_is_checked_again()
    {
        var alarms = new UncheckedAlarms();
        int changed = 0;
        alarms.Changed += () => changed++;

        Assert.Equal(Change.None, alarms.Update(None, T0));
        Assert.Null(alarms.Current);

        Assert.Equal(Change.Started, alarms.Update(Readings(Unreadable, "Manille 1 / weight"), T0.AddMinutes(1)));
        Assert.Equal(T0.AddMinutes(1), alarms.Current.Since);

        // The same thing again: nothing new to say.
        Assert.Equal(Change.None, alarms.Update(Readings(Unreadable, "Manille 1 / weight"), T0.AddMinutes(2)));

        // Something else unchecked: said, but it is the same outage.
        Assert.Equal(Change.Updated, alarms.Update(Readings(Unreadable, "Manille 1 / weight", "Manille 2 / weight"), T0.AddMinutes(3)));
        Assert.Equal(T0.AddMinutes(1), alarms.Current.Since);
        Assert.Contains("2 voie(s)", alarms.Current.Message);

        Assert.Equal(Change.Ended, alarms.Update(None, T0.AddMinutes(4)));
        Assert.Null(alarms.Current);
        Assert.Equal(3, changed);

        // A later outage starts again, from its own time.
        Assert.Equal(Change.Started, alarms.Update(Readings(Unusable, "Manille 2 / weight"), T0.AddMinutes(5)));
        Assert.Equal(T0.AddMinutes(5), alarms.Current.Since);
    }

    [Fact]
    public void The_message_gives_each_reason_once_with_how_many_channels_it_stops_and_names_a_few()
    {
        var alarms = new UncheckedAlarms();
        var readings = Readings(Unreadable, Enumerable.Range(1, 7).Select(i => $"Manille {i} / weight").ToArray())
            .Concat(Readings(Unreadable, "Manille 1 / weight"))      // the same channel in another report of the batch
            .Concat(Readings(Unusable, "Manille 9 / weight"))
            .ToArray();

        alarms.Update(readings, T0);

        Assert.Equal(
            $"{Unreadable} (7 voie(s) : Manille 1 / weight, Manille 2 / weight, Manille 3 / weight, Manille 4 / weight, Manille 5 / weight…)"
            + $" ; {Unusable} (1 voie(s) : Manille 9 / weight)",
            alarms.Current.Message);
    }
}
