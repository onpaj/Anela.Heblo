# Design: ShoptetPaging unit tests

## Component Design
`ShoptetPagingTests` (public sealed class, xUnit, FluentAssertions) in `Anela.Heblo.Adapters.Shoptet.Tests.Analytics`. A private helper `Paginator(int totalCount, int page, int pageCount, int itemsOnPage)` builds `ShoptetPaginatorDto`. Constant description string e.g. "orders".

Tests:
- `IsLastPage_throws_when_paginator_is_null`
- `IsLastPage_returns_true_when_page_reaches_page_count` (Theory: page == PageCount, page > PageCount; also itemsOnPage 0 case)
- `IsLastPage_throws_when_page_is_empty_but_more_pages_remain`
- `IsLastPage_returns_false_when_page_has_items_and_more_pages_remain`
- `EnsureComplete_throws_when_collected_is_less_than_total_count`
- `EnsureComplete_does_not_throw_when_collected_meets_or_exceeds_total_count` (Theory: equal, greater)
- `EnsureComplete_does_not_throw_when_paginator_is_null`

## Data Schemas
No schema changes. `ShoptetPaginatorDto { TotalCount, Page, PageCount, ItemsOnPage, ItemsPerPage }` is used as-is.
