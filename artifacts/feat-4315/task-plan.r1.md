# Bulk Mutation Support in IUserDashboardSettingsMutator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `MutateBulkAsync` method to `IUserDashboardSettingsMutator`/`UserDashboardSettingsMutator` and rewrite `SaveUserSettingsHandler` to delegate to it, eliminating the handler's hand-rolled duplicate of the provision/lock/load scaffold.

**Architecture:** `UserDashboardSettingsMutator` gains a second method, `MutateBulkAsync`, that reuses its existing four dependencies (`IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator`) to run the same provision-outside-lock → lock → load scaffold as `MutateAsync`, then upserts a batch of tiles by `TileId` and unconditionally persists. `SaveUserSettingsHandler` is thinned to inject only `IUserDashboardSettingsMutator` and `ICurrentUserService`, matching `EnableTileHandler`/`DisableTileHandler`'s existing shape.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

### task: add-mutate-bulk-async-interface

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`

- [ ] **Step 1: Add the `MutateBulkAsync` method signature with XML docs to the interface**

Open `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`. It currently ends like this:

```csharp
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

Add the new method directly after `MutateAsync`'s closing `);` and before the interface's closing `}`, so the file becomes:

```csharp
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);

    /// <param name="userId">
    /// Caller-supplied user id. Null or empty is normalized to <c>"anonymous"</c> inside
    /// the mutator — handlers must not pre-normalize.
    /// </param>
    /// <param name="tiles">
    /// The full set of tile upserts to apply in this call. For each entry, an existing
    /// <see cref="UserDashboardTile"/> with matching <c>TileId</c> is updated in place
    /// (<c>IsVisible</c>, <c>DisplayOrder</c>); otherwise a new tile is appended. Tiles
    /// present in storage but absent from this list are left untouched — this is an
    /// upsert-by-id operation, not a full replace. An empty list is valid and still
    /// results in a persisted write (see remarks).
    /// </param>
    /// <remarks>
    /// Shares the exact same provision → lock → load → mutate → save scaffold as
    /// <see cref="MutateAsync"/>, including the provisioning-before-lock invariant
    /// documented on this interface. Unlike <see cref="MutateAsync"/>, which skips the
    /// persistence call when nothing was found or appended, <c>MutateBulkAsync</c> always
    /// calls <c>UpdateAsync</c> once <c>settings</c> is loaded (even for an empty
    /// <paramref name="tiles"/> list), to preserve <c>SaveUserSettingsHandler</c>'s
    /// existing "always persist on save" behavior. <c>TileFound</c>/<c>TileAppended</c> on
    /// the returned <see cref="UserDashboardSettingsMutationResult"/> mean "at least one
    /// tile in the batch was found / appended", not "the one tile".
    /// </remarks>
    Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
        string? userId,
        IReadOnlyList<UserDashboardTileDto> tiles,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

Note: `UserDashboardTileDto` lives in `Anela.Heblo.Application.Features.Dashboard.Contracts`. Add
`using Anela.Heblo.Application.Features.Dashboard.Contracts;` to the top of this file (it
currently only has `using Anela.Heblo.Domain.Features.Dashboard;`).

- [ ] **Step 2: Build to confirm the interface change compiles (implementation not yet updated, so build will fail — that's expected)**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: FAIL with `'UserDashboardSettingsMutator' does not implement interface member 'IUserDashboardSettingsMutator.MutateBulkAsync(...)'`. This confirms the interface change took effect; the next task implements it.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs
git commit -m "feat(dashboard): add MutateBulkAsync to IUserDashboardSettingsMutator"
```

---

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

### task: refactor-save-user-settings-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs`

- [ ] **Step 1: Rewrite `SaveUserSettingsHandlerTests.cs` to mock `IUserDashboardSettingsMutator` (failing first, since the handler hasn't changed yet)**

Replace the full contents of `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class SaveUserSettingsHandlerTests
{
    private readonly Mock<IUserDashboardSettingsMutator> _mutatorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly SaveUserSettingsHandler _handler;

    public SaveUserSettingsHandlerTests()
    {
        _mutatorMock = new Mock<IUserDashboardSettingsMutator>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: null, Email: "user@example.com", IsAuthenticated: true));

        _mutatorMock
            .Setup(x => x.MutateBulkAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyList<UserDashboardTileDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDashboardSettingsMutationResult(SettingsLoaded: true, TileFound: true, TileAppended: false));

        _handler = new SaveUserSettingsHandler(_mutatorMock.Object, _currentUserServiceMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: id ?? string.Empty, Name: null, Email: "user@example.com", IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_PassesCurrentUserIdAndTilesToMutator()
    {
        SetCurrentUserId("user123");
        var tiles = new[]
        {
            new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
        };
        var request = new SaveUserSettingsRequest { Tiles = tiles };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            "user123",
            It.Is<IReadOnlyList<UserDashboardTileDto>>(t => t.SequenceEqual(tiles)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTilesIsNull_PassesEmptyListToMutator()
    {
        SetCurrentUserId("user123");
        var request = new SaveUserSettingsRequest { Tiles = null! };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            "user123",
            It.Is<IReadOnlyList<UserDashboardTileDto>>(t => t.Count == 0),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNullOrEmpty_PassesItThroughUnchanged()
    {
        SetCurrentUserId(null);
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            string.Empty,
            It.IsAny<IReadOnlyList<UserDashboardTileDto>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AlwaysReturnsSuccessResponse()
    {
        SetCurrentUserId("user123");
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CallsMutatorExactlyOnce()
    {
        SetCurrentUserId("user123");
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<UserDashboardTileDto>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

(Note: `SaveUserSettingsResponse.Success` is asserted here because the pre-existing test suite already asserted it in `Handle_ShouldReturnSuccessResponse` — confirm the property name matches `SaveUserSettingsResponse.cs` before running; it does, per the existing test file this replaces.)

- [ ] **Step 2: Run the new test file to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SaveUserSettingsHandlerTests`
Expected: FAIL — `SaveUserSettingsHandler`'s constructor does not yet accept `(IUserDashboardSettingsMutator, ICurrentUserService)`.

- [ ] **Step 3: Rewrite `SaveUserSettingsHandler.cs`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;
    private readonly ICurrentUserService _currentUserService;

    public SaveUserSettingsHandler(
        IUserDashboardSettingsMutator mutator,
        ICurrentUserService currentUserService)
    {
        _mutator = mutator;
        _currentUserService = currentUserService;
    }

    public async Task<SaveUserSettingsResponse> Handle(SaveUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        var userId = currentUser.Id;

        await _mutator.MutateBulkAsync(
            userId,
            request.Tiles ?? Array.Empty<UserDashboardTileDto>(),
            cancellationToken);

        return new SaveUserSettingsResponse();
    }
}
```

- [ ] **Step 4: Run the test file to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SaveUserSettingsHandlerTests`
Expected: PASS — all 5 tests green.

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: PASS — full solution builds, is correctly formatted, and all tests (including `EnableTileHandlerTests`, `DisableTileHandlerTests`, `GetUserSettingsHandlerTests`, `UserDashboardSettingsMutatorTests`, `SaveUserSettingsHandlerTests`) pass with no regressions.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs
git commit -m "refactor(dashboard): thin SaveUserSettingsHandler down to a MutateBulkAsync caller"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (add `MutateBulkAsync` to interface, with docs) → `add-mutate-bulk-async-interface`.
- FR-2 (implement `MutateBulkAsync` scaffold + upsert + unconditional persist) → `implement-mutate-bulk-async`.
- FR-3 (thin `SaveUserSettingsHandler` to two dependencies) → `refactor-save-user-settings-handler`.
- NFR-1 (behavioral equivalence) → covered by the new `UserDashboardSettingsMutatorTests` (lock-once, provision-order, anonymous fallback, unconditional update, shared timestamp) plus the rewritten `SaveUserSettingsHandlerTests` (delegation, null-tiles handling, success response).
- NFR-2 (test coverage carries over into a new mutator-level fixture, not lost) → `implement-mutate-bulk-async` Step 1 creates `UserDashboardSettingsMutatorTests.cs` in the same task/commit that adds `MutateBulkAsync`, before the handler test is narrowed, per the architecture review's "Specification Amendments" note.
- NFR-3/NFR-4 (concurrency/security no-ops) → no new code paths introduced; scaffold is identical to `MutateAsync`'s existing, already-reviewed one.
- Data Model / API / Dependencies / Out of Scope — no tasks needed, nothing changes.

**Placeholder scan:** No TBD/TODO, no "add appropriate error handling"-style steps, no "similar to Task N" — all three tasks contain full, exact code and exact commands.

**Type consistency:** `MutateBulkAsync(string? userId, IReadOnlyList<UserDashboardTileDto> tiles, CancellationToken cancellationToken)` is declared identically in the interface (task `add-mutate-bulk-async-interface`), implemented identically in `UserDashboardSettingsMutator` (task `implement-mutate-bulk-async`), and called identically from `SaveUserSettingsHandler` (task `refactor-save-user-settings-handler`). `UserDashboardSettingsMutationResult` field names (`SettingsLoaded`, `TileFound`, `TileAppended`) are reused unchanged throughout.

## Status: COMPLETE
