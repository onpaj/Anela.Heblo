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
