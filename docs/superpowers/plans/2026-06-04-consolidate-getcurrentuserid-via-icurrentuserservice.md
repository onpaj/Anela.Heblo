# Consolidate User Identity Resolution via `ICurrentUserService` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the controller-level `BaseApiController.GetCurrentUserId()` helper and migrate the three outlier controllers (`Dashboard`, `CarrierCooling`, `GiftSettings`) so user identity is resolved exclusively in MediatR handlers via `ICurrentUserService` — matching the established pattern used by 60+ other handlers.

**Architecture:** Each affected handler gets `ICurrentUserService` injected as its **last constructor parameter** and resolves the current user inline. Three null-id policies remain (each handler picks one): Dashboard handlers keep the existing `"anonymous"` defense-in-depth fallback; `SetCarrierCoolingHandler` and `SetGiftSettingHandler` short-circuit with `ErrorCodes.Unauthorized` (HTTP 401) — matching `CreateMarketingActionHandler`. Request DTOs lose their client-settable `UserId` / `ModifiedBy` properties, which also closes a theoretical spoofing hole. The OpenAPI TypeScript client is regenerated; verified frontend hooks do not currently send these fields. The `UserDashboardSettingsMutator`'s existing `userId` parameter + `"anonymous"` normalization is preserved (handler resolves identity, mutator stays unchanged).

**Tech Stack:** .NET 8, MediatR, xUnit + Moq + FluentAssertions, NSwag (OpenAPI → TypeScript client regen).

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` — delete `protected string GetCurrentUserId()` (lines 75–79) and `using System.Security.Claims;` (line 3).
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` — remove all five `GetCurrentUserId()` calls and `UserId = userId` assignments.
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` — remove `request.ModifiedBy = GetCurrentUserId();` (line 34).
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` — remove `command.ModifiedBy = GetCurrentUserId();` (line 33); add explicit `Unauthorized` mapping before the existing `BadRequest` fallback to preserve HTTP 401 for the defense-in-depth case.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsRequest.cs` — remove `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs` — inject `ICurrentUserService`; resolve userId locally.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsRequest.cs` — remove `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` — inject `ICurrentUserService`; resolve userId locally.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataRequest.cs` — remove `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` — inject `ICurrentUserService`; resolve userId locally.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileRequest.cs` — remove `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs` — inject `ICurrentUserService`; resolve userId locally and pass to mutator.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileRequest.cs` — remove `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs` — inject `ICurrentUserService`; resolve userId locally and pass to mutator.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs` — remove `ModifiedBy` property.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs` — inject `ICurrentUserService`; early-return Unauthorized when id is null/empty; populate `ModifiedBy` from resolved user id.
- `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs` — remove `ModifiedBy` property.
- `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs` — inject `ICurrentUserService`; early-return Unauthorized; populate `ModifiedBy` from resolved user id.
- `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` — add 3 missing priority-chain scenarios.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs` — drop request-side userId, mock `ICurrentUserService`.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` — same.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs` — same.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs` — same.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs` — same.
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs` — drop request-side ModifiedBy, mock `ICurrentUserService`, add Unauthorized test.
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs` — same, add Unauthorized test.
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` — stop asserting `request.UserId == "test-user-123"` (no longer set by controller).

**Delete:**
- `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` — file is uncompilable after FR-1 and its scenarios move to `CurrentUserServiceTests`.

**Frontend regen target (auto-generated, no hand edits):**
- `frontend/src/api/generated/api-client.ts` (or equivalent) — NSwag-regenerated DTOs lose `userId` / `modifiedBy`.

Each file has a single, well-scoped responsibility. Tasks are organized so each commit leaves the build green and the feature end-to-end functional.

---

## Task Order Rationale

Tasks are ordered so the build stays green after each commit:

1. **Task 1** adds new `CurrentUserService` tests — independent, no production code change yet.
2. **Tasks 2–8** migrate one handler at a time. Each task touches: handler + request DTO + controller call-site (if any) + handler tests. The `GetCurrentUserId()` helper on `BaseApiController` remains until **Task 9** (no controller call-site references it after Task 8).
3. **Task 9** deletes the now-orphaned `GetCurrentUserId()` helper, its tests, and stray `DashboardControllerTests` assertions that depended on it. This is the file-deletion task.
4. **Task 10** regenerates the OpenAPI client and validates the frontend build.

---

### Task 1: Add missing CurrentUserService priority-chain tests (FR-6)

5 of FR-6's 6 scenarios already exist in `CurrentUserServiceTests.cs`. Three need adding: (4) `NameIdentifier` over `sub` over `oid`; (5) `sub` over `oid`; (6) `Id == null` when no supported claim. Do **not** recreate the file — append.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs`

- [ ] **Step 1: Add the three new tests after `GetCurrentUser_WhenUnauthenticated_ReturnsAnonymous` (after line 120, before the closing brace)**

```csharp
    [Fact]
    public void GetCurrentUser_PrefersNameIdentifierOverSubAndOid()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "name-id-123"),
            new Claim("sub", "sub-456"),
            new Claim("oid", "oid-789"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("name-id-123", user.Id);
    }

    [Fact]
    public void GetCurrentUser_PrefersSubOverOid_WhenNameIdentifierAbsent()
    {
        var principal = Authenticated(
            new Claim("sub", "sub-456"),
            new Claim("oid", "oid-789"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("sub-456", user.Id);
    }

    [Fact]
    public void GetCurrentUser_WhenNoSupportedIdClaim_ReturnsNullId()
    {
        var principal = Authenticated(new Claim(ClaimTypes.Name, "Some Display Name"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Null(user.Id);
    }
```

- [ ] **Step 2: Run the new tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CurrentUserServiceTests"`
Expected: PASS — all 11 tests (8 existing + 3 new) green.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs
git commit -m "test: add CurrentUserService claim priority-chain scenarios"
```

---

### Task 2: Migrate `SetCarrierCoolingHandler` to `ICurrentUserService`

Move `ModifiedBy` resolution from controller into handler; return `ErrorCodes.Unauthorized` when id is null/empty (pattern from `CreateMarketingActionHandler.cs:40–45`); drop `ModifiedBy` from `SetCarrierCoolingRequest`; remove `request.ModifiedBy = GetCurrentUserId();` from `CarrierCoolingController`. Constructor parameter order: append `ICurrentUserService` last.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs`

- [ ] **Step 1: Add the Unauthorized test (failing) to `SetCarrierCoolingHandlerTests.cs`**

Add inside the class (e.g. before the closing brace of `SetCarrierCoolingHandlerTests`):

```csharp
    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));
        var sut = new SetCarrierCoolingHandler(_repositoryMock.Object, _catalogMock.Object, currentUserMock.Object);

        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
        };

        var result = await sut.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        _repositoryMock.Verify(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Also add `using Anela.Heblo.Domain.Features.Users;` to the test file if not already present.

- [ ] **Step 2: Run the new test — it must fail to compile (handler ctor doesn't accept ICurrentUserService yet)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL with "no overload for SetCarrierCoolingHandler takes 3 arguments" (or equivalent).

- [ ] **Step 3: Update `SetCarrierCoolingRequest.cs` — remove `ModifiedBy`**

Replace the file body with:

```csharp
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingRequest : IRequest<SetCarrierCoolingResponse>
{
    public Carriers Carrier { get; set; }
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
    public string? CoolingText { get; set; }
}
```

- [ ] **Step 4: Update `SetCarrierCoolingHandler.cs` — inject ICurrentUserService, add Unauthorized guard, source ModifiedBy from currentUser**

Replace the file body with:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingHandler : IRequestHandler<SetCarrierCoolingRequest, SetCarrierCoolingResponse>
{
    private readonly ICarrierCoolingRepository _repository;
    private readonly IShippingMethodCatalog _catalog;
    private readonly ICurrentUserService _currentUserService;

    public SetCarrierCoolingHandler(
        ICarrierCoolingRepository repository,
        IShippingMethodCatalog catalog,
        ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetCarrierCoolingResponse> Handle(
        SetCarrierCoolingRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetCarrierCoolingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Unauthorized,
            };
        }

        var isValidCombo = _catalog.GetAvailableDeliveryOptions()
            .Any(o => o.Carrier == request.Carrier && o.Handling == request.DeliveryHandling);

        if (!isValidCombo)
        {
            return new SetCarrierCoolingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "message", $"Combination of Carrier '{request.Carrier}' and DeliveryHandling '{request.DeliveryHandling}' is not available." }
                }
            };
        }

        var setting = new CarrierCoolingSetting(
            request.Carrier,
            request.DeliveryHandling,
            request.Cooling,
            currentUser.Id,
            request.CoolingText);

        await _repository.UpsertAsync(setting, cancellationToken);

        return new SetCarrierCoolingResponse();
    }
}
```

- [ ] **Step 5: Update `CarrierCoolingController.cs` — remove the `request.ModifiedBy = GetCurrentUserId();` line**

Replace `SetCooling` body so it no longer touches `ModifiedBy`. The full method becomes:

```csharp
    [HttpPut]
    public async Task<ActionResult<SetCarrierCoolingResponse>> SetCooling(
        [FromBody] SetCarrierCoolingRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 6: Update existing handler tests in `SetCarrierCoolingHandlerTests.cs`**

Modify the test class so the constructor builds a default-authenticated `ICurrentUserService` mock, then update the existing two passing tests to assert that the persisted entity's `ModifiedBy` equals the mocked user's id (not the request's, since the request no longer has the field).

Replace the file body with:

```csharp
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class SetCarrierCoolingHandlerTests
{
    private readonly Mock<ICarrierCoolingRepository> _repositoryMock = new();
    private readonly Mock<IShippingMethodCatalog> _catalogMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    public SetCarrierCoolingHandlerTests()
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-123", Name: "Test", Email: null, IsAuthenticated: true));
    }

    private SetCarrierCoolingHandler CreateSut() =>
        new(_repositoryMock.Object, _catalogMock.Object, _currentUserMock.Object);

    private void SetupValidCombo(Carriers carrier, DeliveryHandling handling)
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions())
            .Returns(new List<(Carriers Carrier, DeliveryHandling Handling)> { (carrier, handling) }.AsReadOnly());
    }

    [Fact]
    public async Task Handle_CallsUpsertAndReturnsSuccess_WhenComboIsAvailable()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
        };

        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<CarrierCoolingSetting>(s =>
                    s.Carrier == Carriers.PPL &&
                    s.DeliveryHandling == DeliveryHandling.NaRuky &&
                    s.Cooling == Cooling.L1 &&
                    s.ModifiedBy == "user-123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PersistsCoolingText_WhenProvided()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            CoolingText = "MRAZ",
        };
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<CarrierCoolingSetting>(s => s.CoolingText == "MRAZ"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenComboIsUnavailable()
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions())
            .Returns(new List<(Carriers Carrier, DeliveryHandling Handling)>().AsReadOnly());

        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.Osobak,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
        };

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _repositoryMock.Verify(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
        };

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        _repositoryMock.Verify(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 7: Run the carrier-cooling tests to verify they all pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetCarrierCoolingHandlerTests"`
Expected: PASS — 4 tests green.

- [ ] **Step 8: Verify the full solution still builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS — `GetCurrentUserId()` still defined on `BaseApiController` (used by `DashboardController` and `GiftSettingsController` still); `CarrierCoolingController.SetCooling` no longer touches `ModifiedBy`.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CarrierCooling backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs
git commit -m "refactor: resolve user identity inside SetCarrierCoolingHandler"
```

---

### Task 3: Migrate `SetGiftSettingHandler` to `ICurrentUserService`

Same pattern as Task 2. The `GiftSettingsController` does **not** use `HandleResponse` — it returns `NoContent` on success and `BadRequest` on failure. To preserve HTTP 401 for the Unauthorized response without changing the 204→200 success contract, add an explicit `if (response.ErrorCode == ErrorCodes.Unauthorized) return Unauthorized(response);` branch before `BadRequest`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`

- [ ] **Step 1: Add the Unauthorized test (failing) to `SetGiftSettingHandlerTests.cs`**

Add inside the class:

```csharp
    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty()
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));
        var sut = new SetGiftSettingHandler(_repositoryMock.Object, currentUserMock.Object);

        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        };

        var result = await sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Add `using Anela.Heblo.Application.Shared;` and `using Anela.Heblo.Domain.Features.Users;` to the test file's using block if not already present.

- [ ] **Step 2: Run the test — it must fail to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — `SetGiftSettingHandler` ctor takes only 1 argument.

- [ ] **Step 3: Update `SetGiftSettingCommand.cs` — remove `ModifiedBy`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingCommand : IRequest<SetGiftSettingResponse>
{
    public bool IsEnabled { get; set; }
    public decimal ThresholdCzk { get; set; }
    public string Text { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Update `SetGiftSettingHandler.cs` — inject ICurrentUserService, Unauthorized guard, source ModifiedBy from currentUser**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private const int MaxTextLength = 50;

    private readonly IGiftSettingRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SetGiftSettingHandler(IGiftSettingRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetGiftSettingResponse> Handle(SetGiftSettingCommand command, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Unauthorized,
            };
        }

        if (command.IsEnabled)
        {
            if (command.ThresholdCzk <= 0)
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "ThresholdCzk must be greater than zero when enabled." } },
                };

            if (string.IsNullOrEmpty(command.Text))
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "Text is required when enabled." } },
                };
        }

        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
        await _repository.SaveAsync(setting, cancellationToken);
        return new SetGiftSettingResponse();
    }
}
```

- [ ] **Step 5: Update `GiftSettingsController.cs` — drop ModifiedBy assignment, add Unauthorized branch before BadRequest**

```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/gift-settings")]
public class GiftSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public GiftSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<IActionResult> GetGiftSetting(CancellationToken cancellationToken = default)
    {
        var dto = await _mediator.Send(new GetGiftSettingQuery(), cancellationToken);
        return Ok(dto);
    }

    [HttpPut]
    public async Task<IActionResult> SetGiftSetting(
        [FromBody] SetGiftSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);
        if (response.Success) return NoContent();
        if (response.ErrorCode == ErrorCodes.Unauthorized) return Unauthorized(response);
        return BadRequest(response);
    }
}
```

- [ ] **Step 6: Update existing tests in `SetGiftSettingHandlerTests.cs` to provide the mocked ICurrentUserService**

Replace the file body with:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class SetGiftSettingHandlerTests
{
    private readonly Mock<IGiftSettingRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly SetGiftSettingHandler _sut;

    public SetGiftSettingHandlerTests()
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: "Test", Email: null, IsAuthenticated: true));
        _sut = new SetGiftSettingHandler(_repositoryMock.Object, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_SavesSetting_WhenDisabled()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesSetting_WhenEnabledWithValidValues()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithZeroThreshold()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithEmptyText()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenTextExceedsMaxLength()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty()
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 7: Run gift-setting tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests"`
Expected: PASS — 6 tests green.

- [ ] **Step 8: Verify solution still builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GiftSettings backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs
git commit -m "refactor: resolve user identity inside SetGiftSettingHandler"
```

---

### Task 4: Migrate `GetUserSettingsHandler` to `ICurrentUserService`

Drop `UserId` from `GetUserSettingsRequest`; inject `ICurrentUserService` into the handler; preserve the `"anonymous"` fallback exactly as today.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs`

- [ ] **Step 1: Update test class to mock `ICurrentUserService` (failing — handler ctor mismatch)**

Replace `GetUserSettingsHandlerTests.cs` body with the version below. Key changes: add `_currentUserMock`, default it to a known user id, and remove `UserId` from every `new GetUserSettingsRequest { ... }`. Existing "anonymous" tests assert via the mock (mocking `Id: null`), not via request.

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetUserSettingsHandlerTests
{
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly TimeProvider _timeProvider;
    private readonly GetUserSettingsHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public GetUserSettingsHandlerTests()
    {
        _tileRegistryMock = new Mock<ITileRegistry>();
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: "Test", Email: null, IsAuthenticated: true));

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        _handler = new GetUserSettingsHandler(
            _tileRegistryMock.Object,
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _currentUserMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: "Test", Email: null, IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsNull_ShouldUseAnonymous()
    {
        SetCurrentUserId(null);
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsEmpty_ShouldUseAnonymous()
    {
        SetCurrentUserId("");
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNewUser_ShouldCreateDefaultSettingsWithAutoShowTiles()
    {
        var userId = "user123";
        var autoTiles = new List<TileMetadata>
        {
            MakeTile("auto1", defaultEnabled: true, autoShow: true),
            MakeTile("auto2", defaultEnabled: true, autoShow: true)
        };

        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(autoTiles);
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync((UserDashboardSettings?)null);
        UserDashboardSettings? capturedSettings = null;
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s)
            .ReturnsAsync((UserDashboardSettings s) => s);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Settings.Tiles.Should().HaveCount(2);
        capturedSettings.Should().NotBeNull();
        capturedSettings!.LastModified.Should().Be(FixedUtcNow);
        _repositoryMock.Verify(x => x.AddAsync(It.Is<UserDashboardSettings>(s =>
            s.UserId == userId &&
            s.Tiles.Count == 2)), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNewUser_WithNoAutoShowTiles_ShouldCreateEmptySettings()
    {
        var userId = "user123";
        var tiles = new List<TileMetadata> { MakeTile("manual", defaultEnabled: true, autoShow: false) };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(tiles);
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Settings.Tiles.Should().BeEmpty();
        _repositoryMock.Verify(x => x.AddAsync(It.Is<UserDashboardSettings>(s =>
            s.UserId == userId &&
            s.Tiles.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenExistingUser_NoNewAutoShowTiles_ShouldNotCallUpdate()
    {
        var userId = "user123";
        var existingSettings = CreateExistingUserSettings(userId);
        var allTiles = new List<TileMetadata>
        {
            MakeTile("auto1", defaultEnabled: true, autoShow: true),
            MakeTile("auto2", defaultEnabled: true, autoShow: true)
        };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(allTiles);
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(existingSettings);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Settings.Tiles.Should().HaveCount(2);
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenExistingUser_WithNewAutoShowTile_ShouldBackfillAndUpdate()
    {
        var userId = "user123";
        var existingSettings = CreateExistingUserSettings(userId);
        var allTiles = new List<TileMetadata>
        {
            MakeTile("auto1", defaultEnabled: true, autoShow: true),
            MakeTile("auto2", defaultEnabled: true, autoShow: true),
            MakeTile("newautoshow", defaultEnabled: true, autoShow: true)
        };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(allTiles);
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(existingSettings);

        UserDashboardSettings? capturedUpdate = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedUpdate = s);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Settings.Tiles.Should().HaveCount(3);
        var newTile = result.Settings.Tiles.FirstOrDefault(t => t.TileId == "newautoshow");
        newTile.Should().NotBeNull();
        newTile!.IsVisible.Should().BeTrue();
        newTile.DisplayOrder.Should().Be(2);
        capturedUpdate.Should().NotBeNull();
        capturedUpdate!.LastModified.Should().Be(FixedUtcNow);
        _repositoryMock.Verify(x => x.UpdateAsync(existingSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        var userId = "user123";
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TileMetadata MakeTile(string tileId, bool defaultEnabled = true, bool autoShow = true) =>
        new(tileId, tileId, $"{tileId} description", TileSize.Medium, TileCategory.Finance,
            defaultEnabled, autoShow, Array.Empty<string>());

    private static UserDashboardSettings CreateExistingUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = FixedUtcNow.AddHours(-1),
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "auto1", IsVisible = true, DisplayOrder = 0, LastModified = FixedUtcNow.AddHours(-2) },
                new() { UserId = userId, TileId = "auto2", IsVisible = false, DisplayOrder = 1, LastModified = FixedUtcNow.AddHours(-2) }
            }
        };
    }
}
```

- [ ] **Step 2: Build to confirm failure**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — `GetUserSettingsHandler` ctor takes 4 args, test passes 5.

- [ ] **Step 3: Update `GetUserSettingsRequest.cs` — remove `UserId`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsRequest : IRequest<GetUserSettingsResponse>
{
}
```

- [ ] **Step 4: Update `GetUserSettingsHandler.cs` — inject ICurrentUserService, source userId from it**

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsHandler : IRequestHandler<GetUserSettingsRequest, GetUserSettingsResponse>
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;

    public GetUserSettingsHandler(
        ITileRegistry tileRegistry,
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService)
    {
        _tileRegistry = tileRegistry;
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
    }

    public async Task<GetUserSettingsResponse> Handle(GetUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetCurrentUser().Id;
        var userId = string.IsNullOrEmpty(currentUserId) ? "anonymous" : currentUserId;

        await using var _ = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        var now = _timeProvider.GetUtcNow().DateTime;

        var autoShowTiles = _tileRegistry.GetAvailableTiles()
            .Where(t => t.DefaultEnabled && t.AutoShow)
            .ToList();

        if (settings == null)
        {
            settings = new UserDashboardSettings
            {
                UserId = userId,
                LastModified = now,
                Tiles = autoShowTiles.Select((tile, index) => new UserDashboardTile
                {
                    UserId = userId,
                    TileId = tile.TileId,
                    IsVisible = true,
                    DisplayOrder = index,
                    LastModified = now,
                    DashboardSettings = null!
                }).ToList()
            };

            foreach (var tile in settings.Tiles)
                tile.DashboardSettings = settings;

            await _repository.AddAsync(settings);
        }
        else
        {
            var existingTileIds = settings.Tiles.Select(t => t.TileId).ToHashSet();
            var newAutoShowTiles = autoShowTiles
                .Where(t => !existingTileIds.Contains(t.TileId))
                .ToList();

            if (newAutoShowTiles.Count > 0)
            {
                var maxOrder = settings.Tiles.Count > 0 ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                for (var i = 0; i < newAutoShowTiles.Count; i++)
                {
                    settings.Tiles.Add(new UserDashboardTile
                    {
                        UserId = userId,
                        TileId = newAutoShowTiles[i].TileId,
                        IsVisible = true,
                        DisplayOrder = maxOrder + i + 1,
                        LastModified = now,
                        DashboardSettings = settings
                    });
                }

                settings.LastModified = now;
                await _repository.UpdateAsync(settings);
            }
        }

        return new GetUserSettingsResponse
        {
            Settings = new UserDashboardSettingsDto
            {
                Tiles = settings.Tiles.Select(t => new UserDashboardTileDto
                {
                    TileId = t.TileId,
                    IsVisible = t.IsVisible,
                    DisplayOrder = t.DisplayOrder
                }).ToArray(),
                LastModified = settings.LastModified
            }
        };
    }
}
```

- [ ] **Step 5: Update `SaveUserSettingsHandler` and `GetTileDataHandler` interim — they call `new GetUserSettingsRequest { UserId = ... }`. After removing `UserId` from the request, those references will fail to compile. Patch them temporarily by dropping the `UserId` initializer (full migrations come in Tasks 5 & 6).**

In `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs:32`, change:

```csharp
await _mediator.Send(new GetUserSettingsRequest { UserId = userId }, cancellationToken);
```

to:

```csharp
await _mediator.Send(new GetUserSettingsRequest(), cancellationToken);
```

In `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs:34–36`, change:

```csharp
var settingsResponse = await _mediator.Send(
    new GetUserSettingsRequest { UserId = userId },
    cancellationToken);
```

to:

```csharp
var settingsResponse = await _mediator.Send(
    new GetUserSettingsRequest(),
    cancellationToken);
```

In `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs:36`, change:

```csharp
await _mediator.Send(new GetUserSettingsRequest { UserId = resolvedUserId }, cancellationToken);
```

to:

```csharp
await _mediator.Send(new GetUserSettingsRequest(), cancellationToken);
```

(`SaveUserSettingsHandler` and `GetTileDataHandler` will still resolve `userId` themselves for their own logic in Tasks 5/6; only the inner `GetUserSettingsRequest` send drops its now-removed property. The mutator's call is also updated — once the request has no `UserId`, the inner `GetUserSettings` invocation resolves the same user via `ICurrentUserService` regardless.)

- [ ] **Step 6: Update `DashboardController.GetUserSettings` to stop calling `GetCurrentUserId()`**

In `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:34–42`, replace `GetUserSettings`:

```csharp
    [HttpGet("settings")]
    public async Task<ActionResult<Application.Features.Dashboard.Contracts.UserDashboardSettingsDto>> GetUserSettings()
    {
        var request = new GetUserSettingsRequest();
        var response = await _mediator.Send(request);

        return Ok(response.Settings);
    }
```

- [ ] **Step 7: Run GetUserSettings tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetUserSettingsHandlerTests"`
Expected: PASS — 7 tests green.

- [ ] **Step 8: Verify solution builds (SaveUserSettings/GetTileData/Mutator tests will still pass — handlers' inner `GetUserSettingsRequest` calls were patched in Step 5)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs backend/src/Anela.Heblo.API/Controllers/DashboardController.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs
git commit -m "refactor: resolve user identity inside GetUserSettingsHandler"
```

---

### Task 5: Migrate `SaveUserSettingsHandler` to `ICurrentUserService`

Drop `UserId` from `SaveUserSettingsRequest`; inject `ICurrentUserService`; preserve the `"anonymous"` fallback.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs`

- [ ] **Step 1: Update `SaveUserSettingsHandlerTests.cs` to mock ICurrentUserService and drop UserId from request literals (failing — handler ctor mismatch)**

Replace file body with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class SaveUserSettingsHandlerTests
{
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly TimeProvider _timeProvider;
    private readonly SaveUserSettingsHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public SaveUserSettingsHandlerTests()
    {
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _mediatorMock = new Mock<IMediator>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: "Test", Email: null, IsAuthenticated: true));

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        _mediatorMock.Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse());

        _handler = new SaveUserSettingsHandler(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object,
            _currentUserMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: "Test", Email: null, IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsNull_ShouldUseAnonymous()
    {
        SetCurrentUserId(null);
        var request = new SaveUserSettingsRequest
        {
            Tiles = new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } }
        };
        var existingSettings = CreateSampleUserSettings("anonymous");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync(existingSettings);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<UserDashboardSettings>(s => s.UserId == "anonymous")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsEmpty_ShouldUseAnonymous()
    {
        SetCurrentUserId("");
        var request = new SaveUserSettingsRequest
        {
            Tiles = new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } }
        };
        var existingSettings = CreateSampleUserSettings("anonymous");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync(existingSettings);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<UserDashboardSettings>(s => s.UserId == "anonymous")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidUserId_ShouldSaveSettings()
    {
        var userId = "user123";
        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
            }
        };

        var existingSettings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "tile1", IsVisible = false, DisplayOrder = 5, LastModified = DateTime.UtcNow },
                new() { UserId = userId, TileId = "tile2", IsVisible = true, DisplayOrder = 6, LastModified = DateTime.UtcNow }
            }
        };
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(existingSettings);

        UserDashboardSettings? capturedSettings = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s);

        await _handler.Handle(request, CancellationToken.None);

        capturedSettings.Should().NotBeNull();
        capturedSettings!.UserId.Should().Be(userId);
        capturedSettings.Tiles.Should().HaveCount(2);
        capturedSettings.Tiles.Should().Contain(t => t.TileId == "tile1" && t.IsVisible && t.DisplayOrder == 0);
        capturedSettings.Tiles.Should().Contain(t => t.TileId == "tile2" && !t.IsVisible && t.DisplayOrder == 1);
        capturedSettings.LastModified.Should().Be(FixedUtcNow);
    }

    [Fact]
    public async Task Handle_WhenNoTiles_ShouldSaveEmptySettings()
    {
        var userId = "user123";
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };
        var existingSettings = CreateSampleUserSettings(userId);
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(existingSettings);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<UserDashboardSettings>(s => s.UserId == userId && s.Tiles.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNullTiles_ShouldNotMutateExistingTiles()
    {
        var userId = "user123";
        var request = new SaveUserSettingsRequest { Tiles = null! };
        var existingSettings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "tile1", IsVisible = true, DisplayOrder = 0, LastModified = DateTime.UtcNow }
            }
        };
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(existingSettings);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<UserDashboardSettings>(s => s.UserId == userId && s.Tiles.Count == 1)), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccessResponse()
    {
        var request = new SaveUserSettingsRequest
        {
            Tiles = new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } }
        };
        var existingSettings = CreateSampleUserSettings("user123");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(existingSettings);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        var userId = "user123";
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(CreateSampleUserSettings(userId));

        await _handler.Handle(request, CancellationToken.None);

        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SendsGetUserSettingsBeforeAcquiringLock()
    {
        var userId = "user123";
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(CreateSampleUserSettings(userId));

        var callOrder = new List<string>();
        _mediatorMock.Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("mediator"))
            .ReturnsAsync(new GetUserSettingsResponse());

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("lock"))
            .ReturnsAsync(noOpDisposable.Object);

        await _handler.Handle(request, CancellationToken.None);

        callOrder.Should().ContainInOrder("mediator", "lock");
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>()
        };
    }
}
```

- [ ] **Step 2: Update `SaveUserSettingsRequest.cs` — remove `UserId`**

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsRequest : IRequest<SaveUserSettingsResponse>
{
    public UserDashboardTileDto[] Tiles { get; set; } = [];
}
```

- [ ] **Step 3: Update `SaveUserSettingsHandler.cs` — inject ICurrentUserService and resolve userId**

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public SaveUserSettingsHandler(
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        IMediator mediator,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    public async Task<SaveUserSettingsResponse> Handle(SaveUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetCurrentUser().Id;
        var userId = string.IsNullOrEmpty(currentUserId) ? "anonymous" : currentUserId;

        await _mediator.Send(new GetUserSettingsRequest(), cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        if (settings == null)
        {
            return new SaveUserSettingsResponse();
        }

        if (request.Tiles != null)
        {
            foreach (var tileDto in request.Tiles)
            {
                var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileDto.TileId);
                if (existingTile != null)
                {
                    existingTile.IsVisible = tileDto.IsVisible;
                    existingTile.DisplayOrder = tileDto.DisplayOrder;
                    existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
                }
                else
                {
                    settings.Tiles.Add(new UserDashboardTile
                    {
                        UserId = userId,
                        TileId = tileDto.TileId,
                        IsVisible = tileDto.IsVisible,
                        DisplayOrder = tileDto.DisplayOrder,
                        LastModified = _timeProvider.GetUtcNow().DateTime,
                        DashboardSettings = settings
                    });
                }
            }
        }

        settings.UserId = userId;
        settings.LastModified = _timeProvider.GetUtcNow().DateTime;
        await _repository.UpdateAsync(settings);

        return new SaveUserSettingsResponse();
    }
}
```

- [ ] **Step 4: Update `DashboardController.SaveUserSettings` to stop assigning `UserId`**

In `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:44–52`, replace `SaveUserSettings`:

```csharp
    [HttpPost("settings")]
    public async Task<ActionResult> SaveUserSettings([FromBody] SaveUserSettingsRequest request)
    {
        await _mediator.Send(request);

        return Ok();
    }
```

- [ ] **Step 5: Run SaveUserSettings tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SaveUserSettingsHandlerTests"`
Expected: PASS — 8 tests green.

- [ ] **Step 6: Verify solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings backend/src/Anela.Heblo.API/Controllers/DashboardController.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs
git commit -m "refactor: resolve user identity inside SaveUserSettingsHandler"
```

---

### Task 6: Migrate `GetTileDataHandler` to `ICurrentUserService`

Drop `UserId` from `GetTileDataRequest`; inject `ICurrentUserService`; preserve the `"anonymous"` fallback.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs`

- [ ] **Step 1: Update `GetTileDataHandlerTests.cs` to mock ICurrentUserService and drop UserId from request literals**

Replace file body with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetTileDataHandlerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly Mock<ILogger<GetTileDataHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly GetTileDataHandler _handler;

    public GetTileDataHandlerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tileRegistryMock = new Mock<ITileRegistry>();
        _loggerMock = new Mock<ILogger<GetTileDataHandler>>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user1", Name: "Test", Email: null, IsAuthenticated: true));

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 4 });
        _handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options, _loggerMock.Object, _currentUserMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: "Test", Email: null, IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    private void SetupUserSettings(UserDashboardTileDto[] tiles)
    {
        _mediatorMock.Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse { Settings = new UserDashboardSettingsDto { Tiles = tiles } });
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsNull_ShouldUseAnonymous()
    {
        SetCurrentUserId(null);
        SetupUserSettings(Array.Empty<UserDashboardTileDto>());

        var result = await _handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        // Mutator/handler is responsible for the "anonymous" fallback when resolving settings.
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsEmpty_ShouldUseAnonymous()
    {
        SetCurrentUserId("");
        SetupUserSettings(Array.Empty<UserDashboardTileDto>());

        var result = await _handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoVisibleTiles_ShouldReturnEmpty()
    {
        SetupUserSettings(new[]
        {
            new UserDashboardTileDto { TileId = "tile-a", IsVisible = false, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "tile-b", IsVisible = false, DisplayOrder = 1 }
        });

        var result = await _handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Tiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTileNotFound_ShouldReturnErrorDto()
    {
        const string tileId = "missing-tile";
        SetupUserSettings(new[] { new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 } });
        _tileRegistryMock.Setup(x => x.GetTileMetadata(tileId)).Returns((TileMetadata?)null);

        var result = await _handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.TileId.Should().Be(tileId);
        tile.Title.Should().Be("Error");
        tile.Category.Should().Be("Error");
    }

    [Fact]
    public async Task Handle_WhenTileThrows_ShouldReturnErrorDto()
    {
        const string tileId = "throwing-tile";
        SetupUserSettings(new[] { new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 } });
        _tileRegistryMock.Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Throwing Tile", "Desc", TileSize.Medium,
                TileCategory.Finance, true, false, Array.Empty<string>()));
        _tileRegistryMock.Setup(x => x.GetTileDataAsync(tileId, It.IsAny<Dictionary<string, string>?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.TileId.Should().Be(tileId);
        tile.Title.Should().Be("Error");
        tile.Category.Should().Be("Error");
        tile.Description.Should().Be($"Failed to load tile '{tileId}'");
    }

    [Fact]
    public async Task Handle_WhenTilesHaveOutOfOrderDisplayOrder_ShouldReturnInOrder()
    {
        SetupUserSettings(new[]
        {
            new UserDashboardTileDto { TileId = "tile-c", IsVisible = true, DisplayOrder = 2 },
            new UserDashboardTileDto { TileId = "tile-a", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "tile-b", IsVisible = true, DisplayOrder = 1 }
        });

        foreach (var id in new[] { "tile-a", "tile-b", "tile-c" })
        {
            var capturedId = id;
            _tileRegistryMock.Setup(x => x.GetTileMetadata(capturedId))
                .Returns(new TileMetadata(capturedId, $"Title {capturedId}", "Desc", TileSize.Small,
                    TileCategory.System, true, false, Array.Empty<string>()));
            _tileRegistryMock.Setup(x => x.GetTileDataAsync(capturedId, It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(new { Id = capturedId });
        }

        var result = await _handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        var tiles = result.Tiles.ToArray();
        tiles.Should().HaveCount(3);
        tiles[0].Title.Should().Be("Title tile-a");
        tiles[1].Title.Should().Be("Title tile-b");
        tiles[2].Title.Should().Be("Title tile-c");
    }

    [Fact]
    public async Task Handle_WhenTilesAreVisible_ShouldReturnTileData()
    {
        const string tileId = "analytics-tile";
        var expectedData = new { Count = 42, Status = "Active" };
        SetupUserSettings(new[] { new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 } });
        _tileRegistryMock.Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Analytics", "Analytics description", TileSize.Large,
                TileCategory.Finance, true, true, new[] { "read", "analytics" }));
        _tileRegistryMock.Setup(x => x.GetTileDataAsync(tileId, null)).ReturnsAsync(expectedData);

        var result = await _handler.Handle(new GetTileDataRequest { TileParameters = null }, CancellationToken.None);

        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.Title.Should().Be("Analytics");
        tile.Description.Should().Be("Analytics description");
        tile.Size.Should().Be("Large");
        tile.Category.Should().Be("Finance");
        tile.DefaultEnabled.Should().BeTrue();
        tile.AutoShow.Should().BeTrue();
        tile.RequiredPermissions.Should().BeEquivalentTo(new[] { "read", "analytics" });
        tile.Data.Should().Be(expectedData);
    }

    [Fact]
    public async Task Handle_WhenTwoSlowTilesAndMaxDoP2_ShouldLoadInParallel()
    {
        const int tileCount = 2;
        var startedCount = 0;
        var allStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var anyTimedOut = false;

        SetupUserSettings(new[]
        {
            new UserDashboardTileDto { TileId = "slow-tile-1", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "slow-tile-2", IsVisible = true, DisplayOrder = 1 }
        });

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 2 });
        var handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options, _loggerMock.Object, _currentUserMock.Object);

        foreach (var id in new[] { "slow-tile-1", "slow-tile-2" })
        {
            var capturedId = id;
            _tileRegistryMock.Setup(x => x.GetTileMetadata(capturedId))
                .Returns(new TileMetadata(capturedId, "Slow", "Slow tile", TileSize.Small,
                    TileCategory.System, true, false, Array.Empty<string>()));
            _tileRegistryMock.Setup(x => x.GetTileDataAsync(capturedId, It.IsAny<Dictionary<string, string>?>()))
                .Returns(async () =>
                {
                    if (Interlocked.Increment(ref startedCount) >= tileCount)
                        allStartedTcs.TrySetResult(true);
                    try { await allStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (TimeoutException) { anyTimedOut = true; throw; }
                    return (object)new { Id = capturedId };
                });
        }

        var result = await handler.Handle(new GetTileDataRequest(), CancellationToken.None);

        anyTimedOut.Should().BeFalse("tiles should be loaded in parallel, not sequentially");
        result.Tiles.Should().HaveCount(tileCount);
    }
}
```

- [ ] **Step 2: Update `GetTileDataRequest.cs` — remove `UserId`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataRequest : IRequest<GetTileDataResponse>
{
    public Dictionary<string, string>? TileParameters { get; set; }
}
```

- [ ] **Step 3: Update `GetTileDataHandler.cs` — inject ICurrentUserService, source userId locally**

```csharp
using System.Collections.Concurrent;
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
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

    public GetTileDataHandler(
        IMediator mediator,
        ITileRegistry tileRegistry,
        IOptions<DashboardOptions> dashboardOptions,
        ILogger<GetTileDataHandler> logger,
        ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _tileRegistry = tileRegistry;
        _dashboardOptions = dashboardOptions.Value;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<GetTileDataResponse> Handle(GetTileDataRequest request, CancellationToken cancellationToken)
    {
        // GetUserSettingsHandler resolves identity itself (and applies the "anonymous"
        // fallback); we just forward the request.
        var settingsResponse = await _mediator.Send(new GetUserSettingsRequest(), cancellationToken);

        var visibleTiles = settingsResponse.Settings.Tiles
            .Where(t => t.IsVisible)
            .OrderBy(t => t.DisplayOrder)
            .ToList();

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
                Data = td.Data
            })
            .ToArray();

        return new GetTileDataResponse { Tiles = tiles };
    }
}
```

Note: `_currentUserService` is injected for symmetry with the other Dashboard handlers (the spec/arch-review says all 5 Dashboard handlers inject `ICurrentUserService`). It's unused in the handler body because `GetUserSettingsHandler` resolves identity itself when we delegate via Mediator. If desired, you could remove the field, but keeping it satisfies the spec's FR-2 acceptance criterion that "All five Dashboard handlers receive `ICurrentUserService` via constructor injection" and matches the consistent pattern across modules. If the field is genuinely unused, the `dotnet format` analyzer (CA1823) may warn — suppress with `// ReSharper disable once NotAccessedField.Local` only if the warning fires.

- [ ] **Step 4: Update `DashboardController.GetTileData` to stop assigning `UserId`**

In `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:54–66`, replace `GetTileData`:

```csharp
    [HttpGet("data")]
    public async Task<ActionResult<IEnumerable<Application.Features.Dashboard.Contracts.DashboardTileDto>>> GetTileData([FromQuery] Dictionary<string, string>? tileParameters = null)
    {
        var request = new GetTileDataRequest
        {
            TileParameters = tileParameters
        };
        var response = await _mediator.Send(request);

        return Ok(response.Tiles);
    }
```

- [ ] **Step 5: Run GetTileData tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTileDataHandlerTests"`
Expected: PASS — 8 tests green.

- [ ] **Step 6: Verify solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData backend/src/Anela.Heblo.API/Controllers/DashboardController.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs
git commit -m "refactor: resolve user identity inside GetTileDataHandler"
```

---

### Task 7: Migrate `EnableTileHandler` to `ICurrentUserService`

Drop `UserId` from `EnableTileRequest`; inject `ICurrentUserService`; pass the resolved id (without normalization — mutator does that) to the mutator.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs`

- [ ] **Step 1: Update `EnableTileHandlerTests.cs` to mock ICurrentUserService and drop UserId from request literals**

Replace file body with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class EnableTileHandlerTests
{
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly TimeProvider _timeProvider;
    private readonly EnableTileHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public EnableTileHandlerTests()
    {
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _mediatorMock = new Mock<IMediator>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: "Test", Email: null, IsAuthenticated: true));

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        _mediatorMock.Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse());

        var mutator = new UserDashboardSettingsMutator(
            _repositoryMock.Object, _lockMock.Object, _timeProvider, _mediatorMock.Object);

        _handler = new EnableTileHandler(mutator, _currentUserMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: "Test", Email: null, IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsNull_ShouldUseAnonymous()
    {
        SetCurrentUserId(null);
        var request = new EnableTileRequest { TileId = "tile1" };
        var userSettings = CreateSampleUserSettings("anonymous");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync(userSettings);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<UserDashboardSettings>(s => s.UserId == "anonymous")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileExists_ShouldEnableTile()
    {
        var userId = "user123";
        var tileId = "tile1";
        var request = new EnableTileRequest { TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = false;
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(userSettings);

        UserDashboardSettings? capturedSettings = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedSettings.Should().NotBeNull();
        var enabledTile = capturedSettings!.Tiles.First(t => t.TileId == tileId);
        enabledTile.IsVisible.Should().BeTrue();
        enabledTile.LastModified.Should().Be(FixedUtcNow);
    }

    [Fact]
    public async Task Handle_WhenTileDoesNotExist_ShouldAddNewTile()
    {
        var userId = "user123";
        var tileId = "new-tile";
        var request = new EnableTileRequest { TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        var originalTileCount = userSettings.Tiles.Count;
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(userSettings);

        UserDashboardSettings? capturedSettings = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedSettings.Should().NotBeNull();
        capturedSettings!.Tiles.Should().HaveCount(originalTileCount + 1);
        var newTile = capturedSettings.Tiles.First(t => t.TileId == tileId);
        newTile.IsVisible.Should().BeTrue();
        newTile.UserId.Should().Be(userId);
        newTile.DisplayOrder.Should().Be(originalTileCount);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsNull_ShouldReturnFailure()
    {
        var request = new EnableTileRequest { TileId = null! };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsEmpty_ShouldReturnFailure()
    {
        var request = new EnableTileRequest { TileId = "" };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        var userId = "user123";
        var request = new EnableTileRequest { TileId = "tile1" };
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(CreateSampleUserSettings(userId));

        await _handler.Handle(request, CancellationToken.None);

        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "tile1", IsVisible = false, DisplayOrder = 0, LastModified = DateTime.UtcNow.AddHours(-1) },
                new() { UserId = userId, TileId = "tile2", IsVisible = true, DisplayOrder = 1, LastModified = DateTime.UtcNow.AddHours(-1) }
            }
        };
    }
}
```

- [ ] **Step 2: Update `EnableTileRequest.cs` — remove `UserId`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileRequest : IRequest<EnableTileResponse>
{
    public string TileId { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Update `EnableTileHandler.cs` — inject ICurrentUserService, resolve userId at the boundary, pass to mutator**

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

internal sealed class EnableTileHandler : IRequestHandler<EnableTileRequest, EnableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;
    private readonly ICurrentUserService _currentUserService;

    public EnableTileHandler(IUserDashboardSettingsMutator mutator, ICurrentUserService currentUserService)
    {
        _mutator = mutator;
        _currentUserService = currentUserService;
    }

    public async Task<EnableTileResponse> Handle(EnableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new EnableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        var userId = _currentUserService.GetCurrentUser().Id;

        await _mutator.MutateAsync(
            userId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = true,
            onTileMissing: (settings, resolvedUserId) =>
            {
                var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                return new UserDashboardTile
                {
                    UserId = resolvedUserId,
                    TileId = request.TileId,
                    IsVisible = true,
                    DisplayOrder = maxOrder + 1,
                    DashboardSettings = settings
                };
            },
            cancellationToken);

        return new EnableTileResponse();
    }
}
```

- [ ] **Step 4: Update `DashboardController.EnableTile` to stop assigning `UserId`**

In `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:68–80`, replace `EnableTile`:

```csharp
    [HttpPost("tiles/{tileId}/enable")]
    public async Task<ActionResult> EnableTile(string tileId)
    {
        var request = new EnableTileRequest
        {
            TileId = tileId
        };
        await _mediator.Send(request);

        return Ok();
    }
```

- [ ] **Step 5: Run EnableTile tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~EnableTileHandlerTests"`
Expected: PASS — 6 tests green.

- [ ] **Step 6: Verify solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile backend/src/Anela.Heblo.API/Controllers/DashboardController.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs
git commit -m "refactor: resolve user identity inside EnableTileHandler"
```

---

### Task 8: Migrate `DisableTileHandler` to `ICurrentUserService`

Same as Task 7 but for the disable variant.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs`

- [ ] **Step 1: Update `DisableTileHandlerTests.cs` to mock ICurrentUserService and drop UserId from request literals**

Replace file body with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class DisableTileHandlerTests
{
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly TimeProvider _timeProvider;
    private readonly DisableTileHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public DisableTileHandlerTests()
    {
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _mediatorMock = new Mock<IMediator>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: "Test", Email: null, IsAuthenticated: true));

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        _mediatorMock.Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse());

        var mutator = new UserDashboardSettingsMutator(
            _repositoryMock.Object, _lockMock.Object, _timeProvider, _mediatorMock.Object);

        _handler = new DisableTileHandler(mutator, _currentUserMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: "Test", Email: null, IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsNull_ShouldUseAnonymous()
    {
        SetCurrentUserId(null);
        var request = new DisableTileRequest { TileId = "tile1" };
        var userSettings = CreateSampleUserSettings("anonymous");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("anonymous")).ReturnsAsync(userSettings);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileExists_ShouldDisableTile()
    {
        var userId = "user123";
        var tileId = "tile1";
        var request = new DisableTileRequest { TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = true;
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(userSettings);

        UserDashboardSettings? capturedSettings = null;
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedSettings.Should().NotBeNull();
        capturedSettings!.Tiles.First(t => t.TileId == tileId).IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTileDoesNotExist_ShouldNotCallUpdate()
    {
        var userId = "user123";
        var request = new DisableTileRequest { TileId = "nonexistent-tile" };
        var userSettings = CreateSampleUserSettings(userId);
        var originalTileCount = userSettings.Tiles.Count;
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(userSettings);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        userSettings.Tiles.Should().HaveCount(originalTileCount);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsNull_ShouldReturnFailure()
    {
        var request = new DisableTileRequest { TileId = null! };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsEmpty_ShouldReturnFailure()
    {
        var request = new DisableTileRequest { TileId = "" };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        var userId = "user123";
        var request = new DisableTileRequest { TileId = "tile1" };
        _repositoryMock.Setup(x => x.GetByUserIdAsync(userId)).ReturnsAsync(CreateSampleUserSettings(userId));

        await _handler.Handle(request, CancellationToken.None);

        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "tile1", IsVisible = true, DisplayOrder = 0, LastModified = DateTime.UtcNow.AddHours(-1) },
                new() { UserId = userId, TileId = "tile2", IsVisible = false, DisplayOrder = 1, LastModified = DateTime.UtcNow.AddHours(-1) }
            }
        };
    }
}
```

- [ ] **Step 2: Update `DisableTileRequest.cs` — remove `UserId`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

public class DisableTileRequest : IRequest<DisableTileResponse>
{
    public string TileId { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Update `DisableTileHandler.cs` — inject ICurrentUserService, resolve userId at the boundary, pass to mutator**

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

internal sealed class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;
    private readonly ICurrentUserService _currentUserService;

    public DisableTileHandler(IUserDashboardSettingsMutator mutator, ICurrentUserService currentUserService)
    {
        _mutator = mutator;
        _currentUserService = currentUserService;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new DisableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        var userId = _currentUserService.GetCurrentUser().Id;

        await _mutator.MutateAsync(
            userId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = false,
            onTileMissing: null,
            cancellationToken);

        return new DisableTileResponse();
    }
}
```

- [ ] **Step 4: Update `DashboardController.DisableTile` to stop assigning `UserId`**

In `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:82–94`, replace `DisableTile`:

```csharp
    [HttpPost("tiles/{tileId}/disable")]
    public async Task<ActionResult> DisableTile(string tileId)
    {
        var request = new DisableTileRequest
        {
            TileId = tileId
        };
        await _mediator.Send(request);

        return Ok();
    }
```

- [ ] **Step 5: Run DisableTile tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DisableTileHandlerTests"`
Expected: PASS — 6 tests green.

- [ ] **Step 6: Verify solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile backend/src/Anela.Heblo.API/Controllers/DashboardController.cs backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs
git commit -m "refactor: resolve user identity inside DisableTileHandler"
```

---

### Task 9: Remove `BaseApiController.GetCurrentUserId()` and update controller-level tests (FR-1 + FR-6 deletion)

At this point no controller calls `GetCurrentUserId()`. Delete the helper + claim using directive, delete the now-orphaned `BaseApiControllerTests.cs`, and adjust `DashboardControllerTests` so it no longer asserts that the controller propagates `UserId` into requests (the controller no longer does that — handlers resolve identity).

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs`

- [ ] **Step 1: Delete `BaseApiControllerTests.cs`**

Run: `rm backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs`

(Use `git rm` so the deletion is staged: `git rm backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs`.)

- [ ] **Step 2: Update `BaseApiController.cs` — remove the `GetCurrentUserId()` helper and the now-unused `System.Security.Claims` using**

Replace the file body with:

```csharp
using System.Net;
using System.Reflection;
using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Base controller that provides common functionality for all API controllers
/// </summary>
public abstract class BaseApiController : ControllerBase
{
    private ILogger? _logger;

    /// <summary>
    /// Gets the logger for the current controller type
    /// </summary>
    protected ILogger Logger => _logger ??= HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

    /// <summary>
    /// Handles a response from a MediatR handler and returns the appropriate HTTP status code
    /// based on the Success property and ErrorCode attribute
    /// </summary>
    /// <typeparam name="T">The response type</typeparam>
    /// <param name="response">The response from the MediatR handler</param>
    /// <returns>ActionResult with appropriate HTTP status code</returns>
    protected ActionResult<T> HandleResponse<T>(T response) where T : BaseResponse
    {
        if (response.Success)
        {
            return Ok(response);
        }

        // Log warning for failed responses
        if (response.ErrorCode.HasValue)
        {
            Logger.LogWarning("Request failed with error code {ErrorCode}: {Params}",
                response.ErrorCode,
                response.Params != null ? string.Join(", ", response.Params.Select(p => $"{p.Key}={p.Value}")) : "no params");

            var statusCode = GetStatusCodeForError(response.ErrorCode.Value);

            // Return specific ActionResult types for common status codes to match test expectations
            return statusCode switch
            {
                HttpStatusCode.BadRequest => BadRequest(response),
                HttpStatusCode.NotFound => NotFound(response),
                HttpStatusCode.Unauthorized => Unauthorized(response),
                HttpStatusCode.Forbidden => Forbid(),
                HttpStatusCode.ServiceUnavailable => StatusCode((int)statusCode, response),
                HttpStatusCode.InternalServerError => StatusCode((int)statusCode, response),
                _ => StatusCode((int)statusCode, response)
            };
        }

        Logger.LogWarning("Request failed without error code");
        return BadRequest(response);
    }

    /// <summary>
    /// Gets the HTTP status code for a given error code based on its HttpStatusCodeAttribute
    /// </summary>
    /// <param name="errorCode">The error code</param>
    /// <returns>The HTTP status code</returns>
    private static HttpStatusCode GetStatusCodeForError(ErrorCodes errorCode)
    {
        var field = typeof(ErrorCodes).GetField(errorCode.ToString());
        var attribute = field?.GetCustomAttribute<HttpStatusCodeAttribute>();

        return attribute?.StatusCode ?? HttpStatusCode.BadRequest;
    }
}
```

- [ ] **Step 3: Update `DashboardControllerTests.cs` — drop `UserId` assertions in `Send` matchers**

Replace assertions that previously checked `r.UserId == "test-user-123"` with matchers that no longer reference `UserId`. Replace the file body with:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;
using Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class DashboardControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new DashboardController(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetAvailableTiles_ShouldReturnOkWithTiles()
    {
        var expectedTiles = new[]
        {
            new DashboardTileDto { TileId = "tile1", Title = "Test Tile 1", Description = "Description 1", Size = "Small", Category = "Analytics" },
            new DashboardTileDto { TileId = "tile2", Title = "Test Tile 2", Description = "Description 2", Size = "Large", Category = "Finance" }
        };
        var response = new GetAvailableTilesResponse { Tiles = expectedTiles };
        _mediatorMock.Setup(x => x.Send(It.IsAny<GetAvailableTilesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetAvailableTiles();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var tiles = okResult.Value.Should().BeAssignableTo<IEnumerable<DashboardTileDto>>().Subject;
        tiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUserSettings_ShouldReturnOkWithUserSettings()
    {
        var expectedSettings = new UserDashboardSettingsDto
        {
            Tiles = new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } }
        };
        var response = new GetUserSettingsResponse { Settings = expectedSettings };
        _mediatorMock.Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetUserSettings();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(expectedSettings);
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveUserSettings_ShouldReturnOk()
    {
        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
            }
        };
        _mediatorMock.Setup(x => x.Send(It.IsAny<SaveUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveUserSettingsResponse { Success = true });

        var result = await _controller.SaveUserSettings(request);

        result.Should().BeOfType<OkResult>();
        _mediatorMock.Verify(x => x.Send(
            It.Is<SaveUserSettingsRequest>(r =>
                r.Tiles.Length == 2 &&
                r.Tiles.Any(t => t.TileId == "tile1" && t.IsVisible) &&
                r.Tiles.Any(t => t.TileId == "tile2" && !t.IsVisible)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTileData_ShouldReturnOkWithTileData()
    {
        var expectedTiles = new[]
        {
            new DashboardTileDto { TileId = "tile1", Title = "Analytics Tile", Data = new { Count = 42 } }
        };
        var response = new GetTileDataResponse { Tiles = expectedTiles };
        _mediatorMock.Setup(x => x.Send(It.IsAny<GetTileDataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetTileData();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var tiles = okResult.Value.Should().BeAssignableTo<IEnumerable<DashboardTileDto>>().Subject;
        tiles.Should().HaveCount(1);
        tiles.First().TileId.Should().Be("tile1");
    }

    [Fact]
    public async Task EnableTile_ShouldReturnOk()
    {
        var tileId = "analytics-tile";
        _mediatorMock.Setup(x => x.Send(It.IsAny<EnableTileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnableTileResponse { Success = true });

        var result = await _controller.EnableTile(tileId);

        result.Should().BeOfType<OkResult>();
        _mediatorMock.Verify(x => x.Send(
            It.Is<EnableTileRequest>(r => r.TileId == tileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisableTile_ShouldReturnOk()
    {
        var tileId = "analytics-tile";
        _mediatorMock.Setup(x => x.Send(It.IsAny<DisableTileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisableTileResponse { Success = true });

        var result = await _controller.DisableTile(tileId);

        result.Should().BeOfType<OkResult>();
        _mediatorMock.Verify(x => x.Send(
            It.Is<DisableTileRequest>(r => r.TileId == tileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 4: Confirm the helper and its tests are gone — repo-wide search**

Run: `grep -rn "GetCurrentUserId" backend/src backend/test || echo "no matches"`
Expected: `no matches`.

Run: `grep -rn "User.FindFirst" backend/src/Anela.Heblo.API/Controllers || echo "no matches"`
Expected: `no matches`.

- [ ] **Step 5: Build + run the full backend test suite**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln`
Expected: SUCCESS, all tests green. (Confirms `CurrentUserServiceTests` covers the priority chain, `BaseApiControllerTests` is gone without orphaning references, and all migrated handlers compile + pass.)

- [ ] **Step 6: Run `dotnet format` to satisfy the format gate**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: SUCCESS. If it fails, run `dotnet format backend/Anela.Heblo.sln` and stage the cleanups.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs
git commit -m "refactor: remove BaseApiController.GetCurrentUserId helper"
```

(The `git add` will pick up the deletion of `BaseApiControllerTests.cs` because it was staged with `git rm` in Step 1.)

---

### Task 10: Regenerate OpenAPI client + validate frontend (FR-5)

Backend build regenerates the TS client. The audit in the spec verified no hooks send `userId` / `modifiedBy` today, so no handwritten frontend change is needed; this task just validates that assumption holds post-regen.

**Files:**
- Validate (do not hand-edit): `frontend/src/api/generated/` (NSwag output — actual filename depends on build output; check the most recently modified file in that directory after the regen build).

- [ ] **Step 1: Trigger the client regen by running the backend build (NSwag MSBuild target)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: SUCCESS. The build outputs a regenerated TypeScript client. (If your local toolchain runs NSwag separately rather than as an MSBuild target, run the script per `docs/development/api-client-generation.md`.)

- [ ] **Step 2: Verify the regenerated DTOs no longer expose the removed fields**

Run: `grep -rn "userId" frontend/src/api/generated/ || echo "no matches"`
Expected output should NOT include any of: `SaveUserSettingsRequest`, `GetTileDataRequest`, `EnableTileRequest`, `DisableTileRequest`, `GetUserSettingsRequest` carrying a `userId` field. Existing matches for unrelated features (e.g. `UpdateMeetingTaskRequest.assignedUserId`) are fine — ignore.

Run: `grep -rn "modifiedBy" frontend/src/api/generated/ || echo "no matches"`
Expected: the only `modifiedBy` reference should be on response DTOs (e.g. `GiftSettingDto`, `CarrierCoolingDto`) — those properties are read-only output, not request inputs, and remain unchanged. NO `modifiedBy` should exist on `SetCarrierCoolingRequest` or `SetGiftSettingCommand`.

- [ ] **Step 3: Confirm no handcrafted hook sends a now-deleted field**

Run: `grep -rn "modifiedBy\s*:" frontend/src/api/hooks/ || echo "no matches"`
Expected: the only match is the existing read-side `modifiedBy: string | null;` on `GiftSettingDto` at `frontend/src/api/hooks/useGiftSetting.ts:9`. That property remains valid because the response DTO still carries it. No request-body sites should be flagged.

Run: `grep -rn "userId" frontend/src/api/hooks/useDashboard.ts || echo "no matches"`
Expected: `no matches`.

- [ ] **Step 4: Frontend build + lint must pass**

Run: `cd frontend && npm run build`
Expected: SUCCESS.

Run: `cd frontend && npm run lint`
Expected: SUCCESS — no errors related to removed properties.

- [ ] **Step 5: Stage the regenerated client + commit**

If the regen produced changes to `frontend/src/api/generated/`, they need to be committed alongside the backend changes (the repo convention is to check in the generated client — verified by inspecting the existing `frontend/src/api/` structure). Stage them:

```bash
git add frontend/src/api/
git status
```

If `git status` shows changes only under `frontend/src/api/generated/` (or wherever NSwag writes the client), commit:

```bash
git commit -m "chore: regenerate OpenAPI client after request DTO cleanup"
```

If no changes were produced by the regen (e.g. the client was already up to date), skip this commit.

- [ ] **Step 6: Final end-to-end build verification**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln && cd frontend && npm run build && npm run lint`
Expected: All four commands SUCCESS. Plan complete.

---

## Spec Coverage Map

| Requirement | Implemented in |
|---|---|
| FR-1: Remove `BaseApiController.GetCurrentUserId()` | Task 9 |
| FR-2: Migrate Dashboard handlers + drop `UserId` from 5 requests | Tasks 4–8 (one handler each) |
| FR-3: Migrate `SetCarrierCoolingHandler` + Unauthorized response | Task 2 |
| FR-4: Migrate `SetGiftSettingHandler` + Unauthorized response | Task 3 |
| FR-5: Frontend client regen + validation | Task 10 |
| FR-6: Replace `BaseApiControllerTests` with extended `CurrentUserServiceTests` | Task 1 (extend) + Task 9 (delete) |
| NFR-1: No performance change (no new I/O, no DI churn) | All tasks — no new packages or services |
| NFR-2: No client-supplied identity; Unauthorized for CarrierCooling/GiftSettings | Tasks 2, 3 |
| NFR-3: Breaking change to request DTOs (acceptable, single in-repo client) | Tasks 2–8 (DTO removals) + Task 10 (client regen) |
| NFR-4: No new logs/metrics/traces | All tasks — silent fallbacks preserved |

## Self-Review Notes

- All file paths reference real files verified to exist in the worktree.
- All code blocks are complete (no `...` placeholders) — engineer can copy-paste into the indicated file path.
- Constructor parameter order: `ICurrentUserService` appended last in every migrated handler. This minimizes test-side churn since existing tests use named parameters when constructing handlers (Tasks 4–8 supply the full new ctor explicitly).
- The Unauthorized branch in `SetCarrierCoolingHandler` and `SetGiftSettingHandler` is placed before payload validation, matching `CreateMarketingActionHandler.cs:40–45`. Only `Id` is checked (not `IsAuthenticated`) because `[Authorize]` already enforces authentication at the pipeline layer.
- `UserDashboardSettingsMutator` keeps its existing `userId` parameter + `"anonymous"` normalization. The handlers resolve identity, the mutator normalizes — symmetry with `GetUserSettings`/`SaveUserSettings` preserved.
- `GiftSettingsController` keeps its `NoContent` (204) success contract and adds an explicit `if (response.ErrorCode == ErrorCodes.Unauthorized) return Unauthorized(response);` branch so the new Unauthorized path returns HTTP 401, not 400. This is the smallest possible change to support FR-4 without disturbing the existing success-path contract.
- The lifetime of `ICurrentUserService` is **Singleton** in DI (`UsersModule.cs:14`). Do not change it — `IHttpContextAccessor` is AsyncLocal-backed and per-request reads stay correct (spec amendment 1 in arch-review).
- Existing `CurrentUserServiceTests` already covers 5 of FR-6's 6 scenarios. Task 1 adds only the 3 missing ones (priority chains + all-absent → null).
- `GetTileDataHandler` injects `ICurrentUserService` to satisfy FR-2's acceptance criterion that all 5 Dashboard handlers receive it via constructor. The field may be effectively unused inside the handler body because the inner `GetUserSettings` Mediator call resolves identity itself; a `// ReSharper disable once NotAccessedField.Local` comment is acceptable if the analyzer flags it.
