# Refactor UpdateManufactureOrderStatusHandler to use ICurrentUserService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the direct `IHttpContextAccessor` dependency in `UpdateManufactureOrderStatusHandler` with the existing `ICurrentUserService` abstraction so that audit fields use the same identity-resolution chain as the rest of the Application layer, stopping authenticated users from being incorrectly stamped as `"System"` when the `Identity.Name` claim is absent.

**Architecture:** Surgical refactor of a single Application-layer handler. The handler stops resolving `HttpContext.User.Identity.Name` directly and instead calls `ICurrentUserService.GetCurrentUser().GetDisplayName()` once per `Handle` invocation, passing the resolved name into `WriteDownInventoryAsync` so both audit fields (`order.StateChangedByUser` and `ManufacturedProductInventoryItem.CreatedBy`) come from a single resolution. Two existing test classes swap their `IHttpContextAccessor` mock for an `ICurrentUserService` mock; one new test covers the bug-fix scenario (authenticated principal without a `Name` claim → `"Unknown User"`, not `"System"`).

**Tech Stack:** .NET 8, C#, MediatR, xUnit, Moq, FluentAssertions. No new packages.

**Spec amendments adopted from arch-review:**
- **Amendment 1a (FR-4):** Test asserts `"Unknown User"` (not `preferred_username`) for an authenticated principal whose `Name` is null. The existing `CurrentUserService` chain (`Identity.Name → ClaimTypes.Name → "name" → "Unknown User"`) is *not* modified in this refactor.
- **Amendment 2 (FR-2):** The user name is resolved exactly once per `Handle` invocation and passed into `WriteDownInventoryAsync` as a parameter, so `StateChangedByUser` and `ManufacturedProductInventoryItem.CreatedBy` cannot diverge for the same transition.

---

## File Structure

**Modified (3 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` — swap dependency, delete `GetCurrentUserName`, resolve user once, parameterise `WriteDownInventoryAsync`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs` — swap mock, rename `Handle_WithoutHttpContext_ShouldUseSystemAsUser`, add the new FR-4 test.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs` — swap mock.

**Not modified (verified):**
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `ICurrentUserService` is already registered (line 130). `IHttpContextAccessor` registration remains, other consumers still need it.
- `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs` — Out of scope (Amendment 1b explicitly rejected).
- `backend/src/Anela.Heblo.Domain/Features/Users/CurrentUserExtensions.cs` — already returns `"System"` for unauthenticated, `Name ?? "Unknown User"` for authenticated.

**No DI registration changes**, no DB migrations, no frontend changes, no config flags, no new files.

---

## Task 1: Refactor test files to use ICurrentUserService mock + add FR-4 test (RED)

> TDD anchor — write the tests against the new constructor *before* changing the handler. The solution will fail to build at the end of this task; that is the RED state we want before Task 2 makes it GREEN.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs`

### Step 1.1: Rewrite test class `UpdateManufactureOrderStatusHandlerTests` constructor and setup

- [ ] **Replace the `using` block, field declarations, constants, and constructor in `UpdateManufactureOrderStatusHandlerTests.cs`** so the test class mocks `ICurrentUserService` instead of `IHttpContextAccessor`.

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs`:

Replace lines 1-11 (the `using` block) with:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

(Removed: `Microsoft.AspNetCore.Http`, `System.Security.Claims`.)

Replace lines 17-67 (field declarations through the end of the constructor) with:

```csharp
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IConditionsReadingProvider> _conditionsProviderMock;
    private readonly UpdateManufactureOrderStatusHandler _handler;

    private const int ValidOrderId = 1;
    private const int NonExistentOrderId = 999;
    private const string TestUserName = "Test User";
    private const string ValidChangeReason = "Moving to next phase";

    public UpdateManufactureOrderStatusHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _conditionsProviderMock = new Mock<IConditionsReadingProvider>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "test-id",
                Name: TestUserName,
                Email: "test@example.com",
                IsAuthenticated: true));

        _conditionsProviderMock
            .Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable));

        _inventoryRepositoryMock
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ManufacturedProductInventoryItem> items, CancellationToken _) => items);

        _handler = new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _conditionsProviderMock.Object,
            _inventoryRepositoryMock.Object);
    }
```

**Notes for the implementer:**
- The positional ordering of constructor args in the `new UpdateManufactureOrderStatusHandler(...)` call is identical to the production constructor (per Decision 3): `_currentUserServiceMock.Object` takes the slot previously occupied by `_httpContextAccessorMock.Object`.
- `CurrentUser` is a record in `Anela.Heblo.Domain.Features.Users` (already in scope via the new `using`).

### Step 1.2: Rewrite the existing "WithoutHttpContext" test as an unauthenticated-user test

- [ ] **Replace the `Handle_WithoutHttpContext_ShouldUseSystemAsUser` test (lines 194-230 of the original file)** with an unauthenticated-user setup that drives the same assertion through the new abstraction.

Find:

```csharp
    [Fact]
    public async Task Handle_WithoutHttpContext_ShouldUseSystemAsUser()
    {
        _httpContextAccessorMock
            .Setup(x => x.HttpContext)
            .Returns((HttpContext?)null);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StateChangedByUser.Should().Be("System");

        updatedOrder.Should().NotBeNull();
        updatedOrder!.StateChangedByUser.Should().Be("System");
    }
```

Replace with:

```csharp
    [Fact]
    public async Task Handle_UnauthenticatedUser_ShouldUseSystemAsUser()
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: null,
                Name: null,
                Email: null,
                IsAuthenticated: false));

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StateChangedByUser.Should().Be("System");

        updatedOrder.Should().NotBeNull();
        updatedOrder!.StateChangedByUser.Should().Be("System");
    }
```

**Rationale:** `CurrentUserExtensions.GetDisplayName()` returns `"System"` exactly when `IsAuthenticated == false`. Setting up an unauthenticated `CurrentUser` is the new abstraction's analogue of the old "no HttpContext" scenario, and preserves the FR-5 acceptance criterion.

### Step 1.3: Add the new FR-4 bug-fix test

- [ ] **Insert a new test immediately after the renamed `Handle_UnauthenticatedUser_ShouldUseSystemAsUser` test** (i.e. right before `Handle_WhenRepositoryGetThrows_ShouldReturnInternalServerError`):

```csharp
    [Fact]
    public async Task Handle_AuthenticatedUserWithoutNameClaim_ShouldRecordUnknownUserNotSystem()
    {
        // Entra ID access tokens frequently omit the Name/upn claim used by Identity.Name.
        // Before the refactor this case fell through to "System" (the bug). Per spec FR-4
        // (Amendment 1a), the handler should now stamp "Unknown User" for an authenticated
        // principal whose Name is null.
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "abc123",
                Name: null,
                Email: "user@example.com",
                IsAuthenticated: true));

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StateChangedByUser.Should().Be("Unknown User");
        result.StateChangedByUser.Should().NotBe("System");

        updatedOrder.Should().NotBeNull();
        updatedOrder!.StateChangedByUser.Should().Be("Unknown User");
    }
```

### Step 1.4: Rewrite test class `UpdateManufactureOrderStatusHandlerConditionsTests` to use the same mock swap

- [ ] **Edit `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs`** — replace the imports, mock field, constructor setup, and `CreateHandler()` body.

Replace lines 1-9 (the `using` block) with:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
```

(Removed: `Microsoft.AspNetCore.Http`, `System.Security.Claims`.)

Replace lines 15-47 (field declarations through end of `CreateHandler`) with:

```csharp
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IConditionsReadingProvider> _conditionsProviderMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;

    public UpdateManufactureOrderStatusHandlerConditionsTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _conditionsProviderMock = new Mock<IConditionsReadingProvider>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "test-id",
                Name: "Test User",
                Email: "test@example.com",
                IsAuthenticated: true));

        _inventoryRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem item, CancellationToken _) => item);
    }

    private UpdateManufactureOrderStatusHandler CreateHandler() =>
        new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _conditionsProviderMock.Object,
            _inventoryRepositoryMock.Object);
```

### Step 1.5: Verify the solution does NOT compile (RED)

- [ ] **Run `dotnet build` from the repository root** and confirm compilation fails.

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: FAIL.

Expected error symptom (one or both files):
- `CS0246: The type or namespace name 'IHttpContextAccessor' could not be found` — only if leftover references slipped through; if you see this, re-grep and clean.
- `CS1503: cannot convert from 'Moq.Mock<ICurrentUserService>.Object' to 'IHttpContextAccessor'` — this is the *expected* failure: the production constructor still demands `IHttpContextAccessor`. This is what proves the tests now drive a constructor change.

Do **not** proceed if you see only `IHttpContextAccessor` missing-import errors — that means the test files still reference it. Re-check Step 1.1 / Step 1.4 imports.

### Step 1.6: Commit the failing tests

- [ ] **Commit the two test files** so the RED state is captured in history before the handler refactor.

Run:

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs
git commit -m "test: drive UpdateManufactureOrderStatusHandler to ICurrentUserService

Replace IHttpContextAccessor mock with ICurrentUserService mock in both
handler test classes, rename the no-HttpContext test to an
unauthenticated-user test, and add a new test covering FR-4 (authenticated
principal without Name claim must record \"Unknown User\", not \"System\").

Solution does not build yet — production handler still requires
IHttpContextAccessor. The next commit refactors the handler to match."
```

---

## Task 2: Refactor handler to use ICurrentUserService (GREEN)

> Now that the tests drive the new contract, change the production code minimally to match.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`

### Step 2.1: Swap imports and dependency field

- [ ] **In `UpdateManufactureOrderStatusHandler.cs`**, replace the imports and the dependency field.

Find (lines 1-7):

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
```

Replace with:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
```

Then find (line 17):

```csharp
    private readonly IHttpContextAccessor _httpContextAccessor;
```

Replace with:

```csharp
    private readonly ICurrentUserService _currentUserService;
```

### Step 2.2: Swap constructor parameter and assignment

- [ ] **Update the constructor**. Find lines 20-34:

```csharp
    public UpdateManufactureOrderStatusHandler(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider,
        ILogger<UpdateManufactureOrderStatusHandler> logger,
        IHttpContextAccessor httpContextAccessor,
        IConditionsReadingProvider conditionsProvider,
        IManufacturedProductInventoryRepository inventoryRepository)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _conditionsProvider = conditionsProvider;
        _inventoryRepository = inventoryRepository;
    }
```

Replace with:

```csharp
    public UpdateManufactureOrderStatusHandler(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider,
        ILogger<UpdateManufactureOrderStatusHandler> logger,
        ICurrentUserService currentUserService,
        IConditionsReadingProvider conditionsProvider,
        IManufacturedProductInventoryRepository inventoryRepository)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
        _currentUserService = currentUserService;
        _conditionsProvider = conditionsProvider;
        _inventoryRepository = inventoryRepository;
    }
```

The slot position of the swapped parameter is unchanged (slot 4 of 6) per arch-review Decision 3.

### Step 2.3: Resolve user name once and reuse across call sites

- [ ] **Inside `Handle`, resolve the user name once after the state-transition validation and use it for both audit fields.**

Find (lines 48-64 of the original — the block from `var oldState = order.State;` through `order.StateChangedByUser = GetCurrentUserName();`):

```csharp
            var oldState = order.State;

            // Validate state transition (basic validation - can be extended)
            if (!IsValidStateTransition(oldState, request.NewState))
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.InvalidOperation,
                    new Dictionary<string, string>
                    {
                        { "oldState", oldState.ToString() },
                        { "newState", request.NewState.ToString() }
                    });
            }

            // Update state
            order.State = request.NewState;
            order.StateChangedAt = _timeProvider.GetUtcNow().DateTime;
            order.StateChangedByUser = GetCurrentUserName();
```

Replace with:

```csharp
            var oldState = order.State;

            // Validate state transition (basic validation - can be extended)
            if (!IsValidStateTransition(oldState, request.NewState))
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.InvalidOperation,
                    new Dictionary<string, string>
                    {
                        { "oldState", oldState.ToString() },
                        { "newState", request.NewState.ToString() }
                    });
            }

            var currentUserName = _currentUserService.GetCurrentUser().GetDisplayName();

            // Update state
            order.State = request.NewState;
            order.StateChangedAt = _timeProvider.GetUtcNow().DateTime;
            order.StateChangedByUser = currentUserName;
```

**Why resolve here, not at the top of the method?** Resolving after the validation guard skips the (cheap) extension-method call for failed validations and matches the previous placement of `GetCurrentUserName()` — keeps the diff surgical.

### Step 2.4: Pass the resolved user into `WriteDownInventoryAsync`

- [ ] **Update the `WriteDownInventoryAsync` call site.** Find (line 135):

```csharp
                await WriteDownInventoryAsync(order, cancellationToken);
```

Replace with:

```csharp
                await WriteDownInventoryAsync(order, currentUserName, cancellationToken);
```

### Step 2.5: Update `WriteDownInventoryAsync` signature and body

- [ ] **Add the `string changedByUser` parameter and remove the in-body resolution.** Find (lines 175-195 of the original):

```csharp
    private async Task WriteDownInventoryAsync(ManufactureOrder order, CancellationToken cancellationToken)
    {
        var user = GetCurrentUserName();
        var timestamp = _timeProvider.GetUtcNow().DateTime;

        var items = order.Products
            .Where(p => p.ActualQuantity is > 0)
            .Select(p => new ManufacturedProductInventoryItem(
                productCode: p.ProductCode,
                productName: p.ProductName,
                amount: p.ActualQuantity!.Value,
                createdBy: user,
                createdAt: timestamp,
                lotNumber: p.LotNumber,
                expirationDate: p.ExpirationDate,
                manufactureOrderId: order.Id))
            .ToList();

        if (items.Count > 0)
            await _inventoryRepository.AddRangeAsync(items, cancellationToken);
    }
```

Replace with:

```csharp
    private async Task WriteDownInventoryAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken)
    {
        var timestamp = _timeProvider.GetUtcNow().DateTime;

        var items = order.Products
            .Where(p => p.ActualQuantity is > 0)
            .Select(p => new ManufacturedProductInventoryItem(
                productCode: p.ProductCode,
                productName: p.ProductName,
                amount: p.ActualQuantity!.Value,
                createdBy: changedByUser,
                createdAt: timestamp,
                lotNumber: p.LotNumber,
                expirationDate: p.ExpirationDate,
                manufactureOrderId: order.Id))
            .ToList();

        if (items.Count > 0)
            await _inventoryRepository.AddRangeAsync(items, cancellationToken);
    }
```

### Step 2.6: Delete the obsolete `GetCurrentUserName()` helper

- [ ] **Remove the entire `GetCurrentUserName()` method.** Find (lines 169-173):

```csharp
    private string GetCurrentUserName()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.Identity?.Name ?? "System";
    }
```

Delete those five lines plus the blank line that precedes them. After deletion, `IsValidStateTransition` should be immediately followed by `WriteDownInventoryAsync` (with one blank line between them, as in the rest of the file).

### Step 2.7: Verify no stale references to `IHttpContextAccessor` or `GetCurrentUserName` remain in the handler

- [ ] **Grep the handler file to confirm a clean refactor.**

Run:

```bash
grep -n "IHttpContextAccessor\|_httpContextAccessor\|GetCurrentUserName\|Microsoft\.AspNetCore\.Http" \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs
```

Expected: no output (exit code 1). If any line matches, return to the appropriate sub-step above.

### Step 2.8: Build the solution (GREEN — compile)

- [ ] **Compile and confirm success.**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: PASS, zero new warnings.

If the build fails:
- **`CS0246` on `ICurrentUserService` or `CurrentUserExtensions`**: the `using Anela.Heblo.Domain.Features.Users;` import is missing in the handler. Add it.
- **`CS1503` constructor argument mismatch in some other consumer**: there are no other consumers — `UpdateManufactureOrderStatusHandler` is only newed up via DI and in the two test classes you already updated. If you see this, you missed a test-class swap; redo Task 1.

### Step 2.9: Run the affected test classes (GREEN — behavior)

- [ ] **Run both targeted test classes.**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateManufactureOrderStatusHandlerTests|FullyQualifiedName~UpdateManufactureOrderStatusHandlerConditionsTests" \
  --no-build
```

Expected:
- All tests in `UpdateManufactureOrderStatusHandlerTests` pass — including the new `Handle_AuthenticatedUserWithoutNameClaim_ShouldRecordUnknownUserNotSystem` and the renamed `Handle_UnauthenticatedUser_ShouldUseSystemAsUser`.
- All tests in `UpdateManufactureOrderStatusHandlerConditionsTests` pass — the conditions logic is independent of the user resolution.

If a test fails:
- `Handle_WithValidTransition_ShouldUpdateStateAndReturnResponse` asserting `TestUserName == "Test User"`: verify the constructor mock setup in Step 1.1 returns `Name: TestUserName` (not null).
- `Handle_TransitionToCompleted_CreatesInventoryItemsForFinishedProducts` asserting `i.CreatedBy == TestUserName`: same diagnosis — the mock's `Name` must be `TestUserName`. The inventory `CreatedBy` now flows from the same resolution as `StateChangedByUser`, so the assertion still holds.
- `Handle_AuthenticatedUserWithoutNameClaim_ShouldRecordUnknownUserNotSystem` asserting `"Unknown User"`: confirm Step 2.3 calls `GetDisplayName()` (not `currentUser.Name`). The display-name extension is what produces `"Unknown User"` for authenticated principals whose `Name` is null.

### Step 2.10: Commit the handler refactor

- [ ] **Commit the production change.**

Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs
git commit -m "refactor: UpdateManufactureOrderStatusHandler uses ICurrentUserService

Replace the direct IHttpContextAccessor dependency with the shared
ICurrentUserService abstraction and resolve the user display name once
per Handle invocation via CurrentUserExtensions.GetDisplayName(). Pass
the resolved name into WriteDownInventoryAsync so order.StateChangedByUser
and ManufacturedProductInventoryItem.CreatedBy share a single source.

Fixes audit-trail entries that were stamped as \"System\" for authenticated
users whose Entra ID token omits the Name/upn claim. Such users are now
stamped \"Unknown User\" (per existing CurrentUserService fallback chain);
truly unauthenticated callers (background jobs) continue to stamp \"System\".

Removes one Application-layer consumer of IHttpContextAccessor (moves the
broader cleanup from N to N-1; sibling consumer tracked in #1716)."
```

---

## Task 3: Full validation, format, and final sweep

**Files:** No code changes in this task; validation only.

### Step 3.1: Apply formatting and analyzer fixes

- [ ] **Run `dotnet format` over the touched files.**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs
```

Expected: command exits 0, no changes (or only trivial whitespace alignment).

If `dotnet format` did modify files, inspect the diff with `git diff` to confirm only whitespace / import ordering changes. Then commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs
git commit -m "chore: dotnet format"
```

(Skip the commit step if there were no changes.)

### Step 3.2: Run the full backend test suite

- [ ] **Run the entire backend test project** to confirm nothing else regressed.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build`

Expected: all tests pass.

If something unrelated fails, do not "fix" it inside this refactor — note it for the user and stop. Per CLAUDE.md, every changed line must trace directly to the request.

### Step 3.3: Final IHttpContextAccessor consumer audit (informational)

- [ ] **Grep the Application project for remaining `IHttpContextAccessor` consumers** and confirm the count dropped by exactly one.

Run:

```bash
grep -rl "IHttpContextAccessor" backend/src/Anela.Heblo.Application/
```

Expected: `UpdateManufactureOrderStatusHandler.cs` no longer appears in the list. Other consumers (tracked in issue #1716) remain and are out of scope for this refactor — do not touch them.

This step produces no commit; it confirms NFR-3 (`Anela.Heblo.Application` has one fewer file that imports `IHttpContextAccessor`).

---

## Self-review against the spec

This section is a checklist run by the implementer before declaring the refactor done. Each spec requirement is matched to a task that implements it.

| Spec section | Acceptance criterion | Implementing step |
|---|---|---|
| **FR-1** | No `IHttpContextAccessor` field/param/import in handler | Step 2.1, 2.2, 2.7 |
| **FR-1** | Single `ICurrentUserService _currentUserService` field | Step 2.1, 2.2 |
| **FR-1** | `GetCurrentUserName()` removed | Step 2.6 |
| **FR-1** | `dotnet build` succeeds, no new warnings | Step 2.8 |
| **FR-2** | All previous call sites use `GetCurrentUser().GetDisplayName()` | Step 2.3, 2.5 |
| **FR-2** | No `_httpContextAccessor` in handler | Step 2.7 |
| **FR-2** | `StateChangedByUser` populated from `GetDisplayName()` | Step 2.3 |
| **FR-2** (Amendment 2) | User name resolved at most once per `Handle` invocation | Step 2.3, 2.4, 2.5 |
| **FR-3** | Test with `Name = "user@example.com"` records that exact value | Existing `Handle_WithValidTransition_ShouldUpdateStateAndReturnResponse` (asserts `TestUserName`) — preserved via Step 1.1 mock default |
| **FR-3** | No existing assertion loosened | All original assertions kept (Step 1.1 keeps `TestUserName`; the `CreatedBy == TestUserName` assertions in `Handle_TransitionToCompleted_...` are preserved) |
| **FR-4** (Amendment 1a) | Authenticated principal with `Name = null` records `"Unknown User"`, not `"System"` | Step 1.3 (new test), Step 2.3 (production path) |
| **FR-5** | Unauthenticated user → `"System"` | Step 1.2 (renamed test), Step 2.3 via `GetDisplayName()` |
| **FR-6** | All test doubles updated to supply `ICurrentUserService` | Step 1.1, 1.4 |
| **FR-6** | `dotnet build` succeeds across solution | Step 2.8 |
| **FR-6** | No test mocks `IHttpContextAccessor` solely for this handler | Step 1.1, 1.4 (both swap completely) |
| **NFR-1** | No additional async / I/O / reflection | Step 2.3 (single sync extension-method call), Step 2.5 (in-body resolution removed) |
| **NFR-2** | No new endpoints / claims / permission checks | No code changes outside the handler & its tests |
| **NFR-3** | One fewer Application-layer file importing `IHttpContextAccessor` | Step 2.1, 2.7, 3.3 |
| **NFR-4** | Existing log statements still emit user identifier | The handler's `_logger.LogError` (line 150 of original) does not reference the user; observability is unchanged. No log message wired to the local user variable existed — nothing to update. |
| **Out of scope: backfill** | Historical `"System"` rows untouched | Step 3.2 is test-only; no migration in plan |
| **Out of scope: sibling #1716** | Other Application consumer of `IHttpContextAccessor` untouched | Step 3.3 explicitly checks for this and does not modify other files |

**Type/name consistency sweep** (per writing-plans self-review):
- Field name across handler and tests: `_currentUserService` ✓ (Step 1.1, 1.4, 2.1, 2.2)
- Local variable inside `Handle`: `currentUserName` ✓ (Step 2.3, 2.4)
- New parameter on `WriteDownInventoryAsync`: `changedByUser` (string) ✓ (Step 2.4, 2.5)
- Mock field in both test classes: `_currentUserServiceMock` ✓ (Step 1.1, 1.4)
- `CurrentUser` record positional args: `(Id, Name, Email, IsAuthenticated)` — verified against `backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs` ✓ (Step 1.1, 1.2, 1.3, 1.4)
- `CurrentUserExtensions.GetDisplayName()` returns `"System"` when `IsAuthenticated == false`, otherwise `Name ?? "Unknown User"` — verified against source ✓ (drives Step 1.2 and Step 1.3 expected values)

**Placeholder scan:** no `TBD`, no `TODO`, no "implement later", no "similar to Task N", no "add appropriate error handling" — every step contains exact code or exact command output expectations.

---

## Done criteria

- [ ] Three commits land on the branch (test refactor, handler refactor, optional format), or two if `dotnet format` was a no-op.
- [ ] `dotnet build backend/Anela.Heblo.sln` → succeeds with no new warnings.
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build` → all tests pass.
- [ ] `dotnet format` → exit 0, no further changes.
- [ ] Grep on `IHttpContextAccessor` in `backend/src/Anela.Heblo.Application/` no longer returns `UpdateManufactureOrderStatusHandler.cs`.
- [ ] The new `Handle_AuthenticatedUserWithoutNameClaim_ShouldRecordUnknownUserNotSystem` test passes (FR-4, Amendment 1a).
- [ ] The renamed `Handle_UnauthenticatedUser_ShouldUseSystemAsUser` test passes (FR-5).
