# Specification: Unit Test Coverage for GetMaterialsForPurchaseHandler

## Summary
`GetMaterialsForPurchaseHandler` (backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetMaterialForPurchase/GetMaterialsForPurchaseHandler.cs) currently has 11.8% line coverage (4/34 lines), below the 60% CI threshold. This is a test-only change: add a focused unit test suite that exercises the handler's search-term filtering, no-purchase-history price fallback, and post-filter `Take(Limit)` behavior, using a mocked `ICatalogRepository`. No production code is modified.

## Background
This handler backs the search box used when creating purchase orders — it returns materials/goods matching an optional search term, capped at `request.Limit`, with the last known purchase price (or `null` if the item has never been purchased). A weekly automated coverage-gap routine flagged this handler (CI run #29525794843) because none of its three behaviorally significant branches — the OR-based search filter, the purchase-history fallback, and the ordering of filter-then-limit — are exercised by any existing test. A regression in any of these (e.g. OR silently becoming AND, or `Take` moving before the filter) would silently degrade or break purchase-order search and would not be caught by CI today.

No existing test file targets this handler (confirmed: no `GetMaterialsForPurchaseHandlerTests.cs` under `backend/test`). This spec defines a new test file for it, following the repository's established handler-test conventions (see e.g. `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductUsageHandlerTests.cs`).

## Functional Requirements

### FR-1: New test file for GetMaterialsForPurchaseHandler
Create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs` using xUnit + Moq + FluentAssertions (matching existing handler test conventions in that directory: `Mock<ICatalogRepository>` constructor-injected into the handler, `Handle(request, CancellationToken.None)` invoked directly, `.Should()` assertions on the returned `GetMaterialsForPurchaseResponse`).

`ICatalogRepository.FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken)` must be mocked via `Mock.Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(items)`, returning an `IEnumerable<CatalogAggregate>` fixture list. The handler applies its own `Type == Material || Type == Goods` predicate to the repository call, but since the mock intercepts the call itself (not the predicate's evaluation against real data), the fixture list returned by the mock should already reflect the set of items the repository would have returned for that predicate — i.e., construct fixtures as `Material`/`Goods` type items directly (the type filter itself is a one-line Where clause with no branching logic to add value from separate coverage).

**Acceptance criteria:**
- Test file compiles and runs under the existing `dotnet test` backend test suite.
- All new tests pass without modifying any non-test source file.
- Line coverage for `GetMaterialsForPurchaseHandler.cs` reaches at least the 60% CI threshold (all executable branches described in FR-2–FR-4 below are hit).

### FR-2: Search-term filter — Contains on ProductCode OR ProductName
Verify the case-insensitive substring match against `ProductCode` OR `ProductName`, and that the OR is genuinely inclusive (not accidentally AND).

**Acceptance criteria:**
- **Match by ProductCode only**: given a fixture set where one item's `ProductCode` contains the search term but its `ProductName` does not, and other items match neither field, calling `Handle` with that `SearchTerm` returns exactly the matching item.
- **Match by ProductName only**: given a fixture set where one item's `ProductName` contains the search term but its `ProductCode` does not, calling `Handle` with that `SearchTerm` returns exactly the matching item.
- **Match by both fields**: given an item whose `ProductCode` and `ProductName` both contain the search term, it is returned exactly once (no duplication).
- **Case-insensitivity**: a search term in a different case than the stored `ProductCode`/`ProductName` (e.g. uppercase term vs. lowercase stored value) still matches.
- **No match**: an item whose `ProductCode` and `ProductName` both do not contain the search term is excluded from the result.
- **Empty/whitespace/null `SearchTerm`**: when `SearchTerm` is `null`, `""`, or whitespace-only, no filtering is applied — all eligible (Material/Goods) fixture items are returned (subject to `Limit`, see FR-4).

### FR-3: No-purchase-history price fallback
Verify that `item.PurchaseHistory.LastOrDefault()?.PricePerPiece` does not throw and correctly maps to `null` when there is no purchase history, and to the last record's price when history exists.

**Acceptance criteria:**
- An item with an empty `PurchaseHistory` list produces a `MaterialForPurchaseDto` with `LastPurchasePrice == null`, and `Handle` completes without throwing.
- An item with one or more `PurchaseHistory` records produces `LastPurchasePrice` equal to the `PricePerPiece` of the *last* record in the list (matching `LastOrDefault()` semantics — i.e., verify with a multi-record history that the first/earlier record's price is NOT what's returned).

### FR-4: Take(Limit) applied after filtering
Verify that `Take(request.Limit)` operates on the already-filtered sequence, not on the unfiltered set.

**Acceptance criteria:**
- **Restrictive filter returns fewer than Limit**: given a fixture set where `Limit` is set higher than the number of items matching a given `SearchTerm`, all matching items are returned in full (not truncated) — this proves the filter runs before, and independently of, the limit, and that a narrow search isn't clipped by an unrelated limit value.
- **Filter + limit combined**: given a fixture set where more items match the `SearchTerm` than `Limit` allows, the result contains exactly `Limit` items, and every returned item is one of the matching items (i.e., the truncation happens on the filtered set, not by taking N items first and then filtering them, which could otherwise return fewer-than-`Limit` or non-matching results).
- **No search term, Limit applied**: with `SearchTerm` null/empty and a fixture set larger than `Limit`, exactly `Limit` items are returned.
- **Result ordering is by ProductName**: since the handler applies `.OrderBy(m => m.ProductName)` after `Take`, when asserting "which N items survive Take", account for the fact that `Take` operates on the enumeration order returned by the mocked `FindAsync`/`Where` (i.e., fixture list order as filtered), while the final assertion should independently check the response list order is alphabetical by `ProductName`. To keep the test deterministic and unambiguous, construct fixtures so the filtered-and-limited set is unambiguous regardless of pre-sort order (e.g., use a filter that narrows to a known fixed count, or assert set membership/count rather than positional order except when explicitly testing the final `OrderBy`).

### FR-5: Field mapping sanity (supporting coverage, not a separate risk area)
While covering FR-2–FR-4, the same test fixtures should incidentally exercise the DTO field mapping already present in the handler, since these lines currently also lack coverage:
- `ProductType` is mapped from `item.Type.ToString()`.
- `Location`: empty/null source `Location` maps to `null` in the DTO; a non-empty `Location` passes through unchanged.
- `MinimalOrderQuantity`: same empty-to-null mapping behavior as `Location`.
- `CurrentStock` is mapped from `(int)item.Stock.Available`.

**Acceptance criteria:**
- At least one test asserts `ProductType` equals the string representation of the fixture's `ProductType` (e.g. `"Material"` or `"Goods"`).
- At least one test asserts empty `Location`/`MinimalOrderQuantity` map to `null`, and at least one asserts a non-empty value passes through unchanged.
- No dedicated new FR is needed for `CurrentStock`; asserting it in the existing FR-3/FR-4 fixtures (non-zero `Stock.Available`) is sufficient — it is a direct pass-through cast with no branching logic.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a unit test addition with mocked dependencies; no real I/O. Tests must run within the standard backend unit test suite's existing time budget (individual tests should complete in milliseconds; no `Thread.Sleep` or real delays).

### NFR-2: Security
Not applicable — no auth, no sensitive data, no production code paths affected.

## Data Model
No new or changed data model. Tests construct `CatalogAggregate` fixture instances directly (as an object initializer, not via the domain constructor logic used elsewhere for stock-taking, since only `ProductCode`, `ProductName`, `Type`, `Stock`, `Location`, `PurchaseHistory`, and `MinimalOrderQuantity` are relevant to this handler). Relevant existing types (all already defined in `backend/src/Anela.Heblo.Domain/Features/Catalog/`):
- `CatalogAggregate`: `ProductCode` (string, backed by `Id`), `ProductName` (string), `Type` (`ProductType` enum), `Stock` (`StockData`), `Location` (string, defaults to `""`), `PurchaseHistory` (`IReadOnlyList<CatalogPurchaseRecord>`), `MinimalOrderQuantity` (string, defaults to `""`).
- `CatalogPurchaseRecord`: `PricePerPiece` (decimal), plus `Date`, `Amount`, `SupplierId`, etc. (not relevant to this handler).
- `StockData`: `Available` (decimal, computed from `Erp`/`Eshop`/`Transport`/`Manufactured` depending on `PrimaryStockSource`) — tests should set `Erp` (default `PrimaryStockSource`) directly for simplicity.
- `ProductType` enum: includes at minimum `Material` and `Goods` (the two types the handler selects) — confirm exact enum member names in `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductType.cs` (or equivalent) before writing fixtures, as casing/spelling must match exactly for `item.Type.ToString()` assertions.

Note: setting `CatalogAggregate.PurchaseHistory` — confirm at implementation time whether the property has a public setter or must be populated via a method (e.g. `AddPurchaseRecord`), since the source excerpt reviewed shows a custom getter/setter pair that also calls `UpdatePurchaseHistorySummary()`. If it's a plain settable list, initialize directly in the object initializer; otherwise use whatever mutation API the aggregate exposes.

## API / Interface Design
Not applicable — no API or interface changes. The handler's public contract (`GetMaterialsForPurchaseRequest { SearchTerm, Limit }` → `GetMaterialsForPurchaseResponse { Materials: List<MaterialForPurchaseDto> }`) is unchanged.

## Dependencies
- xUnit, Moq, FluentAssertions — already used throughout `backend/test/Anela.Heblo.Tests`; no new package references needed.
- `ICatalogRepository` (mocked) — no real repository or database involved.
- No dependency on other in-flight features or external services.

## Out of Scope
- Any change to `GetMaterialsForPurchaseHandler.cs`, `GetMaterialsForPurchaseRequest.cs`, or `GetMaterialsForPurchaseResponse.cs` production code.
- Integration or E2E tests for this handler or the purchase-order search UI.
- Testing the `ICatalogRepository.FindAsync` predicate expression itself (the `Type == Material || Type == Goods` filter) beyond what's incidentally covered by fixture construction — this is a trivial one-line Where clause and not called out in the coverage-gap brief as a risk area.
- Validator tests (no `FluentValidation` validator exists for `GetMaterialsForPurchaseRequest` in the current codebase; if one is added later, its tests are a separate concern).
- Adding a request validator or new business rules (e.g. `Limit` bounds checking) — the brief does not ask for behavior changes, only tests for existing behavior.
- Performance/load testing of the search endpoint.

## Open Questions
None.

## Status: COMPLETE
