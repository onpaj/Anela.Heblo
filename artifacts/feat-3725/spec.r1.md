# Specification: Remove dead `IGraphService.SearchUsersAsync` method

## Summary
`IGraphService.SearchUsersAsync` is a fully-implemented, fully-tested method with zero callers anywhere in the Application, API, or Domain layers. This specification defines the removal of the method from the interface, its production implementation, its mock stub, and its dedicated test file, restoring `IGraphService` to only the surface area the codebase actually uses.

## Background
`IGraphService` currently declares three methods: `GetGroupMembersAsync`, `SearchUsersAsync`, and `GetAppRoleMembersAsync`. A codebase-wide search found no invocation of `SearchUsersAsync` outside of its own interface declaration and the two adapter implementations. It was apparently built in anticipation of a directory-search feature that was never wired up to any controller, handler, or UI.

Carrying this method has ongoing cost with no offsetting value:
- ~70 lines of production Microsoft Graph API integration code (`GraphService.cs`) that no code path exercises.
- A trivial-but-still-present stub in `MockGraphService.cs` that every future test double must also implement or consciously no-op.
- 5 dedicated unit tests (`GraphServiceSearchTests.cs`) that exercise dead code, adding to test run time and maintenance surface without protecting any real feature.
- Interface bloat: any new `IGraphService` implementation (e.g., a future test double for another module) is forced to implement a method that serves no current use-case.

This is a mechanical YAGNI cleanup with no behavior change to any reachable code path. If directory-style user search is needed later, it should be re-added driven by a concrete use-case at that time, not resurrected from this deleted code.

## Functional Requirements

### FR-1: Remove `SearchUsersAsync` from the `IGraphService` interface
Delete the `Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);` method declaration from `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` (currently line 14). No other members of the interface change.

**Acceptance criteria:**
- `IGraphService.cs` no longer declares `SearchUsersAsync`.
- `IGraphService` still declares `GetGroupMembersAsync` and `GetAppRoleMembersAsync` unchanged.
- The file's XML doc comments for the remaining methods are preserved as-is.

### FR-2: Remove the production implementation from `GraphService`
Delete the `SearchUsersAsync` method body (currently lines 192–266, including its closing brace) from `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`. Additionally remove the `SearchResultLimit` constant (currently line 25), which exists solely to parameterize `SearchUsersAsync`'s `$top` query parameter and has no other reference in the class.

**Acceptance criteria:**
- `GraphService` no longer contains a `SearchUsersAsync` method.
- The `SearchResultLimit` constant is removed from `GraphService.cs`.
- `GraphBatchSize` and all other members used by `GetGroupMembersAsync` and `GetAppRoleMembersAsync` are untouched, since `GetAppRoleMembersAsync` depends on `GraphBatchSize` and on `GetGroupMembersAsync`.
- `GraphService` still compiles as a valid implementation of `IGraphService` after FR-1 is applied.

### FR-3: Remove the stub implementation from `MockGraphService`
Delete the `SearchUsersAsync` method (currently lines 22–26) from `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`.

**Acceptance criteria:**
- `MockGraphService` no longer contains a `SearchUsersAsync` method.
- `MockGraphService` still compiles as a valid implementation of `IGraphService` after FR-1 is applied.
- `GetGroupMembersAsync` and `GetAppRoleMembersAsync` stub bodies are unchanged.

### FR-4: Remove the dedicated test file
Delete `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` in its entirety (all 5 test methods: `SearchUsersAsync_BuildsSearchRequest_AndParsesUsers`, `SearchUsersAsync_NonSuccess_ReturnsEmpty`, `SearchUsersAsync_EmptyQuery_ReturnsEmpty_WithoutTouchingFactory`, `SearchUsersAsync_TokenFailure_ReturnsEmpty_WithoutTouchingFactory`, `SearchUsersAsync_StripsDoubleQuotesFromQuery`).

**Acceptance criteria:**
- The file `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` no longer exists.
- No other test file references `GraphServiceSearchTests` or the deleted `SearchUsersAsync_*` test methods.
- `backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs` is **not** deleted — it is a shared test helper also used by `OutlookCalendarSyncServiceTests.cs` and `GraphServiceTests.cs`.

### FR-5: Verify no remaining references
Confirm that after FR-1 through FR-4 are applied, no reference to `SearchUsersAsync` remains anywhere in the solution (production code, tests, or comments), other than incidental historical mentions in documentation/changelogs, which are out of scope for this change.

**Acceptance criteria:**
- A repository-wide search for `SearchUsersAsync` returns zero matches in `backend/src/**` and `backend/test/**`.
- The solution builds successfully (`dotnet build`) with no new warnings or errors introduced by the removal.
- `dotnet format` reports no new formatting issues in the touched files.
- The full backend test suite passes, with the total test count reduced by exactly 5 (the deleted `GraphServiceSearchTests` methods) and no other test failures or newly-skipped tests.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a code-deletion change with no runtime behavior for any reachable code path; no performance characteristics change.

### NFR-2: Security
N/A — no security-sensitive code paths are affected. The removed method made outbound Microsoft Graph API calls using existing application-permission tokens, but since it was never invoked by any caller, its removal has no effect on any authentication, authorization, or data-exposure surface.

## Data Model
N/A — no data model changes. `UserDto` (in `Anela.Heblo.Application.Features.UserManagement.Contracts`) remains unchanged and continues to be used by `GetGroupMembersAsync` and `GetAppRoleMembersAsync`.

## API / Interface Design
This change modifies an internal C# interface (`IGraphService`) and its two implementations. It does not touch any HTTP API surface, MediatR request/response contracts, or frontend-facing endpoint, since `SearchUsersAsync` had no caller in the API or Application layers to begin with.

## Dependencies
None. This change is self-contained within the `UserManagement` feature slice (`Anela.Heblo.Application`) and its Microsoft 365 adapter (`Anela.Heblo.Adapters.Microsoft365`), plus the corresponding test project.

## Out of Scope
- Re-implementing user search functionality under a new name or in a new location.
- Any change to `GetGroupMembersAsync`, `GetAppRoleMembersAsync`, `ParseMembersFromJson`, `AcquireGraphTokenAsync`, or any other member of `GraphService`/`MockGraphService` not directly tied to `SearchUsersAsync`.
- Changes to `FakeHttpMessageHandler` or any other shared test helper.
- Any changes to DI registration for `IGraphService` (registration of `GraphService`/`MockGraphService` as the interface implementation is unaffected, since the interface itself — not its registration — is what changes).
- Documentation updates beyond what is naturally implied by the code deletion (e.g., no separate changelog entry is required beyond the PR description).

## Open Questions
None. Reasonable assumptions made: (1) the `SearchResultLimit` constant in `GraphService.cs` is removed alongside the method since it has no other consumer; (2) `FakeHttpMessageHandler` is preserved as a shared test helper; (3) no feature flag, DI registration, or configuration references `SearchUsersAsync` by name, since the brief's search confirmed zero callers outside the interface/adapter definitions.

## Status: COMPLETE
