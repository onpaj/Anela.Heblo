# Manufacture `GenerateOrderNumberAsync` — Caller-Supplied Year Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the hidden `DateTime.Now` dependency from `ManufactureOrderRepository.GenerateOrderNumberAsync` by taking the year as an `int` parameter supplied by the caller, and ensure both Manufacture handlers source the year from their injected `TimeProvider` using a single cached reading so the order number's year always agrees with the row's `CreatedDate`.

**Architecture:** Backend-only refactor in the Manufacture vertical slice. Interface change in Domain (`IManufactureOrderRepository`); behavior change in Persistence (`ManufactureOrderRepository`); call-site updates in two Application handlers (`CreateManufactureOrderHandler`, `DuplicateManufactureOrderHandler`). Both handlers cache a single `TimeProvider.GetUtcNow()` reading and reuse it for the year, `CreatedDate`, `StateChangedAt`, and the expiration/lot helpers — guaranteeing all temporal stamps on the new row come from one instant. No HTTP, MediatR, contract, frontend, migration, or DI registration changes.

**Tech Stack:** .NET 8, C#, MediatR, Entity Framework Core, xUnit, Moq, FluentAssertions.

---

## File Structure

**Production code (edit only — no new files):**

| File | Responsibility | Change |
|---|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs` | Repository port | Add `int year` as first parameter to `GenerateOrderNumberAsync`. |
| `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs` | EF Core implementation | Use the `year` parameter to build the prefix; delete the `DateTime.Now.Year` line. Add one short comment guarding against future clock reintroduction. |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` | Create-order handler | Cache `var now = _timeProvider.GetUtcNow();` once at the top of `Handle()`; pass `now.Year` to the repository; reuse `now.DateTime` for `CreatedDate`, `StateChangedAt`, `GetDefaultExpiration`, and `GetDefaultLot`. |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` | Duplicate-order handler | Same pattern as Create: cache `now` once, pass `now.Year`, reuse for all stamps and helpers. |

**Tests (edit existing — no new files):**

| File | Change |
|---|---|
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` | Update all 13 `Setup`/`Verify` calls on `GenerateOrderNumberAsync` to add `It.IsAny<int>()`. Add new year-boundary and audit-consistency tests. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerSinglePhaseTests.cs` | Update both `Setup` calls to add `It.IsAny<int>()`. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` | Update all 3 `Setup`/`Verify` calls to add `It.IsAny<int>()`. Add new year-boundary and audit-consistency tests. |

**Untouched (verified):** `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs` uses a different interface (`IPurchaseOrderNumberGenerator`) and must not be modified.

---

## Task 1: Update interface and repository to accept `int year`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs:24`
- Modify: `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs:148-170`

Rationale: this single interface change is a compile break that cascades through both handlers and three test files. Doing the interface + implementation here gets the contract locked first; subsequent tasks fix every site the compiler now flags.

- [ ] **Step 1: Update the interface signature**

Edit `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs`. Find the existing line:

```csharp
Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default);
```

Replace with:

```csharp
Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Update the repository implementation**

Edit `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs`. Replace the entire `GenerateOrderNumberAsync` method (lines 148-170) with:

```csharp
public async Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default)
{
    // year is supplied by the caller; do not introduce TimeProvider/DateTime.Now here (see spec FR-1).
    var prefix = $"MO-{year}-";

    var lastOrderNumber = await _context.ManufactureOrders
        .Where(x => x.OrderNumber.StartsWith(prefix))
        .OrderByDescending(x => x.OrderNumber)
        .Select(x => x.OrderNumber)
        .FirstOrDefaultAsync(cancellationToken);

    int nextSequence = 1;
    if (lastOrderNumber != null)
    {
        var sequencePart = lastOrderNumber.Substring(prefix.Length);
        if (int.TryParse(sequencePart, out int lastSequence))
        {
            nextSequence = lastSequence + 1;
        }
    }

    return $"{prefix}{nextSequence:D3}"; // Format as 001, 002, etc.
}
```

- [ ] **Step 3: Build to surface every broken caller**

Run from the worktree root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build FAILS with `CS7036` / `CS0117` errors at every site that still calls `GenerateOrderNumberAsync(cancellationToken)` without a year argument. Specifically:
- `CreateManufactureOrderHandler.cs:40`
- `DuplicateManufactureOrderHandler.cs:38`
- Each `Setup`/`Verify` line in the three Manufacture test files listed under File Structure.

These are the expected fix sites for Tasks 2 – 4. Do not commit yet; the build is intentionally broken at the end of this task.

---

## Task 2: Update `CreateManufactureOrderHandler` to cache `now` and pass `now.Year`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs:33-64`

The current handler reads `_timeProvider.GetUtcNow()` four times (lines 46, 52, 62, 63). Per the architecture review (R-2) and the FR-3 clarification, cache the reading once and reuse it so the year, `CreatedDate`, `StateChangedAt`, `GetDefaultExpiration`, and `GetDefaultLot` all come from the same instant.

- [ ] **Step 1: Cache `now`, pass `now.Year`, reuse for all stamps**

Edit the `Handle` method body. Replace the block currently spanning lines 36 – 64 (from the `var currentUser = _currentUserService.GetCurrentUser();` line down to and including the `var lotNumber = ManufactureOrderExtensions.GetDefaultLot(...)` line) with this exact code, preserving the surrounding `Handle` signature, the `if (semiproduct == null)` early return, and everything after the `lotNumber` assignment:

```csharp
        var currentUser = _currentUserService.GetCurrentUser();

        // Cache one TimeProvider reading so the year, CreatedDate, StateChangedAt,
        // expiration date, and lot number on the new row all derive from the same instant.
        var now = _timeProvider.GetUtcNow();

        // Generate unique order number using the UTC year — must agree with CreatedDate below.
        var orderNumber = await _repository.GenerateOrderNumberAsync(now.Year, cancellationToken);

        // Create the manufacture order
        var order = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = now.DateTime,
            CreatedByUser = currentUser.Name,
            ResponsiblePerson = request.ResponsiblePerson,
            PlannedDate = request.PlannedDate,
            ManufactureType = request.ManufactureType,
            State = ManufactureOrderState.Draft,
            StateChangedAt = now.DateTime,
            StateChangedByUser = currentUser.Name
        };

        var semiproduct = await _catalogSource.GetByIdAsync(request.ProductCode, cancellationToken);
        if (semiproduct == null)
        {
            return new CreateManufactureOrderResponse(ErrorCodes.ProductNotFound);
        }

        var expirationDate = ManufactureOrderExtensions.GetDefaultExpiration(now.DateTime, semiproduct.Properties.ExpirationMonths);
        var lotNumber = ManufactureOrderExtensions.GetDefaultLot(now.DateTime);
```

Leave the rest of the method (semi-product creation, products loop, direct-row creation, save, return) unchanged.

- [ ] **Step 2: Verify no `_timeProvider.GetUtcNow()` survives in this handler**

Run:

```bash
grep -n "_timeProvider" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs
```

Expected output: exactly one match on the line `_timeProvider = timeProvider;` inside the constructor, plus the one match inside the cached `var now = _timeProvider.GetUtcNow();` line. No other `_timeProvider` references should remain.

---

## Task 3: Update `DuplicateManufactureOrderHandler` to cache `now` and pass `now.Year`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs:24-59`

The duplicate handler reads `_timeProvider.GetUtcNow()` four times (lines 41, 47, 52, 57, 59). Apply the same single-cached-`now` pattern.

- [ ] **Step 1: Cache `now`, pass `now.Year`, reuse for all stamps**

Edit the `Handle` method body. Replace the block spanning lines 27 – 59 (from `var currentUser = _currentUserService.GetCurrentUser();` down to and including `var lotNumber = ManufactureOrderExtensions.GetDefaultLot(...)`) with this exact code:

```csharp
        var currentUser = _currentUserService.GetCurrentUser();

        // Get the source order
        var sourceOrder = await _repository.GetOrderByIdAsync(request.SourceOrderId, cancellationToken);
        if (sourceOrder == null)
        {
            return new DuplicateManufactureOrderResponse(ErrorCodes.OrderNotFound);
        }

        // Cache one TimeProvider reading so the year, CreatedDate, StateChangedAt,
        // PlannedDate, expiration date, and lot number on the duplicate row all derive from the same instant.
        var now = _timeProvider.GetUtcNow();

        // Generate unique order number for the duplicate using the UTC year.
        var orderNumber = await _repository.GenerateOrderNumberAsync(now.Year, cancellationToken);

        // Today (UTC) for lot number and expiration calculations
        var today = DateOnly.FromDateTime(now.DateTime);

        // Create the duplicate order
        var duplicateOrder = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = now.DateTime,
            CreatedByUser = currentUser.GetDisplayName(),
            ResponsiblePerson = sourceOrder.ResponsiblePerson,
            PlannedDate = today,
            State = ManufactureOrderState.Draft,
            StateChangedAt = now.DateTime,
            StateChangedByUser = currentUser.GetDisplayName()
        };

        var expirationDate = sourceOrder.SemiProduct != null
            ? ManufactureOrderExtensions.GetDefaultExpiration(now.DateTime, sourceOrder.SemiProduct.ExpirationMonths)
            : (DateOnly?)null;
        var lotNumber = ManufactureOrderExtensions.GetDefaultLot(now.DateTime);
```

Leave the semi-product duplication, products loop, save, and return unchanged.

- [ ] **Step 2: Verify no extra `_timeProvider.GetUtcNow()` survives in this handler**

Run:

```bash
grep -n "_timeProvider" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs
```

Expected output: exactly two matches — the assignment inside the constructor and the cached `var now = _timeProvider.GetUtcNow();` line.

---

## Task 4: Fix all existing mock signatures in Manufacture tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` (lines 67, 97, 134, 174, 212, 259, 297, 311, 326, 364, 393, 417, 536)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerSinglePhaseTests.cs` (lines 67, 133)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` (lines 113, 130, 209)

These are mechanical edits that re-establish a green build. Use a single-file find-and-replace for each.

- [ ] **Step 1: Update `CreateManufactureOrderHandlerTests.cs` mocks**

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs`, replace every occurrence of:

```csharp
GenerateOrderNumberAsync(It.IsAny<CancellationToken>())
```

with:

```csharp
GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())
```

(Use `replace_all` — every matching call should be updated.)

- [ ] **Step 2: Update `CreateManufactureOrderHandlerSinglePhaseTests.cs` mocks**

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerSinglePhaseTests.cs`, apply the same `replace_all` for the two occurrences.

- [ ] **Step 3: Update `DuplicateManufactureOrderHandlerTests.cs` mocks**

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`, apply the same `replace_all` for the three occurrences.

- [ ] **Step 4: Build the solution — expect a clean compile**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with zero errors. Warnings are acceptable only if pre-existing; new warnings introduced by these edits must be fixed.

- [ ] **Step 5: Run the Manufacture test suite — expect all green**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture" --no-build
```

Expected: every test in the Manufacture namespace passes. If any test fails, it is a regression introduced by the refactor — fix before continuing.

- [ ] **Step 6: Commit the refactor**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs \
        backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerSinglePhaseTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit -m "refactor(manufacture): pass year as parameter to GenerateOrderNumberAsync

Removes the DateTime.Now read from ManufactureOrderRepository.GenerateOrderNumberAsync
and threads the year through the repository contract instead. Both handlers
(CreateManufactureOrder, DuplicateManufactureOrder) now cache a single
TimeProvider.GetUtcNow() reading and reuse it for the year, CreatedDate,
StateChangedAt, expiration date, and lot number — guaranteeing all temporal
stamps on the new row come from the same instant.

Updates existing test mocks to match the new repository signature. Behavioral
guard tests for the year boundary and audit consistency are added in a
follow-up commit."
```

---

## Task 5: Add year-boundary guard test for `CreateManufactureOrderHandler` (FR-3)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs`

These tests lock in FR-3 behavior: the year the repository receives must come from `TimeProvider.GetUtcNow().Year`, independent of the host clock or time zone.

- [ ] **Step 1: Add a year-end UTC test (verifies year=2026 just before midnight)**

Append the following test method inside the existing `CreateManufactureOrderHandlerTests` class, before the closing brace. If the test class already defines a private `CreateValidRequest()` and a `CreateValidCatalogItem()` helper (used by the existing tests at line 59 / line 64), reuse them. The test asserts that the year the repository receives matches the `TimeProvider`'s UTC year.

```csharp
    [Fact]
    public async Task Handle_AtYearEndUtc_PassesUtcYearToRepository()
    {
        // Arrange — fake clock at 2026-12-31 23:30 UTC; local time zone is irrelevant.
        var yearEndUtc = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(yearEndUtc);

        int? capturedYear = null;
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((year, _) => capturedYear = year)
            .ReturnsAsync("MO-2026-001");

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => { order.Id = 1; return order; });

        // Act
        await _handler.Handle(CreateValidRequest(), CancellationToken.None);

        // Assert — year must be 2026, regardless of the host time zone.
        capturedYear.Should().Be(2026);
    }
```

- [ ] **Step 2: Add a year-start UTC test (verifies year=2027 just after midnight)**

Append directly below the previous test:

```csharp
    [Fact]
    public async Task Handle_AtYearStartUtc_PassesUtcYearToRepository()
    {
        // Arrange — fake clock at 2027-01-01 00:30 UTC.
        var yearStartUtc = new DateTimeOffset(2027, 1, 1, 0, 30, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(yearStartUtc);

        int? capturedYear = null;
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((year, _) => capturedYear = year)
            .ReturnsAsync("MO-2027-001");

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => { order.Id = 1; return order; });

        // Act
        await _handler.Handle(CreateValidRequest(), CancellationToken.None);

        // Assert
        capturedYear.Should().Be(2027);
    }
```

- [ ] **Step 3: Run the two new tests and confirm they pass**

Run:

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture.CreateManufactureOrderHandlerTests.Handle_AtYear" \
  --no-build
```

Expected: 2 passed, 0 failed.

---

## Task 6: Add year-boundary guard test for `DuplicateManufactureOrderHandler` (FR-3)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`

- [ ] **Step 1: Add a year-end UTC test**

Append the following test inside `DuplicateManufactureOrderHandlerTests`, before the closing brace. Reuse the existing `BuildSourceOrder(bool)` private helper (visible at line 41 of the file).

```csharp
    [Fact]
    public async Task Handle_AtYearEndUtc_PassesUtcYearToRepository()
    {
        // Arrange
        var yearEndUtc = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(yearEndUtc);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSourceOrder(includeSemiProduct: true));

        int? capturedYear = null;
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((year, _) => capturedYear = year)
            .ReturnsAsync("MO-2026-001");

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => { order.Id = PersistedOrderId; return order; });

        // Act
        await _handler.Handle(new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId }, CancellationToken.None);

        // Assert
        capturedYear.Should().Be(2026);
    }
```

- [ ] **Step 2: Add a year-start UTC test**

Append directly below:

```csharp
    [Fact]
    public async Task Handle_AtYearStartUtc_PassesUtcYearToRepository()
    {
        // Arrange
        var yearStartUtc = new DateTimeOffset(2027, 1, 1, 0, 30, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(yearStartUtc);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSourceOrder(includeSemiProduct: true));

        int? capturedYear = null;
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((year, _) => capturedYear = year)
            .ReturnsAsync("MO-2027-001");

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => { order.Id = PersistedOrderId; return order; });

        // Act
        await _handler.Handle(new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId }, CancellationToken.None);

        // Assert
        capturedYear.Should().Be(2027);
    }
```

- [ ] **Step 3: Run the new tests and confirm they pass**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture.DuplicateManufactureOrderHandlerTests.Handle_AtYear" \
  --no-build
```

Expected: 2 passed, 0 failed.

---

## Task 7: Add audit-consistency guard tests (arch review R-2 / FR-3 clarification)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`

These tests guard the arch review's mandatory clarification: even when the underlying `TimeProvider` would tick across the year boundary between reads, the handler must call `GetUtcNow()` once and reuse that instant — so the year segment of `OrderNumber` always agrees with `CreatedDate.Year` on the same row. We use `SetupSequence` so the second mock read crosses midnight; if the handler reads more than once, the assertion fails.

- [ ] **Step 1: Add the Create-handler audit-consistency test**

Append inside `CreateManufactureOrderHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_WhenClockCrossesYearBoundaryBetweenReads_KeepsYearAndCreatedDateConsistent()
    {
        // Arrange — first read just before midnight UTC on 2026-12-31; any subsequent read jumps to 2027-01-01.
        var beforeMidnight = new DateTimeOffset(2026, 12, 31, 23, 59, 59, 999, TimeSpan.Zero);
        var afterMidnight  = new DateTimeOffset(2027, 1, 1, 0, 0, 0, 1, TimeSpan.Zero);

        _timeProviderMock
            .SetupSequence(x => x.GetUtcNow())
            .Returns(beforeMidnight)
            .Returns(afterMidnight)
            .Returns(afterMidnight)
            .Returns(afterMidnight)
            .Returns(afterMidnight);

        int? capturedYear = null;
        ManufactureOrder? capturedOrder = null;

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((year, _) => capturedYear = year)
            .ReturnsAsync((int year, CancellationToken _) => $"MO-{year}-001");

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        // Act
        await _handler.Handle(CreateValidRequest(), CancellationToken.None);

        // Assert — year used for the OrderNumber must match the row's CreatedDate year.
        capturedYear.Should().Be(2026);
        capturedOrder.Should().NotBeNull();
        capturedOrder!.CreatedDate.Year.Should().Be(2026);
        capturedOrder.StateChangedAt.Year.Should().Be(2026);
        capturedOrder.OrderNumber.Should().StartWith("MO-2026-");
    }
```

- [ ] **Step 2: Add the Duplicate-handler audit-consistency test**

Append inside `DuplicateManufactureOrderHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_WhenClockCrossesYearBoundaryBetweenReads_KeepsYearAndCreatedDateConsistent()
    {
        // Arrange
        var beforeMidnight = new DateTimeOffset(2026, 12, 31, 23, 59, 59, 999, TimeSpan.Zero);
        var afterMidnight  = new DateTimeOffset(2027, 1, 1, 0, 0, 0, 1, TimeSpan.Zero);

        _timeProviderMock
            .SetupSequence(x => x.GetUtcNow())
            .Returns(beforeMidnight)
            .Returns(afterMidnight)
            .Returns(afterMidnight)
            .Returns(afterMidnight)
            .Returns(afterMidnight);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSourceOrder(includeSemiProduct: true));

        int? capturedYear = null;
        ManufactureOrder? capturedOrder = null;

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((year, _) => capturedYear = year)
            .ReturnsAsync((int year, CancellationToken _) => $"MO-{year}-001");

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) =>
            {
                capturedOrder = order;
                order.Id = PersistedOrderId;
                return order;
            });

        // Act
        await _handler.Handle(new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId }, CancellationToken.None);

        // Assert
        capturedYear.Should().Be(2026);
        capturedOrder.Should().NotBeNull();
        capturedOrder!.CreatedDate.Year.Should().Be(2026);
        capturedOrder.StateChangedAt.Year.Should().Be(2026);
        capturedOrder.OrderNumber.Should().StartWith("MO-2026-");
    }
```

- [ ] **Step 3: Run the two new tests and confirm they pass**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture&FullyQualifiedName~WhenClockCrossesYearBoundary" \
  --no-build
```

Expected: 2 passed, 0 failed.

---

## Task 8: Full Manufacture suite, format, and final commit

- [ ] **Step 1: Run the entire Manufacture suite**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture" \
  --no-build
```

Expected: all Manufacture tests pass, including the six new behavioral tests from Tasks 5 – 7.

- [ ] **Step 2: Run the whole backend test suite to catch any indirect regression**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all green. The `Purchase` namespace is unaffected by this change — its tests must remain unchanged and passing.

- [ ] **Step 3: Format**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no diff, or only trivial whitespace corrections on the files this PR touches.

- [ ] **Step 4: Verify zero clock reads remain in the repository method**

```bash
grep -nE "DateTime\.Now|DateTime\.UtcNow|TimeProvider" \
  backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs
```

Expected: zero matches inside `GenerateOrderNumberAsync`. The grep may match other methods on the repository — that is acceptable as long as it does not match anywhere between lines `public async Task<string> GenerateOrderNumberAsync` and its closing brace. Read the file and verify visually if uncertain.

- [ ] **Step 5: Commit the guard tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit -m "test(manufacture): add year-boundary and audit-consistency guards

Adds six xUnit guard tests covering FR-3 and the arch review's clarification:

- Year-end / year-start tests for both Create and Duplicate handlers verifying
  the year passed to GenerateOrderNumberAsync derives from TimeProvider.GetUtcNow().Year.
- SetupSequence-based tests that fail if the handler reads TimeProvider more
  than once per call, guaranteeing the OrderNumber year and CreatedDate year
  stay consistent even at the stroke of midnight UTC."
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Implementing task |
|---|---|
| FR-1: interface takes `int year`; repo emits `MO-{year}-` with no clock read | Task 1 (interface), Task 1 (impl with guarding comment), Task 8 Step 4 (grep guard) |
| FR-2: both handlers compute year from `TimeProvider` and pass it | Task 2 (Create), Task 3 (Duplicate) |
| FR-3: order numbers reflect UTC year; year + `CreatedDate` derive from same reading | Tasks 5 & 6 (year-end / year-start asserts); Task 7 (SetupSequence audit consistency) |
| FR-3 clarification (arch review): handlers cache one `GetUtcNow()` reading | Task 2 Step 1 (caches `now`), Task 3 Step 1 (caches `now`); Task 7 SetupSequence test fails if there is more than one read of significance |
| FR-4: existing sequence-suffix behavior preserved (zero-padding, lookup logic) | Task 1 Step 2 (impl preserved verbatim except for the prefix source); existing tests covering successful path act as the regression net |
| NFR-2: testability without OS-clock manipulation | Tasks 5 – 7 all rely on `Mock<TimeProvider>` only |
| NFR-3: backwards compatibility (no migration, no format change) | No DB or DTO changes anywhere in the plan |
| NFR-4: no Manufacture-module clock reads after change | Task 2 Step 2 grep, Task 3 Step 2 grep, Task 8 Step 4 grep — three independent verifications |

No spec requirement is left without a task.

**Placeholder scan:** zero. Every step has the actual code, exact path, exact command, exact expected output.

**Type consistency:**
- The repository signature is consistent across the interface (Task 1 Step 1), impl (Task 1 Step 2), the handler call sites (Tasks 2 / 3), and every test mock (Tasks 4 – 7): `GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default)`.
- Field names referenced from `ManufactureOrder` (`OrderNumber`, `CreatedDate`, `StateChangedAt`, `PlannedDate`) match the existing entity as observed in the live handler code.
- Helper names (`ManufactureOrderExtensions.GetDefaultExpiration`, `GetDefaultLot`) match their call sites in the current handlers.
- The Duplicate-test helper `BuildSourceOrder(bool includeSemiProduct)` and the constants `SourceOrderId`, `PersistedOrderId` referenced in Tasks 6 & 7 exist in the current `DuplicateManufactureOrderHandlerTests.cs`.
- The Create-test fields `_repositoryMock`, `_catalogRepositoryMock`, `_timeProviderMock`, `_handler`, the constants `ValidProductCode` / `GeneratedOrderNumber`, and the helpers `CreateValidRequest()` / `CreateValidCatalogItem()` exist in the current `CreateManufactureOrderHandlerTests.cs`.
