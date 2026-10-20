# Implementation: add-bearer-auth-regression-tests

## What was implemented

Extended the test double used for multi-request capture (`CapturingSequentialHandler`) so tests can assert on the `Authorization` header of every outbound request, and added failing (RED) assertions in `GetTransactionsAsync_PaginationUrl_UsedVerbatim` that pin down the target behavior (no `access_token` query parameter, `Authorization: Bearer <token>` header on every request) ahead of the production-code fix in the next task.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` — added `using System.Net.Http.Headers;`; extended `CapturingSequentialHandler` with a `capturedAuthHeaders` list and constructor parameter; updated the `GetTransactionsAsync_InitialUrl_ContainsTimeRange` call site to pass the new list; rewrote `GetTransactionsAsync_PaginationUrl_UsedVerbatim` to assert no URL contains `access_token=` and every captured request carries `Authorization: Bearer test-token`; removed the stale `&access_token=test-token` fragment from the `paging.next` fixture in `GetTransactionsAsync_Pagination_AllPagesCollected`.

## Tests

- `MetaAdsTransactionSourceTests.cs` — ran the full file via `dotnet test --filter "FullyQualifiedName~MetaAdsTransactionSourceTests"`.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MetaAdsTransactionSourceTests"
```

Confirmed RED: `GetTransactionsAsync_PaginationUrl_UsedVerbatim` FAILS (the initial URL still contains `&access_token=test-token` and no `Authorization` header is set yet — production code untouched). Result: `Failed! - Failed: 1, Passed: 6, Skipped: 0, Total: 7`. This exactly matches the task context's expected RED state; production code for FR-1 remains outstanding for the next task (`move-access-token-to-authorization-header`).

## Notes

No deviations from the task context. The task-plan's step 6 expected 8 tests in the file; the actual count is 7 (there is no separate 8th test in the current file) — not a discrepancy introduced by this task, just the file's existing test count.

## PR Summary
Added regression test coverage (RED, per TDD) pinning down that the Meta Ads access token must be sent via an `Authorization: Bearer` header rather than a URL query parameter. No production code was touched in this task — only the test double and assertions.

### Changes
- `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` — capture and assert on the `Authorization` header; assert `access_token` never appears in request URLs; removed stale `access_token` fixture value

## Status
DONE
