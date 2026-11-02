# Specification: Unit tests for ShoptetPaging guard paths

## Summary
Add a unit-test class covering `ShoptetPaging` (`IsLastPage`, `EnsureComplete`), the shared stop-condition guard for all paged Shoptet order reads. Line coverage is 39.1%; the three throw paths that protect the irreversible cursor/watermark advance are untested.

## Background
`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetPaging.cs` is `internal static`. A silently truncated read permanently loses orders because the backfill and incremental sync advance their cursor afterwards. The guards must throw `ShoptetOrderSyncException`. No test file exists for the class (it is only exercised indirectly through sync service tests). `InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")` is already declared, so the class is directly testable.

## Functional Requirements

### FR-1: IsLastPage tests
**Acceptance criteria:**
- `paginator == null` throws `ShoptetOrderSyncException`; message mentions the description and page.
- `page >= PageCount` returns true (cover page == PageCount and page > PageCount), even when `itemsOnPage == 0`.
- `page < PageCount` and `itemsOnPage == 0` throws `ShoptetOrderSyncException`; message includes page, pageCount, totalCount.
- `page < PageCount` and `itemsOnPage > 0` returns false.

### FR-2: EnsureComplete tests
**Acceptance criteria:**
- `collected < TotalCount` throws `ShoptetOrderSyncException`; message includes totalCount and collected.
- `collected == TotalCount` does not throw.
- `collected > TotalCount` does not throw (extra rows tolerated).
- `paginator == null` does not throw.

## Non-Functional Requirements
### NFR-1: Performance
Pure in-memory tests, milliseconds.
### NFR-2: Security
None; no production code change.

## Data Model
Uses existing `ShoptetPaginatorDto` (TotalCount, Page, PageCount, ItemsOnPage, ItemsPerPage).

## API / Interface Design
New file `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Analytics/ShoptetPagingTests.cs`, namespace `Anela.Heblo.Adapters.Shoptet.Tests.Analytics`, xUnit + FluentAssertions, matching sibling tests.

## Dependencies
xUnit, FluentAssertions (already referenced by the test project).

## Out of Scope
Production code changes; sync service tests; process docs (no behaviour change).

## Open Questions
None.

## Status: COMPLETE
