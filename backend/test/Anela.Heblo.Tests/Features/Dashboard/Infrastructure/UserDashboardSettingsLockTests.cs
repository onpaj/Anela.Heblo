using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard.Infrastructure;

public class UserDashboardSettingsLockTests
{
    private readonly UserDashboardSettingsLock _lock = new();

    [Fact]
    public async Task AcquireAsync_SameUser_SerializesAccess()
    {
        // Arrange
        const string userId = "user-serial";
        var executionOrder = new List<string>();

        var firstLock = await _lock.AcquireAsync(userId);

        // Act — start second acquisition; it must block until first is released
        var secondTask = Task.Run(async () =>
        {
            await using var _ = await _lock.AcquireAsync(userId);
            executionOrder.Add("second");
        });

        // Give the second task a moment to block
        await Task.Delay(50);
        executionOrder.Add("first-about-to-release");
        await firstLock.DisposeAsync();

        await secondTask;

        // Assert — "first-about-to-release" must precede "second"
        executionOrder.Should().ContainInOrder("first-about-to-release", "second");
    }

    [Fact]
    public async Task AcquireAsync_DifferentUsers_DoNotBlockEachOther()
    {
        // Arrange
        const string userA = "user-a";
        const string userB = "user-b";
        var bothAcquired = false;

        var lockA = await _lock.AcquireAsync(userA);

        // Act — userB should acquire immediately while userA still holds its lock
        var lockBTask = _lock.AcquireAsync(userB);
        var completed = await Task.WhenAny(lockBTask, Task.Delay(200));

        bothAcquired = completed == lockBTask;

        // Cleanup
        await lockA.DisposeAsync();
        if (bothAcquired)
            await (await lockBTask).DisposeAsync();

        // Assert
        bothAcquired.Should().BeTrue("different-user locks must not block each other");
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_ReleasesExactlyOnce()
    {
        // Arrange
        const string userId = "user-double-dispose";
        var handle = await _lock.AcquireAsync(userId);

        // Act
        await handle.DisposeAsync();
        await handle.DisposeAsync(); // second dispose must be a no-op

        // Assert — if the semaphore was released twice, a second AcquireAsync would
        // immediately succeed; a third acquire would also succeed (semaphore count == 2).
        // We verify the semaphore was only released once by acquiring twice: the second
        // should block (semaphore count should be 1, not 2).
        var firstHandle = await _lock.AcquireAsync(userId);

        var secondAcquireTask = _lock.AcquireAsync(userId);
        var completedEarly = await Task.WhenAny(secondAcquireTask, Task.Delay(100));

        var blockedCorrectly = completedEarly != secondAcquireTask;

        // Cleanup
        await firstHandle.DisposeAsync();
        await (await secondAcquireTask).DisposeAsync();

        blockedCorrectly.Should().BeTrue("semaphore must not be released more than once on double-dispose");
    }

    [Fact]
    public async Task AcquireAsync_CancellationTokenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        const string userId = "user-cancel";
        var firstLock = await _lock.AcquireAsync(userId);

        using var cts = new CancellationTokenSource();

        // Act — start waiting for the lock, then cancel
        var acquireTask = _lock.AcquireAsync(userId, cts.Token);
        await Task.Delay(30); // let the task block on the semaphore
        await cts.CancelAsync();

        Func<Task> act = async () => await acquireTask;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Verify lock was NOT acquired — we should be able to acquire it after releasing firstLock
        await firstLock.DisposeAsync();
        var newHandle = await _lock.AcquireAsync(userId);
        newHandle.Should().NotBeNull();
        await newHandle.DisposeAsync();
    }
}
