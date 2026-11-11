# Implementation Plan: ListByExternalCodePrefixAsync unit tests

### task: add-routing-helpers
**Files:** modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

- [ ] Add private helpers `BuildOrderRoutingClient`, `ListPage`, `Detail` as described in design.r1.md (record requested `PathAndQuery`; route list vs detail; 404 otherwise).
- [ ] Run `dotnet build backend/test/Anela.Heblo.Tests` and confirm it compiles.
- [ ] Commit: `test(shoptet): add routing helpers for order client tests`

### task: add-pagination-and-prefix-tests
**Files:** modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

- [ ] Write `ListByExternalCodePrefixAsync_SinglePage_ReturnsOnlyPrefixMatches` (A `TEST-1` match, B `OTHER-1` no match; assert one result with mapped fields and one list request).
- [ ] Write `_MultiplePages_FindsMatchOnLaterPageAndStops` (pageCount=2, match on page 2; assert requests include page=1 and page=2, none for page=3).
- [ ] Write `_PrefixIsCaseSensitive_AndNullExternalCodeIgnored` (`test-1` and null ExternalCode excluded for prefix `TEST-`).
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetOrderClientTests"`; expect pass.
- [ ] Commit: `test(shoptet): cover pagination and prefix match in ListByExternalCodePrefixAsync`

### task: add-email-filter-and-empty-tests
**Files:** modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

- [ ] Write `_EmailFilter_ReturnsOnlyMatchingEmailCaseInsensitive` (two prefix-matching orders, filter with different casing; assert one result and that the filtered-out order's detail path was never requested).
- [ ] Write `_PageCountOne_NoMatch_ReturnsEmpty` and `_PageCountZero_ReturnsEmpty` (empty list, single list request, no exception).
- [ ] Run the same filtered `dotnet test`; expect pass. Run `dotnet format backend/Anela.Heblo.sln --include <test file>` (or repo-standard format command) and verify no diff remains.
- [ ] Commit: `test(shoptet): cover email filter and empty results in ListByExternalCodePrefixAsync`
