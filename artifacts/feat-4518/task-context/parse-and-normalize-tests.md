### task: parse-and-normalize-tests
Create `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsNegativeKeywordHandlerTests.cs` with:
- Theory: levels Ad and Account -> Execute Failed, no mutation; ReadCurrent Exists=false, `api.Queries` empty.
- Theory: target ids "", "abc", "12x" -> same expectations.
- Missing Text key and missing MatchType key (construct `AdAction` with empty payload) -> Failed, no mutation.
- Boundaries: text of exactly 80 chars (single word) succeeds; 10 words succeeds; whitespace-only text fails.
- Normalization: text "a\t b\n  c" sends "a b c" in `create.keyword.text`; existing negative "A B C" EXACT reads Present for that input.
- Different text with same match type reads Absent.
Verify: `dotnet test ... --filter GoogleAdsNegativeKeywordHandlerTests` passes. Commit: `test(google-ads): negative keyword parse and normalize cases`.

