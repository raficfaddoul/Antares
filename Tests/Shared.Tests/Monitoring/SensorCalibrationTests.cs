using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static AliantWebS.Data.SensorCalibration;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The calibration y = C1·x + C0 must never turn into raw values that pass for calibrated ones (Rafic, 2026-10-22).
/// A load that fails, or a coefficient that is not a number, is a CalibrationReadException for whoever asks for those
/// values: the pages show it where they show an unreadable InfluxDB, the export is refused, and the alarm checks skip
/// those readings (UncheckedAlarmsTests). No calibration configured (no database, no coefficient) stays identity.
///
/// The load itself (SQL) is tested on LocalDB in the web harness; here the database is a factory that fails.
/// </summary>
public class SensorCalibrationTests : IDisposable
{
    const string Weight = "11111111-1111-1111-1111-111111111111";
    const string Temperature = "22222222-2222-2222-2222-222222222222";
    static readonly Guid SensorA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid SensorB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    static string A => SensorA.ToString();
    static string B => SensorB.ToString();

    static readonly (Guid Id, string Name)[] Channels =
        { (Guid.Parse(Weight), "weight"), (Guid.Parse(Temperature), "temperature") };
    static readonly HashSet<string> C1Names = new(StringComparer.OrdinalIgnoreCase) { "C1", "Pente" };
    static readonly HashSet<string> C0Names = new(StringComparer.OrdinalIgnoreCase) { "C0" };

    const string LoadFailed = "Les coefficients d'étalonnage (C1/C0) n'ont pas pu être lus";

    private readonly IDbContextFactory<AliantWebContext> savedFactory = DbFactory;

    public SensorCalibrationTests() => Invalidate();

    public void Dispose()
    {
        DbFactory = savedFactory;
        Invalidate();
    }

    /// <summary>A database that cannot be reached: every context it is asked for fails, and it counts the asks.</summary>
    sealed class FailingFactory : IDbContextFactory<AliantWebContext>
    {
        public int Created;
        public AliantWebContext CreateDbContext()
        {
            Interlocked.Increment(ref Created);
            throw new InvalidOperationException("Base injoignable (test).");
        }
    }

    static CalibrationTable Table(params (Guid Sensor, string Name, string Value)[] attributes) =>
        CalibrationTable.FromAttributes(
            attributes.Select(a => new CalibrationTable.Attribute(a.Sensor, a.Sensor == SensorA ? "Manille A" : "Manille B", a.Name, a.Value)),
            Channels, C1Names, C0Names);

    static Dictionary<DateTime, Dictionary<string, double>> Export(params (string Pair, double Value)[] values) =>
        new() { [new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)] = values.ToDictionary(v => v.Pair, v => v.Value) };

    // ── A load that fails ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_failed_load_is_a_CalibrationReadException_for_every_reader_never_raw_values()
    {
        DbFactory = new FailingFactory();

        var plan = await Assert.ThrowsAsync<CalibrationReadException>(() => BuildPlanAsync(new[] { A }, new[] { Weight }, "max"));
        Assert.StartsWith(LoadFailed, plan.Message);
        Assert.Contains("Base injoignable (test).", plan.Message);
        Assert.IsType<InvalidOperationException>(plan.InnerException);

        // The pages catch InfluxReadException: a calibration that cannot be read is shown as unreadable data.
        Assert.IsAssignableFrom<InfluxReadException>(plan);

        var data = Export(($"{A}|{Weight}".ToLowerInvariant(), 10.0));
        var export = await Assert.ThrowsAsync<CalibrationReadException>(() => ApplyToExportAsync(data));
        Assert.StartsWith(LoadFailed, export.Message);
        Assert.Equal(10.0, data.Values.Single().Values.Single());   // untouched, and nothing written

        var table = await LoadTableAsync();
        var alarm = Assert.Throws<CalibrationReadException>(() => table.For(A, Weight));
        Assert.StartsWith(LoadFailed, alarm.Message);
        Assert.IsType<InvalidOperationException>(alarm.InnerException);
    }

    [Fact]
    public async Task A_failed_load_is_not_cached_so_the_next_read_tries_again()
    {
        var factory = new FailingFactory();
        DbFactory = factory;

        await Assert.ThrowsAsync<CalibrationReadException>(() => BuildPlanAsync(new[] { A }, new[] { Weight }, null));
        await Assert.ThrowsAsync<CalibrationReadException>(() => BuildPlanAsync(new[] { A }, new[] { Weight }, null));

        Assert.Equal(2, factory.Created);
    }

    [Fact]
    public async Task Without_a_database_nothing_is_calibrated()
    {
        // DbFactory null: no calibration configured (tests, probes), which is not a failure.
        DbFactory = null;

        Assert.Equal(CalibrationMode.None, (await BuildPlanAsync(new[] { A }, new[] { Weight }, "max")).Mode);

        var data = Export(($"{A}|{Weight}", 10.0));
        await ApplyToExportAsync(data);
        Assert.Equal(10.0, data.Values.Single().Values.Single());

        Assert.Equal((1.0, 0.0), (await LoadTableAsync()).For(A, Weight));
    }

    [Fact]
    public void An_unreadable_table_refuses_every_channel_with_the_loads_cause()
    {
        var cause = new TimeoutException("Délai SQL dépassé (test).");
        var table = CalibrationTable.Unreadable(new CalibrationReadException($"{LoadFailed} : {cause.Message}", cause));

        var ex = Assert.Throws<CalibrationReadException>(() => table.For(B, Temperature));
        Assert.Equal($"{LoadFailed} : Délai SQL dépassé (test).", ex.Message);
        Assert.Same(cause, ex.InnerException);
        Assert.Throws<CalibrationReadException>(() => BuildPlan(table, new[] { B }, new[] { Temperature }, "mean"));
    }

    // ── The coefficients as typed ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("2,5", 2.5)]          // typed under the French locale
    [InlineData(" -1e-3 ", -0.001)]
    [InlineData("0", 0.0)]
    public void A_coefficient_is_a_finite_number_with_a_point_or_a_comma(string raw, double expected)
    {
        Assert.True(CalibrationTable.TryParseCoefficient(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    [InlineData("1 000,5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("∞")]
    [InlineData("1e400")]             // overflows to infinity
    public void Anything_else_is_not_a_coefficient(string raw)
    {
        Assert.False(CalibrationTable.TryParseCoefficient(raw, out _));
    }

    [Fact]
    public void Coefficients_are_found_under_every_accepted_name_whatever_the_case()
    {
        var table = Table((SensorA, "pente_WEIGHT", "2,5"), (SensorA, "C0_weight", "-1"), (SensorB, "C0_temperature", "3"));

        Assert.Equal((2.5, -1.0), table.For(A, Weight));
        Assert.Equal((1.0, 3.0), table.For(B, Temperature));
        Assert.Equal((1.0, 0.0), table.For(B, Weight));            // nothing set: identity
        Assert.Equal((2.5, -1.0), table.For(A.ToUpperInvariant(), Weight.ToUpperInvariant()));
    }

    [Fact]
    public void A_blank_coefficient_is_not_set_and_identity_is_never_mapped()
    {
        var table = Table((SensorA, "C1_weight", "  "), (SensorA, "C0_weight", ""), (SensorB, "C1_weight", "1"), (SensorB, "C0_weight", "0"));

        Assert.Equal((1.0, 0.0), table.For(A, Weight));
        Assert.Empty(table.UnusableChannels);
        Assert.Equal(CalibrationMode.None, BuildPlan(table, new[] { A, B }, new[] { Weight }, "max").Mode);
    }

    [Fact]
    public void An_unusable_coefficient_fails_its_own_channel_only_and_says_which()
    {
        var table = Table((SensorA, "C1_weight", "abc"), (SensorA, "C1_temperature", "2"), (SensorB, "C1_weight", "3"));

        var ex = Assert.Throws<CalibrationReadException>(() => table.For(A, Weight));
        Assert.Contains("Manille A / weight", ex.Message);
        Assert.Contains("C1_weight = « abc » n'est pas un nombre", ex.Message);
        Assert.Single(table.UnusableChannels);

        Assert.Equal((2.0, 0.0), table.For(A, Temperature));
        Assert.Equal((3.0, 0.0), table.For(B, Weight));

        // A query reads every sensor × channel it names: it fails when one of them is unusable, not otherwise.
        Assert.Throws<CalibrationReadException>(() => BuildPlan(table, new[] { A, B }, new[] { Weight }, "max"));
        Assert.Equal(CalibrationMode.PostMap, BuildPlan(table, new[] { B }, new[] { Weight }, "max").Mode);
        Assert.Equal(CalibrationMode.PostMap, BuildPlan(table, new[] { A }, new[] { Temperature }, "max").Mode);
    }

    // ── Where the plan puts the calibration (unchanged by the move to CalibrationTable) ──────────────────────────

    [Theory]
    [InlineData(null, CalibrationMode.PreMap)]
    [InlineData("mean", CalibrationMode.PostMap)]
    [InlineData("max", CalibrationMode.PostMap)]
    [InlineData("sum", CalibrationMode.PreMap)]
    public void A_positive_slope_maps_after_any_order_preserving_aggregation(string aggregateFn, CalibrationMode mode)
    {
        var plan = BuildPlan(Table((SensorA, "C1_weight", "2")), new[] { A, B }, new[] { Weight }, aggregateFn);

        Assert.Equal(mode, plan.Mode);
        Assert.Equal($"|> map(fn: (r) => ({{ r with _value: if r.sensor == \"{A}\" and r._field == \"{Weight}\" then 2.0 * r._value + 0.0 else r._value }}))",
            plan.MapClause);
    }

    [Fact]
    public void A_negative_slope_splits_max_so_that_pair_aggregates_with_min()
    {
        var plan = BuildPlan(Table((SensorA, "C1_weight", "-2")), new[] { A, B }, new[] { Weight, Temperature }, "max");

        Assert.Equal(CalibrationMode.Split, plan.Mode);
        Assert.Contains(plan.Branches, b => b.Fn == "min" && b.Filter == $"(r.sensor == \"{A}\") and (r._field == \"{Weight}\")");
        Assert.Contains(plan.Branches, b => b.Fn == "max" && b.Filter == $"(r.sensor == \"{B}\") and (r._field == \"{Weight}\")");
        Assert.Contains(plan.Branches, b => b.Fn == "max" && b.Filter.EndsWith($"(r._field == \"{Temperature}\")"));
    }

    // ── The export ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_export_holds_reported_values()
    {
        var table = Table((SensorA, "C1_weight", "2"), (SensorA, "C0_weight", "1"));
        var data = Export(($"{A}|{Weight}", 10.0), ($"{B}|{Weight}", 10.0));

        ApplyToExport(table, data);

        Assert.Equal(21.0, data.Values.Single()[$"{A}|{Weight}"]);
        Assert.Equal(10.0, data.Values.Single()[$"{B}|{Weight}"]);
    }

    [Fact]
    public void An_export_holding_an_unusable_channel_is_refused_before_any_value_changes()
    {
        var table = Table((SensorA, "C1_weight", "2"), (SensorB, "C0_weight", "deux"));
        var data = Export(($"{A}|{Weight}", 10.0), ($"{B}|{Weight}", 10.0));

        var ex = Assert.Throws<CalibrationReadException>(() => ApplyToExport(table, data));
        Assert.Contains("C0_weight = « deux »", ex.Message);
        Assert.All(data.Values.Single().Values, v => Assert.Equal(10.0, v));
    }

    [Fact]
    public void An_export_key_that_is_not_a_sensor_channel_pair_is_refused()
    {
        var data = Export(("no-separator", 10.0));

        Assert.Throws<ArgumentException>(() => ApplyToExport(CalibrationTable.Identity, data));
    }
}
