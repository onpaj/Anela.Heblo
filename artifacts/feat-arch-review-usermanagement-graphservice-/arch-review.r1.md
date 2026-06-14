# Architecture Review: Refactor `GraphService.GetGroupMembersAsync` into Focused Helper Methods

## Skip Design: true

## Architectural Fit Assessment

This is a **strictly internal, behaviour-preserving refactor** of one method inside an existing Application-layer service. It does not touch any architectural seam: the public `IGraphService` contract, the `UserManagement` module boundary, MediatR handlers (`GetGroupMembersHandler`), `UserDto`, and the `MicrosoftGraph` named `HttpClient` registration all remain untouched. There is no new module, no new DI registration, no new file under `Features/UserManagement/`.

The refactor aligns well with existing patterns:
- **File organisation** — `GraphService.cs` already lives in the correct place per `docs/architecture/filesystem.md` (`Features/UserManagement/Services/`). Helpers stay inside the same file as private members; no new files are warranted (KISS, YAGNI).
- **Coding standards** — the global C# rules (`csharp-coding-style.md`, `coding-style.md`) call out the 50-line function ceiling, single-responsibility, and immutability. The current method violates the first two; the refactor fixes both without introducing mutation.
- **Existing test patterns** — `GraphServiceTests` already exercises the HTTP path through `FakeHttpMessageHandler`. A new `ParseMembersFromJsonTests` fixture is the natural complement and matches the project's xUnit + FluentAssertions convention.

The one integration point worth naming: `GetAppRoleMembersAsync` (same file, lines 267–418) **calls `GetGroupMembersAsync` directly** at line 382 to expand nested-group members. The orchestrator's public signature and behaviour must remain identical or `GetAppRoleMembersAsync` regresses silently — the spec already requires this, but it is the single highest-stakes invariant.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ GraphService (class, unchanged surface)                              │
│                                                                       │
│  public GetGroupMembersAsync(groupId, ct)  ◄── orchestrator (~30 LOC)│
│    │                                                                  │
│    ├─► IMemoryCache.TryGetValue / Set      (inline, unchanged)       │
│    │                                                                  │
│    ├─► AcquireGraphTokenAsync(groupId, ct)  ◄── NEW private instance │
│    │     └─► ITokenAcquisition.GetAccessTokenForAppAsync             │
│    │     └─► MsalException → rethrow (caught by outer handler)       │
│    │                                                                  │
│    ├─► HttpClient.SendAsync (inline, unchanged — see Out of Scope)   │
│    │                                                                  │
│    └─► ParseMembersFromJson(responseBody)   ◄── NEW private static   │
│          └─► JsonDocument walk + @odata.type filter → List<UserDto>  │
└──────────────────────────────────────────────────────────────────────┘
```

No public surface changes. No new types. No new DI registrations.

### Key Design Decisions

#### Decision 1: Keep helpers as private members of `GraphService`, not separate classes
**Options considered:**
- (A) Private helpers inside `GraphService.cs` — chosen.
- (B) Extract `ParseMembersFromJson` into a separate internal static class `GraphMembersParser` in `Features/UserManagement/Services/`.
- (C) Extract a full `IGraphTokenProvider` interface and `GraphMemberParser` interface for DI.

**Chosen approach:** (A).

**Rationale:** The brief and spec are explicit that this is **strictly internal**, two extractions only. Option (B) would create a one-call-site class for marginal benefit; option (C) is speculative DI generality (YAGNI). The parser remains testable as a `private static` via a tiny internal accessor *only if needed* — but the simpler path is to ship the parser as `private static`, write `ParseMembersFromJsonTests` against it via `InternalsVisibleTo` if absolutely required, or **prefer to make the parser `internal static` on a nested or partner static class only if the test project cannot otherwise reach it.** Given `GraphServiceTests` lives in `backend/test/Anela.Heblo.Tests`, check `Anela.Heblo.Application.csproj` for an existing `InternalsVisibleTo` to that test assembly; if present, declare the parser `internal static` on `GraphService`. If absent, the cleanest minimal-impact move is to mark the parser `internal static` and add the `InternalsVisibleTo` entry. **Do not change visibility to `public` solely for testing** — that violates the "no public surface change" requirement in FR-3.

#### Decision 2: `AcquireGraphTokenAsync` stays instance, not static
**Options considered:** Instance method (captures `_tokenAcquisition`, `_logger` via `this`) vs static method taking dependencies as parameters.

**Chosen approach:** Instance method.

**Rationale:** It depends on two injected fields and needs no parameters beyond the cancellation context. Making it static would force a longer parameter list with no testability gain — `AcquireGraphTokenAsync` is exercised through existing token-path tests (`GetGroupMembersAsync_TokenAcquisitionMsalException_Throws`), which already inject mocks at the constructor.

#### Decision 3: Do NOT extract HTTP send block in this iteration
**Options considered:** Extract a third helper `SendGraphRequestAsync` covering lines 84–104; defer it.

**Chosen approach:** Defer (spec Out of Scope).

**Rationale:** The brief says "exactly two extractions." Three would expand scope and risk subtle behaviour changes (e.g. who reads `errorContent`, where status logging lives). Leaving the HTTP block inline keeps the diff focused and reviewable. A follow-up arch-review can decide whether `SendGraphRequestAsync` carries its weight, especially given `SearchUsersAsync` and `GetAppRoleMembersAsync` duplicate the same pattern — that's the right time to extract, when there are three call sites instead of one.

#### Decision 4: MSAL exception handling must move with the token call, not stay in the outer `try/catch`
**Options considered:**
- (A) `AcquireGraphTokenAsync` does not catch `MsalException`; the existing outer `catch (MsalException msalEx)` block at line 168 handles it as today.
- (B) `AcquireGraphTokenAsync` catches `MsalException`, logs, and rethrows.

**Chosen approach:** (A).

**Rationale:** The current code does **not** catch and return null on `MsalException` — it lets the exception propagate to the outer catch at line 168, which logs and **rethrows**. The spec's FR-1 says "Catches `MsalException` and returns `null`," but the source code disagrees: today, the call fails the whole method with a rethrow. **Treat the spec language as an inaccuracy** (see Specification Amendments below) — preserving today's behaviour means the helper must NOT swallow `MsalException`. The outer catch at line 168 stays in `GetGroupMembersAsync` and continues to log + rethrow.

## Implementation Guidance

### Directory / Module Structure

No new files. All changes are within:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs        (no changes, must still pass)
backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs  (NEW)
```

If `InternalsVisibleTo("Anela.Heblo.Tests")` is not already declared on `Anela.Heblo.Application`, add it in `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` (or via `AssemblyInfo.cs`) — this is the only project-file change permitted by the refactor.

### Interfaces and Contracts

**Unchanged:**
- `IGraphService.GetGroupMembersAsync(string, CancellationToken)` — exact signature preserved.
- `UserDto` — no field changes.
- `GetGroupMembersHandler` / `GetGroupMembersRequest` / `GetGroupMembersResponse` — untouched.

**New (internal to `GraphService.cs`):**
```csharp
private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken);
internal static List<UserDto> ParseMembersFromJson(string json);
```

Note the token helper returns `string` (non-nullable), not `string?` as the spec suggests. The current code at line 70 assigns the result to `var graphToken` and the compiler does not flag a null dereference at line 85 — `GetAccessTokenForAppAsync` is declared as returning `Task<string>`. The spec's "returns `null` … with whatever sentinel the current code uses" is incorrect; there is no sentinel. The helper should mirror today's return type: `Task<string>`.

### Data Flow

For the cache-miss path:

```
caller → GetGroupMembersAsync(groupId, ct)
       │
       ├── _cache.TryGetValue(cacheKey) ──► HIT: return cached, done
       │
       ├── validate groupId ──► empty: return empty list, done
       │
       ├── graphToken = await AcquireGraphTokenAsync(groupId, ct)
       │     ├── stopwatch start
       │     ├── _tokenAcquisition.GetAccessTokenForAppAsync(scope)
       │     │     └── on MsalException: propagates → outer catch logs+rethrows
       │     ├── stopwatch stop, log duration
       │     └── return token
       │
       ├── httpClient = _httpClientFactory.CreateClient("MicrosoftGraph")
       ├── build request with Bearer header
       ├── response = await httpClient.SendAsync(request, ct)
       ├── if !IsSuccessStatusCode: log + return empty list, done
       ├── responseBody = await response.Content.ReadAsStringAsync(ct)
       │
       ├── members = ParseMembersFromJson(responseBody)
       │     ├── JsonDocument.Parse(json) using
       │     ├── if no "value" array: return new List<UserDto>()
       │     ├── for each entry: if @odata.type contains "user" OR has userPrincipalName
       │     │     ├── extract id, displayName, mail, userPrincipalName (each null-tolerant)
       │     │     └── add UserDto { Id, DisplayName, Email = mail ?? upn ?? "" }
       │     └── return list
       │
       ├── _cache.Set(cacheKey, members, _cacheExpiration)
       └── return members
```

For the cache-hit path: returns at step 1, never touches token, HTTP, or parser — this is exercised by `GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory` and must continue to pass without modification.

**Logging-equivalence checkpoints** (FR-1 acceptance criterion):
- The "Successfully acquired MS Graph application token in {Duration}ms" message at line 72 must be emitted by `AcquireGraphTokenAsync` with the same template, level (`Information`), and `Duration` value type.
- The "Token acquired with length: {TokenLength}" message at line 75 must remain — decide whether it lives in the helper (preferred, since it logs the helper's output) or in the orchestrator (also acceptable). Either is fine as long as it fires under identical conditions.
- The "Attempting to acquire MS Graph token with application scope: {Scope}" at line 67 belongs in the helper.

**Parser-equivalence checkpoint** (FR-2 acceptance criterion):
The current parser also emits per-member `LogDebug` messages at lines 135 and 148, and aggregate `LogInformation` at lines 117 and 152. The spec does not explicitly mention these. **Preserve them inside `ParseMembersFromJson`** by passing an `ILogger` parameter, OR — preferred for testability — emit aggregate counts only and let the orchestrator log them from the returned list. Decision: **drop the per-element `LogDebug` lines and the array-length `LogInformation` line into the parser as a static-method `ILogger` parameter, since they reference `groupId` (line 153) which is not available to a pure parser.** Either keep them as `private static` taking `ILogger` and `groupId`, or move all four log statements back into the orchestrator after the parse returns (recommended — keeps the parser pure for unit testing).

If you move the logs into the orchestrator, you lose `totalMembers` vs `userMembers` distinction (the parser only returns `userMembers`). To preserve the existing log message at line 152, have `ParseMembersFromJson` return a small `(List<UserDto> users, int totalCount)` tuple — see Specification Amendments.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `GetAppRoleMembersAsync` (line 382) silently regresses because it calls the refactored `GetGroupMembersAsync` | HIGH | All existing `GraphServiceTests` must pass unchanged; add an integration smoke test if not already present that exercises the nested-group expansion path via `GetAppRoleMembersAsync`. |
| Logging output drift — message templates, levels, or structured property names diverge from current output | MEDIUM | Before merging, diff the new code's log statements line-by-line against the current ones. The spec requires "byte-for-byte equivalent" for token-success and MSAL-exception paths. |
| `ParseMembersFromJson` test access — making it `public` to satisfy NFR-3 would violate "no public surface change" in FR-3 | MEDIUM | Use `internal static` + `InternalsVisibleTo("Anela.Heblo.Tests")`. Verify the attribute exists on `Anela.Heblo.Application` before assuming it. |
| Moving aggregate logs into the parser couples it to `ILogger<GraphService>` and to `groupId`, undermining the pure-parser testability win | MEDIUM | Keep `ParseMembersFromJson` pure (no `ILogger`). Return `(List<UserDto>, int totalCount)`, log totals in the orchestrator. |
| Misreading the spec's "returns null on MsalException" as today's behaviour — risk of swallowing exceptions that today are rethrown | HIGH | Read lines 168–173 of the current implementation: `MsalException` is rethrown, not swallowed. Mirror that. |
| Cache-hit short-circuit is broken if the cache `TryGetValue` is moved inside the helper | LOW | Spec FR-4 keeps cache read/write inline in the orchestrator. Verify `GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory` and `GetGroupMembersAsync_EmptyGroupId_ReturnsEmptyList_WithoutTouchingFactory` still pass without changes. |
| Allocation regression from incidental LINQ or `ToList()` introduced during extraction | LOW | NFR-1 requires ≤5% deviation. The parser must keep the `foreach` + index-style pattern; no `Select`/`Where`. |

## Specification Amendments

The spec is largely accurate but contains three points that need correction or refinement before implementation:

1. **FR-1 token return type and MSAL handling.** The spec says `AcquireGraphTokenAsync` returns `Task<string?>` and "catches `MsalException` and returns `null` (or whatever sentinel the current code uses)." This contradicts the current implementation: `GetAccessTokenForAppAsync` returns `Task<string>` (non-nullable), there is no sentinel, and `MsalException` is **not caught** at the call site — it propagates to the outer `catch (MsalException msalEx)` at line 168, which logs and **rethrows**. **Amend FR-1**: return type is `Task<string>`; the helper does not catch `MsalException`; logging on MSAL failure remains in the outer catch block of `GetGroupMembersAsync`.

2. **FR-2 parser signature.** To preserve the aggregate log message at line 152 ("Processed {TotalMembers} total members, {UserMembers} user members for group {GroupId}") without polluting the parser with `ILogger` and `groupId`, **amend the parser signature** to:
   ```csharp
   internal static (List<UserDto> Users, int TotalCount) ParseMembersFromJson(string json);
   ```
   The orchestrator then logs `result.TotalCount` and `result.Users.Count`. This keeps the parser pure for unit testing while preserving existing observability.

3. **FR-2 visibility.** The spec says `private static`. NFR-3 requires direct unit testing of the parser, which is impossible with `private` from a separate test assembly. **Amend to `internal static`** and require `InternalsVisibleTo("Anela.Heblo.Tests")` on `Anela.Heblo.Application` (add if missing). This preserves the FR-3 "no public surface change" intent — internal is not part of the public API.

4. **FR-2 per-element debug logs.** The spec is silent on the existing `LogDebug` calls at lines 135 ("Processing user member…") and 148 ("Skipping non-user member…"). Because moving them into the pure parser would force an `ILogger` dependency, **amend Out of Scope** to explicitly state: "Per-element `LogDebug` lines inside the parsed loop are removed in this refactor; aggregate counts remain via the parser's return-tuple, logged by the orchestrator." This is a minor observability change that should be called out — `LogDebug` lines are rarely enabled in production, so the impact is minimal, but the developer should not silently drop them without an explicit decision.

## Prerequisites

1. **Verify `InternalsVisibleTo` on `Anela.Heblo.Application`.** Grep `backend/src/Anela.Heblo.Application/` for `InternalsVisibleTo`. If `Anela.Heblo.Tests` is not listed, add it — either in `Anela.Heblo.Application.csproj`:
   ```xml
   <ItemGroup>
     <InternalsVisibleTo Include="Anela.Heblo.Tests" />
   </ItemGroup>
   ```
   or in a new `Properties/AssemblyInfo.cs`. This is the only prerequisite change outside the two files in scope.

2. **Re-read the current logging template formats** before extracting, so the new helpers emit identical message templates, log levels, and structured property names (`Duration`, `TokenLength`, `Scope`, `GroupId`).

3. **Run `GraphServiceTests` against `main` first** to capture the current pass-state baseline. All seven tests in that fixture must still pass after the refactor with no test code changes — this is the load-bearing safety net for behaviour preservation.

4. **No infrastructure, configuration, migration, or feature-flag work is required.** No new packages. No DI registration changes. No `Program.cs` edits. No `appsettings.*.json` edits. No Key Vault secrets.