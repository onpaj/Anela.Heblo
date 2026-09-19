# Architecture Review: Extract Helper Methods from `GraphService.GetAppRoleMembersAsync`

## Skip Design: true
Pure backend private-method extraction inside `Anela.Heblo.Adapters.Microsoft365`. No controller, MCP tool, DTO, endpoint, or frontend surface changes. Nothing visual or interface-facing to design.

## Architectural Fit Assessment

This aligns cleanly with the precedent already set by finding #2630 (`GetGroupMembersAsync`, now ~88 lines) and with an almost identical prior extraction elsewhere in the codebase: `feat-4200` (`artifacts/feat-4200/task-plan.r1.md`) moved four sequential private methods out of `GetPurchaseStockAnalysisHandler` into `StockAnalysisCalculator`, one TDD-guarded step at a time, each step verbatim-moving code and re-running the full test suite before committing. This issue is architecturally simpler than that one: everything stays `private` on the same class (`GraphService`), there is no new service/interface to introduce, and no DI registration changes.

Verified during exploration (not assumed):
- `GetAppRoleMembersAsync` has **no MCP tool wrapper** — `docs/integrations/mcp-server.md` has zero references to `GetAppRoleMembers` or `UserManagement`. Nothing to update there.
- The only production caller of `IGraphService.GetAppRoleMembersAsync` is `EntraAccessUserSourceAdapter` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`), which calls through the interface — unaffected by a `private`-only refactor.
- `MockGraphService` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`) is a separate implementation of `IGraphService`, not a subclass of `GraphService` — unaffected.
- `IGraphService` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`) declares only the two public methods; no change needed or permitted here.

## Specification Amendment — regression coverage is broader than spec.r1.md stated

The spec's Background section names only `GetAppRoleMembersTests.cs` (3 tests) as the regression guard. Exploration found a **second, more detailed test class** exercising the exact same method with 6 additional tests in `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`:

| Test | What it locks down |
|---|---|
| `GetAppRoleMembersAsync_SingleUser_IssuesOneBatchCall` | Exact HTTP call count (3: SP → assignments → batch) and exact `$batch` request body/URL/method for a single assignee |
| `GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls` | `GraphBatchSize` chunking produces exactly 2 batch calls (20 + 1) with exact per-request array lengths — **this pins the batching to happen after all 21 assignees are collected, not per-page** |
| `GetAppRoleMembersAsync_NonTwoHundredSubResponse_SkipsUserAndLogsWarning` | Exact warning log text `"Could not resolve user"` on a non-200 batch sub-response, and that the method continues (doesn't abort) |
| `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError` | Exact error log text `"Graph $batch request failed"` and empty-list return on 500 from `$batch` |
| `GetAppRoleMembersAsync_TokenAcquisitionMsalException_Throws` | `GraphServiceAuthException` thrown when token acquisition throws `MsalException`, **and `IHttpClientFactory.CreateClient` is never called** — token acquisition must stay strictly before any HTTP-client creation |
| `GetAppRoleMembersAsync_TransportThrows_Throws` | A raw `HttpRequestException` from the transport layer (not an HTTP error status) propagates **unwrapped** — helpers must not add their own try/catch around `SendAsync` calls that would swallow or rewrap transport exceptions |

**This changes NFR-2 and FR-6's acceptance criteria: both `GetAppRoleMembersTests.cs` (3 tests) and `GraphServiceTests.cs`'s 6 `GetAppRoleMembersAsync_*` tests (9 total) must pass unmodified.** The two files use different `BuildService`/`BuildServiceSequential` helper harnesses but both drive the same public method through a fake `HttpMessageHandler`, so neither constrains internal method structure — the extraction is safe. The important constraints these tests add on top of the spec:

1. **Do not wrap extracted helpers' `HttpClient.SendAsync` calls in their own try/catch that could turn a transport exception into a different type or an empty-list return.** The current code has no such try/catch around individual `SendAsync` calls (only success/failure is checked via `IsSuccessStatusCode`); extraction must preserve that — a thrown `HttpRequestException` from `SendAsync` itself must still propagate all the way out of `GetAppRoleMembersAsync` uncaught (until the outer `catch (Exception ex)` re-throws it, per the existing `catch (Exception ex) { ...; throw; }` block at the end of the method).
2. **Token acquisition must remain the first HTTP-adjacent action, strictly before `httpClient` is created/used.** `ResolveServicePrincipalAsync` (FR-1) must receive the already-acquired `graphToken` as a parameter — it must not itself call `_tokenAcquisition`. This matches the spec's proposed signature already, just calling it out explicitly since a test enforces it.
3. **Batching happens once, after the full paginated assignment collection completes** (all pages walked, all groups expanded) — not per-page and not per-group. FR-5's `BatchResolveUserDtosAsync` must be called exactly once, after FR-3 and FR-4 finish, over the complete `directUserIds` set. This is already implied by spec FR-6's ordering but is now verified by a concrete test (`TwentyOneUsers_IssuesTwoBatchCalls`), so implementers must not "helpfully" batch-resolve inside the assignment-collection loop.

No other spec content needs correction. FR-1 through FR-6 and the Out of Scope section are accurate.

## Proposed Architecture

### Component Overview

```
GraphService (class, unchanged public surface: IGraphService)
│
├─ GetGroupMembersAsync(...)            [public, unchanged — extracted under #2630]
│
└─ GetAppRoleMembersAsync(appRoleValue) [public — becomes a thin orchestrator]
   ├─ guard clauses (empty appRoleValue, cache hit)           [unchanged, stays inline]
   ├─ AcquireGraphTokenAsync(...)                              [existing private helper, unchanged]
   ├─ ResolveServicePrincipalAsync(clientId, token, http, ct)  [NEW private — FR-1]
   ├─ FindAppRoleId(appRoles, appRoleValue)                    [NEW private, sync — FR-2]
   ├─ CollectRoleAssigneesAsync(spId, appRoleId, token, http, ct) [NEW private — FR-3]
   ├─ foreach (groupId in groupIdsToExpand) GetGroupMembersAsync(groupId, ct)  [UNCHANGED inline loop — FR-4, do not extract]
   ├─ BatchResolveUserDtosAsync(userIds, token, http, ct)      [NEW private — FR-5]
   └─ cache.Set(...) + return                                  [unchanged, stays inline]
```

### Key Design Decisions

#### Decision 1: Failure signaling — nullable/tuple return, not exceptions
**Options considered:**
- (A) Each helper returns a nullable/`(bool Success, T Value)` result on failure; the orchestrator checks it and does the *same* `_logger.LogError`/`LogWarning` + `return new List<UserDto>()` that the monolithic method does today.
- (B) Each helper does its own logging internally and returns `null`/empty as a sentinel; orchestrator just checks for the sentinel and returns early (no logging duplicated at the call site).
- (C) Helpers throw a new custom exception on failure; orchestrator catches it.

**Chosen approach:** (B) — each helper logs its own failure(s) internally (using the exact existing message templates and log levels) and returns `null` (for `ResolveServicePrincipalAsync`, `FindAppRoleId`, `BatchResolveUserDtosAsync`) or a sentinel value the orchestrator recognizes as failure. `CollectRoleAssigneesAsync` returns `(HashSet<string>? DirectUserIds, List<string>? GroupIdsToExpand)` — both null together signal failure on any page.

**Rationale:** (B) keeps the logging colocated with the HTTP call that produces the log data (status code, response body) — the same locality the original inline code already has. (A) would require passing enough context (status, body) back up to the orchestrator just to log there, adding parameters for no benefit. (C) is rejected per spec NFR-2: these are expected/loggable-and-return paths in the current design, not exceptional — introducing exceptions here is an intentional behavior change the spec explicitly excludes, and it would require rewriting the `GraphServiceTests.cs` assertions that check for an empty-list return (not a caught exception) on `BatchLevelFailure` and `NonTwoHundredSubResponse`.

#### Decision 2: `FindAppRoleId` is synchronous, not `Task<T>`
**Options considered:**
- (A) `private static string? FindAppRoleId(JsonElement appRoles, string appRoleValue)` — synchronous, no I/O.
- (B) `private async Task<string?> FindAppRoleIdAsync(...)` — matches the issue's suggested name literally.

**Chosen approach:** (A). The issue's suggested names (`FindAppRoleId`) are illustrative; the step itself (lines 249–261) is a pure in-memory loop over already-fetched JSON with no `await`. Making it `async Task<T>` would need a `Task.FromResult` wrapper or an unnecessary `await` — needless ceremony. `AcquireGraphTokenAsync` is `async` because it awaits `_tokenAcquisition`; this step awaits nothing.

**Rationale:** Matches existing codebase convention (see `ParseMembersFromJson` in the same file — a synchronous `internal static` helper doing pure JSON-to-DTO work, not `async`). Also directly addressed by spec FR-2, which already allows this.

#### Decision 3: `ResolveServicePrincipalAsync` returns both `spId` and the raw `appRoles` JsonElement, not a pre-parsed DTO
**Options considered:**
- (A) Return `(string? SpId, JsonElement? AppRoles)` — caller passes `AppRoles` straight into `FindAppRoleId`.
- (B) Introduce a small internal DTO/record (e.g. `private record ServicePrincipalInfo(string SpId, JsonElement AppRoles)`).
- (C) Return only `spId`, and have `FindAppRoleId` re-parse `spJson` itself (re-fetch or re-parse the same JSON document from a captured string).

**Chosen approach:** (A) tuple. (B) is acceptable too if the implementer prefers a named type over a tuple for readability — either is fine, this is a style choice with no behavioral difference. (C) is rejected: re-parsing means either keeping the raw JSON string alive as an extra parameter (duplicated data already available as a `JsonElement`) or re-fetching, which would double the Graph call — a real behavior/performance regression.

**Rationale:** `JsonElement` is a lightweight struct view over the parent `JsonDocument`; passing it onward is idiomatic `System.Text.Json` usage already used elsewhere in this file (e.g., `appRolesEl.EnumerateArray()` in the original code operates the same way). One caveat implementers must handle: `JsonDocument` is `IDisposable` and the original code uses `using var spDoc = ...`. The `using` must be scoped so that `spDoc` stays alive for as long as any `JsonElement` derived from it (`appRoles`) is still being read — i.e., `ResolveServicePrincipalAsync` cannot dispose `spDoc` before returning `appRoles` to the caller if the caller will still enumerate it. Two safe options: keep the `using var spDoc` in the *orchestrating* method (fetch the JSON there, call a synchronous parse-only sub-step), or have `ResolveServicePrincipalAsync` return `appRoles.Clone()` (an owned, disconnected `JsonElement` per the `System.Text.Json` API) before its own local `spDoc` goes out of scope. **Recommend `.Clone()`** — it's a one-line fix, keeps `ResolveServicePrincipalAsync` self-contained (owns its `using`), and avoids a dangling-`JsonDocument` bug that unit tests exercising only the public method would not reliably catch (since `JsonElement` reads on a disposed `JsonDocument` throw `ObjectDisposedException`, not silently return wrong data — a bug here would fail loudly in the happy-path test, but it's cheaper to get it right by construction).

#### Decision 4: Keep the group-expansion loop (FR-4) un-extracted, exactly as the issue instructs
**Options considered:**
- (A) Leave the `foreach (var groupId in groupIdsToExpand) { ... }` loop inline in the orchestrator (issue's explicit instruction).
- (B) Extract it into `ExpandGroupsAsync` for full symmetry with the other four steps.

**Chosen approach:** (A), per the issue text: *"The recursive `GetGroupMembersAsync` call in step 5 stays as-is."*

**Rationale:** The issue author already made this call — it's a 6-line loop delegating to an already-extracted, already-tested public method (`GetGroupMembersAsync`), so extracting it adds a layer of indirection with no testability gain (you'd just be testing that the loop calls the mock the right number of times, which the existing end-to-end tests already implicitly cover). Overriding an explicit instruction in the issue would be scope creep.

## Implementation Guidance

### Directory / Module Structure

No new files. Single file modified:

```
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs   [MODIFIED — add 4 new private methods, shrink GetAppRoleMembersAsync body]
```

No test files are *required* to change (both existing suites call only the public method and assert on its behavior), but implementers must run both after each extraction step:
```
backend/test/Anela.Heblo.Tests/Features/UserManagement/GetAppRoleMembersTests.cs   [must pass unmodified — 3 tests]
backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs        [must pass unmodified — 6 GetAppRoleMembersAsync_* tests + other unrelated tests in this file]
```

### Interfaces and Contracts

All four new members are `private` on `GraphService` — no interface changes. Suggested signatures (implementer may adjust return-type shape per Decision 1/3, but must preserve the described behavior):

```csharp
private async Task<(string? SpId, JsonElement? AppRoles)> ResolveServicePrincipalAsync(
    string clientId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)

private static string? FindAppRoleId(JsonElement appRoles, string appRoleValue)

private async Task<(HashSet<string>? DirectUserIds, List<string>? GroupIdsToExpand)> CollectRoleAssigneesAsync(
    string spId, string appRoleId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)

private async Task<List<UserDto>?> BatchResolveUserDtosAsync(
    IReadOnlyCollection<string> userIds, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)
```

`GetAppRoleMembersAsync` orchestration shape after extraction (illustrative — exact log/return statements per step come from the original code, moved verbatim into the corresponding helper per Decision 1):

```csharp
public async Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default)
{
    // unchanged: empty-value guard, cache-hit guard, clientId-missing guard (lines 195–212)
    try
    {
        var graphToken = await AcquireGraphTokenAsync... // unchanged try/catch around token acquisition (lines 214–225)
        var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");

        var (spId, appRoles) = await ResolveServicePrincipalAsync(clientId, graphToken, httpClient, cancellationToken);
        if (spId is null) return new List<UserDto>();

        var appRoleId = FindAppRoleId(appRoles!.Value, appRoleValue);
        if (appRoleId is null)
        {
            _logger.LogWarning("App role '{RoleValue}' not found on service principal {SpId}", appRoleValue, spId);
            return new List<UserDto>();
        }

        var (directUserIds, groupIdsToExpand) = await CollectRoleAssigneesAsync(spId, appRoleId, graphToken, httpClient, cancellationToken);
        if (directUserIds is null) return new List<UserDto>();

        foreach (var groupId in groupIdsToExpand!)
        {
            var members = await GetGroupMembersAsync(groupId, cancellationToken);
            foreach (var member in members)
                if (!string.IsNullOrEmpty(member.Id))
                    directUserIds.Add(member.Id);
        }

        var users = await BatchResolveUserDtosAsync(directUserIds.ToList(), graphToken, httpClient, cancellationToken);
        if (users is null) return new List<UserDto>();

        _cache.Set(cacheKey, users, _cacheExpiration);
        _logger.LogInformation("Resolved {Count} app role members for role '{RoleValue}'", users.Count, appRoleValue);
        return users;
    }
    catch (GraphServiceAuthException) { throw; }
    catch (Exception ex) { _logger.LogError(ex, ...); throw; }
}
```

Note the "app role not found" warning (currently lines 262–266) stays in the orchestrator per spec FR-2, since it needs both `appRoleId == null` and `spId` (for the log message) together — moving it into `FindAppRoleId` would require passing `spId` in just to log it, which is a reasonable alternative too if the implementer prefers; either placement is acceptable.

### Data Flow
No change to the runtime data flow (Graph API call sequence, pagination, batching) — this refactor is a pure code-structure change. Sequence diagram is unchanged from today: token → SP lookup → role-id lookup → paginated assignment walk → group expansion (recursive `GetGroupMembersAsync`) → batched user resolution → cache → return.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `JsonDocument`/`JsonElement` lifetime bug: `appRoles` read after its parent `JsonDocument` is disposed | Medium | Use `.Clone()` on the returned `JsonElement` before the local `using var spDoc` goes out of scope inside `ResolveServicePrincipalAsync` (Decision 3). Covered indirectly by the happy-path tests in both test files (would throw `ObjectDisposedException` if mishandled — a loud, easy-to-catch failure during implementation, not silent data corruption). |
| Accidentally wrapping an extracted helper's `SendAsync` in a new try/catch that changes exception propagation | Medium | `GetAppRoleMembersAsync_TransportThrows_Throws` in `GraphServiceTests.cs` fails immediately if this happens — run it after every extraction step, not just at the end. |
| Re-ordering token acquisition after `httpClient` creation, or vice versa, during the move | Low | `GetAppRoleMembersAsync_TokenAcquisitionMsalException_Throws` asserts `factoryMock.Verify(f => f.CreateClient(...), Times.Never)` — catches this immediately. |
| Batching accidentally happening per-page or per-group instead of once over the full assignee set | Medium | `GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls` asserts exact `handler.Requests.Should().HaveCount(4)` (2 SP/assignment calls + 2 batch calls) — any extra or misplaced batch call changes this count and fails the test. |
| Verbatim-move introduces a subtle diff (e.g., a `??` vs `?.` change, or a reordered null-check) that changes behavior in an edge case not covered by any test | Low | Move code verbatim (copy-paste, then adjust only the receiver of calls that move from local variables to helper return values), never retype logic by hand. `dotnet format` after each step to catch only formatting drift, then diff-review the extraction commit before moving to the next step. |
| `dotnet build`/`dotnet format` catches unused `using` directives or now-dead local variables in `GetAppRoleMembersAsync` after extraction | Low | Run `dotnet build` and `dotnet format` after each task, not just at the end, per project validation rules (`CLAUDE.md` → Validation before completion). |

## Specification Amendments

1. **Background / regression-coverage correction (see above section).** `GraphServiceTests.cs`'s 6 `GetAppRoleMembersAsync_*` tests are additional, stronger regression coverage beyond the 3 tests in `GetAppRoleMembersTests.cs`. Both files (9 tests total) must pass unmodified. FR-6's acceptance criteria and NFR-1/NFR-2 should be read as covering both files.
2. **FR-1 / Decision 3 clarification.** The spec's suggested `ResolveServicePrincipalAsync` return shape should explicitly return a `JsonElement` that outlives the helper's own `JsonDocument` (via `.Clone()` or by keeping the `using` scope in the caller) — the spec did not call out this lifetime concern; this review adds it as an explicit implementation requirement (see Decision 3 and the first Risk row).
3. **No other amendments.** FR-2 through FR-5, NFR-1, NFR-3, NFR-4, Data Model, API/Interface Design, Dependencies, and Out of Scope in `spec.r1.md` are accurate as written and require no changes.

## Prerequisites
None. No migrations, no config, no infrastructure changes, no DI registration changes (all new methods are `private`). Implementation can start immediately against the current `main`/feature branch state.
