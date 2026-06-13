# Ungate Dashboard + Per-Tile Permission Placeholder — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the read-only dashboard available to every authenticated user, and have any tile the user lacks permission for render an inline "Přístup zakázán" placeholder instead of leaking data or raising errors.

**Architecture:** Remove the controller-level admin gate so the dashboard falls back to the app's default authenticated policy. Move per-tile authorization into `GetTileDataHandler`, which resolves the caller's effective permissions once (reusing `IPermissionResolver`, the same logic behind `/api/auth/me`) and, for any tile whose `RequiredPermissions` the user lacks, skips the data load and flags the tile `IsUnauthorized`. The frontend renders a placeholder for flagged tiles. The mechanism stays dormant because no current tile declares `RequiredPermissions`.

**Tech Stack:** .NET 8, MediatR, xUnit + FluentAssertions + Moq (backend); React 18 + TypeScript, TanStack Query, Jest + React Testing Library (frontend).

---

## Background facts (verified against the codebase)

- Dashboard data is aggregated server-side in one call (`GET /api/dashboard/data`); tiles load **in-process** via `TileRegistry` — there is no per-tile HTTP request and therefore no per-tile 403 to intercept.
- Global default policy already protects all endpoints: `RequireAuthenticatedUser().RequireRole(AccessRoles.Base)` (`"heblo_user"`) in `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs:104-121`. Removing `[FeatureAuthorize]` from a controller drops it to this policy — authenticated, not anonymous.
- `Feature.Admin_Administration` remains gated on 10 other controllers, so `GateConsistencyTests.EveryMenuPath_FeatureHasController` stays satisfied after this change.
- `IPermissionResolver` is already registered in DI (used by `GetMeHandler`) and is cached, so resolving per request (30s refetch) is cheap. No DI registration changes are required.

## File Structure

**Backend**
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` — remove the class-level admin gate.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileData.cs` — add `IsUnauthorized` flag (internal model).
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDto.cs` — add `IsUnauthorized` flag (wire contract).
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` — resolve permissions and enforce per tile.
- `backend/test/Anela.Heblo.Tests/Authorization/DashboardControllerAuthorizationTests.cs` — NEW guard test.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs` — updated setup + new permission tests.

**Frontend**
- `frontend/src/api/hooks/useDashboard.ts` — add `isUnauthorized` to the `DashboardTile` type.
- `frontend/src/components/dashboard/tiles/UnauthorizedTile.tsx` — NEW placeholder component.
- `frontend/src/components/dashboard/tiles/TileContent.tsx` — render the placeholder when flagged.
- `frontend/src/components/pages/Dashboard.tsx` — stop client-side permission hiding; trust the backend flag.
- `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` — new placeholder tests.
- `frontend/src/components/pages/__tests__/Dashboard.test.tsx` — replace the two client-side-permission tests.

---

## Task 1: Remove the admin gate from DashboardController

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/DashboardControllerAuthorizationTests.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:15`

- [ ] **Step 1: Write the failing guard test**

Create `backend/test/Anela.Heblo.Tests/Authorization/DashboardControllerAuthorizationTests.cs`:

```csharp
using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class DashboardControllerAuthorizationTests
{
    [Fact]
    public void DashboardController_IsNotGatedByFeatureAuthorize()
    {
        var attribute = typeof(DashboardController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().BeNull(
            "the read-only dashboard must be available to every authenticated user; " +
            "per-tile access is enforced in GetTileDataHandler");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~DashboardControllerAuthorizationTests"`
Expected: FAIL — the attribute is still present, so `attribute` is not null.

- [ ] **Step 3: Remove the attribute**

In `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`, delete line 15:

```csharp
[FeatureAuthorize(Feature.Admin_Administration)]
```

Then remove the now-unused import at the top of the file if the build warns about it:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
```

The class declaration must end up as:

```csharp
[ApiController]
[Route("api/[controller]")]
public class DashboardController : BaseApiController
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~DashboardControllerAuthorizationTests"`
Expected: PASS.

- [ ] **Step 5: Verify the consistency tests still pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GateConsistencyTests"`
Expected: PASS (both `EveryGatedEndpoint_HasFeatureAuthorize` and `EveryMenuPath_FeatureHasController`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/DashboardController.cs \
        backend/test/Anela.Heblo.Tests/Authorization/DashboardControllerAuthorizationTests.cs
git commit -m "fix: ungate read-only dashboard from admin permission"
```

---

## Task 2: Add the `IsUnauthorized` flag to the tile models

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileData.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDto.cs`

No test in this task — it's a pure data-shape addition exercised by Task 3's tests.

- [ ] **Step 1: Add `IsUnauthorized` to the internal `TileData` model**

In `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileData.cs`, add the property after `RequiredPermissions` so the file reads:

```csharp
namespace Anela.Heblo.Xcc.Services.Dashboard;

public class TileData
{
    public string TileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TileSize Size { get; set; }
    public TileCategory Category { get; set; }
    public bool DefaultEnabled { get; set; }
    public bool AutoShow { get; set; }
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public bool IsUnauthorized { get; set; }
    public object Data { get; set; } = new();
}
```

- [ ] **Step 2: Add `IsUnauthorized` to the wire contract `DashboardTileDto`**

In `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDto.cs`, add the property so the file reads:

```csharp
namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

public class DashboardTileDto
{
    public string TileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool DefaultEnabled { get; set; }
    public bool AutoShow { get; set; }
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public bool IsUnauthorized { get; set; }
    public object? Data { get; set; }
}
```

(`DashboardTileDto` is a class, not a record — required by the OpenAPI generators per `CLAUDE.md`.)

- [ ] **Step 3: Verify the build compiles**

Run: `cd backend && dotnet build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileData.cs \
        backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDto.cs
git commit -m "feat: add IsUnauthorized flag to dashboard tile models"
```

---

## Task 3: Enforce per-tile permissions in GetTileDataHandler

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs`

A constructor-signature change makes red/green here a compile-failure loop: update the test file first (it won't compile until the handler is updated), then implement.

- [ ] **Step 1: Update the test setup and add the new permission tests**

In `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs`:

Add the using (with the other usings near the top):

```csharp
using Anela.Heblo.Domain.Features.Authorization;
```

Add a resolver mock field and wire it in the constructor. Replace the fields block + constructor (lines 17-33) with:

```csharp
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly Mock<ILogger<GetTileDataHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IPermissionResolver> _permissionResolverMock;
    private readonly GetTileDataHandler _handler;

    public GetTileDataHandlerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tileRegistryMock = new Mock<ITileRegistry>();
        _loggerMock = new Mock<ILogger<GetTileDataHandler>>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("test-user", null, "test@example.com", true));

        _permissionResolverMock = new Mock<IPermissionResolver>();
        _permissionResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EffectivePermissions.Empty);

        // Permission-agnostic tests run as super user so existing behavior is unchanged;
        // the permission-specific tests below override this to false.
        _currentUserMock.Setup(x => x.IsInRole(AccessRoles.SuperUser)).Returns(true);

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 4 });
        _handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options, _loggerMock.Object, _currentUserMock.Object, _permissionResolverMock.Object);
    }
```

Update the second handler construction inside `Handle_WhenTwoSlowTilesAndMaxDoP2_ShouldLoadInParallel` (currently line 253) to pass the resolver mock:

```csharp
        var handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options, _loggerMock.Object, _currentUserMock.Object, _permissionResolverMock.Object);
```

Add these three new tests inside the class (e.g. just before the closing brace):

```csharp
    [Fact]
    public async Task Handle_WhenUserLacksRequiredPermission_ShouldReturnUnauthorizedWithoutData()
    {
        // Arrange
        const string tileId = "finance-tile";
        var request = new GetTileDataRequest();
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 }
        });

        _currentUserMock.Setup(x => x.IsInRole(AccessRoles.SuperUser)).Returns(false);
        _permissionResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, Array.Empty<string>(), Array.Empty<string>()));

        _tileRegistryMock
            .Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Finance", "Finance desc", TileSize.Medium,
                TileCategory.Finance, true, false, new[] { "finance.financial_overview.read" }));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.TileId.Should().Be(tileId);
        tile.IsUnauthorized.Should().BeTrue();
        tile.Data.Should().BeNull();
        tile.Title.Should().Be("Finance"); // metadata still present for the header
        _tileRegistryMock.Verify(
            x => x.GetTileDataAsync(tileId, It.IsAny<Dictionary<string, string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserHasRequiredPermission_ShouldReturnData()
    {
        // Arrange
        const string tileId = "finance-tile";
        var expectedData = new { Revenue = 1000 };
        var request = new GetTileDataRequest();
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 }
        });

        _currentUserMock.Setup(x => x.IsInRole(AccessRoles.SuperUser)).Returns(false);
        _permissionResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "finance.financial_overview.read" }, Array.Empty<string>()));

        _tileRegistryMock
            .Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Finance", "Finance desc", TileSize.Medium,
                TileCategory.Finance, true, false, new[] { "finance.financial_overview.read" }));
        _tileRegistryMock
            .Setup(x => x.GetTileDataAsync(tileId, It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.IsUnauthorized.Should().BeFalse();
        tile.Data.Should().Be(expectedData);
    }

    [Fact]
    public async Task Handle_WhenTileHasNoRequiredPermissions_ShouldReturnDataForNonSuperUser()
    {
        // Arrange
        const string tileId = "open-tile";
        var expectedData = new { Count = 7 };
        var request = new GetTileDataRequest();
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 }
        });

        _currentUserMock.Setup(x => x.IsInRole(AccessRoles.SuperUser)).Returns(false);
        _permissionResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EffectivePermissions.Empty);

        _tileRegistryMock
            .Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Open", "Open desc", TileSize.Small,
                TileCategory.System, true, false, Array.Empty<string>()));
        _tileRegistryMock
            .Setup(x => x.GetTileDataAsync(tileId, It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.IsUnauthorized.Should().BeFalse();
        tile.Data.Should().Be(expectedData);
    }
```

- [ ] **Step 2: Run the tests to verify they fail (compile error)**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GetTileDataHandlerTests"`
Expected: FAIL — compile error, `GetTileDataHandler` has no constructor taking `IPermissionResolver`.

- [ ] **Step 3: Implement the handler changes**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` with:

```csharp
using System.Collections.Concurrent;
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataHandler : IRequestHandler<GetTileDataRequest, GetTileDataResponse>
{
    private readonly IMediator _mediator;
    private readonly ITileRegistry _tileRegistry;
    private readonly DashboardOptions _dashboardOptions;
    private readonly ILogger<GetTileDataHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPermissionResolver _permissionResolver;

    public GetTileDataHandler(
        IMediator mediator,
        ITileRegistry tileRegistry,
        IOptions<DashboardOptions> dashboardOptions,
        ILogger<GetTileDataHandler> logger,
        ICurrentUserService currentUserService,
        IPermissionResolver permissionResolver)
    {
        _mediator = mediator;
        _tileRegistry = tileRegistry;
        _dashboardOptions = dashboardOptions.Value;
        _logger = logger;
        _currentUserService = currentUserService;
        _permissionResolver = permissionResolver;
    }

    public async Task<GetTileDataResponse> Handle(GetTileDataRequest request, CancellationToken cancellationToken)
    {
        var settingsResponse = await _mediator.Send(
            new GetUserSettingsRequest(),
            cancellationToken);

        var visibleTiles = settingsResponse.Settings.Tiles
            .Where(t => t.IsVisible)
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        // Resolve the caller's effective permissions once (cached), mirroring GetMeHandler.
        var currentUser = _currentUserService.GetCurrentUser();
        var isSuperUser = _currentUserService.IsInRole(AccessRoles.SuperUser);
        var userPermissions = isSuperUser
            ? new HashSet<string>()
            : (await _permissionResolver.ResolveAsync(
                currentUser.Id ?? string.Empty,
                currentUser.Email,
                currentUser.Name,
                cancellationToken)).Permissions.ToHashSet();

        bool HasTileAccess(string[] required) =>
            isSuperUser || required.Length == 0 || required.All(userPermissions.Contains);

        var results = new ConcurrentBag<(int Index, TileData Data)>();

        await Parallel.ForEachAsync(
            visibleTiles.Select((tile, index) => (tile, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _dashboardOptions.MaxConcurrentTileLoads,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                var (tileSettings, index) = item;

                try
                {
                    var tile = _tileRegistry.GetTileMetadata(tileSettings.TileId);
                    if (tile == null)
                    {
                        results.Add((index, new TileData
                        {
                            TileId = tileSettings.TileId,
                            Title = "Error",
                            Description = $"Tile '{tileSettings.TileId}' not found",
                            Size = TileSize.Small,
                            Category = TileCategory.Error,
                            Data = new { Error = $"Tile '{tileSettings.TileId}' not found" }
                        }));
                        return;
                    }

                    if (!HasTileAccess(tile.RequiredPermissions))
                    {
                        // Skip loading data entirely — return metadata only so the
                        // frontend can render an "unauthorized" placeholder.
                        results.Add((index, new TileData
                        {
                            TileId = tile.TileId,
                            Title = tile.Title,
                            Description = tile.Description,
                            Size = tile.Size,
                            Category = tile.Category,
                            DefaultEnabled = tile.DefaultEnabled,
                            AutoShow = tile.AutoShow,
                            RequiredPermissions = tile.RequiredPermissions,
                            IsUnauthorized = true
                        }));
                        return;
                    }

                    var data = await _tileRegistry.GetTileDataAsync(tileSettings.TileId, request.TileParameters);

                    results.Add((index, new TileData
                    {
                        TileId = tile.TileId,
                        Title = tile.Title,
                        Description = tile.Description,
                        Size = tile.Size,
                        Category = tile.Category,
                        DefaultEnabled = tile.DefaultEnabled,
                        AutoShow = tile.AutoShow,
                        RequiredPermissions = tile.RequiredPermissions,
                        Data = data
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load tile {TileId}", tileSettings.TileId);
                    results.Add((index, new TileData
                    {
                        TileId = tileSettings.TileId,
                        Title = "Error",
                        Description = $"Failed to load tile '{tileSettings.TileId}'",
                        Size = TileSize.Small,
                        Category = TileCategory.Error,
                        Data = new { Error = "An error occurred while loading this tile." }
                    }));
                }
            });

        var tiles = results
            .OrderBy(r => r.Index)
            .Select(r => r.Data)
            .Select(td => new DashboardTileDto
            {
                TileId = td.TileId,
                Title = td.Title,
                Description = td.Description,
                Size = td.Size.ToString(),
                Category = td.Category.ToString(),
                DefaultEnabled = td.DefaultEnabled,
                AutoShow = td.AutoShow,
                RequiredPermissions = td.RequiredPermissions,
                IsUnauthorized = td.IsUnauthorized,
                Data = td.IsUnauthorized ? null : td.Data
            })
            .ToArray();

        return new GetTileDataResponse { Tiles = tiles };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GetTileDataHandlerTests"`
Expected: PASS — all existing tests plus the three new ones (10 total).

- [ ] **Step 5: Build and format**

Run: `cd backend && dotnet build && dotnet format`
Expected: build succeeds, no format diff left uncommitted.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs
git commit -m "feat: enforce per-tile dashboard permissions server-side"
```

---

## Task 4: Add the frontend `UnauthorizedTile` placeholder + type field

**Files:**
- Modify: `frontend/src/api/hooks/useDashboard.ts:4-14`
- Create: `frontend/src/components/dashboard/tiles/UnauthorizedTile.tsx`

- [ ] **Step 1: Add `isUnauthorized` to the `DashboardTile` type**

In `frontend/src/api/hooks/useDashboard.ts`, update the interface (lines 4-14) to:

```typescript
export interface DashboardTile {
  tileId: string;
  title: string;
  description: string;
  size: 'Small' | 'Medium' | 'Large';
  category: string;
  defaultEnabled: boolean;
  autoShow: boolean;
  requiredPermissions: string[];
  isUnauthorized?: boolean;
  data?: any;
}
```

- [ ] **Step 2: Create the placeholder component**

Create `frontend/src/components/dashboard/tiles/UnauthorizedTile.tsx`:

```typescript
import React from 'react';
import { Lock } from 'lucide-react';

export const UnauthorizedTile: React.FC = () => {
  return (
    <div
      className="flex flex-col items-center justify-center h-full text-gray-400"
      data-testid="unauthorized-tile"
    >
      <Lock className="h-8 w-8 mb-2" />
      <span className="text-sm">Přístup zakázán</span>
    </div>
  );
};
```

(The dashboard components use hardcoded Czech strings — e.g. `Dashboard.tsx` renders "Nastavení"/"Přehled systému…" — so this matches the surrounding style rather than introducing an i18n lookup here.)

- [ ] **Step 3: Verify the type compiles**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useDashboard.ts \
        frontend/src/components/dashboard/tiles/UnauthorizedTile.tsx
git commit -m "feat: add UnauthorizedTile placeholder and isUnauthorized tile field"
```

---

## Task 5: Render the placeholder in TileContent

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/TileContent.tsx:1-27`
- Modify: `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx`

- [ ] **Step 1: Write the failing tests**

In `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx`, add these two tests inside the `describe('TileContent', …)` block (e.g. right after the existing "should render LoadingTile when data is undefined" test):

```typescript
  it('should render UnauthorizedTile when the tile is unauthorized', () => {
    const tile = { ...createMockTile('backgroundtaskstatus', null), isUnauthorized: true };
    render(<TileContent tile={tile} />);

    expect(screen.getByTestId('unauthorized-tile')).toBeInTheDocument();
    expect(screen.getByText('Přístup zakázán')).toBeInTheDocument();
  });

  it('should render UnauthorizedTile even when data is present', () => {
    const tile = { ...createMockTile('backgroundtaskstatus', { running: 1 }), isUnauthorized: true };
    render(<TileContent tile={tile} />);

    expect(screen.getByTestId('unauthorized-tile')).toBeInTheDocument();
    expect(screen.queryByTestId('background-tasks-tile')).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx react-scripts test --watchAll=false TileContent`
Expected: FAIL — no `unauthorized-tile` is rendered (the unauthorized tile with `null` data currently renders `LoadingTile`; the one with data renders `BackgroundTasksTile`).

- [ ] **Step 3: Implement the branch in TileContent**

In `frontend/src/components/dashboard/tiles/TileContent.tsx`, add the import alongside the existing tile imports (after line 3's `LoadingTile` import):

```typescript
import { UnauthorizedTile } from './UnauthorizedTile';
```

Then change the start of the component body (lines 24-27) from:

```typescript
export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
  if (!tile.data) {
    return <LoadingTile />;
  }
```

to:

```typescript
export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
  if (tile.isUnauthorized) {
    return <UnauthorizedTile />;
  }

  if (!tile.data) {
    return <LoadingTile />;
  }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx react-scripts test --watchAll=false TileContent`
Expected: PASS — all existing TileContent tests plus the two new ones.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/tiles/TileContent.tsx \
        frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx
git commit -m "feat: render UnauthorizedTile placeholder for flagged tiles"
```

---

## Task 6: Stop client-side permission hiding in Dashboard

The backend is now the source of truth: unauthorized tiles arrive flagged with no data and must render the placeholder rather than being filtered out. Remove the client-side `hasPermission` gate.

**Files:**
- Modify: `frontend/src/components/pages/Dashboard.tsx`
- Modify: `frontend/src/components/pages/__tests__/Dashboard.test.tsx:407-443`

- [ ] **Step 1: Replace the two client-side-permission tests**

In `frontend/src/components/pages/__tests__/Dashboard.test.tsx`, replace the two tests at lines 407-443 ("shows a tile when the user has its required permission" and "hides tiles whose requiredPermissions the user lacks") with:

```typescript
  it("renders a normally visible tile", () => {
    mockUseTileData.mockReturnValue({
      data: [{ ...mockTileData[0] }],
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: [{ tileId: "tile1", isVisible: true, displayOrder: 0 }], lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    expect(screen.getByTestId("tile-count")).toHaveTextContent("1");
  });

  it("renders (does not hide) tiles the backend flagged as unauthorized", () => {
    mockUseTileData.mockReturnValue({
      data: [
        { ...mockTileData[0] },                                       // tile1 visible
        { ...mockTileData[1], isUnauthorized: true, data: null },     // tile2 autoShow + unauthorized
      ],
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    // Both tiles pass through; the unauthorized one renders a placeholder instead of being hidden.
    expect(screen.getByTestId("tile-count")).toHaveTextContent("2");
  });
```

- [ ] **Step 2: Run the tests to verify the unauthorized one fails**

Run: `cd frontend && npx react-scripts test --watchAll=false Dashboard.test`
Expected: FAIL — "renders (does not hide) tiles the backend flagged as unauthorized" expects 2 but the current client-side filter still hides `tile2` when `hasPermission` is mocked... actually `mockHasPermission` defaults to `() => true`, and `tile2` has `requiredPermissions: []`, so this specific case may already pass. If it passes here, that is acceptable — proceed; Step 3 still removes the dead client-side gate. The "renders a normally visible tile" replacement must pass.

- [ ] **Step 3: Remove the client-side permission gate from Dashboard.tsx**

In `frontend/src/components/pages/Dashboard.tsx`:

Remove the import (line 12):

```typescript
import { usePermissionsContext } from "../../auth/PermissionsContext";
```

Remove the destructure (line 28):

```typescript
  const { hasPermission } = usePermissionsContext();
```

Replace the `visibleTileData` memo (lines 34-57) with:

```typescript
  // Filter visible tiles based on user settings and AutoShow.
  // Per-tile permission enforcement happens on the backend: unauthorized tiles arrive
  // flagged (isUnauthorized, no data) and render a placeholder, so there is no
  // client-side permission gating here.
  const visibleTileData = React.useMemo(() => {
    if (!userSettings || !allTileData.length) return [];

    const userTileSettings = userSettings.tiles.reduce((acc, tile) => {
      acc[tile.tileId] = tile;
      return acc;
    }, {} as Record<string, any>);

    return allTileData
      .filter(tile => {
        const userSetting = userTileSettings[tile.tileId];
        return userSetting?.isVisible || (tile.autoShow && userSetting?.isVisible !== false);
      })
      .sort((a, b) => {
        const aOrder = userTileSettings[a.tileId]?.displayOrder ?? 999;
        const bOrder = userTileSettings[b.tileId]?.displayOrder ?? 999;
        return aOrder - bOrder;
      });
  }, [userSettings, allTileData]);
```

- [ ] **Step 4: Run the Dashboard tests to verify they pass**

Run: `cd frontend && npx react-scripts test --watchAll=false Dashboard.test`
Expected: PASS — including both replacement tests.

- [ ] **Step 5: Build and lint the frontend**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds (FE build is stricter than `tsc --noEmit`), lint clean. Fix any "unused variable" lint error if `usePermissionsContext`/`hasPermission` references remain.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/Dashboard.tsx \
        frontend/src/components/pages/__tests__/Dashboard.test.tsx
git commit -m "refactor: rely on backend tile authorization instead of client-side hiding"
```

---

## Final verification

- [ ] Backend: `cd backend && dotnet build && dotnet format`
- [ ] Backend: `dotnet test --filter "FullyQualifiedName~Dashboard|FullyQualifiedName~Authorization"` — all green.
- [ ] Frontend: `cd frontend && npm run build && npm run lint`
- [ ] Frontend: `npx react-scripts test --watchAll=false Dashboard` — TileContent + Dashboard specs green.
- [ ] Manual smoke (optional): as a non-admin `heblo_user`, `GET /api/dashboard/tiles` returns 200 (was 403). To exercise the placeholder end-to-end, temporarily give one tile a `RequiredPermissions` value the test user lacks (e.g. add `=> new[] { "finance.financial_overview.read" }` to a tile's `RequiredPermissions`), confirm the `/api/dashboard/data` payload returns that tile with `isUnauthorized: true` and `data: null`, and the UI shows "Přístup zakázán" — then revert the temporary change.

## Notes / out of scope

- **Mechanism is dormant by design.** No current tile declares `RequiredPermissions`, so after this change every authenticated user sees all current tiles with data — matching the intent that the dashboard is fully viewable. The placeholder path only activates when a tile opts in by returning a non-empty `RequiredPermissions`.
- **`GetAvailableTilesHandler`** (the "add tile" catalog, metadata only — no sensitive data) is intentionally left unchanged. If the picker should later reflect access, apply the same `HasTileAccess` check there.

---

## Self-Review

**Spec coverage:**
- Remove admin gate → Task 1. ✓
- No data leak for restricted tiles → Task 3 skips `LoadDataAsync` and nulls `Data` in the DTO for unauthorized tiles. ✓
- Show "Přístup zakázán" placeholder instead of error/toast → Tasks 4–5 (`UnauthorizedTile` + `TileContent` branch). ✓
- Backend enforcement (not frontend-only) → Task 3 (`IPermissionResolver`); Task 6 removes client-side gating. ✓

**Placeholder scan:** No TBD/TODO/"add validation"/"similar to" placeholders; every code step shows complete code.

**Type consistency:** `IsUnauthorized` (C#) ↔ `isUnauthorized` (TS, default camelCase serialization) used consistently across `TileData`, `DashboardTileDto`, `DashboardTile`, `TileContent`, and tests. Handler constructor arity (6 args) updated in both handler and both test construction sites. `HasTileAccess` defined and used within `GetTileDataHandler`. `UnauthorizedTile` exported and imported by the same name. `EffectivePermissions(bool, IReadOnlyCollection<string>, IReadOnlyCollection<string>)` constructor used as defined.
