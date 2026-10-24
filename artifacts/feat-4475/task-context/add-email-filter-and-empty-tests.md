### task: add-email-filter-and-empty-tests
**Files:** modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

- [ ] Write `_EmailFilter_ReturnsOnlyMatchingEmailCaseInsensitive` (two prefix-matching orders, filter with different casing; assert one result and that the filtered-out order's detail path was never requested).
- [ ] Write `_PageCountOne_NoMatch_ReturnsEmpty` and `_PageCountZero_ReturnsEmpty` (empty list, single list request, no exception).
- [ ] Run the same filtered `dotnet test`; expect pass. Run `dotnet format backend/Anela.Heblo.sln --include <test file>` (or repo-standard format command) and verify no diff remains.
- [ ] Commit: `test(shoptet): cover email filter and empty results in ListByExternalCodePrefixAsync`
