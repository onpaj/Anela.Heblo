# Implementation: add-recalculate-product-weight-handler-tests

## What was implemented
Added a new xUnit test class covering the four previously-uncovered code paths of `RecalculateProductWeightHandler`: single-product dispatch, full-catalog dispatch, `Success` flag derivation (both true and false), and the exception fallback path. No production code was changed.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs` — new test class `RecalculateProductWeightHandlerTests` with 5 test methods (6 executed cases, one `[Theory]` with 2 `[InlineData]` cases), mocking `IProductWeightRecalculationService` and `ILogger<RecalculateProductWeightHandler>` with Moq and asserting with FluentAssertions.

## Tests
- `Handle_WithProductCode_DispatchesToSingleProductRecalculation` (FR-1) — non-empty `ProductCode` calls `RecalculateProductWeight`, never `RecalculateAllProductWeights`; response mirrors service result.
- `Handle_WithoutProductCode_DispatchesToFullCatalogRecalculation` (FR-2, `[Theory]` over `null`/`""`) — empty `ProductCode` calls `RecalculateAllProductWeights`, never `RecalculateProductWeight`.
- `Handle_WhenServiceReturnsNoErrors_SetsSuccessTrue` (FR-3) — `ErrorCount = 0` → `Success == true`.
- `Handle_WhenServiceReturnsErrors_SetsSuccessFalseAndPassesThrough` (FR-3) — `ErrorCount = 1` → `Success == false`, counts/messages pass through (load-bearing against `BaseResponse.Success` defaulting to `true`).
- `Handle_WhenServiceThrows_ReturnsFallbackResponseWithoutRethrowing` (FR-4) — service throws → handler catches, returns `ProcessedCount = 0`, `SuccessCount = 0`, `ErrorCount = 1`, `Success = false`, `ErrorMessages` containing `"Internal error"` and the original message.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecalculateProductWeightHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

```bash
dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes --include test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs
```
Result: exit code 0, no formatting differences.

## Notes
No deviations from the task plan — the test file was written exactly as specified and compiled/ran without modification. No production code touched; this is a pure test-coverage addition.

## PR Summary
`RecalculateProductWeightHandler` sat at 17.1% line coverage, below the 60% CI threshold, with its riskiest branch — single-product vs. full-catalog dispatch — completely untested. This adds one test file that pins that dispatch in both directions (asserting the expected service method is called and its sibling is not), verifies the `Success = result.ErrorCount == 0` mapping in both directions, and covers the exception fallback path that populates the user-facing `ErrorMessages`. All 6 test cases pass; no production code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs` — new unit test class for `RecalculateProductWeightHandler`

## Status
DONE
