# Architecture Review: Fix silently-swallowed HTTP error in `GraphService.GetGroupMembersAsync`

## Skip Design: true

## Architectural Fit Assessment

This is a one-file, one-branch bug fix inside an existing adapter implementation, and it fits the codebase's existing conventions exactly rather than introducing anything new:

- `GraphService` (in `Anela.Heblo.Adapters.Microsoft365`) already implements `IGraphService` (in `Anela.Heblo.Application.Features.UserManagement.Services`), a clean Ports-and-Adapters boundary. `GraphServiceException` already lives in the Application layer's `Contracts` folder specifically so infrastructure-specific SDK exceptions (`ODataError`, `MsalException`, raw `HttpRequestException`) don't leak across the boundary — the class doc comment says as much.
- The fix does not add a new exception type or a new catch branch shape. It makes an existing branch (`!response.IsSuccessStatusCode`, line 140) consistent with the sibling branch three catch-blocks below (`catch (ODataError odataEx)`, line 174) that already does the right thing: log, then `throw new GraphServiceException(message, innerException)`.
- Both downstream consumers (`GetGroupMembersHandler.Handle`, `GraphArticleUserResolver.ResolveByGroupAsync`) already have a `catch (GraphServiceException ex)` block wired up and tested against the `ODataError` path. No consumer code changes are required — confirmed by reading both files. This is a textbook Liskov Substitution Principle fix: making the implementation honor a contract two callers are already coded against.
- No new module, no new DTO, no new interface, no new UI surface. `GraphServiceException` is a sealed class (not a record), consistent with the DTO/exception conventions in this codebase.

There is nothing to push back on architecturally — the brief and spec's proposed fix is the correct, minimal one and matches the pattern already present in the same method.

## Proposed Architecture

### Component Overview

No new components. Existing call chain, unchanged shape:

```
GetGroupMembersHandler.Handle()                 GraphArticleUserResolver.ResolveByGroupAsync()
        │  catch(GraphServiceException) [already wired, tested]   │  catch(GraphServiceException) [already wired, tested]
        └───────────────────┬─────────────────────────────────────┘
                             │
                    IGraphService.GetGroupMembersAsync(groupId, ct)
                             │
                    GraphService.GetGroupMembersAsync(groupId, ct)   [Adapters.Microsoft365]
                             │
                    HTTP GET /groups/{groupId}/members
                             │
              ┌──────────────┴───────────────┐
       IsSuccessStatusCode == true     IsSuccessStatusCode == false   ← ONLY this branch changes
              │                               │
        parse + cache + return         BEFORE: log, return []  (bug)
                                        AFTER:  log, throw GraphServiceException(msg, HttpRequestException(errorContent))
```

### Key Design Decisions

#### Decision 1: Exception type and inner exception to use
**Options considered:**
1. Throw a new, more specific exception type (e.g. `GraphServiceHttpException`) distinguishing HTTP-status failures from OData failures.
2. Throw the existing `GraphServiceException`, wrapping an `HttpRequestException(errorContent)` as inner exception — matching the brief/spec exactly.

**Chosen approach:** Option 2.

**Rationale:** `IGraphService`'s XML doc already documents a single `GraphServiceException` contract for "Microsoft Graph returns an OData error response" (arguably under-specified — it should say "returns an error response," OData or otherwise, but that's a doc wording nit, not a design gap). Both existing consumers switch on `GraphServiceException` as one class of "external service failed" error and don't need finer-grained discrimination today. Introducing a second exception type would require touching both consumers' catch blocks — an unjustified expansion of a bug-fix's blast radius for a distinction nobody currently needs. `HttpRequestException(errorContent)` as the inner exception is a reasonable, low-effort carrier for the raw Graph error body; it's a slight abuse of `HttpRequestException`'s intended meaning (no actual transport failure occurred) but it is scoped to being an inner-exception payload for diagnostics only, never inspected by type. Acceptable.

#### Decision 2: Scope boundary — do not touch `SearchUsersAsync` / `GetAppRoleMembersAsync`
**Options considered:**
1. Fix all three swallow sites in `GraphService` in one change, for consistency.
2. Fix only `GetGroupMembersAsync`, per the brief.

**Chosen approach:** Option 2, matching the spec's explicit Out-of-Scope section.

**Rationale:** `SearchUsersAsync` and `GetAppRoleMembersAsync` do not carry the same documented `IGraphService` contract violation — nothing in `IGraphService`'s XML doc promises `GraphServiceException` for those two methods, and their callers were never written expecting it (confirmed: no `catch (GraphServiceException)` wraps calls to those two methods elsewhere in the codebase, unlike `GetGroupMembersAsync`'s two callers). Bundling an unrelated, larger behavior change into this fix would violate the "surgical changes" rule and risk regressing paths that aren't part of this bug report. Worth a follow-up ticket, not part of this change.

One second-order effect to flag explicitly (already called out correctly in the spec's NFR-1): `GetAppRoleMembersAsync` (line 383) calls `GetGroupMembersAsync` internally to expand nested-group members, and that call site sits inside `GetAppRoleMembersAsync`'s own top-level `catch (Exception ex)` (lines 448–452). After this fix, a Graph HTTP failure on a nested group will propagate as `GraphServiceException`, get caught by that generic handler, and `GetAppRoleMembersAsync` will return `[]` — same externally observable behavior as today, just via a different code path. This must be covered by regression testing (existing `GetAppRoleMembersAsync_*` tests), not by a code change.

## Implementation Guidance

### Directory / Module Structure

No new files. Two files change:

- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — lines 140–150, replace `return new List<UserDto>();` with a `throw new GraphServiceException(...)`, keeping the existing `_logger.LogError` and `_logger.LogDebug` calls untouched.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — lines ~438–450, update `GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList`.

### Interfaces and Contracts

No interface changes. `IGraphService.GetGroupMembersAsync` signature is untouched. The exception type thrown (`GraphServiceException`, constructor `(string message, Exception innerException)`) already exists at `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs` and needs no modification.

Suggested replacement code (matches brief/spec exactly, no deviation warranted):

```csharp
if (!response.IsSuccessStatusCode)
{
    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
    _logger.LogError("Microsoft Graph API call failed for groupId: {GroupId}. Status: {StatusCode}, RequestUrl: {RequestUrl}, ResponseContent: {Content}",
        groupId, response.StatusCode, requestUrl, errorContent);

    _logger.LogDebug("Response headers: {@Headers}", response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)));

    throw new GraphServiceException(
        $"Microsoft Graph returned {(int)response.StatusCode} for group {groupId}.",
        new HttpRequestException(errorContent));
}
```

### Data Flow

Before: `HTTP non-2xx → log → return [] → GetGroupMembersHandler returns {Success:true, Members:[]} (200 OK)` / `GraphArticleUserResolver returns empty match list silently`.

After: `HTTP non-2xx → log → throw GraphServiceException → GetGroupMembersHandler's existing catch(GraphServiceException) → {Success:false, ErrorCode:ExternalServiceError, Members:[]}` / `GraphArticleUserResolver's existing catch(GraphServiceException) → throw ArticleUserResolverServiceException`. Both catch blocks already exist and are already exercised by tests against the `ODataError` path — no new wiring needed, confirmed by reading both files.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `GetAppRoleMembersAsync`'s nested-group expansion (line 383) now receives an exception instead of `[]` from `GetGroupMembersAsync` on a failing nested group | Low | Already caught by `GetAppRoleMembersAsync`'s own top-level `catch (Exception)`; externally observable behavior (empty list) is unchanged. Covered by existing `GetAppRoleMembersAsync_*` tests — verify they still pass, no code change needed. |
| Any other untested caller of `GetGroupMembersAsync` (e.g. a UI-facing endpoint) that doesn't expect an exception could now surface a 500 or unhandled fault where it previously got a silent empty list | Low | Both known callers already handle `GraphServiceException` correctly. Grep the solution for other call sites of `GetGroupMembersAsync` before merging to confirm none exist outside `GetGroupMembersHandler`, `GraphArticleUserResolver`, and `GetAppRoleMembersAsync` (all three are already accounted for above). |
| Test rename (`..._ReturnsEmptyList` → `..._ThrowsGraphServiceException`) could be missed if any other test or CI dashboard references the old test name | Trivial | Standard `dotnet test` full-suite run after the change; no external references to individual test names exist in this repo's CI config. |

## Specification Amendments

None required — the spec (`spec.r1.md`) is complete, correctly scoped, and its suggested code change is exactly what should be implemented. One optional, non-blocking wording note: `IGraphService.GetGroupMembersAsync`'s XML doc (`/// Thrown when Microsoft Graph returns an OData error response.`) is slightly narrower than reality even after this fix ships, since `GraphServiceException` will now also cover plain HTTP-status failures, not just OData-shaped errors. Consider widening the doc comment to "an error response (OData error or non-success HTTP status)" while touching this file — a one-line, zero-risk doc clarification, not a scope expansion. Not required for FR-1/FR-2 acceptance criteria.

## Prerequisites

None. No migrations, no config, no infrastructure changes. The fix can be implemented and merged immediately against `main`/the feature branch.
