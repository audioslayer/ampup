using System.Collections.Concurrent;

namespace AmpUp.Core.Services;

/// <summary>
/// Runs hardware input handlers away from device read loops while preserving
/// event order. High-frequency absolute inputs can be coalesced by key so a
/// slow target never builds a backlog of stale positions.
/// </summary>
public sealed class HardwareInputPump : IDisposable
{
    private sealed class WorkItem
    {
        public required Func<string> DescribeSource { get; init; }
        public required Action Action { get; init; }
        public long QueuedAtTick { get; init; }
        public int? CoalesceKey { get; init; }
    }

    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly object _coalesceGate = new();
    private readonly Dictionary<int, WorkItem> _latestByKey = new();
    private readonly HashSet<int> _scheduledKeys = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _worker;
    private readonly int _slowThresholdMs;
    private int _disposed;

    public HardwareInputPump(int slowThresholdMs = 1000)
    {
        _slowThresholdMs = Math.Max(1, slowThresholdMs);
        _worker = new Thread(ProcessLoop)
        {
            IsBackground = true,
            Name = "AmpUp hardware input",
        };
        _worker.Start();
    }

    /// <summary>Queues a discrete event whose ordering must be preserved.</summary>
    public void Queue(Func<string> describeSource, Action action)
    {
        ArgumentNullException.ThrowIfNull(describeSource);
        ArgumentNullException.ThrowIfNull(action);

        TryAdd(new WorkItem
        {
            DescribeSource = describeSource,
            Action = action,
            QueuedAtTick = Environment.TickCount64,
        });
    }

    /// <summary>
    /// Queues an absolute-value event. If the same key is already waiting,
    /// its stale work item is replaced by this newest value.
    /// </summary>
    public void QueueLatest(int key, Func<string> describeSource, Action action)
    {
        ArgumentNullException.ThrowIfNull(describeSource);
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref _disposed) != 0) return;

        var item = new WorkItem
        {
            DescribeSource = describeSource,
            Action = action,
            QueuedAtTick = Environment.TickCount64,
            CoalesceKey = key,
        };

        lock (_coalesceGate)
        {
            if (_disposed != 0) return;

            _latestByKey[key] = item;
            if (!_scheduledKeys.Add(key))
                return;

            // The queued marker carries no stale action. The consumer resolves
            // the newest work item for this key when the marker reaches it.
            if (!TryAdd(item))
            {
                _scheduledKeys.Remove(key);
                _latestByKey.Remove(key);
            }
        }
    }

    private bool TryAdd(WorkItem item)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;

        try
        {
            return _queue.TryAdd(item);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ProcessLoop()
    {
        try
        {
            foreach (var queuedItem in _queue.GetConsumingEnumerable(_cts.Token))
            {
                WorkItem? item = queuedItem;
                if (queuedItem.CoalesceKey is int key)
                {
                    lock (_coalesceGate)
                    {
                        _latestByKey.Remove(key, out item);
                        _scheduledKeys.Remove(key);
                    }
                }

                if (item != null)
                    Execute(item);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void Execute(WorkItem item)
    {
        long startedAt = Environment.TickCount64;
        long queueDelayMs = startedAt - item.QueuedAtTick;
        if (queueDelayMs >= _slowThresholdMs)
            Logger.Log($"Hardware input delayed ({item.DescribeSource()}): queued {queueDelayMs}ms");

        try
        {
            item.Action();
        }
        catch (Exception ex)
        {
            Logger.Log($"Hardware input handler failed ({item.DescribeSource()}): {ex.Message}");
        }
        finally
        {
            long elapsedMs = Environment.TickCount64 - startedAt;
            if (elapsedMs >= _slowThresholdMs)
                Logger.Log($"Hardware input handler slow ({item.DescribeSource()}): {elapsedMs}ms");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        lock (_coalesceGate)
        {
            _latestByKey.Clear();
            _scheduledKeys.Clear();
        }

        _cts.Cancel();
        _queue.CompleteAdding();
        bool stopped = Thread.CurrentThread != _worker
            && _worker.Join(TimeSpan.FromSeconds(2));

        // A handler can be inside a slow native API during shutdown. Leave
        // these tiny objects for process teardown rather than disposing them
        // underneath a worker that has not returned yet.
        if (stopped)
        {
            _queue.Dispose();
            _cts.Dispose();
        }
    }
}
