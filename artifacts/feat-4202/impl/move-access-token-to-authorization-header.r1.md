# Implementation: move-access-token-to-authorization-header

## What was implemented

Stopped sending the Meta Ads access token as a URL query parameter and switched to sending it via the HTTP `Authorization: Bearer <token>` header instead. Removed the now-unused `RedactToken` helper, since the token no longer appears in URLs that get logged.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`
  - Added `using System.Net.Http.Headers;`.
  - `BuildInitialUrl`: removed the `&access_token={_settings.AccessToken}` fragment.
  - `FetchPageAsync`: builds a fresh `HttpRequestMessage` per pipeline attempt (inside the `_pipeline.ExecuteAsync` lambda, not hoisted out), sets `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken)`, and sends via `_httpClient.SendAsync(request, innerCt)` instead of `GetAsync(url, innerCt)`. The debug log now logs the raw `url` directly (no longer needs redaction since it never contains the token).
  - Deleted `RedactToken` (no remaining references).

## Tests

- `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` — all 7 tests in `MetaAdsTransactionSourceTests` pass, including `GetTransactionsAsync_PaginationUrl_UsedVerbatim` (asserts no `access_token=` in any request URL and `Authorization: Bearer test-token` on every request) and `GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt` (confirms the fresh-`HttpRequestMessage`-per-attempt approach doesn't break Polly retry, since a single `HttpRequestMessage` can't be sent twice).

  ```
  Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 29 ms - Anela.Heblo.Tests.dll (net8.0)
  ```

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MetaAdsTransactionSourceTests"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --no-restore
```

## Notes

- `dotnet build Anela.Heblo.sln` succeeds with 0 errors (91 pre-existing warnings, none introduced by this change).
- `dotnet format Anela.Heblo.sln --no-restore` reports no changes needed.
- Ran the full `Anela.Heblo.Tests` project. This sandbox has no Docker available, so all Testcontainers-backed integration tests (e.g. `Leaflet.Integration.*`, which spin up Postgres via Testcontainers) fail with `System.ArgumentException: Docker is either not running or misconfigured` — this is a pre-existing environment limitation unrelated to this change (also documented in `memory/context/state.md` from an earlier branch). A completed full run showed `Failed: 110, Passed: 7159, Skipped: 4, Total: 7273`, and every failure sampled was this same Docker/Testcontainers error; none referenced `MetaAdsTransactionSource`, `RedactToken`, `BuildInitialUrl`, or `FetchPageAsync`. No regressions attributable to this change.
- The task-context's step 5 mentioned "8 tests" in the file; the file actually contains 7 (already noted as a pre-existing discrepancy by the prior task `add-bearer-auth-regression-tests`, not introduced here).

## PR Summary

The Meta Ads access token was previously placed in the request URL as an `access_token` query parameter, which risked the token leaking into logs, proxies, or browser history. This change moves it to a standard `Authorization: Bearer <token>` header instead, matching how the token is meant to be transmitted, and removes the `RedactToken` log-scrubbing helper that existed only to work around the old approach.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` — send the access token via the `Authorization` header instead of a URL query parameter; delete the now-unused `RedactToken` helper

## Status
DONE
