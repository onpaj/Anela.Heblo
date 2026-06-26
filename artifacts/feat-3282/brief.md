## Module / File
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandler.cs

## Coverage
Line coverage: 10.6% (filter threshold: 60%)

## What's not tested
The handler performs a two-stage filter that no test exercises:
1. **Cancelled-state exclusion**: after the DB query by date range, orders with `State == ManufactureOrderState.Cancelled` are filtered out in memory. A test with a cancelled order in the range would confirm the filter is applied; regression here would surface cancelled orders on the calendar.
2. **PlannedDate boundary check**: even though orders are already queried by date range, each order's `PlannedDate` is re-checked against the request bounds before creating a `CalendarEventDto`. An order returned by the repository whose PlannedDate falls outside the window would be silently dropped.
3. **SemiProduct null guard**: when `order.SemiProduct` is null, the event's `SemiProduct` property is null rather than a DTO. No test verifies both branches (order with and without semi-product).
4. **Products null guard**: `order.Products` being null falls back to an empty list rather than a null reference.
5. **Title formatting**: the `ProductName.Replace(" - meziprodukt", "")` strip on the semi-product name has no assertion.

## Why it matters
The manufacture calendar is the primary planning view for production staff. If cancelled orders appear, planners would act on stale data. If the SemiProduct null guard breaks, the UI would receive a null where it expects an object, causing crashes for orders without semi-products.

## Suggested approach
Unit tests with a mocked `IManufactureOrderRepository`:
- Return a mix of active and cancelled orders → verify cancelled ones are excluded from events
- Order with PlannedDate at the exact start/end boundary → verify inclusion
- Order with SemiProduct = null → verify event.SemiProduct is null
- Order with non-null SemiProduct containing " - meziprodukt" suffix → verify title stripped correctly
- Order with null Products → verify event.Products is empty list
Estimated effort: ~2 hours.

---
_Filed by weekly coverage-gap routine on 2026-06-22. Based on CI run #27941952679 (9463aa5983b2a6d201782725aeeaaba777d8c07d)._
