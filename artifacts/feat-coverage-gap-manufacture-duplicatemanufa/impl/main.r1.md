All 5 tasks complete. Here is the output summary:

---

# Implementation: Unit Tests for DuplicateManufactureOrderHandler

## What was implemented

Added a focused unit-test class `DuplicateManufactureOrderHandlerTests` that locks in the current behavior of `DuplicateManufactureOrderHandler` across three branches: source order not found, full duplication with a semi-product, and duplication when no semi-product exists. No production code was modified.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` — new test class with 3 `[Fact]` tests, constants, mocks, constructor, and `BuildSourceOrder` helper

## Tests

`DuplicateManufactureOrderHandlerTests.cs` covers:
- `Handle_ReturnsOrderNotFound_WhenSourceOrderDoesNotExist` — FR-2: null source order → `ErrorCodes.OrderNotFound`, `GenerateOrderNumberAsync` and `AddOrderAsync` never called
- `Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts` — FR-3: full field duplication including `ActualQuantity` reset to `PlannedQuantity`, lot/expiration via `ManufactureOrderExtensions` static helpers, per-product assertions
- `Handle_OmitsSemiProductAndLeavesProductExpirationNull_WhenSourceHasNoSemiProduct` — FR-4: `SemiProduct == null` path, products still duplicated with `ExpirationDate == null`, `LotNumber` still set from helpers

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests"
# Expected: Failed: 0, Passed: 3, Skipped: 0
```

Full Manufacture suite: `Failed: 0, Passed: 534, Skipped: 0`

## Notes

- Architecture review amendments honored: flat path (no nested `UseCases/` subfolder), `public class` (not `sealed`), `Mock<TimeProvider>` with `GetUtcNow()` stub (no `Microsoft.Extensions.Time.Testing` package added), Moq only, `FixedNow.UtcDateTime` for lot/expiration expected values
- Pre-existing `AccessMatrixGen` MSB3073 warning appears in build output — not introduced by this change
- `git diff main...HEAD --stat -- backend/src` is empty; no production code touched

## PR Summary

Added three unit tests for `DuplicateManufactureOrderHandler` which had zero direct test coverage — only controller-level tests with a mocked mediator existed. The new tests lock in the handler's source-not-found error path, full field duplication semantics (including `ActualQuantity` reset and lot/expiration derivation via `ManufactureOrderExtensions`), and the `SemiProduct == null` branch behavior.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` — new test class; 3 `[Fact]` tests, class-level constants, `Mock<TimeProvider>` pattern matching sibling handler tests, Moq `Callback` capture for `AddOrderAsync` assertions

## Status

DONE