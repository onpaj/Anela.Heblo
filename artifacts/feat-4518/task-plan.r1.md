# Implementation Plan: Coverage for GoogleAdsNegativeKeywordHandler

Goal: raise and lock verified coverage of the handler via tests only. Run tests with `dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests`. Read `GoogleAdsAddNegativeKeywordTests.cs` and `Support/ExecutorHarness.cs` first to avoid duplicates.

### task: parse-and-normalize-tests
Create `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsNegativeKeywordHandlerTests.cs` with:
- Theory: levels Ad and Account -> Execute Failed, no mutation; ReadCurrent Exists=false, `api.Queries` empty.
- Theory: target ids "", "abc", "12x" -> same expectations.
- Missing Text key and missing MatchType key (construct `AdAction` with empty payload) -> Failed, no mutation.
- Boundaries: text of exactly 80 chars (single word) succeeds; 10 words succeeds; whitespace-only text fails.
- Normalization: text "a\t b\n  c" sends "a b c" in `create.keyword.text`; existing negative "A B C" EXACT reads Present for that input.
- Different text with same match type reads Absent.
Verify: `dotnet test ... --filter GoogleAdsNegativeKeywordHandlerTests` passes. Commit: `test(google-ads): negative keyword parse and normalize cases`.

### task: revert-and-routing-tests
Extend the same file with:
- Revert with original Failed outcome but non-null resource name -> Failed, no mutations.
- Revert Succeeded with null id and valid action -> Failed, no mutations.
- Revert with invalid payload (empty text) and valid original -> Failed, no mutations.
- Ad-group level: revert with campaignCriteria name or wrong parent -> Failed, no mutations.
- Ad-group level: `SucceedNextMutateWithoutResourceName` -> Failed with "verify in the account".
- ValidateOnly (`ExecutorHarness.Create(api, validateOnly: true)`): Execute at Campaign and AdGroup level sends one mutation with `validateOnly` true to the correct service and returns Failed with error starting `ValidateOnly:`; mirror assertions from `GoogleAdsValidateOnlyTests.cs`; revert validateOnly likewise.
Verify: run the whole GoogleAds test project; then collect coverage (`--collect:"XPlat Code Coverage"`) and confirm handler line-rate >= 0.95 (RevertAsync fully covered). Run `dotnet format backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --verify-no-changes` and `dotnet build`. Commit: `test(google-ads): negative keyword revert and routing cases`.
Process docs: no behaviour change, so bump `verified_at` in `docs/processes/flow-google-ads-action-execution.md` only if the CI process-docs check requires it, then `python3 scripts/process-docs/check.py index`.
PR note: report that the issue's 0% figure was not reproducible (baseline 95%).
