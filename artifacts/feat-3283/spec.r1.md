# Specification: Unit Tests for ProductFilterService

## Summary

`ProductFilterService` has two methods — `PassesFilters` and `FilterProductsAsync` — that together drive analytics product filtering, yet have 0% line coverage. This feature adds a focused unit-test suite that exercises every branch of both methods to reach the required 60% threshold and protect the analytics view from silent data corruption.

## Background

The weekly coverage-gap routine (CI run #27941952679, commit 9463aa5983b2a6d201782725aeeaaba777d8c07d) flagged `ProductFilterService` at 0% coverage against a 60% threshold. The class lives in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ProductFilterService.cs` and is a pure, stateless service: no database, no HTTP calls, no external dependencies. Both methods are straightforward to exercise directly with in-memory test data. Untested filtering logic could silently return the wrong products in analytics views — wrong category matches, wrong case-sensitivity behaviour, or an off-by-one on the `maxProducts` cap — without any runtime signal.

## Functional Requirements

### FR-1: Test `PassesFilters` — product name filter

Verify that the `productFilter` parameter is applied as a case-insensitive substring match on `ProductName` and is skipped when null or whitespace.

**Acceptance criteria:**
- A product whose `ProductName` contains the filter string (same case) passes.
- A product whose `ProductName` contains the filter string (different case, e.g. filter `"cream"`, name `"Day Cream SPF30"`) passes.
- A product whose `ProductName` does not contain the filter string fails.
- When `productFilter` is `null`, the name check is skipped and the product is not rejected on that basis.
- When `productFilter` is an empty string or whitespace, the name check is skipped and the product is not rejected on that basis.

### FR-2: Test `PassesFilters` — category filter

Verify that the `categoryFilter` parameter is applied as a case-insensitive exact match on `ProductCategory` and is skipped when null or whitespace.

**Acceptance criteria:**
- A product whose `ProductCategory` exactly matches the filter string (same case) passes.
- A product whose `ProductCategory` exactly matches the filter string (different case, e.g. filter `"skincare"`, category `"Skincare"`) passes.
- A product whose `ProductCategory` is a different value fails.
- A product whose `ProductCategory` is a substring or superset of the filter string (e.g. filter `"Skin"`, category `"Skincare"`) fails, because the match is `Equals` not `Contains`.
- When `categoryFilter` is `null`, the category check is skipped.
- When `categoryFilter` is an empty string or whitespace, the category check is skipped.

### FR-3: Test `PassesFilters` — combined filters

Verify that when both `productFilter` and `categoryFilter` are provided, both conditions must hold simultaneously.

**Acceptance criteria:**
- A product that matches the name filter but not the category filter fails.
- A product that matches the category filter but not the name filter fails.
- A product that matches both filters passes.
- When both filters are null, any product passes (returns `true`).

### FR-4: Test `FilterProductsAsync` — filtering applied to async stream

Verify that products that do not pass `PassesFilters` are excluded from the returned list and products that do pass are included.

**Acceptance criteria:**
- Given a stream of 5 products where 3 match the filters and 2 do not, the result list contains exactly the 3 matching products.
- Given a stream of products with no filters set, all products are returned (up to `maxProducts`).

### FR-5: Test `FilterProductsAsync` — `maxProducts` cap

Verify that the method stops consuming the stream and returns at most `maxProducts` entries, even when more matching products remain in the stream.

**Acceptance criteria:**
- Given `maxProducts = 3` and a stream of 10 products that all pass the filters, the returned list has exactly 3 items.
- The 3 items returned are the first 3 from the stream (stream-order is preserved).
- Given `maxProducts = 3` and a stream of 2 matching products, the returned list has 2 items (does not pad or error).

### FR-6: Test `FilterProductsAsync` — empty stream

Verify graceful handling of an empty input.

**Acceptance criteria:**
- Given an empty async stream, `FilterProductsAsync` returns an empty list without throwing.

### FR-7: Test `FilterProductsAsync` — CancellationToken propagation

Verify that cancellation is respected.

**Acceptance criteria:**
- Given a pre-cancelled `CancellationToken`, `FilterProductsAsync` throws `OperationCanceledException` (or returns immediately without processing items, depending on async-enumerable semantics). The test must not hang.

## Non-Functional Requirements

### NFR-1: Performance

No targets apply — these are in-memory unit tests. Each test must complete in under 1 second.

### NFR-2: Test project placement and tooling

Tests must be added to the existing `backend/test/Anela.Heblo.Tests/` project (`Anela.Heblo.Tests.csproj`) under the path `Application/Analytics/` to match the directory convention used by other Application-layer tests (e.g. `Application/FinancialOverview/`). The project already references `Anela.Heblo.Application` and `FluentAssertions` + `Moq`/`NSubstitute` + `xunit`.

### NFR-3: Coverage threshold

After the tests are added, line coverage on `ProductFilterService` must meet or exceed 60% as measured by the project's CI coverage run. Full branch coverage of all six `if`-guards in the two methods is the practical target.

### NFR-4: No new dependencies

No new NuGet packages are required. The existing test project already provides all necessary tooling.

## Data Model

The tests operate on two domain types from `Anela.Heblo.Domain.Features.Analytics`:

**`AnalyticsProduct`** (class, `required` init properties):
- `ProductCode` — `string`
- `ProductName` — `string` (subject of name filter)
- `ProductCategory` — `string?` (subject of category filter)
- `Type` — `AnalyticsProductType`
- `MarginAmount` — `decimal`
- `SalesHistory` — `List<SalesDataPoint>` (can be empty for filter tests)
- Various pricing/cost/margin decimal properties (use `0` defaults in test fixtures)

**`SalesDataPoint`** (class, `required` init properties):
- `Date`, `AmountB2B`, `AmountB2C` — not relevant to filter logic; use minimal values.

Test fixtures should be created as private static helper methods or inline object initialisers within the test class. No shared fixture infrastructure is needed.

## API / Interface Design

No API surface changes. The tests call `ProductFilterService` directly (the concrete class, not via the interface) to keep the test setup minimal. The interface `IProductFilterService` is not mocked.

Helper for producing an async stream from an `IEnumerable<AnalyticsProduct>`:

```csharp
private static async IAsyncEnumerable<AnalyticsProduct> ToAsyncEnumerable(
    IEnumerable<AnalyticsProduct> source)
{
    foreach (var item in source)
        yield return item;
    await Task.CompletedTask;
}
```

This pattern is the standard approach used in the codebase for testing `IAsyncEnumerable`-based methods without infrastructure.

## Dependencies

- `Anela.Heblo.Application` project (already referenced) — provides `ProductFilterService`.
- `Anela.Heblo.Domain` (transitively available) — provides `AnalyticsProduct`, `SalesDataPoint`, `AnalyticsProductType`.
- `xunit` (already referenced) — test runner.
- `FluentAssertions` (already referenced) — assertions (`Should().Be()`, `Should().HaveCount()`, etc.).

No mocking framework is needed; `ProductFilterService` has no dependencies to inject.

## Out of Scope

- Testing the callers of `ProductFilterService` (e.g. query handlers that use `IProductFilterService`).
- Integration tests against a real database or async enumerable source backed by EF Core.
- Performance or load tests.
- Changes to `ProductFilterService` implementation — the service is correct; only tests are missing.
- Frontend or E2E coverage of analytics filtering UI.

## Open Questions

None.

## Status: COMPLETE
