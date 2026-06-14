All tasks complete. Here is the implementation summary:

---

# Implementation: Replace DateTime.Now with caller-supplied year in ManufactureOrderRepository.GenerateOrderNumberAsync

## What was implemented

Removed the hidden `DateTime.Now` dependency from `ManufactureOrderRepository.GenerateOrderNumberAsync` by adding an `int year` parameter. Both Manufacture handlers now cache a single `TimeProvider.GetUtcNow()` read and pass `now.Year` to the repository, ensuring the order number's year always agrees with `CreatedDate` on the same row. Six new guard tests lock in the year-boundary and audit-consistency behavior.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs` — added `int year` as first parameter to `GenerateOrderNumberAsync`
- `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs` — removed `DateTime.Now.Year`; uses `year` argument to build prefix; added guard comment (FR-1)
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` — caches `var now = _timeProvider.GetUtcNow()` once; passes `now.Year` and reuses `now.DateTime` for all timestamps
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` — same pattern
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` — updated 13 mock signatures; added 3 new guard tests
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerSinglePhaseTests.cs` — updated 2 mock signatures
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` — updated 3 mock signatures; added 3 new guard tests

## Tests

**Existing tests:** 18 in `CreateManufactureOrderHandlerTests`, 2 in `CreateManufactureOrderHandlerSinglePhaseTests`, 3 in `DuplicateManufactureOrderHandlerTests` — all updated and green.

**New guard tests (6 total):**
- `Handle_AtYearEndUtc_PassesUtcYearToRepository` — both handlers
- `Handle_AtYearStartUtc_PassesUtcYearToRepository` — both handlers
- `Handle_WhenClockCrossesYearBoundaryBetweenReads_KeepsYearAndCreatedDateConsistent` — both handlers (uses `SetupSequence` to detect multiple `GetUtcNow()` reads)

## How to verify

```bash
cd backend && dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture"
```

## Notes

- Purchase module untouched (uses a different interface `IPurchaseOrderNumberGenerator`)
- No database migration required — order number format unchanged
- Two commits on branch: `8da06e3c` (refactor) and `612103c8` (guard tests)

## PR Summary

Removes the hidden `DateTime.Now` dependency from `ManufactureOrderRepository.GenerateOrderNumberAsync` by threading the year as a caller-supplied `int` parameter. Both handlers now cache a single `TimeProvider.GetUtcNow()` reading at the top of `Handle()` and reuse it for the year argument, `CreatedDate`, `StateChangedAt`, expiration date, and lot number — guaranteeing all temporal stamps on a new order row derive from the same instant and eliminating the year-boundary skew (e.g., CET 00:30 on Jan 1 = UTC Dec 31, which previously produced a `MO-{next_year}` prefix while `CreatedDate` recorded the old year).

### Changes
- `IManufactureOrderRepository.cs` — added `int year` to `GenerateOrderNumberAsync` signature
- `ManufactureOrderRepository.cs` — removed `DateTime.Now.Year`; uses supplied year; added FR-1 guard comment
- `CreateManufactureOrderHandler.cs` — single cached `now` read; passes `now.Year` to repo
- `DuplicateManufactureOrderHandler.cs` — same pattern
- `CreateManufactureOrderHandlerTests.cs` — updated 13 mocks + 3 new year-boundary/consistency tests
- `CreateManufactureOrderHandlerSinglePhaseTests.cs` — updated 2 mocks
- `DuplicateManufactureOrderHandlerTests.cs` — updated 3 mocks + 3 new year-boundary/consistency tests

## Status

DONE