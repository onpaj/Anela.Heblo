# Implementation: revert-and-routing-tests (r1)

Extended `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsNegativeKeywordHandlerTests.cs` (test-only; no production code changed).

Added: revert with Failed original + non-null resource name; Succeeded original with null id; invalid payload (empty text) on revert; ad-group revert refusing campaignCriteria / wrong parent; ad-group `SucceedNextMutateWithoutResourceName` -> Failed "verify in the account"; ValidateOnly execute and revert at Campaign and AdGroup level (one validateOnly mutation, correct service, marker error, nothing changed).

Verification: GoogleAds test project 232 passed / 1 skipped (smoke, pre-existing); coverage of GoogleAdsNegativeKeywordHandler line-rate = 1.0 (>= 0.95); `dotnet format --verify-no-changes` clean; build OK.
Process docs: no behaviour change, no doc edit required.
PR note: the issue's 0% coverage figure was not reproducible (baseline 95%).
