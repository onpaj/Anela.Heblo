### task: add-410-resync-tests
**Files:** `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`

Add four tests following the existing arrange style (root with `DeltaLink = "https://graph/stale"`, `DriveId = "drive-1"`, `RootItemId = "root-item-1"`):
- [ ] `ExecuteAsync_Graph410WithStoredDeltaLink_RetriesWithoutTokenAndPersistsNewDeltaLink`: first `GetDeltaAsync("drive-1","root-item-1","https://graph/stale",_)` throws `new HttpRequestException("Gone", null, HttpStatusCode.Gone)`; second with `null` returns `GraphDeltaResult { Items = [], NewDeltaLink = "new-link" }`. Assert `root.DeltaLink == "new-link"`, `LastIndexedAt` set, `GetDeltaAsync` called once with the stale token and once with null.
- [ ] `ExecuteAsync_Graph410WithoutStoredDeltaLink_DoesNotRetry`: `DeltaLink = null`, call with null throws Gone; verify exactly one call; `root.DeltaLink` stays null.
- [ ] `ExecuteAsync_NonGoneHttpErrorWithStoredDeltaLink_DoesNotRetryOrClearToken`: throws 500; verify one call and `root.DeltaLink == "https://graph/stale"`.
- [ ] `ExecuteAsync_Graph410OnRetry_StopsAfterTwoCallsAndDoesNotThrow`: both calls throw Gone; `Func<Task>` over `ExecuteAsync` does not throw; exactly two calls.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter PhotobankIndexJobTests`; the first test must FAIL before task 2 (others may already pass).

