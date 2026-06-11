# Specification: Unit Test Coverage for SubmitStockTakingHandler

## Summary
Add unit-test coverage to `SubmitStockTakingHandler` in the Catalog feature, currently at zero. The new tests cover all four execution paths of the `Handle` method and lock down the silent-correctness hole where a successful eshop stock-taking does not update the local catalog snapshot when the product code is not found.

## Background
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingHandler.cs` orchestrates submitting a stock-taking operation to the eshop via `IEshopStockDomainService` and then synchronizing the result with the in-memory catalog aggregate via `CatalogAggregate.SyncStockTaking(StockTakingRecord)`. The handler is unreferenced by any test file (verified against `backend/test/Anela.Heblo.Tests/Features/Catalog/**`), while the sibling handler `SubmitManufactureStockTakingHandler` already has comprehensive coverage (`SubmitManufactureStockTakingHandlerTests.cs`).

The most important gap is **path 2**: when `IEshopStockDomainService.SubmitStockTakingAsync` succeeds (no `Error`) but `ICatalogRepository.GetByIdAsync` returns `null`, the handler returns a successful response while silently skipping the local stock sync. The UI sees a success, but the local snapshot diverges from the eshop. There is currently no test guarding against regressions of this branch.

The weekly coverage-gap routine identified this gap in CI run #27104028537 on 2026-06-08.

## Functional Requirements

### FR-1: Test failure path when domain service returns an error
Add a unit test that exercises the branch where `_eshopStockDomainService.SubmitStockTakingAsync` returns a `StockTakingRecord` with a non-empty `Error` value.

**Acceptance criteria:**
- A test named after the behavior (e.g. `Handle_DomainServiceReturnsError_ReturnsStockTakingFailedResponse`) exists in a new file `backend/test/Anela.Heblo.Tests/Features/Catalog/SubmitStockTakingHandlerTests.cs`.
- The mocked `IEshopStockDomainService` returns a `StockTakingRecord` with `Error` set to a non-empty string (e.g. `"Eshop rejected"`).
- The response's `ErrorCode` is `ErrorCodes.StockTakingFailed`.
- The response's error parameters dictionary contains the keys `"ProductCode"` and `"Error"` with the request `ProductCode` and the error message respectively.
- `ICatalogRepository.GetByIdAsync` is **never** invoked (verified via `Mock.Verify(..., Times.Never)`).
- No `SyncStockTaking` call is made.

### FR-2: Test silent-correctness hole when product is not found
Add a unit test that exercises the branch where the domain service succeeds but `_catalogRepository.GetByIdAsync` returns `null` (product not in local catalog).

**Acceptance criteria:**
- A test named e.g. `Handle_DomainServiceSucceeds_ProductNotFoundInCatalog_ReturnsSuccessWithoutSyncing` exists.
- The mocked `IEshopStockDomainService` returns a fully populated `StockTakingRecord` with `Error == null` (or empty).
- The mocked `ICatalogRepository.GetByIdAsync` returns `null` for the requested `ProductCode`.
- The response is constructed via the parameterless constructor (i.e. `Success == true` per `BaseResponse` convention) and is populated from the returned `StockTakingRecord` (`Id`, `Type`, `Code`, `AmountNew`, `AmountOld`, `Date`, `User`, `Error`).
- No `SyncStockTaking` call is made on any `CatalogAggregate` instance (verified via `Mock.Verify(..., Times.Never)` on whatever aggregate spy/stub is supplied; in practice this means the mock for `GetByIdAsync` returns `null` so the call path is short-circuited and the assertion is implicit — call it out as a comment).
- The test serves as a regression guard for the documented silent-correctness hole.

### FR-3: Test success path when product is found
Add a unit test that exercises the happy path where the domain service succeeds and the catalog repository returns the product.

**Acceptance criteria:**
- A test named e.g. `Handle_DomainServiceSucceeds_ProductFound_CallsSyncStockTakingAndReturnsResponse` exists.
- The mocked `IEshopStockDomainService` returns a populated `StockTakingRecord` matching the request `ProductCode` (so `SyncStockTaking`'s code-equality guard does not throw).
- The mocked `ICatalogRepository.GetByIdAsync(ProductCode, ct)` returns a real `CatalogAggregate` (preferred over deep mocking) with the matching `ProductCode`, allowing the real `SyncStockTaking` method to execute.
- After `Handle` returns, the aggregate's `StockTakingHistory` contains the supplied `StockTakingRecord` (asserting the side effect actually happened).
- The response is populated from the `StockTakingRecord` (same fields as FR-2).
- `ICatalogRepository.GetByIdAsync` is invoked exactly once with the request `ProductCode` and the supplied `CancellationToken`.

### FR-4: Test exception path from the domain service
Add a unit test that exercises the catch block when `_eshopStockDomainService.SubmitStockTakingAsync` throws.

**Acceptance criteria:**
- A test named e.g. `Handle_DomainServiceThrows_ReturnsInternalServerErrorResponse` exists.
- The mocked `IEshopStockDomainService.SubmitStockTakingAsync` is configured to throw an arbitrary `Exception` (e.g. `new InvalidOperationException("boom")`).
- The response's `ErrorCode` is `ErrorCodes.InternalServerError`.
- The response's error parameters dictionary contains the key `"ProductCode"` with the request `ProductCode`.
- `Handle` does not propagate the exception (the test passes by completing normally rather than the exception bubbling).
- `ICatalogRepository.GetByIdAsync` is **never** invoked.

### FR-5: Test fixture and shared setup
Provide a single test fixture that follows the AAA pattern and mirrors the structure of `SubmitManufactureStockTakingHandlerTests`.

**Acceptance criteria:**
- A class `SubmitStockTakingHandlerTests` is created in `backend/test/Anela.Heblo.Tests/Features/Catalog/SubmitStockTakingHandlerTests.cs`.
- The class uses xUnit (`[Fact]`), FluentAssertions (`Should().Be(...)`), and Moq (`Mock<T>`), matching the framework choices in `SubmitManufactureStockTakingHandlerTests.cs`.
- Mocks for `ICatalogRepository`, `ILogger<SubmitStockTakingHandler>`, and `IEshopStockDomainService` are created in the constructor and injected into a single `SubmitStockTakingHandler` field.
- Tests are organized AAA-style with `// Arrange`, `// Act`, `// Assert` comments.

## Non-Functional Requirements

### NFR-1: Performance
- Each test must run synchronously without external infrastructure (no database, no HTTP).
- The entire test class should complete in under 1 second locally.

### NFR-2: Security
- No real secrets, connection strings, or tokens in the test source.
- No network calls or filesystem writes from tests.

### NFR-3: Maintainability
- File is under 400 lines; functions under 50 lines (per global coding-style).
- No magic strings repeated across tests — extract shared constants (`productCode`, `targetAmount`) into local variables within each test (or fields if reused widely), matching the convention in `SubmitManufactureStockTakingHandlerTests`.
- Tests follow descriptive `Handle_<scenario>_<expected>` naming.

### NFR-4: Coverage
- All four `Handle` execution paths (FR-1 through FR-4) are exercised, taking the file from 0% to effectively 100% statement coverage on the handler's `Handle` method.

## Data Model
No new persistent types are introduced. The tests interact with existing types:

- `SubmitStockTakingRequest` (`ProductCode: string`, `TargetAmount: decimal`, `SoftStockTaking: bool`)
- `SubmitStockTakingResponse` (`BaseResponse` subclass with `Id`, `Type`, `Code`, `AmountNew`, `AmountOld`, `Date`, `User`, `Error`)
- `EshopStockTakingRequest` (domain DTO)
- `StockTakingRecord` (`Id: int`, `Type: StockTakingType`, `Code: string`, `AmountNew: double`, `AmountOld: double`, `Date: DateTime`, `User: string?`, `Error: string?`)
- `CatalogAggregate` (real instance used in FR-3 so `SyncStockTaking` runs end-to-end)
- `ErrorCodes.StockTakingFailed`, `ErrorCodes.InternalServerError`

## API / Interface Design
No production-code interface changes. The work is confined to test infrastructure under `backend/test/Anela.Heblo.Tests/Features/Catalog/`.

The new test file collaborates with:
- `Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking.SubmitStockTakingHandler`
- `Anela.Heblo.Domain.Features.Catalog.ICatalogRepository` (mocked)
- `Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockDomainService` (mocked)
- `Microsoft.Extensions.Logging.ILogger<SubmitStockTakingHandler>` (mocked)

## Dependencies
- **xUnit** (test framework) — already used across the project.
- **FluentAssertions** — already used across the project.
- **Moq** — already used across the project.
- No new NuGet packages required.
- No production code changes required.

## Out of Scope
- Integration tests against a real `IEshopStockDomainService` implementation (e.g. Flexi adapter).
- Tests for `SubmitStockTakingRequestValidator` (separate concern; track separately if also uncovered).
- Tests for the `CatalogAggregate.SyncStockTaking` method itself beyond what the happy-path test in FR-3 incidentally exercises.
- Fixing the underlying silent-correctness hole described in path 2 — the test in FR-2 only **documents and locks in** the current behavior so any future intentional change is visible. A separate ticket should decide whether to surface an error (e.g. `ErrorCodes.ProductNotFound`) when the product is missing.
- Refactoring the handler (e.g. extracting helpers, changing logging).

## Open Questions
None.

## Status: COMPLETE