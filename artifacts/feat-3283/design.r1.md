# Design: Unit Tests for ProductFilterService

*Skipped — backend-only test addition, no UI/UX components. See arch-review.r1.md.*

## Component Design

Single test class `ProductFilterServiceTests` in `backend/test/Anela.Heblo.Tests/Features/Analytics/`.

- Instantiates `ProductFilterService` directly (no interface, no mocking)
- Private `static MakeProduct(...)` factory for `AnalyticsProduct` test fixtures
- Private `static async IAsyncEnumerable<AnalyticsProduct> ToAsyncEnumerable(IEnumerable<AnalyticsProduct> source)` helper for stream tests
- xUnit `[Fact]` tests, FluentAssertions for assertions

## Data Schemas

No schema changes. All test data is constructed in-memory using `AnalyticsProduct` with required properties:
- `ProductCode` — arbitrary string
- `ProductName` — varied per test scenario
- `ProductCategory` — varied per test scenario (nullable)
- `Type` — `AnalyticsProductType.Product`
- `MarginAmount` — `0m` (irrelevant to filtering)
- `SalesHistory` — empty list `[]`
