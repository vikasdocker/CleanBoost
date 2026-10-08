using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;

namespace CleanBoost.App.Services;

/// <summary>
/// An <see cref="IProgress{T}"/> that does not marshal one callback per report.
///
/// <see cref="System.Progress{T}"/> posts to the UI thread on every single call.
/// A clean that removes 40,000 files would post 40,000 callbacks and turn the
/// live activity list into the very freeze we are trying to remove.
///
/// This class instead parks reports on a concurrent queue and drains a bounded
/// number per timer tick on the UI thread. Producers never block and the UI does
/// bounded work. If producers outrun the drain, the oldest queued rows are dropped
/// and counted — reports themselves carry a monotonic index, so the progress bar
/// stays exact regardless.
/// </summary>
public sealed class ThrottledProgress<T> : IProgress<T>, IDisposable
{
    private readonly ConcurrentQueue<T> _pending = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly Action<IReadOnlyList<T>> _onFlush;
    private readonly Action<T>? _observer;
    private readonly int _maxPerTick;
    private readonly int _queueCeiling;
    private readonly List<T> _scratch = new();
    private long _dropped;
    private bool _disposed;

    /// <param name="queue">The UI thread's dispatcher queue.</param>
    /// <param name="onFlush">Invoked on the UI thread with a batch of reports.</param>
    /// <param name="observer">
    /// Optional. Invoked on the <i>reporting</i> thread for every value, before it is
    /// queued. Use this for exact running totals that must not be affected by dropped
    /// rows — keep them lock-free, the reporting thread is a worker.
    /// </param>
    /// <param name="maxPerTick">Upper bound on rows handed to the UI per tick.</param>
    /// <param name="queueCeiling">Backlog size past which the oldest rows are dropped.</param>
    public ThrottledProgress(DispatcherQueue queue,
                             Action<IReadOnlyList<T>> onFlush,
                             Action<T>? observer = null,
                             int maxPerTick = 200,
                             int queueCeiling = 5000,
                             TimeSpan? interval = null)
    {
        _onFlush = onFlush;
        _observer = observer;
        _maxPerTick = Math.Max(1, maxPerTick);
        _queueCeiling = Math.Max(_maxPerTick, queueCeiling);

        _timer = queue.CreateTimer();
        _timer.Interval = interval ?? TimeSpan.FromMilliseconds(100);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Reports discarded because the UI could not keep up.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Reports currently waiting to reach the UI thread.</summary>
    public int PendingCount => _pending.Count;

    public void Report(T value)
    {
        _observer?.Invoke(value);
        _pending.Enqueue(value);
    }

    /// <summary>
    /// Hands every still-queued value to <c>onFlush</c> immediately. Must be called
    /// from the queue's thread.
    ///
    /// Without this a run that finishes faster than one timer tick loses its entire
    /// activity list — a quick clean of a few dozen files completes in well under
    /// 100ms, so the timer never fires and the list the user is watching stays blank.
    /// </summary>
    public void Flush()
    {
        while (_pending.TryDequeue(out var item))
            _onFlush(new[] { item });
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || _pending.IsEmpty)
            return;

        _scratch.Clear();
        while (_scratch.Count < _maxPerTick && _pending.TryDequeue(out var item))
            _scratch.Add(item);

        // Still badly backed up — shed the oldest so memory stays bounded.
        while (_pending.Count > _queueCeiling && _pending.TryDequeue(out _))
            Interlocked.Increment(ref _dropped);

        if (_scratch.Count > 0)
            _onFlush(_scratch);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;

        // Deliver whatever the timer never got to. Callers dispose this from the UI
        // thread right after the run finishes.
        Flush();
    }
}