# Code Review: move-access-token-to-authorization-header

## Summary
The implementation correctly moves the Meta Ads access token from a URL query parameter to the `Authorization: Bearer` header, matches every step in the task context (including the fresh-`HttpRequestMessage`-per-retry-attempt detail called out as important for Polly compatibility), and deletes the now-dead `RedactToken` helper. All 7 tests in `MetaAdsTransactionSourceTests` pass, the solution builds with 0 errors, and `dotnet format` reports no issues.

## Review Result: PASS

### task: move-access-token-to-authorization-header
**Status:** PASS

## Docs to Update
(None — this is an internal adapter implementation detail with no public API, CLI, or operational surface change.)

## Overall Notes
- Verified the full `Anela.Heblo.Tests` project run shows no regressions: the only failures present are pre-existing Docker/Testcontainers-dependent integration tests (this sandbox has no Docker available), none of which reference `MetaAdsTransactionSource` or its collaborators.
- The task context's step 5 said "8 tests"; the file has 7. This was already flagged as a pre-existing discrepancy by the prior task (`add-bearer-auth-regression-tests`), not something this task introduced, so it is not a compliance issue.
