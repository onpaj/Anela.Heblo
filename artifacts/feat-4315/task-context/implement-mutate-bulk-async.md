### task: implement-mutate-bulk-async

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsMutatorTests.cs`

- [ ] **Step 1: Write the failing tests for `MutateBulkAsync`**

Create `backend/test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsMutatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard.Infrastructure;

public class UserDashboardSettingsMutatorTests
{
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly TimeProvider _timeProvider;
    private readonly UserDashboardSettingsMutator _mutator;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public UserDashboardSettingsMutatorTests()
    {
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _mediatorMock = new Mock<IMediator>();

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock
            .Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse());

        _mutator = new UserDashboardSettingsMutator(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object);
    }

    private static UserDashboardSettings CreateSampleSettings(string userId, List<UserDashboardTile>? tiles = null)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = tiles ?? new List<UserDashboardTile>()
        };
    }

    [Fact]
    public async Task MutateBulkAsync_WhenSettingsIsNull_ReturnsWithoutPersisting()
    {
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync((UserDashboardSettings?)null);

        var result = await _mutator.MutateBulkAsync(
            "user123",
            new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } },
            CancellationToken.None);

        result.SettingsLoaded.Should().BeFalse();
        result.TileFound.Should().BeFalse();
        result.TileAppended.Should().BeFalse();
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task MutateBulkAsync_WhenUserIdIsNullOrEmpty_ResolvesToAnonymous()
    {
        var settings = CreateSampleSettings("anonymous");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync(settings);

        await _mutator.MutateBulkAsync(null, Array.Empty<UserDashboardTileDto>(), CancellationToken.None);

        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
        _lockMock.Verify(x => x.AcquireAsync("anonymous", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MutateBulkAsync_SendsGetUserSettingsBeforeAcquiringLock()
    {
        var settings = CreateSampleSettings("user123");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(settings);

        var callOrder = new List<string>();
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("mediator"))
            .ReturnsAsync(new GetUserSettingsResponse());
        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock
            .Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("lock"))
            .ReturnsAsync(noOpDisposable.Object);

        await _mutator.MutateBulkAsync("user123", Array.Empty<UserDashboardTileDto>(), CancellationToken.None);

        callOrder.Should().ContainInOrder("mediator", "lock");
    }

    [Fact]
    public async Task MutateBulkAsync_AcquiresLockExactlyOnceRegardlessOfTileCount()
    {
        var settings = CreateSampleSettings("user123");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(settings);

        await _mutator.MutateBulkAsync(
            "user123",
            new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 },
                new UserDashboardTileDto { TileId = "tile3", IsVisible = true, DisplayOrder = 2 }
            },
            CancellationToken.None);

        _lockMock.Verify(x => x.AcquireAsync("user123", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.GetByUserIdAsync("user123"), Times.Once);
    }

    [Fact]
    public async Task MutateBulkAsync_WhenTileMatchesExisting_UpdatesInPlace()
    {
        var existingTile = new UserDashboardTile { UserId = "user123", TileId = "tile1", IsVisible = false, DisplayOrder = 5, LastModified = DateTime.UtcNow };
        var settings = CreateSampleSettings("user123", new List<UserDashboardTile> { existingTile });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(settings);

        UserDashboardSettings? captured = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>())).Callback<UserDashboardSettings>(s => captured = s);

        var result = await _mutator.MutateBulkAsync(
            "user123",
            new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } },
            CancellationToken.None);

        result.TileFound.Should().BeTrue();
        result.TileAppended.Should().BeFalse();
        captured.Should().NotBeNull();
        captured!.Tiles.Should().ContainSingle(t => t.TileId == "tile1" && t.IsVisible && t.DisplayOrder == 0 && t.LastModified == FixedUtcNow);
    }

    [Fact]
    public async Task MutateBulkAsync_WhenTileMissing_AppendsNewTile()
    {
        var settings = CreateSampleSettings("user123");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(settings);

        UserDashboardSettings? captured = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>())).Callback<UserDashboardSettings>(s => captured = s);

        var result = await _mutator.MutateBulkAsync(
            "user123",
            new[] { new UserDashboardTileDto { TileId = "newTile", IsVisible = true, DisplayOrder = 0 } },
            CancellationToken.None);

        result.TileFound.Should().BeFalse();
        result.TileAppended.Should().BeTrue();
        captured!.Tiles.Should().ContainSingle(t => t.TileId == "newTile" && t.IsVisible && t.UserId == "user123" && t.LastModified == FixedUtcNow);
    }

    [Fact]
    public async Task MutateBulkAsync_WhenTilesEmpty_StillPersistsSettings()
    {
        var settings = CreateSampleSettings("user123");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(settings);

        var result = await _mutator.MutateBulkAsync("user123", Array.Empty<UserDashboardTileDto>(), CancellationToken.None);

        result.SettingsLoaded.Should().BeTrue();
        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<UserDashboardSettings>(s => s.UserId == "user123" && s.LastModified == FixedUtcNow)), Times.Once);
    }

    [Fact]
    public async Task MutateBulkAsync_AllTouchedAndAppendedTilesShareSameTimestamp()
    {
        var existingTile = new UserDashboardTile { UserId = "user123", TileId = "tile1", IsVisible = false, DisplayOrder = 5, LastModified = DateTime.UtcNow };
        var settings = CreateSampleSettings("user123", new List<UserDashboardTile> { existingTile });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(settings);

        UserDashboardSettings? captured = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>())).Callback<UserDashboardSettings>(s => captured = s);

        await _mutator.MutateBulkAsync(
            "user123",
            new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = true, DisplayOrder = 1 }
            },
            CancellationToken.None);

        captured!.Tiles.Should().OnlyContain(t => t.LastModified == FixedUtcNow);
        captured.LastModified.Should().Be(FixedUtcNow);
    }
}
```

- [ ] **Step 2: Run the new test file to verify it fails to compile / fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~UserDashboardSettingsMutatorTests`
Expected: FAIL — `UserDashboardSettingsMutator` does not yet have a `MutateBulkAsync` method (compile error), or if Task `add-mutate-bulk-async-interface` already updated the interface only, this fails because `UserDashboardSettingsMutator` does not implement it yet.

- [ ] **Step 3: Implement `MutateBulkAsync` in `UserDashboardSettingsMutator`**

Open `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs`. Add `using Anela.Heblo.Application.Features.Dashboard.Contracts;` to the top (needed for `UserDashboardTileDto`). Add the new method inside the class, after the existing `MutateAsync` method and before the class's closing `}`:

```csharp
    public async Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
        string? userId,
        IReadOnlyList<UserDashboardTileDto> tiles,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = string.IsNullOrEmpty(userId) ? "anonymous" : userId;

        // Trigger provisioning outside the write lock (lock is non-reentrant).
        await _mediator.Send(new GetUserSettingsRequest(), cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(resolvedUserId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(resolvedUserId);
        if (settings == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: false,
                TileFound: false,
                TileAppended: false);
        }

        var now = _timeProvider.GetUtcNow().DateTime;
        var anyFound = false;
        var anyAppended = false;

        foreach (var tileDto in tiles)
        {
            var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileDto.TileId);
            if (existingTile != null)
            {
                existingTile.IsVisible = tileDto.IsVisible;
                existingTile.DisplayOrder = tileDto.DisplayOrder;
                existingTile.LastModified = now;
                anyFound = true;
            }
            else
            {
                settings.Tiles.Add(new UserDashboardTile
                {
                    UserId = resolvedUserId,
                    TileId = tileDto.TileId,
                    IsVisible = tileDto.IsVisible,
                    DisplayOrder = tileDto.DisplayOrder,
                    LastModified = now,
                    DashboardSettings = settings
                });
                anyAppended = true;
            }
        }

        settings.UserId = resolvedUserId;
        settings.LastModified = now;
        await _repository.UpdateAsync(settings);

        return new UserDashboardSettingsMutationResult(
            SettingsLoaded: true,
            TileFound: anyFound,
            TileAppended: anyAppended);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~UserDashboardSettingsMutatorTests`
Expected: PASS — all 8 tests green.

- [ ] **Step 5: Run the full Dashboard test folder to check nothing else regressed**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~Features.Dashboard`
Expected: PASS (note: `SaveUserSettingsHandlerTests` still references the old constructor at this point and will only be fixed in the next task — if this run fails only in that file, that is expected and resolved by task `refactor-save-user-settings-handler`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsMutatorTests.cs
git commit -m "feat(dashboard): implement MutateBulkAsync in UserDashboardSettingsMutator"
```

---
