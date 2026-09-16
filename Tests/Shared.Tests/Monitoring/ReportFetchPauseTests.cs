using System.Net;
using System.Text;
using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The collection pause, on the one property that matters when it interrupts a replay of months of reports:
/// nothing read is lost, nothing is counted twice, and the next fetch starts right after the last report
/// processed.
///
/// The collecting server is faked with an HTTP handler that serves a JSON array of reports - the same stream
/// ReportsService.GetReportsAsync reads in production - so the rule is checked on the real reader, batching
/// and all, with no network, no InfluxDB and no database.
/// </summary>
public class ReportFetchPauseTests
{
    private const int ReportCount = 250;

    /// <summary>A collecting server that answers every request with the same reports, timestamps 1..N.</summary>
    private sealed class FakeCollector : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            token.ThrowIfCancellationRequested();

            var json = new StringBuilder("[");
            for (var i = 1; i <= ReportCount; i++)
            {
                if (i > 1) json.Append(',');
                json.Append("{\"report_type\":\"t\",\"report_version\":1,\"report_datetime\":")
                    .Append(i)
                    .Append(",\"report_data\":[]}");
            }
            json.Append(']');

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task Without_a_pause_the_whole_stream_is_read()
    {
        // The default: a token nobody cancels changes nothing about what a fetch reads.
        var processed = new List<long>();
        using var http = new HttpClient(new FakeCollector());

        var (count, max) = await ReportsService.GetReportsAsync(0, http,
            batch => { processed.AddRange(batch.Select(r => r.report_datetime)); return Task.CompletedTask; },
            batchSize: 100);

        Assert.Equal(ReportCount, count);
        Assert.Equal(ReportCount, max);
        Assert.Equal(ReportCount, processed.Count);
    }

    [Fact]
    public async Task A_pause_in_the_middle_of_a_replay_stops_it_and_loses_nothing_it_had_read()
    {
        // Paused while the first batch is being processed - the moment a person clicks during a long replay.
        using var stop = new CancellationTokenSource();
        var processed = new List<long>();
        using var http = new HttpClient(new FakeCollector());

        var (count, max) = await ReportsService.GetReportsAsync(0, http,
            batch =>
            {
                processed.AddRange(batch.Select(r => r.report_datetime));
                stop.Cancel();
                return Task.CompletedTask;
            },
            batchSize: 100,
            stopToken: stop.Token);

        // It stopped: the rest of the replay was not read.
        Assert.True(count < ReportCount, $"la collecte suspendue a lu {count} rapports sur {ReportCount}");

        // Everything counted was processed, once each - the reader's count and the processed list agree.
        Assert.Equal(count, processed.Count);
        Assert.Equal(processed.Count, processed.Distinct().Count());

        // The resume point is the last report processed, not one read and dropped: the next fetch starts
        // from `max`, so a report above it that was never processed must not exist.
        Assert.Equal(processed.Max(), max);
    }

    [Fact]
    public async Task A_pause_already_in_force_reads_nothing_and_moves_the_resume_point_nowhere()
    {
        // The next cycle after a pause, or a start with the pause persisted: no report, and a MaxTimestamp of
        // zero, which the fetch loop reads as "do not advance".
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var processed = 0;
        using var http = new HttpClient(new FakeCollector());

        var (count, max) = await ReportsService.GetReportsAsync(0, http,
            batch => { processed += batch.Count; return Task.CompletedTask; },
            batchSize: 100,
            stopToken: stop.Token);

        Assert.Equal(0, count);
        Assert.Equal(0, max);
        Assert.Equal(0, processed);
    }
}
