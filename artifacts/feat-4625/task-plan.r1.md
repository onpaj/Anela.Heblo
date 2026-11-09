# Implementation Plan: Photobank delta 410 recovery

Backend-only. TDD: tests first, then implementation, then docs.

### task: add-410-resync-tests
**Files:** `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`

Add four tests following the existing arrange style (root with `DeltaLink = "https://graph/stale"`, `DriveId = "drive-1"`, `RootItemId = "root-item-1"`):
- [ ] `ExecuteAsync_Graph410WithStoredDeltaLink_RetriesWithoutTokenAndPersistsNewDeltaLink`: first `GetDeltaAsync("drive-1","root-item-1","https://graph/stale",_)` throws `new HttpRequestException("Gone", null, HttpStatusCode.Gone)`; second with `null` returns `GraphDeltaResult { Items = [], NewDeltaLink = "new-link" }`. Assert `root.DeltaLink == "new-link"`, `LastIndexedAt` set, `GetDeltaAsync` called once with the stale token and once with null.
- [ ] `ExecuteAsync_Graph410WithoutStoredDeltaLink_DoesNotRetry`: `DeltaLink = null`, call with null throws Gone; verify exactly one call; `root.DeltaLink` stays null.
- [ ] `ExecuteAsync_NonGoneHttpErrorWithStoredDeltaLink_DoesNotRetryOrClearToken`: throws 500; verify one call and `root.DeltaLink == "https://graph/stale"`.
- [ ] `ExecuteAsync_Graph410OnRetry_StopsAfterTwoCallsAndDoesNotThrow`: both calls throw Gone; `Func<Task>` over `ExecuteAsync` does not throw; exactly two calls.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter PhotobankIndexJobTests`; the first test must FAIL before task 2 (others may already pass).

### task: implement-410-resync
**Files:** `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`

- [ ] Add `using System.Net;`.
- [ ] Add private helper `GetDeltaWithResyncAsync(PhotobankIndexRoot root, CancellationToken ct)` exactly as in arch-review (catch `HttpRequestException` when `StatusCode == HttpStatusCode.Gone && !string.IsNullOrEmpty(root.DeltaLink)`; warn-log with root id only; set `root.DeltaLink = null`; single retry with null token).
- [ ] Replace the `_graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct)` call in `IndexRootAsync` with `await GetDeltaWithResyncAsync(root, ct)`.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter Photobank`; all pass.
- [ ] Run `dotnet build` and `dotnet format` on the backend.

### task: update-process-doc
**Files:** `docs/processes/sync-photobank-index.md`

- [ ] Flow step 2: add that a 410 Gone on a stored `DeltaLink` clears it and retries once as a full enumeration.
- [ ] Replace the "expired or invalid DeltaLink blocks the root" quirk: now auto-recovers (once per run); note the full walk reports no deletions so photos deleted while the token was stale stay indexed; note retry failure is logged as `Failed to index root`.
- [ ] Add to Runtime facts/history: Graph 410 on stored token observed in telemetry 2026-11-03..07 (issue #4625).
- [ ] Bump `verified_at` and run `python3 scripts/process-docs/check.py index`.
