# Design: Unit Tests for GetCalendarViewHandler

## Component Design

### Test Class: `GetCalendarViewHandlerTests`

**Location:** `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandlerTests.cs`

**Namespace:** `Anela.Heblo.Tests.Features.Manufacture.UseCases.GetCalendarView`

**Responsibilities:** Verify all five conditional branches inside `GetCalendarViewHandler.Handle` — state filtering, date-boundary enforcement, SemiProduct null/non-null mapping, Products null fallback, title-string stripping, sort order, and exception handling.

**Dependencies (constructor-injected mocks):**

| Field | Type | Purpose |
|---|---|---|
| `_repositoryMock` | `Mock<IManufactureOrderRepository>` | Controls `GetOrdersForDateRangeAsync` return value |
| `_loggerMock` | `Mock<ILogger<GetCalendarViewHandler>>` | Satisfies constructor; not asserted on |
| `_handler` | `GetCalendarViewHandler` | System under test, created in constructor |

**Anchor dates (constants, not computed):**

| Constant | Value | Type |
|---|---|---|
| `StartDate` | `new DateTime(2025, 6, 1)` | `DateTime` |
| `EndDate` | `new DateTime(2025, 6, 30)` | `DateTime` |
| `StartDateOnly` | `new DateOnly(2025, 6, 1)` | `DateOnly` (derived inline or as constant) |
| `EndDateOnly` | `new DateOnly(2025, 6, 30)` | `DateOnly` |

**Repository mock pattern:** All setups use `It.IsAny<DateOnly>()` for both date parameters. The handler's in-memory second-pass filter — not the repository — is what the boundary tests exercise.

**Test method naming convention:** `Handle_{Scenario}_{ExpectedOutcome}` — consistent with the existing `GetManufactureOrderHandlerTests` in the same project.

---

### Helper: `CreateOrder`

A private static factory method that constructs a `ManufactureOrder` with deterministic values:

```
private static ManufactureOrder CreateOrder(
    string orderNumber,
    DateOnly plannedDate,
    ManufactureOrderState state = ManufactureOrderState.Planned)
```

- Calls `order.InitializeState(state, DateTime.UtcNow, "Test User")` to bypass the state-machine guard (required for the `Cancelled` state, which cannot be reached via `ChangeState` from any reachable state in these tests).
- Sets `order.Products = new List<ManufactureOrderProduct>()` explicitly; overridden to `null!` in the Products-null test.
- Leaves `order.SemiProduct = null` by default; assigned explicitly in SemiProduct tests.

---

## Data Schemas

### Test Input: `GetCalendarViewRequest`

```csharp
new GetCalendarViewRequest
{
    StartDate = new DateTime(2025, 6, 1),
    EndDate   = new DateTime(2025, 6, 30)
}
```

Used verbatim across all test methods (extracted to a local or shared builder).

### Domain Object Shapes Used in Tests

**`ManufactureOrder` (minimal baseline):**

| Property | Test value |
|---|---|
| `Id` | Sequential int (1, 2, …) |
| `OrderNumber` | `"MO-2025-001"` etc. |
| `PlannedDate` | Varied per test (see boundary table below) |
| `State` | Set via `InitializeState(...)` |
| `SemiProduct` | `null` or populated object |
| `Products` | `new List<>()`, `null!`, or populated list |

**Boundary values for `PlannedDate` (FR-2 / FR-3):**

| Test case | `PlannedDate` | Expected in result |
|---|---|---|
| Exactly StartDate | `new DateOnly(2025, 6, 1)` | Included |
| Exactly EndDate | `new DateOnly(2025, 6, 30)` | Included |
| One day before StartDate | `new DateOnly(2025, 5, 31)` | Excluded |
| One day after EndDate | `new DateOnly(2025, 7, 1)` | Excluded |

**`ManufactureOrderSemiProduct` (FR-5):**

| Property | Test value |
|---|---|
| `ProductCode` | `"SP-001"` |
| `ProductName` | `"Argan Cream - meziprodukt"` or `"Plain Name"` |
| `PlannedQuantity` | `500m` |
| `ActualQuantity` | `480m` |
| `BatchMultiplier` | `1.5m` |

**`ManufactureOrderProduct` (FR-7):**

| Property | Test value |
|---|---|
| `ProductCode` | `"PROD-001"`, `"PROD-002"` |
| `ProductName` | `"Product One"`, `"Product Two"` |
| `PlannedQuantity` | `100m`, `200m` |
| `ActualQuantity` | `90m`, `195m` |

### Expected Output: `CalendarEventDto` fields verified per test

| Field | Verified in |
|---|---|
| `Title` | FR-4 (equals `OrderNumber`), FR-5 (stripped or unstripped name) |
| `SemiProduct` | FR-4 (is null), FR-5 (all 5 DTO fields match source) |
| `Products.Count` | FR-6 (0), FR-7 (2) |
| `Products[n].ProductCode/Name/PlannedQuantity/ActualQuantity` | FR-7 |
| `Events.Count` | FR-1 (1 event, not 2) |
| `Events` ordered ascending by `Date` | FR-9 |
| `Success` | FR-8 (false) |
| `ErrorCode` | FR-8 (`ErrorCodes.InternalServerError`) |

### Test Coverage Matrix

| FR | Test method (illustrative name) | Branch exercised |
|---|---|---|
| FR-1 | `Handle_WithCancelledOrder_ExcludesCancelledAndIncludesPlanned` | State != Cancelled filter |
| FR-2a | `Handle_PlannedDateEqualsStartDate_IsIncluded` | Boundary: == StartDate |
| FR-2b | `Handle_PlannedDateEqualsEndDate_IsIncluded` | Boundary: == EndDate |
| FR-3a | `Handle_PlannedDateBeforeStartDate_IsExcluded` | Boundary: < StartDate |
| FR-3b | `Handle_PlannedDateAfterEndDate_IsExcluded` | Boundary: > EndDate |
| FR-4 | `Handle_WithNullSemiProduct_SemiProductIsNullAndTitleIsOrderNumber` | SemiProduct null branch |
| FR-5a | `Handle_WithSemiProductNameContainingSuffix_TitleIsStripped` | Title strip: `" - meziprodukt"` |
| FR-5b | `Handle_WithSemiProductNameWithoutSuffix_TitleIsUnchanged` | Title strip: no suffix |
| FR-5c | `Handle_WithNonNullSemiProduct_DtoFieldsMatchSource` | SemiProduct DTO mapping |
| FR-6 | `Handle_WithNullProducts_EventProductsIsEmptyList` | Products null → `?? new List<>()` |
| FR-7 | `Handle_WithTwoProducts_EventProductsMappedCorrectly` | Products mapping |
| FR-8 | `Handle_WhenRepositoryThrows_ReturnsInternalServerError` | catch block |
| FR-9 | `Handle_WithUnorderedDates_EventsSortedAscending` | `OrderBy(e => e.Date)` |
