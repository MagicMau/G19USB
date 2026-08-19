using System;
using System.Collections.Generic;
using System.Threading;

namespace G19USB;

/// <summary>
/// Coordinates one LCD worker with a bounded ordered queue and one replaceable latest-frame slot.
/// The worker removes an item before writing it, so a frame is never replaced while its buffer is in flight.
/// </summary>
internal sealed class LcdWriteQueue<T> : IDisposable
    where T : class
{
    private readonly object _gate = new();
    private readonly Queue<T> _orderedItems = new();
    private readonly int _orderedCapacity;
    private readonly AutoResetEvent _wake = new(initialState: false);
    private T? _latestItem;
    private bool _completed;
    private bool _disposed;

    public LcdWriteQueue(int orderedCapacity)
    {
        if (orderedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(orderedCapacity));

        _orderedCapacity = orderedCapacity;
    }

    public int PendingOrderedCount
    {
        get
        {
            lock (_gate)
                return _orderedItems.Count;
        }
    }

    public bool HasPendingLatest
    {
        get
        {
            lock (_gate)
                return _latestItem != null;
        }
    }

    /// <summary>
    /// Adds an ordered write, waiting for bounded capacity rather than growing indefinitely.
    /// </summary>
    public void EnqueueOrdered(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gate)
        {
            while (!_completed && _orderedItems.Count >= _orderedCapacity)
                Monitor.Wait(_gate);

            ThrowIfCompleted_NoLock();
            _orderedItems.Enqueue(item);
        }

        _wake.Set();
    }

    /// <summary>
    /// Stores one replaceable pending item. If an older item has not been taken by the worker,
    /// it is returned to the caller for completion and buffer release.
    /// </summary>
    public void EnqueueLatest(T item, Action<T> onReplaced)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(onReplaced);

        T? replaced;
        lock (_gate)
        {
            ThrowIfCompleted_NoLock();
            replaced = _latestItem;
            _latestItem = item;
        }

        if (replaced != null)
            onReplaced(replaced);

        _wake.Set();
    }

    /// <summary>
    /// Takes the newest pending item first, then an ordered item. A false result means the
    /// queue has completed and no item remains.
    /// </summary>
    public bool TryTake(CancellationToken cancellationToken, out T? item)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_latestItem != null)
                {
                    item = _latestItem;
                    _latestItem = null;
                    return true;
                }

                if (_orderedItems.Count > 0)
                {
                    item = _orderedItems.Dequeue();
                    Monitor.PulseAll(_gate);
                    return true;
                }

                if (_completed)
                {
                    item = null;
                    return false;
                }
            }

            int signaled = WaitHandle.WaitAny(new[]
            {
                _wake,
                cancellationToken.WaitHandle
            });
            if (signaled == 1)
                cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Stops accepting work and returns all items not yet taken by the worker. The caller owns
    /// completion and buffer cleanup for those abandoned items.
    /// </summary>
    public IReadOnlyList<T> Complete()
    {
        var abandoned = new List<T>();
        lock (_gate)
        {
            if (_completed)
                return abandoned;

            _completed = true;
            while (_orderedItems.Count > 0)
                abandoned.Add(_orderedItems.Dequeue());
            if (_latestItem != null)
            {
                abandoned.Add(_latestItem);
                _latestItem = null;
            }

            Monitor.PulseAll(_gate);
        }

        _wake.Set();
        return abandoned;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Complete();
        _wake.Dispose();
    }

    private void ThrowIfCompleted_NoLock()
    {
        if (_completed || _disposed)
            throw new ObjectDisposedException(nameof(LcdWriteQueue<T>));
    }
}
