using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// Latest wins for one kind of read (decision 7): a new read cancels the previous one, only the newest read may show
/// its result even when an older one finishes after it, and disposing it (with the page) cancels
/// the current read, and nothing throws once the page is gone.
/// </summary>
public class LatestReadTests
{
    [Fact]
    public void Begin_cancels_the_previous_read()
    {
        using var reads = new LatestRead();
        var first = reads.Begin();
        var second = reads.Begin();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Only_the_latest_token_is_current_even_when_the_old_read_finished_late()
    {
        using var reads = new LatestRead();
        var first = reads.Begin();
        var second = reads.Begin();

        // The first read's result arrives now, after the second began: it must not be applied.
        Assert.False(reads.IsCurrent(first));
        Assert.True(reads.IsCurrent(second));
        Assert.False(reads.IsCurrent(CancellationToken.None));
    }

    [Fact]
    public void An_old_token_still_reports_cancelled_and_accepts_a_registration_after_a_newer_Begin()
    {
        using var reads = new LatestRead();
        var old = reads.Begin();
        reads.Begin();                                    // the old read's source is cancelled, then disposed

        bool called = false;
        using (old.Register(() => called = true)) { }     // what an HTTP client does with the token: no ObjectDisposedException
        Assert.True(called);
        Assert.True(old.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => old.ThrowIfCancellationRequested());
    }

    [Fact]
    public void Begin_after_Dispose_returns_a_cancelled_token()
    {
        var reads = new LatestRead();
        var current = reads.Begin();

        reads.Dispose();
        var late = reads.Begin();

        Assert.True(current.IsCancellationRequested);
        Assert.True(late.IsCancellationRequested);
        Assert.False(reads.IsCurrent(late));
        reads.Dispose();                                  // twice: nothing to do, nothing thrown
    }
}
