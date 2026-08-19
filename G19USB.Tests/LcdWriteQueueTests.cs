using System;
using System.Threading.Tasks;

using System.Collections.Concurrent;
using System.Threading;
using Xunit;

namespace G19USB.Tests;

public sealed class LcdWriteQueueTests
{
    [Fact]
    public void Latest_frame_replaces_only_pending_frame_and_preserves_buffer_reference()
    {
        using var queue = new LcdWriteQueue<byte[]>(orderedCapacity: 2);
        var replaced = new ConcurrentQueue<byte[]>();
        var first = new byte[] { 1 };
        var second = new byte[] { 2 };

        queue.EnqueueLatest(first, replaced.Enqueue);
        queue.EnqueueLatest(second, replaced.Enqueue);

        Assert.True(replaced.TryDequeue(out var dropped));
        Assert.Same(first, dropped);
        Assert.True(queue.TryTake(CancellationToken.None, out var selected));
        Assert.Same(second, selected);
        Assert.False(queue.HasPendingLatest);
    }

    [Fact]
    public async Task Ordered_queue_is_bounded_until_worker_takes_an_item()
    {
        using var queue = new LcdWriteQueue<string>(orderedCapacity: 1);
        queue.EnqueueOrdered("first");

        var secondEnqueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task second = Task.Run(() =>
        {
            queue.EnqueueOrdered("second");
            secondEnqueued.TrySetResult(true);
        });

        await Task.Delay(100);
        Assert.False(secondEnqueued.Task.IsCompleted);
        Assert.Equal(1, queue.PendingOrderedCount);

        Assert.True(queue.TryTake(CancellationToken.None, out var first));
        Assert.Equal("first", first);
        await secondEnqueued.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(queue.TryTake(CancellationToken.None, out var secondItem));
        Assert.Equal("second", secondItem);
    }

    [Fact]
    public void Complete_returns_unstarted_ordered_and_latest_items_and_rejects_new_work()
    {
        using var queue = new LcdWriteQueue<string>(orderedCapacity: 2);
        queue.EnqueueOrdered("ordered");
        queue.EnqueueLatest("latest", _ => { });

        var abandoned = queue.Complete();

        Assert.Equal(2, abandoned.Count);
        Assert.False(queue.TryTake(CancellationToken.None, out _));
        Assert.Throws<ObjectDisposedException>(() => queue.EnqueueOrdered("after-stop"));
        Assert.Throws<ObjectDisposedException>(() => queue.EnqueueLatest("after-stop", _ => { }));
    }
}
