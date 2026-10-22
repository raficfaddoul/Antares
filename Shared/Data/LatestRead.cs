using System;
using System.Threading;

namespace AliantWebS.Data
{
    /// <summary>
    /// Latest wins for one kind of read on a page: each <see cref="Begin"/> cancels the read before it, and only the
    /// latest read is <see cref="IsCurrent"/>, so an older read that finishes late is never shown and a superseded read
    /// is not an error. Disposing it (with the page) cancels the current read. Nothing throws after
    /// <see cref="Dispose"/>: a Begin then returns an already-cancelled token.
    ///
    /// Not thread-safe, on purpose: a Blazor Server page calls it only from its circuit's synchronization context (its
    /// handlers, the continuations of their awaits and Dispose all run there, one at a time).
    /// </summary>
    public sealed class LatestRead : IDisposable
    {
        private CancellationTokenSource? current;
        private CancellationToken currentToken = new(canceled: true);   // kept, so .Token is never read from a disposed source
        private bool disposed;

        /// <summary>Starts a read: cancels (then disposes) the previous one's source and returns the new read's token.</summary>
        public CancellationToken Begin()
        {
            if (disposed) return new CancellationToken(canceled: true);
            var previous = current;
            current = new CancellationTokenSource();
            currentToken = current.Token;
            Retire(previous);
            return currentToken;
        }

        /// <summary>True only for the latest Begin's token, while it is not cancelled.</summary>
        public bool IsCurrent(CancellationToken token) =>
            !disposed && token == currentToken && !token.IsCancellationRequested;

        /// <summary>Cancels the current read; later Begins return cancelled tokens.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Retire(current);
            current = null;
        }

        // Cancel BEFORE dispose: a token handed out stays cancelled (and accepts registrations) after its source is gone.
        private static void Retire(CancellationTokenSource? source)
        {
            if (source == null) return;
            source.Cancel();
            source.Dispose();
        }
    }
}
