# Specification: Unit Tests for GetCalendarViewHandler

## Summary

`GetCalendarViewHandler` currently has 10.6% line coverage against a 60% threshold. This specification defines a suite of unit tests covering the five untested branches in the handler: cancelled-order exclusion, PlannedDate boundary enforcement, SemiProduct null/non-null mapping, Products null fallback, and the title-formatting strip for the " - meziprodukt" suffix.

## Background

The manufacture calendar is the primary planning view for production staff. The handler (`GetCalendarViewHandler`) performs two filtering stages after the repository call — a state check and a date-boundary check — plus several conditional mappings before building `CalendarEventDto` objects. None of these paths are exercised today. Regression in the cancelled-order filter would surface stale data to planners; a broken SemiProduct null guard would cause the frontend to receive an unexpected null where it expects an object, crashing the calendar view.

## Functional Requirements

### FR-1: Cancelled-order exclusion filter

The handler must exclude orders whose `State == ManufactureOrderState.Cancelled` from the returned event list, even when such orders fall within the requested date range.

**Acceptance criteria:**
- Given the repository returns one active order (State = Planned) and one cancelled order (State = Cancelled), both with PlannedDate inside the request window, the response contains exactly one event corresponding to the active order.
- The cancelled order's Id does not appear in `response.Events`.

### FR-2: PlannedDate boundary inclusion

An order whose `PlannedDate` equals exactly `DateOnly.FromDateTime(request.StartDate)` or exactly `DateOnly.FromDateTime(request.EndDate)` must be included in the response.

**Acceptance criteria:**
- Given an order with PlannedDate == StartDate boundary, the response contains one event with that order's Id.
- Given an order with PlannedDate == EndDate boundary, the response contains one event with that order's Id.

### FR-3: PlannedDate boundary exclusion

An order returned by the repository whose `PlannedDate` falls outside the request window (before StartDate or after EndDate) must be silently dropped.

**Acceptance criteria:**
- Given the repository returns an order with PlannedDate one day before StartDate, the response contains zero events.
- Given the repository returns an order with PlannedDate one day after EndDate, the response contains zero events.

### FR-4: SemiProduct null branch

When `order.SemiProduct` is null, `CalendarEventDto.SemiProduct` must be null (not a default-constructed object).

**Acceptance criteria:**
- Given an order with SemiProduct = null, the corresponding event's `SemiProduct` property is null.

### FR-5: SemiProduct non-null branch and title formatting

When `order.SemiProduct` is non-null, `CalendarEventDto.SemiProduct` must be a fully populated `CalendarEventSemiProductDto`. The event's `Title` must be the SemiProduct's `ProductName` with the literal suffix " - meziprodukt" stripped.

**Acceptance criteria:**
- Given an order with `SemiProduct.ProductName = "Argan Cream - meziprodukt"`, the event title is `"Argan Cream"`.
- Given an order with `SemiProduct.ProductName = "Plain Name"` (no suffix), the event title is `"Plain Name"`.
- `CalendarEventDto.SemiProduct.ProductCode`, `ProductName`, `PlannedQuantity`, `ActualQuantity`, and `BatchMultiplier` match the source `ManufactureOrderSemiProduct` values exactly.

### FR-6: Products null fallback

When `order.Products` is null, `CalendarEventDto.Products` must be an empty list (not null).

**Acceptance criteria:**
- Given an order with Products = null, the corresponding event's `Products` is an empty `List<CalendarEventProductDto>` (not null, count == 0).

### FR-7: Products non-null mapping

When `order.Products` is a non-empty list, each product is mapped to `CalendarEventProductDto` with correct field values.

**Acceptance criteria:**
- Given an order with two products, the event's `Products` has count 2, and each item's `ProductCode`, `ProductName`, `PlannedQuantity`, `ActualQuantity` match the source.

### FR-8: Repository exception handling

When the repository throws an exception, the handler catches it, logs the error, and returns a response with `ErrorCodes.InternalServerError` (no unhandled exception propagates).

**Acceptance criteria:**
- Given `GetOrdersForDateRangeAsync` throws any `Exception`, `Handle` returns without throwing.
- The returned `GetCalendarViewResponse` carries `ErrorCodes.InternalServerError`.

### FR-9: Events sorted by date

When the repository returns orders with different PlannedDates, the events in the response are ordered ascending by `Date`.

**Acceptance criteria:**
- Given orders with PlannedDates in reverse order, `response.Events` is ascending by `Date`.

## Non-Functional Requirements

### NFR-1: Performance

Tests must complete in under 5 seconds total; each test case in under 1 second. No I/O, real database, or network calls — all dependencies are mocked.

### NFR-2: Test isolation

Each test must be fully independent. No shared mutable state between test cases. The repository mock is configured fresh per test (constructor or per-test setup).

### NFR-3: Maintainability

Follow the project's existing naming convention: `[MethodName]_[Scenario]_[ExpectedBehavior]()`. Use Moq for mocking `IManufactureOrderRepository` and `ILogger<GetCalendarViewHandler>`. Use FluentAssertions for assertions. Mirror the project's Arrange/Act/Assert structure.

### NFR-4: Coverage target

After these tests are added, line coverage for `GetCalendarViewHandler` must reach at least 60%.

## Data Model

No new entities or schema changes. The tests operate on the existing domain types:

- `ManufactureOrder` — aggregate with `Id`, `OrderNumber`, `PlannedDate` (DateOnly), `State` (ManufactureOrderState), `SemiProduct` (ManufactureOrderSemiProduct?), `Products` (List\<ManufactureOrderProduct\>?)
- `ManufactureOrderSemiProduct` — value type with `ProductCode`, `ProductName`, `PlannedQuantity`, `ActualQuantity` (decimal?), `BatchMultiplier`
- `ManufactureOrderProduct` — value type with `ProductCode`, `ProductName`, `PlannedQuantity`, `ActualQuantity` (decimal?)
- `ManufactureOrderState` — enum values: Draft=1, Planned=2, SemiProductManufactured=4, Completed=5, Cancelled=6

## API / Interface Design

This feature adds no new API surface. The tests call `GetCalendarViewHandler.Handle(request, CancellationToken.None)` directly.

**Test class location:**
`backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandlerTests.cs`

**Handler constructor signature (under test):**
```csharp
public GetCalendarViewHandler(
    IManufactureOrderRepository repository,
    ILogger<GetCalendarViewHandler> logger)
```

**Repository method under mock:**
```csharp
Task<List<ManufactureOrder>> GetOrdersForDateRangeAsync(
    DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
```

**Request shape:**
```csharp
new GetCalendarViewRequest
{
    StartDate = new DateTime(2025, 6, 1),
    EndDate   = new DateTime(2025, 6, 30)
}
```

**Test cases to implement:**

| Test method | Scenario | Assert |
|---|---|---|
| `Handle_WithCancelledOrderInRange_ExcludesCancelledOrder` | One Planned + one Cancelled order, both in range | Events.Count == 1, Id matches Planned order |
| `Handle_WithOrderOnStartDateBoundary_IncludesEvent` | PlannedDate == StartDate | Events.Count == 1 |
| `Handle_WithOrderOnEndDateBoundary_IncludesEvent` | PlannedDate == EndDate | Events.Count == 1 |
| `Handle_WithOrderBeforeStartDate_ExcludesEvent` | PlannedDate == StartDate - 1 day | Events is empty |
| `Handle_WithOrderAfterEndDate_ExcludesEvent` | PlannedDate == EndDate + 1 day | Events is empty |
| `Handle_WithNullSemiProduct_SetsEventSemiProductToNull` | order.SemiProduct == null | event.SemiProduct is null |
| `Handle_WithSemiProductContainingSuffix_StripsProductNameSuffix` | ProductName == "X - meziprodukt" | event.Title == "X" |
| `Handle_WithSemiProductWithoutSuffix_LeavesProductNameUnchanged` | ProductName == "Plain Name" | event.Title == "Plain Name" |
| `Handle_WithNonNullSemiProduct_MapsSemiProductDtoCorrectly` | SemiProduct fully populated | DTO fields match source |
| `Handle_WithNullProducts_SetsEventProductsToEmptyList` | order.Products == null | event.Products not null, Count == 0 |
| `Handle_WithNonNullProducts_MapsProductDtosCorrectly` | Two products | event.Products.Count == 2, fields match |
| `Handle_WhenRepositoryThrows_ReturnsInternalServerError` | Repository throws Exception | response has InternalServerError, no throw |
| `Handle_WithMultipleOrdersAtDifferentDates_ReturnsSortedByDateAscending` | Two orders, dates reversed | Events[0].Date < Events[1].Date |

## Dependencies

- `Anela.Heblo.Domain.Features.Manufacture.IManufactureOrderRepository` — mocked via Moq
- `Microsoft.Extensions.Logging.ILogger<GetCalendarViewHandler>` — mocked via Moq (`Mock.Of<ILogger<...>>()` or `new Mock<ILogger<...>>()`)
- xUnit, FluentAssertions, Moq — already present in `Anela.Heblo.Tests.csproj`
- `ManufactureOrder` must be constructable in tests; if the constructor is private or enforces invariants via the state machine, test instances must be built using whatever factory or builder pattern the domain exposes (or object initializers if the setters are accessible). Assumption: `ManufactureOrder` properties have at least `internal` setters reachable from the test project, or a public parameterless constructor is available.

## Out of Scope

- Integration tests against a real database or the HTTP layer
- Tests for `GetOrdersForDateRangeAsync` implementation (repository-layer concern)
- Tests for other manufacture use cases
- Changes to the handler implementation itself — tests must pass against the existing code
- E2E or Playwright tests

## Open Questions

1. **ManufactureOrder constructability**: The domain exploration noted that `State` has an internal setter and that `ChangeState` enforces a state machine. Can tests set `State = ManufactureOrderState.Cancelled` directly (e.g., via object initializer or reflection), or is a factory/builder needed? If `internal` setters are not visible from the test assembly, `InternalsVisibleTo` or a test-builder helper must be added.

2. **ManufactureOrderSemiProduct and ManufactureOrderProduct constructability**: Are these plain classes with public setters, or do they enforce invariants? If parameterless constructors are absent, sample test data setup may need a different approach.

## Status: HAS_QUESTIONS
