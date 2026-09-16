using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// A report name that matches no sensor is said once, not once per reading.
///
/// Said per reading it was about 1 500 console lines a second during a replay of months of reports, which
/// slowed the application until its circuits dropped. The set behind the rule lives for the whole process, so
/// every test below uses names of its own.
/// </summary>
public class UnknownReportNameTests
{
    private static string Fresh(string stem) => $"{stem}_{Guid.NewGuid():N}";

    [Fact]
    public void A_name_is_said_the_first_time_and_never_again()
    {
        var name = Fresh("Manille_C2");

        Assert.True(ReportsService.IsFirstUnknownReportName(name), "la première occurrence doit être signalée");
        Assert.False(ReportsService.IsFirstUnknownReportName(name), "la deuxième occurrence ne doit plus l'être");
        Assert.False(ReportsService.IsFirstUnknownReportName(name), "ni aucune des suivantes");
    }

    [Fact]
    public void A_name_the_lookup_takes_for_the_same_sensor_is_the_same_name_here()
    {
        // The sensor lookup trims and ignores case; a line per spelling would bring the flood back in pieces.
        var name = Fresh("Inclino_F9");

        Assert.True(ReportsService.IsFirstUnknownReportName(name));
        Assert.False(ReportsService.IsFirstUnknownReportName(name.ToUpperInvariant()));
        Assert.False(ReportsService.IsFirstUnknownReportName("  " + name + " "));
    }

    [Fact]
    public void Each_distinct_name_is_still_said_once()
    {
        // Once per NAME, not once in all: the log must still list every sensor that is missing.
        var first = Fresh("Hygro_E5");
        var second = Fresh("Extenso_B9");

        Assert.True(ReportsService.IsFirstUnknownReportName(first));
        Assert.True(ReportsService.IsFirstUnknownReportName(second));
    }

    [Fact]
    public void A_reading_with_no_name_does_not_throw()
    {
        // The server's report_data.name can be missing; the collection loop must not fall over on it.
        var ex = Record.Exception(() => ReportsService.IsFirstUnknownReportName(null));
        Assert.Null(ex);
    }

    [Fact]
    public void Many_readings_of_the_same_names_at_once_are_said_once_each()
    {
        // The collection loop and anything else may ask concurrently; the answer must still be "once".
        var names = Enumerable.Range(0, 20).Select(i => Fresh($"Deforma_{i}")).ToList();
        int said = 0;

        Parallel.For(0, 2_000, i =>
        {
            if (ReportsService.IsFirstUnknownReportName(names[i % names.Count]))
                Interlocked.Increment(ref said);
        });

        Assert.Equal(names.Count, said);
    }
}
