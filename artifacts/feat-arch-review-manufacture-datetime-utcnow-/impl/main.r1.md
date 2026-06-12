---

# Implementation: Consistent TimeProvider Usage in Manufacture Order Handlers

## What was implemented

Replaced five `DateTime.UtcNow` occurrences across three Manufacture handlers with `_timeProvider.GetUtcNow().DateTime`, making `CreatedDate`, `StateChangedAt`, and note `CreatedAt` fully controllable by `FakeTimeProvider` in tests. Three test classes were updated with frozen-clock `Mock<TimeProvider>` infrastructure and exact-equality assertions.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` — lines 47 and 52: `DateTime.UtcNow` → `_timeProvider.GetUtcNow().DateTime`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs` — line 145: `DateTime.UtcNow` → `_timeProvider.GetUtcNow().DateTime`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` — lines 46 and 52: `DateTime.UtcNow` → `_timeProvider.GetUtcNow().DateTime`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` — added `CreatedDate` and `StateChangedAt` assertions (mock already in place)
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs` — migrated from `TimeProvider.System` to `Mock<TimeProvider>`, tightened `BeCloseTo` to exact `Be(FixedNow.UtcDateTime)`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` — migrated from `TimeProvider.System` to `Mock<TimeProvider>`, added `CreatedDate` and `StateChangedAt` assertions

## Tests

- `DuplicateManufactureOrderHandlerTests.cs` — adds frozen-time assertions for `CreatedDate`/`StateChangedAt`; TDD red-green verified
- `UpdateManufactureOrderHandlerTests.cs` — frozen-time assertion for note `CreatedAt` (11 tests all pass)
- `CreateManufactureOrderHandlerTests.cs` — frozen-time assertions for `CreatedDate`/`StateChangedAt` (20 tests all pass including `SinglePhaseTests`)
- Full Manufacture namespace: **534/534 passing**

## How to verify

```bash
# From worktree root
grep -n "DateTime\.UtcNow" \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs \
  || echo "ALL THREE HANDLERS CLEAN"

dotnet build
dotnet test --no-build --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture"
```

## Notes

- `CreateManufactureOrderHandlerSinglePhaseTests.cs` was intentionally left unchanged — it uses its own mock setup and doesn't assert on `CreatedDate`/`StateChangedAt`, so it passed without modification.
- `CreateExistingOrder()` helper in `UpdateManufactureOrderHandlerTests.cs` still calls `DateTime.UtcNow.AddDays(-1)` for test fixture data — this is out of scope per spec and does not affect the frozen-clock assertions.
- Spec's reference to `Microsoft.Extensions.TimeProvider.Testing` was not used; `Mock<TimeProvider>` from Moq (already the project convention) was used instead, per arch review guidance.

## PR Summary

Replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime` in the three Manufacture order handlers (`Create`, `Duplicate`, `Update`) so that `CreatedDate`, `StateChangedAt`, and note `CreatedAt` are fully controllable by `FakeTimeProvider` in tests. Each handler already injected `TimeProvider` and used it for adjacent fields; this change completes the convention for the five remaining fields that had silently reverted to the system clock.

Test classes for `Update` and `Create` handlers were migrated from `TimeProvider.System` to `Mock<TimeProvider>` with a fixed `DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)`, and new exact-equality assertions (`Should().Be(FixedNow.UtcDateTime)`) were added for all five previously-uncontrolled fields.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` — replaced 2 `DateTime.UtcNow` calls
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs` — replaced 1 `DateTime.UtcNow` call
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` — replaced 2 `DateTime.UtcNow` calls
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` — added 2 frozen-time assertions
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs` — migrated to `Mock<TimeProvider>`, tightened note `CreatedAt` assertion
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` — migrated to `Mock<TimeProvider>`, added 2 frozen-time assertions

## Status
DONE