# write-calendar-view-handler-tests — Implementation Report

## What was implemented

Unit tests for `GetCalendarViewHandler` covering all branches of the handler logic.

## Files created

- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandlerTests.cs`

## Files modified

None.

## Test results

**13 passed, 0 failed, 0 skipped.**

### Test cases

1. `Handle_WithCancelledOrderInRange_ExcludesCancelledOrder` — cancelled orders are excluded from the calendar view
2. `Handle_WithOrderOnStartDateBoundary_IncludesEvent` — orders exactly on the start date are included
3. `Handle_WithOrderOnEndDateBoundary_IncludesEvent` — orders exactly on the end date are included
4. `Handle_WithOrderBeforeStartDate_ExcludesEvent` — orders before the start date are excluded
5. `Handle_WithOrderAfterEndDate_ExcludesEvent` — orders after the end date are excluded
6. `Handle_WithNullSemiProduct_SetsEventSemiProductToNull` — null SemiProduct maps to null in the DTO
7. `Handle_WithSemiProductContainingSuffix_StripsProductNameSuffix` — ` - meziprodukt` suffix is stripped from ProductName
8. `Handle_WithSemiProductWithoutSuffix_LeavesProductNameUnchanged` — ProductName without suffix is unchanged
9. `Handle_WithNonNullSemiProduct_MapsSemiProductDtoCorrectly` — all SemiProduct fields map correctly to DTO
10. `Handle_WithNullProducts_SetsEventProductsToEmptyList` — null Products list maps to empty list in DTO
11. `Handle_WithNonNullProducts_MapsProductDtosCorrectly` — all Product fields map correctly to DTOs
12. `Handle_WhenRepositoryThrows_ReturnsInternalServerError` — repository exception returns `ErrorCodes.InternalServerError`
13. `Handle_WithMultipleOrdersAtDifferentDates_ReturnsSortedByDateAscending` — results are sorted by date ascending
