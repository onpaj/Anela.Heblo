using Anela.Heblo.Xcc.Services.Concurrency;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public sealed class KeyedAsyncLockTests : IDisposable
{
    private readonly KeyedAsyncLock _sut = new();

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task AcquireAsync_SerializesConcurrentAccessForSameKey()
    {
        // Two concurrent acquirers for the same key — verify they run serially
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();

        // Use a TCS so t2 is guaranteed to be waiting before t1 releases
        var t1Acquired = new TaskCompletionSource();

        var t1 = Task.Run(async () =>
        {
            await using (await _sut.AcquireAsync("key", TimeSpan.FromMinutes(1)))
            {
                log.Enqueue("t1-start");
                t1Acquired.SetResult(); // signal t2 to start trying to acquire
                await Task.Delay(50);
                log.Enqueue("t1-end");
            }
        });

        // Wait until t1 has the lock, then launch t2
        await t1Acquired.Task;

        var t2 = Task.Run(async () =>
        {
            await using (await _sut.AcquireAsync("key", TimeSpan.FromMinutes(1)))
            {
                log.Enqueue("t2-start");
                log.Enqueue("t2-end");
            }
        });

        await Task.WhenAll(t1, t2);

        // t2 must not start until t1 ends
        var events = log.ToArray();
        var t1EndIndex = Array.IndexOf(events, "t1-end");
        var t2StartIndex = Array.IndexOf(events, "t2-start");
        t2StartIndex.Should().BeGreaterThan(t1EndIndex);
    }

    [Fact]
    public async Task AcquireAsync_AllowsParallelAccessForDifferentKeys()
    {
        var t1Started = new TaskCompletionSource();
        var t2Started = new TaskCompletionSource();

        var t1 = Task.Run(async () =>
        {
            await using (await _sut.AcquireAsync("key-a", TimeSpan.FromMinutes(1)))
            {
                t1Started.SetResult();
                await t2Started.Task; // wait for t2 to also acquire its lock
            }
        });

        var t2 = Task.Run(async () =>
        {
            await using (await _sut.AcquireAsync("key-b", TimeSpan.FromMinutes(1)))
            {
                t2Started.SetResult();
                await t1Started.Task; // wait for t1 to also acquire its lock
            }
        });

        // Both tasks acquire simultaneously — neither blocks the other
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(2));
        // If we reach here without timeout, both tasks ran concurrently
    }

    [Fact]
    public async Task AcquireAsync_DoesNotThrowObjectDisposedException_WhenEvictedWhileHeld()
    {
        // Use a very short TTL so eviction fires while we hold the lock
        var ttl = TimeSpan.FromMilliseconds(50);

        var act = async () =>
        {
            await using var handle = await _sut.AcquireAsync("evict-key", ttl);
            // Trigger eviction by compacting the internal cache
            // (The simplest proxy: wait longer than the TTL so sliding expiration fires)
            await Task.Delay(200); // longer than TTL
            // Release here — should not throw ObjectDisposedException
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AcquireAsync_WhenCancelled_DoesNotLeakEntry()
    {
        // Hold the lock so the second acquirer must wait
        var held = new TaskCompletionSource();
        var released = new TaskCompletionSource();

        _ = Task.Run(async () =>
        {
            await using (await _sut.AcquireAsync("cancel-key", TimeSpan.FromMinutes(1)))
            {
                held.SetResult();
                await released.Task;
            }
        });

        await held.Task;

        // Second acquirer gets cancelled
        using var cts = new CancellationTokenSource(50);
        var act = async () =>
        {
            await using (await _sut.AcquireAsync("cancel-key", TimeSpan.FromMinutes(1), cts.Token))
            { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Release first holder — should succeed without error
        released.SetResult();

        // Now a third acquirer should be able to get the lock normally
        await using var finalHandle = await _sut.AcquireAsync("cancel-key", TimeSpan.FromMinutes(1));
    }
}
