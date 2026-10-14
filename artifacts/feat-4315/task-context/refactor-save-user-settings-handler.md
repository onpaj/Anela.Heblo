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
