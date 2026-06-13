# Consistent TimeProvider Usage in Manufacture Order Handlers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace five remaining `DateTime.UtcNow` occurrences in three Manufacture handlers with `_timeProvider.GetUtcNow().DateTime` so that `CreatedDate`, `StateChangedAt`, and note `CreatedAt` are deterministically controllable by `FakeTimeProvider` in tests.

**Architecture:** Surgical inline replacement — no new abstractions, no DI changes, no new packages. Each of the three handlers already injects `TimeProvider` and uses it correctly for adjacent fields; this plan completes that convention for the missing fields. Tests use `Mock<TimeProvider>` from Moq (already the dominant project convention) and assert exact equality against `FixedNow.UtcDateTime`.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq. No new NuGet packages.

---

## Background Context (for the implementing engineer)

You do not need to read the spec or arch review documents to execute this plan — the relevant facts are inlined below.

**Three handlers** in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/` mix `_timeProvider` and `DateTime.UtcNow` calls within the same method. Five lines need to change:

| File | Line | Current | Replacement |
|------|------|---------|-------------|
| `CreateManufactureOrder/CreateManufactureOrderHandler.cs` | 46 | `CreatedDate = DateTime.UtcNow,` | `CreatedDate = _timeProvider.GetUtcNow().DateTime,` |
| `CreateManufactureOrder/CreateManufactureOrderHandler.cs` | 52 | `StateChangedAt = DateTime.UtcNow,` | `StateChangedAt = _timeProvider.GetUtcNow().DateTime,` |
| `DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` | 47 | `CreatedDate = DateTime.UtcNow,` | `CreatedDate = _timeProvider.GetUtcNow().DateTime,` |
| `DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` | 52 | `StateChangedAt = DateTime.UtcNow,` | `StateChangedAt = _timeProvider.GetUtcNow().DateTime,` |
| `UpdateManufactureOrder/UpdateManufactureOrderHandler.cs` | 145 | `CreatedAt = DateTime.UtcNow,` | `CreatedAt = _timeProvider.GetUtcNow().DateTime,` |

**Key conventions (don't deviate):**
- Test double: `Mock<TimeProvider>` from Moq (NOT `Microsoft.Extensions.TimeProvider.Testing` — that package is not in this codebase).
- Assertion target: `FixedNow.UtcDateTime` (NOT `FixedNow.DateTime` — the property type is `DateTime` and `UtcDateTime` is guaranteed `DateTimeKind.Utc`).
- Inline replacement: do not cache `_timeProvider.GetUtcNow().DateTime` into a local — the surrounding code already calls it inline at multiple points and the spec keeps that behavior.
- Mocked `DateTimeOffset`: `new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)` — matches the existing pattern in `DuplicateManufactureOrderHandlerTests.cs:19-20`. Reuse that value where you introduce a new mock.

**Out of scope (do not touch):** any other handler in the Manufacture module (e.g., `GetManufactureProtocolHandler.cs`, `ResolveManualActionHandler.cs`) even though they also call `DateTime.UtcNow`. Spec is explicit.

---

## File Structure

```
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/
  ├── CreateManufactureOrder/CreateManufactureOrderHandler.cs       (2 line edits)
  ├── DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs (2 line edits)
  └── UpdateManufactureOrder/UpdateManufactureOrderHandler.cs       (1 line edit)

backend/test/Anela.Heblo.Tests/Features/Manufacture/
  ├── CreateManufactureOrderHandlerTests.cs       (migrate TimeProvider.System → Mock<TimeProvider>; add frozen-time assertions)
  ├── DuplicateManufactureOrderHandlerTests.cs    (add frozen-time assertions; mock already in place)
  └── UpdateManufactureOrderHandlerTests.cs       (migrate TimeProvider.System → Mock<TimeProvider>; fix existing BeCloseTo assertion)
```

No new files. `CreateManufactureOrderHandlerSinglePhaseTests.cs` already mocks `TimeProvider` and is not modified by this plan — multi-phase test coverage in `CreateManufactureOrderHandlerTests.cs` satisfies FR-1 acceptance criteria for both `CreatedDate` and `StateChangedAt`.

---

## Task 0: Baseline — capture current build and test state

Establish that the unchanged repo builds and the three Manufacture handler test classes pass before any edits. This catches preexisting breakage so it is not misattributed to your changes.

**Files:** none modified

- [ ] **Step 1: Build the backend solution**

Run from repository root:

```bash
cd backend && dotnet build
```

Expected: `Build succeeded` with 0 errors. Warnings are acceptable.

- [ ] **Step 2: Run the three target Manufacture test classes**

Run from repository root:

```bash
cd backend && dotnet test --no-build \
  --filter "FullyQualifiedName~CreateManufactureOrderHandlerTests|FullyQualifiedName~DuplicateManufactureOrderHandlerTests|FullyQualifiedName~UpdateManufactureOrderHandlerTests|FullyQualifiedName~CreateManufactureOrderHandlerSinglePhaseTests"
```

Expected: `Passed!` with all tests succeeding. If anything fails here, stop and report — do not proceed with changes on a broken baseline.

---

## Task 1: Duplicate handler — frozen-time assertions + handler replacement

The test class already uses `Mock<TimeProvider>` with `FixedNow = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)`. You only need to add assertions for `CreatedDate` and `StateChangedAt` to an existing test, then replace the two `DateTime.UtcNow` calls in the handler.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs:160-167` (assertion block in `Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs:47` and `:52`

- [ ] **Step 1: Add frozen-time assertions to the existing test**

Open `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`. Locate the assertion block in `Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts` that currently reads (around lines 160–167):

```csharp
        // Assert captured order
        captured.Should().NotBeNull();
        captured!.OrderNumber.Should().Be(GeneratedOrderNumber);
        captured.State.Should().Be(ManufactureOrderState.Draft);
        captured.CreatedByUser.Should().Be(DisplayName);
        captured.StateChangedByUser.Should().Be(DisplayName);
        captured.ResponsiblePerson.Should().Be(ResponsiblePerson);
        captured.PlannedDate.Should().Be(expectedPlannedDate);
```

Replace that block with the same lines plus two new frozen-time assertions:

```csharp
        // Assert captured order
        captured.Should().NotBeNull();
        captured!.OrderNumber.Should().Be(GeneratedOrderNumber);
        captured.State.Should().Be(ManufactureOrderState.Draft);
        captured.CreatedByUser.Should().Be(DisplayName);
        captured.StateChangedByUser.Should().Be(DisplayName);
        captured.ResponsiblePerson.Should().Be(ResponsiblePerson);
        captured.PlannedDate.Should().Be(expectedPlannedDate);
        captured.CreatedDate.Should().Be(FixedNow.UtcDateTime);
        captured.StateChangedAt.Should().Be(FixedNow.UtcDateTime);
```

- [ ] **Step 2: Run the test to verify it FAILS (red)**

```bash
cd backend && dotnet test --no-build \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests.Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts"
```

Expected: FAIL. The two new assertions should fail with messages like `Expected captured.CreatedDate to be 2026-06-08T10:00:00.0000000, but found <today's UTC timestamp>.` This confirms the handler still reads from `DateTime.UtcNow`.

If the test passes here, stop — that means the handler may have already been changed or the time-mock setup is not actually being used. Investigate before proceeding.

- [ ] **Step 3: Replace `DateTime.UtcNow` in the handler**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs`. Edit the order-construction block (around lines 44–54):

Replace:

```csharp
        // Create the duplicate order
        var duplicateOrder = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = currentUser.GetDisplayName(),
            ResponsiblePerson = sourceOrder.ResponsiblePerson,
            PlannedDate = today,
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = currentUser.GetDisplayName()
        };
```

With:

```csharp
        // Create the duplicate order
        var duplicateOrder = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = _timeProvider.GetUtcNow().DateTime,
            CreatedByUser = currentUser.GetDisplayName(),
            ResponsiblePerson = sourceOrder.ResponsiblePerson,
            PlannedDate = today,
            State = ManufactureOrderState.Draft,
            StateChangedAt = _timeProvider.GetUtcNow().DateTime,
            StateChangedByUser = currentUser.GetDisplayName()
        };
```

- [ ] **Step 4: Confirm no remaining `DateTime.UtcNow` in the handler**

```bash
grep -n "DateTime\.UtcNow" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs || echo "CLEAN"
```

Expected: `CLEAN`. If any line is printed, repeat Step 3 for that line.

- [ ] **Step 5: Build and run the test to verify it PASSES (green)**

```bash
cd backend && dotnet build && dotnet test --no-build \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests"
```

Expected: `Build succeeded`, then `Passed!` for all DuplicateManufactureOrderHandlerTests.

- [ ] **Step 6: Format**

```bash
cd backend && dotnet format --include \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs \
  test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
```

Expected: no output (or formatter messages only). No errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs

git commit -m "refactor: use TimeProvider for CreatedDate/StateChangedAt in DuplicateManufactureOrderHandler"
```

---

## Task 2: Update handler — migrate test to mock + fix existing assertion + handler replacement

The test class currently injects `TimeProvider.System` and uses a `BeCloseTo(DateTime.UtcNow, ...)` assertion for the note's `CreatedAt`. You will (a) migrate the constructor field to `Mock<TimeProvider>` with a frozen `FixedNow`, (b) tighten the existing assertion to exact equality against the frozen time, and (c) replace the single `DateTime.UtcNow` in the handler.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs` (constructor + field declarations + one assertion)
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs:145`

- [ ] **Step 1: Replace the field declaration with a mocked TimeProvider**

Open `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs`. Locate the field declarations at lines 14–18:

```csharp
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<UpdateManufactureOrderHandler>> _loggerMock;
    private readonly TimeProvider _timeProvider;
    private readonly UpdateManufactureOrderHandler _handler;
```

Replace with:

```csharp
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<UpdateManufactureOrderHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly UpdateManufactureOrderHandler _handler;

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
```

- [ ] **Step 2: Update the constructor to use the mocked TimeProvider**

Locate the constructor body at lines 25–41:

```csharp
    public UpdateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderHandler>>();
        _timeProvider = TimeProvider.System;

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProvider,
            _loggerMock.Object);
    }
```

Replace with:

```csharp
    public UpdateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);
    }
```

- [ ] **Step 3: Tighten the existing `BeCloseTo` assertion to exact frozen-time equality**

Still in the same file, locate the assertion in `Handle_WithNewNote_ShouldAddNoteToOrder` at line 223:

```csharp
        note.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
```

Replace with:

```csharp
        note.CreatedAt.Should().Be(FixedNow.UtcDateTime);
```

- [ ] **Step 4: Build, then run the test to verify the note assertion FAILS (red)**

The test infrastructure migration alone (without the assertion change) would still let the handler-side `DateTime.UtcNow` slip through. After Step 3 the assertion is strict, so it should now fail until the handler is fixed.

```bash
cd backend && dotnet build && dotnet test --no-build \
  --filter "FullyQualifiedName~UpdateManufactureOrderHandlerTests.Handle_WithNewNote_ShouldAddNoteToOrder"
```

Expected: FAIL with `Expected note.CreatedAt to be 2026-06-08T10:00:00.0000000, but found <today's UTC timestamp>.`

If the test passes here, the handler may have already been changed; verify and investigate before proceeding.

- [ ] **Step 5: Replace `DateTime.UtcNow` in the handler**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs`. Locate the note-creation block around lines 141–147:

```csharp
                var currentUser = _currentUserService.GetCurrentUser();
                order.Notes.Add(new ManufactureOrderNote
                {
                    Text = request.NewNote.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUser = currentUser.GetDisplayName()
                });
```

Replace with:

```csharp
                var currentUser = _currentUserService.GetCurrentUser();
                order.Notes.Add(new ManufactureOrderNote
                {
                    Text = request.NewNote.Trim(),
                    CreatedAt = _timeProvider.GetUtcNow().DateTime,
                    CreatedByUser = currentUser.GetDisplayName()
                });
```

- [ ] **Step 6: Confirm no remaining `DateTime.UtcNow` in the handler**

```bash
grep -n "DateTime\.UtcNow" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs || echo "CLEAN"
```

Expected: `CLEAN`.

- [ ] **Step 7: Build and run all UpdateManufactureOrderHandlerTests (green)**

```bash
cd backend && dotnet build && dotnet test --no-build \
  --filter "FullyQualifiedName~UpdateManufactureOrderHandlerTests"
```

Expected: `Build succeeded`, then `Passed!` for all UpdateManufactureOrderHandlerTests. If any other tests in the class fail (e.g., due to the now-frozen time interacting with `CreateExistingOrder()` which calls `DateTime.UtcNow.AddDays(-1)`), investigate — those assertions only compare against `request` values and `existingOrder` values, not against `_timeProvider`, so they should remain unaffected.

- [ ] **Step 8: Format**

```bash
cd backend && dotnet format --include \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs \
  test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs
```

Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs

git commit -m "refactor: use TimeProvider for note CreatedAt in UpdateManufactureOrderHandler"
```

---

## Task 3: Create handler — migrate test to mock + frozen-time assertions + handler replacement

The test class currently injects `TimeProvider.System` and never asserts on `CreatedDate` or `StateChangedAt`. You will (a) migrate the constructor field to `Mock<TimeProvider>`, (b) add frozen-time assertions to the existing `Handle_ShouldCreateOrderWithCorrectBasicProperties` test, and (c) replace the two `DateTime.UtcNow` calls in the handler.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` (field declarations + constructor + one assertion block)
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs:46` and `:52`

- [ ] **Step 1: Replace the handler construction in the test constructor with a mocked TimeProvider**

Open `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs`. Locate the field declarations at lines 16–20:

```csharp
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IProductNameFormatter> _productNameFormatterMock;
    private readonly CreateManufactureOrderHandler _handler;
```

Replace with:

```csharp
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IProductNameFormatter> _productNameFormatterMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly CreateManufactureOrderHandler _handler;

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
```

- [ ] **Step 2: Update the constructor body to use the mocked TimeProvider**

Locate the constructor body at lines 30–48:

```csharp
    public CreateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _productNameFormatterMock = new Mock<IProductNameFormatter>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _productNameFormatterMock.Setup(s => s.ShortProductName(It.IsAny<string>())).Returns<string>(_ => ValidProductName);
        _handler = new CreateManufactureOrderHandler(
            _repositoryMock.Object,
            _productNameFormatterMock.Object,
            _catalogRepositoryMock.Object,
            _currentUserServiceMock.Object,
            TimeProvider.System);
    }
```

Replace with:

```csharp
    public CreateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _productNameFormatterMock = new Mock<IProductNameFormatter>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _productNameFormatterMock.Setup(s => s.ShortProductName(It.IsAny<string>())).Returns<string>(_ => ValidProductName);
        _handler = new CreateManufactureOrderHandler(
            _repositoryMock.Object,
            _productNameFormatterMock.Object,
            _catalogRepositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }
```

- [ ] **Step 3: Add frozen-time assertions to `Handle_ShouldCreateOrderWithCorrectBasicProperties`**

Locate the assertion block at the end of `Handle_ShouldCreateOrderWithCorrectBasicProperties` (lines 105–112):

```csharp
        capturedOrder.Should().NotBeNull();
        capturedOrder!.OrderNumber.Should().Be(GeneratedOrderNumber);
        capturedOrder.CreatedByUser.Should().Be("Test User");
        capturedOrder.ResponsiblePerson.Should().Be(ValidResponsiblePerson);
        capturedOrder.PlannedDate.Should().Be(request.PlannedDate);
        capturedOrder.State.Should().Be(ManufactureOrderState.Draft);
        capturedOrder.StateChangedByUser.Should().Be("Test User");
    }
```

Replace with:

```csharp
        capturedOrder.Should().NotBeNull();
        capturedOrder!.OrderNumber.Should().Be(GeneratedOrderNumber);
        capturedOrder.CreatedByUser.Should().Be("Test User");
        capturedOrder.ResponsiblePerson.Should().Be(ValidResponsiblePerson);
        capturedOrder.PlannedDate.Should().Be(request.PlannedDate);
        capturedOrder.State.Should().Be(ManufactureOrderState.Draft);
        capturedOrder.StateChangedByUser.Should().Be("Test User");
        capturedOrder.CreatedDate.Should().Be(FixedNow.UtcDateTime);
        capturedOrder.StateChangedAt.Should().Be(FixedNow.UtcDateTime);
    }
```

- [ ] **Step 4: Build, then run the test to verify it FAILS (red)**

```bash
cd backend && dotnet build && dotnet test --no-build \
  --filter "FullyQualifiedName~CreateManufactureOrderHandlerTests.Handle_ShouldCreateOrderWithCorrectBasicProperties"
```

Expected: FAIL with `Expected capturedOrder.CreatedDate to be 2026-06-08T10:00:00.0000000, but found <today's UTC timestamp>.`

If the test passes here, the handler may already have been changed. Investigate before proceeding.

- [ ] **Step 5: Replace `DateTime.UtcNow` in the handler**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`. Locate the order-construction block at lines 43–54:

```csharp
        var order = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = currentUser.Name,
            ResponsiblePerson = request.ResponsiblePerson,
            PlannedDate = request.PlannedDate,
            ManufactureType = request.ManufactureType,
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = currentUser.Name
        };
```

Replace with:

```csharp
        var order = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = _timeProvider.GetUtcNow().DateTime,
            CreatedByUser = currentUser.Name,
            ResponsiblePerson = request.ResponsiblePerson,
            PlannedDate = request.PlannedDate,
            ManufactureType = request.ManufactureType,
            State = ManufactureOrderState.Draft,
            StateChangedAt = _timeProvider.GetUtcNow().DateTime,
            StateChangedByUser = currentUser.Name
        };
```

- [ ] **Step 6: Confirm no remaining `DateTime.UtcNow` in the handler**

```bash
grep -n "DateTime\.UtcNow" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs || echo "CLEAN"
```

Expected: `CLEAN`.

- [ ] **Step 7: Build and run all CreateManufactureOrderHandlerTests (green)**

```bash
cd backend && dotnet build && dotnet test --no-build \
  --filter "FullyQualifiedName~CreateManufactureOrderHandlerTests|FullyQualifiedName~CreateManufactureOrderHandlerSinglePhaseTests"
```

Expected: `Build succeeded`, then `Passed!` for both test classes. The `CreateManufactureOrderHandlerSinglePhaseTests` class is included because it constructs the same handler — its preexisting mock setup uses a different `currentTime`, but it does not assert on `CreatedDate` or `StateChangedAt`, so it should continue to pass unchanged.

- [ ] **Step 8: Format**

```bash
cd backend && dotnet format --include \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs \
  test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs
```

Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs

git commit -m "refactor: use TimeProvider for CreatedDate/StateChangedAt in CreateManufactureOrderHandler"
```

---

## Task 4: Final verification — full build, format, and Manufacture test sweep

Confirm the worktree is clean and all Manufacture tests still pass together, not just per-handler.

**Files:** none modified

- [ ] **Step 1: Verify no `DateTime.UtcNow` remains in any of the three handlers**

```bash
grep -n "DateTime\.UtcNow" \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs \
  || echo "ALL THREE HANDLERS CLEAN"
```

Expected: `ALL THREE HANDLERS CLEAN`.

- [ ] **Step 2: Full backend build**

```bash
cd backend && dotnet build
```

Expected: `Build succeeded` with 0 errors. Warning count should not have increased from the baseline in Task 0.

- [ ] **Step 3: Full backend format check**

```bash
cd backend && dotnet format --verify-no-changes
```

Expected: no diff and no errors. If `dotnet format` reports changes, run `cd backend && dotnet format` to apply them, then add the formatted files to a follow-up commit.

- [ ] **Step 4: Run the full Manufacture test namespace**

```bash
cd backend && dotnet test --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture"
```

Expected: `Passed!` for every test. If anything fails, locate and fix before declaring done — this catches accidental coupling, e.g. a Manufacture test elsewhere that happens to construct one of these handlers with `TimeProvider.System`.

- [ ] **Step 5: Confirm git working tree is clean**

```bash
git status
```

Expected: `nothing to commit, working tree clean`. If there are stray untracked or unstaged files from formatting, commit them with a `chore: dotnet format` message.

---

## Self-Review Notes (for the implementing engineer)

This plan addresses each spec requirement exactly once:

| Requirement | Covered by |
|-------------|-----------|
| FR-1 (Create handler — 2 lines) | Task 3, Steps 5–6 |
| FR-2 (Duplicate handler — 2 lines) | Task 1, Steps 3–4 |
| FR-3 (Update handler — 1 line)  | Task 2, Steps 5–6 |
| FR-4 (Test coverage per handler) | Task 1 Step 1 (Duplicate), Task 2 Step 3 (Update), Task 3 Step 3 (Create) |
| NFR-3 (Build & format clean) | Task 4, Steps 2–3 |

There are no placeholder steps. Each step contains either exact code, exact commands, or both. All `_timeProvider.GetUtcNow().DateTime` references are spelled identically across tasks. The `FixedNow` value is identical to the existing convention in `DuplicateManufactureOrderHandlerTests.cs`.
