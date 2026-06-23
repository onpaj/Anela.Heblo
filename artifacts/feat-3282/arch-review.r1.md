# Architecture Review: Unit Tests for GetCalendarViewHandler

## Skip Design: false

---

## Architectural Fit Assessment

This feature adds a pure test file — no production code changes, no new abstractions, no schema impact. The work is a straightforward application of the project's existing unit-test conventions to a handler that has been left uncovered.

The handler under test (`GetCalendarViewHandler`) is a clean, side-effect-free MediatR handler: one repository call, in-memory LINQ filtering, a mapping loop, and a single try/catch. There are no circular dependencies, no ambient state, and no infrastructure concerns to mock beyond `IManufactureOrderRepository` and `ILogger<GetCalendarViewHandler>`. All domain types required to build test data (`ManufactureOrder`, `ManufactureOrderSemiProduct`, `ManufactureOrderProduct`) are already accessible from the test assembly.

The 13 test cases described in the spec cover every branch in the handler — the spec is complete and correctly scoped.

---

## Proposed Architecture

### Component Overview

One new file:

```
backend/test/Anela.Heblo.Tests/
  Features/
    Manufacture/
      UseCases/
        GetCalendarView/
          GetCalendarViewHandlerTests.cs    ← NEW
```

No subdirectory or helper file is needed. All test data construction fits comfortably inside private factory methods in the test class itself, following the same pattern as `GetManufactureOrderHandlerTests.cs`.

### Key Design Decisions

#### Decision 1: One class, private factory methods — no shared base class

**Options considered:**

- (A) Single test class with private `CreateOrder(...)` helper methods.
- (B) Shared `ManufactureOrderTestBuilder` class reused across test files.

**Chosen approach:** Option A.

**Rationale:** Only one test file needs this data today. A builder class is the right call when three or more test files converge on the same domain object construction — that threshold is not met here. Option A matches `GetManufactureOrderHandlerTests.cs` exactly and keeps the file self-contained. If a second or third handler test class later needs similar helpers, extract at that point.

#### Decision 2: `InitializeState` for state assignment, not reflection

**Options considered:**

- (A) `order.InitializeState(ManufactureOrderState.Cancelled, ...)` — the public API the domain explicitly provides for this purpose.
- (B) Direct assignment via `InternalsVisibleTo` (the internal setter is technically accessible from the test assembly).

**Chosen approach:** Option A.

**Rationale:** The spec brief confirms that `InternalsVisibleTo` is set, so Option B is technically possible. However, the existing test suite consistently uses `InitializeState`. Using the internal setter directly would be a silent deviation from the established pattern with no benefit — and it couples tests to an implementation detail. `InitializeState` is the sanctioned construction path.

#### Decision 3: `Products = null` by explicit assignment, not default

**Options considered:**

- (A) `new ManufactureOrder { ... }` leaves `Products` as `new List<...>()` (the field default). To test the null branch the test must explicitly set `order.Products = null!`.
- (B) Create a separate domain subtype or constructor overload that allows null.

**Chosen approach:** Option A.

**Rationale:** `ManufactureOrder.Products` is declared as `List<ManufactureOrderProduct> Products { get; set; } = new()`, so the default is not null. The test must assign `null!` after construction. This is intentional and documents the defensive behaviour being tested (`?? new List<>()`). Option B is unnecessary complexity.

#### Decision 4: Mock `ILogger` with `Mock.Of<>()`, not a full `new Mock<>`

**Options considered:**

- (A) `Mock.Of<ILogger<GetCalendarViewHandler>>()` — a null-object mock, no setup required.
- (B) `new Mock<ILogger<GetCalendarViewHandler>>()` and verify log calls.

**Chosen approach:** Option A for all tests except FR-8.

**Rationale:** Logging is a side effect, not an output. Verifying log calls adds test fragility with no correctness value. The exception-handling test (FR-8) only needs to assert on the returned response — log verification is still unnecessary.

#### Decision 5: Anchor dates as constants, not `DateTime.UtcNow`

**Options considered:**

- (A) Fixed anchor `new DateTime(2025, 6, 15)` (or similar) — deterministic across all test runs.
- (B) `DateTime.UtcNow` — test data shifts with wall-clock time.

**Chosen approach:** Option A.

**Rationale:** The boundary tests (FR-2, FR-3) require exact equality between `PlannedDate` and `DateOnly.FromDateTime(request.StartDate/EndDate)`. Using `DateTime.UtcNow` creates time-zone sensitivity risk and makes test output non-reproducible. Fixed dates remove both concerns with zero cost.

---

## Implementation Guidance

### Directory / Module Structure

Create one new directory and one new file:

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/
    GetCalendarViewHandlerTests.cs
```

The parent directory `UseCases/` already exists (see `GetManufactureStockTakingHistory/`). Create only `GetCalendarView/`.

### Interfaces and Contracts

No new interfaces. The test class depends on:

| Type | Source | Usage |
|---|---|---|
| `IManufactureOrderRepository` | `Anela.Heblo.Domain` | `new Mock<IManufactureOrderRepository>()` |
| `ILogger<GetCalendarViewHandler>` | `Microsoft.Extensions.Logging` | `Mock.Of<ILogger<...>>()` |
| `GetCalendarViewHandler` | `Anela.Heblo.Application` | system under test |
| `GetCalendarViewRequest` | `Anela.Heblo.Application` | request input |
| `GetCalendarViewResponse` | `Anela.Heblo.Application` | result assertions |
| `CalendarEventDto` | `Anela.Heblo.Application` | result assertions |
| `ManufactureOrder` | `Anela.Heblo.Domain` | test data |
| `ManufactureOrderSemiProduct` | `Anela.Heblo.Domain` | test data |
| `ManufactureOrderProduct` | `Anela.Heblo.Domain` | test data |
| `ManufactureOrderState` | `Anela.Heblo.Domain` | test data + assertions |
| `ErrorCodes` | `Anela.Heblo.Application.Shared` | FR-8 assertion |

### Data Flow

```
[Test arranges orders + state]
        |
        v
Mock<IManufactureOrderRepository>
  .Setup(GetOrdersForDateRangeAsync(...))
  .ReturnsAsync(orders)
        |
        v
GetCalendarViewHandler.Handle(request, CancellationToken.None)
        |
        |--- (handler) filters Cancelled, filters PlannedDate range
        |--- (handler) maps SemiProduct / Products
        |--- (handler) sorts by Date
        v
GetCalendarViewResponse
        |
        v
FluentAssertions on response.Events / response.ErrorCode
```

No async coordination issues — all mocks return completed tasks synchronously via `ReturnsAsync`.

### Recommended test class skeleton

```csharp
namespace Anela.Heblo.Tests.Features.Manufacture.UseCases.GetCalendarView;

public class GetCalendarViewHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly GetCalendarViewHandler _handler;

    // Anchor dates — fixed for deterministic boundary tests
    private static readonly DateTime StartDate = new DateTime(2025, 6, 1);
    private static readonly DateTime EndDate   = new DateTime(2025, 6, 30);
    private static readonly DateOnly  MidDate  = new DateOnly(2025, 6, 15);

    public GetCalendarViewHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _handler = new GetCalendarViewHandler(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetCalendarViewHandler>>());
    }

    private static GetCalendarViewRequest MakeRequest() => new()
    {
        StartDate = StartDate,
        EndDate   = EndDate
    };

    private static ManufactureOrder MakeOrder(int id, DateOnly plannedDate,
        ManufactureOrderState state = ManufactureOrderState.Planned)
    {
        var order = new ManufactureOrder
        {
            Id          = id,
            OrderNumber = $"MO-2025-{id:000}",
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = "Test",
            PlannedDate = plannedDate
        };
        order.InitializeState(state, DateTime.UtcNow, "Test");
        return order;
    }

    private void SetupRepository(List<ManufactureOrder> orders)
    {
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
    }

    // --- FR-1 ---
    [Fact]
    public async Task Handle_WithCancelledOrderInRange_ExcludesCancelledOrder() { ... }

    // --- FR-2 ---
    [Fact]
    public async Task Handle_WithOrderOnStartDateBoundary_IncludesEvent() { ... }

    [Fact]
    public async Task Handle_WithOrderOnEndDateBoundary_IncludesEvent() { ... }

    // --- FR-3 ---
    [Fact]
    public async Task Handle_WithOrderBeforeStartDate_ExcludesEvent() { ... }

    [Fact]
    public async Task Handle_WithOrderAfterEndDate_ExcludesEvent() { ... }

    // --- FR-4 ---
    [Fact]
    public async Task Handle_WithNullSemiProduct_SetsEventSemiProductToNull() { ... }

    // --- FR-5 ---
    [Fact]
    public async Task Handle_WithSemiProductContainingSuffix_StripsProductNameSuffix() { ... }

    [Fact]
    public async Task Handle_WithSemiProductWithoutSuffix_LeavesProductNameUnchanged() { ... }

    [Fact]
    public async Task Handle_WithNonNullSemiProduct_MapsSemiProductDtoCorrectly() { ... }

    // --- FR-6 ---
    [Fact]
    public async Task Handle_WithNullProducts_SetsEventProductsToEmptyList() { ... }

    // --- FR-7 ---
    [Fact]
    public async Task Handle_WithNonNullProducts_MapsProductDtosCorrectly() { ... }

    // --- FR-8 ---
    [Fact]
    public async Task Handle_WhenRepositoryThrows_ReturnsInternalServerError() { ... }

    // --- FR-9 ---
    [Fact]
    public async Task Handle_WithMultipleOrdersAtDifferentDates_ReturnsSortedByDateAscending() { ... }
}
```

### Critical implementation notes per test

**FR-6 (null Products):** `ManufactureOrder.Products` defaults to `new List<>()`. The test must explicitly set `order.Products = null!` after `MakeOrder()`. Without this the Products branch is never null and the test trivially passes while exercising the wrong code path.

**FR-3 (date boundary exclusion):** The handler applies a second date filter *after* calling the repository. The repository mock must return the out-of-range order — simulating a repository that returns data slightly outside the requested window. Do not restrict the mock to matching exact dates (`DateOnly` args); use `It.IsAny<DateOnly>()` to keep the setup permissive.

**FR-8 (exception handling):** Use `.ThrowsAsync(new Exception("db failure"))`. Assert `result.Success == false` and `result.ErrorCode == ErrorCodes.InternalServerError`. Do not wrap the `Handle` call in try/catch — FluentAssertions `Awaiting(...).Should().NotThrowAsync()` idiom is cleaner and more explicit.

**FR-5 (title suffix stripping):** The handler expression is:
```csharp
order.SemiProduct?.ProductName?.Replace(" - meziprodukt", "") ?? order.OrderNumber
```
"Plain Name" (no suffix) should produce `"Plain Name"` — the `Replace` call is a no-op when the substring is absent. Both sub-cases (with suffix, without suffix) need a separate test method to give clear failure messages.

**FR-1 (cancelled order):** The easiest construction path is `MakeOrder(id: 2, ..., state: ManufactureOrderState.Cancelled)`. The `InitializeState` method accepts `Cancelled` as the initial state — there is no state-machine enforcement in `InitializeState`, only in `ChangeState`.

---

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| FR-6 null Products test passes vacuously because default is `new List<>()` | Medium | Explicitly assign `order.Products = null!` in the test body and add an inline comment explaining why. |
| FR-3 tests pass vacuously if the repository mock filters by exact date args | Low | Mock with `It.IsAny<DateOnly>()` so out-of-range orders are returned and the in-handler filter is actually exercised. |
| Title-stripping logic uses `Replace` not `TrimEnd` — the suffix must appear verbatim | Low | Use `"Argan Cream - meziprodukt"` (exact casing and spacing) as the test ProductName. |
| `Handle_WhenRepositoryThrows` might accidentally swallow legitimate assertion failures | Low | Assert on the returned response, not inside a try/catch; let xUnit surface assertion exceptions normally. |
| Coverage does not reach 60% if test data bypasses a branch | Low | Verify after adding all 13 tests with `dotnet test --collect:"XPlat Code Coverage"`. |

---

## Specification Amendments

**Amendment 1 — FR-6 construction detail (clarification, not a change):**
The spec says "order with Products = null". Because the domain type initializes `Products` to `new List<>()`, the test must perform an explicit null assignment. The spec is correct in intent; the implementor just needs to be aware of this. No spec text change required.

**Amendment 2 — FR-5 title fallback when SemiProduct is null (gap):**
The spec covers both "with suffix" and "without suffix" when SemiProduct is non-null. It does not explicitly test the title fallback to `order.OrderNumber` when `SemiProduct` is null and `ProductName` is null. FR-4 (null SemiProduct) covers `event.SemiProduct == null` but does not assert on `event.Title`. The FR-4 test should additionally assert that `event.Title == order.OrderNumber` to confirm the null-coalescing fallback branch. This is a minor gap — add the title assertion to the FR-4 test without creating a new test case.

**Amendment 3 — CalendarEventProductDto field scope (clarification):**
The spec lists `ProductCode`, `ProductName`, `PlannedQuantity`, `ActualQuantity` for FR-7. These are the only fields on `CalendarEventProductDto`. The implementation does not map `LotNumber`, `ExpirationDate`, or `SemiProductCode` — they are not present on the DTO. No test data needs to supply them.

---

## Prerequisites

- No new NuGet packages. xUnit, Moq, and FluentAssertions are all present in `Anela.Heblo.Tests.csproj`.
- No changes to production code.
- No changes to `InternalsVisibleTo` — the declaration is already present in `Anela.Heblo.Domain.csproj`.
- Create the directory `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/` before creating the test file.
- Run `dotnet build` from `backend/` before running tests to confirm the new file compiles cleanly.
- After adding tests, run `dotnet test --collect:"XPlat Code Coverage"` and confirm `GetCalendarViewHandler` line coverage reaches ≥60%.
