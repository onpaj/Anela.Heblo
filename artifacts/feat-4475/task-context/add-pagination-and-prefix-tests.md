### task: add-pagination-and-prefix-tests
**Files:** modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

- [ ] Write `ListByExternalCodePrefixAsync_SinglePage_ReturnsOnlyPrefixMatches` (A `TEST-1` match, B `OTHER-1` no match; assert one result with mapped fields and one list request).
- [ ] Write `_MultiplePages_FindsMatchOnLaterPageAndStops` (pageCount=2, match on page 2; assert requests include page=1 and page=2, none for page=3).
- [ ] Write `_PrefixIsCaseSensitive_AndNullExternalCodeIgnored` (`test-1` and null ExternalCode excluded for prefix `TEST-`).
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetOrderClientTests"`; expect pass.
- [ ] Commit: `test(shoptet): cover pagination and prefix match in ListByExternalCodePrefixAsync`

