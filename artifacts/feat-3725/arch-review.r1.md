# Architecture Review: Remove dead `IGraphService.SearchUsersAsync` method

## Skip Design: true

## Architectural Fit Assessment
This is a pure dead-code removal within the `UserManagement` vertical slice and its `Anela.Heblo.Adapters.Microsoft365` adapter. It touches no MediatR handlers, no controllers, no DTO contracts consumed outside the slice, and no persisted data. I verified independently (grep across `backend/src/Anela.Heblo.Application`, `backend/src/Anela.Heblo.API`, `backend/src/Anela.Heblo.Domain`, and `backend/frontend`) that `SearchUsersAsync` has zero callers anywhere in the solution or the frontend — the only occurrences are the interface declaration, the two adapter implementations (`GraphService`, `MockGraphService`), and the dedicated test file. This confirms the brief's and spec's findings exactly, including line numbers.

The change is consistent with the codebase's Clean Architecture / Vertical Slice conventions: `IGraphService` is an Application-layer port with two Adapter-layer implementations (production `GraphService` and `MockGraphService`, presumably selected by DI based on environment/feature flag). Removing an unused method from a port and both its adapters is a textbook interface-segregation cleanup and requires no architectural exception or new pattern. No module boundary, DTO rule, or persistence concern in `docs/architecture/development_guidelines.md` is implicated.

**Verdict: proceed as specified.** No architectural objections. This is mechanical, low-risk, and fully reversible via git history if search is ever needed again.

## Proposed Architecture

### Component Overview
No new components. Three existing files shrink and one test file is deleted:

- `IGraphService` (Application port) — drops one method signature.
- `GraphService` (production adapter, Microsoft365) — drops the method body and its now-orphaned `SearchResultLimit` constant.
- `MockGraphService` (test/dev-mode adapter, Microsoft365) — drops the stub method.
- `GraphServiceSearchTests` (unit tests) — file deleted entirely.

No DI registration changes: both adapters continue to implement `IGraphService` fully after removal, so whatever mechanism selects `GraphService` vs `MockGraphService` at startup (in `Microsoft365AdapterServiceCollectionExtensions.cs`) is untouched.

### Key Design Decisions

#### Decision 1: Delete vs. deprecate
**Options considered:**
- (a) Mark `SearchUsersAsync` `[Obsolete]` and leave it in place for a deprecation window.
- (b) Delete outright now.

**Chosen approach:** (b) Delete outright, per the spec.

**Rationale:** This is an internal C# interface with no external consumers (no HTTP surface, no other module depends on it, confirmed by grep). There is no cross-team or cross-release contract to honor, so a deprecation window adds process overhead without protecting anyone. Standard YAGNI: the method never shipped a reachable feature, so there's nothing to phase out — only to delete.

#### Decision 2: Scope of constant removal (`SearchResultLimit`)
**Options considered:**
- (a) Leave `SearchResultLimit` in `GraphService.cs` since it's harmless.
- (b) Remove it alongside the method, per FR-2.

**Chosen approach:** (b), matching the spec.

**Rationale:** `SearchResultLimit` has no reference outside the deleted method body (confirmed by grep: its only two occurrences are the declaration and its use inside `SearchUsersAsync`). Leaving it behind would itself be new dead code — the same class of problem this change is fixing. Removing it is consistent with the change's own stated purpose and carries zero risk since nothing else reads it.

#### Decision 3: Test file granularity
**Options considered:**
- (a) Delete the 5 `SearchUsersAsync_*` test methods but keep `GraphServiceSearchTests.cs` as an empty/near-empty class.
- (b) Delete `GraphServiceSearchTests.cs` in its entirety.

**Chosen approach:** (b), per FR-4.

**Rationale:** Every test in the file exists solely to exercise `SearchUsersAsync`; the class has no other purpose. Deleting the whole file is cleaner than leaving a class with a private `BuildService` helper and no test methods. Confirmed the shared `FakeHttpMessageHandler` helper (`backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs`) is used elsewhere (`GraphServiceTests.cs`, `OutlookCalendarSyncServiceTests.cs`) and must NOT be touched — verified via grep, it has no other dependency on the deleted file.

## Implementation Guidance

### Directory / Module Structure
Exact changes, verified against the current file contents in this worktree:

1. **`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`**
   Delete line 14 only:
   ```csharp
   Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
   ```
   Lines 5–13 (interface declaration + XML doc on `GetGroupMembersAsync`) and line 15 (`GetAppRoleMembersAsync`) remain unchanged.

2. **`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`**
   - Delete line 25: `private const int SearchResultLimit = 25;`
   - Delete lines 192–266 (the full `SearchUsersAsync` method, from `public async Task<List<UserDto>> SearchUsersAsync(...)` through its closing `}`). Verified this range is exactly the method body — line 267 is a blank separator line before `GetAppRoleMembersAsync` at line 268.
   - No other changes to this file. `GraphBatchSize` (line 26), `AcquireGraphTokenAsync`, `ParseMembersFromJson`, `GetGroupMembersAsync`, and `GetAppRoleMembersAsync` are untouched and confirmed to have no dependency on the deleted method or constant.

3. **`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`**
   Delete lines 22–26 (the `SearchUsersAsync` stub method):
   ```csharp
   public Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
   {
       _logger.LogInformation("Mock GraphService: SearchUsersAsync called for query '{Query}'", query);
       return Task.FromResult(new List<UserDto>());
   }
   ```
   `GetGroupMembersAsync` and `GetAppRoleMembersAsync` stubs are unchanged.

4. **`backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`**
   Delete the entire file (all 121 lines, 5 test methods).

5. **No changes** to `backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs`, `GraphServiceTests.cs`, `GetGroupMembersHandlerTests.cs`, `GetGroupMembersValidationPipelineTests.cs`, `GraphArticleUserResolver.cs`, `EntraAccessUserSourceAdapter.cs`, `UserManagementModule.cs`, or `Microsoft365AdapterServiceCollectionExtensions.cs` — none reference `SearchUsersAsync`.

### Interfaces and Contracts
`IGraphService` shrinks from three methods to two (`GetGroupMembersAsync`, `GetAppRoleMembersAsync`). No other interface, DTO, or MediatR contract changes. `UserDto` is unaffected — it remains used by the two surviving methods.

### Data Flow
N/A — pure deletion of unreachable code. No request ever exercised this path in production (confirmed: zero callers), so there is no data flow to redraw.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future feature silently depended on `SearchUsersAsync` via reflection, config-driven dispatch, or a currently-disabled feature flag not caught by static grep | Low | Grep confirmed zero references outside the 4 files across `Application`, `API`, `Domain`, and `frontend`. No feature-flag docs (`docs/development/feature-flags.md`) mention it. Given the method is fully synchronous C# with no string-based invocation pattern in this codebase, reflection-based use is implausible. If wrong, the build fails immediately (compile-time interface mismatch) rather than at runtime, giving fast, cheap detection. |
| Deleting `SearchResultLimit` inadvertently removes something still needed | Very Low | Confirmed via grep it has exactly two occurrences: its declaration and its single use inside the deleted method body. Safe to remove. |
| Test count assertion (FR-5: "reduced by exactly 5") drifts if `dotnet test` counts differently (e.g., theory expansion) | Low | All 5 tests are `[Fact]`, not `[Theory]`, so each maps 1:1 to a test count. Verify via `dotnet test` output diff before/after as a build-gate check, not by manual line-counting. |
| Git history/blame loses context on why the method existed, complicating future re-introduction | Very Low | Not a mitigation needed for this change; the deleted code remains recoverable via git history if directory-search is genuinely needed later, as the spec's "Out of Scope" section already anticipates. |

## Specification Amendments
None. Independent verification confirms every factual claim in `spec.r1.md` (file paths, line ranges 14, 192–266, 22–26, zero external callers, `FakeHttpMessageHandler` shared-helper status) matches the actual code in this worktree exactly. The spec's FR-1 through FR-5 are implementable as written with no gaps.

## Prerequisites
None.
