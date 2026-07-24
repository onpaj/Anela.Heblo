# Plan: Stop `GetAppRoleMembersAsync` from swallowing Graph failures

## Summary

`GraphService.GetAppRoleMembersAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:268-453`)
catches every exception and returns `new List<UserDto>()`, so a Graph token failure and "the app role legitimately has
zero members" are indistinguishable to callers. This makes `GetAppRoleMembersAsync` behave differently from
`GetGroupMembersAsync` on the same interface, and means `EntraAccessUserSourceAdapter` — the sole production caller,
feeding the Authorization module's Base-role membership list — has no way to detect or surface an auth failure. This
plan aligns the method's failure semantics with `GetGroupMembersAsync` and updates the one call chain that depends on it.

## Context

This is a backend-only correctness/robustness fix identified by the daily arch-review routine, not a user-facing
feature. The affected data flows into `IEntraAccessUserSource.GetBaseMembersAsync` →
`GetEntraAccessUsersHandler` → `GET /api/admin/authorization/entra-users`, which the Admin UI's Entra member picker
(`EntraMemberSearch.tsx`) uses to know which Entra users already hold the Base access role. If this list comes back
empty due to a swallowed auth failure, downstream access-control decisions are made on stale/wrong data with no
error signal anywhere in the stack — exactly the risk called out in the finding.

## Functional requirements

**FR-1 — Token acquisition failure throws, matching `GetGroupMembersAsync`.**
In `GetAppRoleMembersAsync`, the `catch (Exception ex)` around `_tokenAcquisition.GetAccessTokenForAppAsync(...)`
(lines 289-298) must narrow to `catch (MsalException ex)` and throw `GraphServiceAuthException`, mirroring
`GetGroupMembersAsync:167-173` exactly (same message shape, same exception type).
- Acceptance: a unit test that makes `ITokenAcquisition.GetAccessTokenForAppAsync` throw `MsalException` asserts
  `GetAppRoleMembersAsync` throws `GraphServiceAuthException` (not swallows it) — same pattern as the existing
  `GetGroupMembersAsync_TokenAcquisitionMsalException_Throws` test at `GraphServiceTests.cs:420-435`.

**FR-2 — Outer catch-all no longer swallows unexpected failures.**
Remove the outer `catch (Exception ex) { return new List<UserDto>(); }` at lines 448-452. Let unexpected exceptions
(JSON parse errors, cancellation, transport-level `HttpRequestException`, or an `MsalException` type not matched by
FR-1 if one somehow reaches this far) propagate to the caller, matching `GetGroupMembersAsync`'s outer
`catch (Exception ex) { ...; throw; }` at lines 185-189.
- Acceptance: existing test `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError`
  (`GraphServiceTests.cs:282-…`) is rewritten to assert the method throws (it currently only covers an HTTP-level
  non-success status inside the method, which stays empty-list per FR-3 below — see Open Questions for why this one
  specific test needs re-examination, not blind renaming).

**FR-3 — Internal non-success-HTTP branches are unchanged (explicitly out of scope).**
The early `return new List<UserDto>()` branches for: missing `AzureAd:ClientId` config (283-287), non-success
service-principal lookup (308-312), missing `spId`/`appRoleId` (315-319, 335-339), non-success assignment-page
fetch (352-356), and non-success `$batch` response (411-415) are **not** changed by this task. This matches
`GetGroupMembersAsync`'s own precedent: a non-success HTTP status on its primary Graph call (lines 140-150) also
logs and returns an empty list rather than throwing — that is the established convention in this codebase for
"Graph responded, just not with 2xx," as opposed to "we couldn't talk to Graph or authenticate at all."
- Acceptance: `GetAppRoleMembersAsync_NonTwoHundredSubResponse_SkipsUserAndLogsWarning` keeps passing unmodified.
  A new or renamed test confirms the SP-lookup-failure and assignment-page-failure branches still return an empty
  list (behavior preserved, just verified precisely so a future change doesn't silently regress the intentional
  parts of this task alongside the unintentional ones).

**FR-4 — `IGraphService` documents the new contract.**
Add the same `<exception cref="GraphServiceAuthException">` xmldoc block used on `GetGroupMembersAsync`
(`IGraphService.cs:7-9`) to `GetAppRoleMembersAsync`'s signature, so the interface itself states the auth-failure
contract instead of requiring callers to read both implementations.

**FR-5 — Caller must not turn a thrown exception into an unhandled 500.**
`GetEntraAccessUsersHandler.Handle` (`GetEntraAccessUsersHandler.cs:12-24`) currently has no try/catch at all,
and `GetEntraAccessUsersResponse` never sets `Success = false`. Once FR-1/FR-2 land, a Graph auth failure will
propagate as an unhandled `GraphServiceAuthException` all the way to the ASP.NET Core exception-handler chain
(`ArgumentExceptionHandler` / `UnauthorizedAccessExceptionHandler` / `ValidationExceptionHandler` — none of which
match it), falling through to the framework's default handler as a bare 500 `ProblemDetails` with no `ErrorCode`.
Add a try/catch in `GetEntraAccessUsersHandler` mirroring `GetGroupMembersHandler`'s pattern
(`GetGroupMembersHandler.cs:34-77`): `GraphServiceAuthException` → `ErrorCodes.ConfigurationError`,
`GraphServiceException` → `ErrorCodes.ExternalServiceError`, returning `Success = false` with an empty `Users` list
so the response stays within the existing `BaseResponse`/`HandleResponse` contract used by every other admin
endpoint in this controller.
- Acceptance: new handler test — mock `IEntraAccessUserSource.GetBaseMembersAsync` to throw
  `GraphServiceAuthException`, assert `GetEntraAccessUsersHandler.Handle` returns `Success = false` and
  `ErrorCode = ErrorCodes.ConfigurationError` (not an unhandled exception, not a silently-empty success response).

## Non-functional requirements

- No behavior change for the legitimate-empty-result case (role exists, zero members assigned) — must still return
  `Success = true` with an empty list, not an error.
- No new external dependencies or exception types; reuse `GraphServiceAuthException` / `GraphServiceException`,
  which already exist for exactly this purpose (`Contracts/GraphServiceAuthException.cs`, `Contracts/GraphServiceException.cs`).
- Logging: keep the existing `_logger.LogError` calls before throwing (matches `GetGroupMembersAsync`'s pattern of
  log-then-throw) so ops visibility doesn't regress.

## Data model

No new or changed entities. `UserDto`, `EntraAccessUserRecord`, and `EntraUserDto` are unaffected — this is purely
an error-propagation change through the existing types.

## Interfaces

- No new endpoints. Behavior change on the existing `GET /api/admin/authorization/entra-users` endpoint: on a Graph
  auth failure it now returns HTTP 500-mapped-via-`ErrorCodes.ConfigurationError` (exact status code per
  `HttpStatusCodeAttribute` on that enum value) with `{ Success: false, ErrorCode: "ConfigurationError", Users: [] }`
  instead of HTTP 200 with `{ Success: true, Users: [] }`.
- `IGraphService.GetAppRoleMembersAsync` xmldoc gains the `GraphServiceAuthException` contract (FR-4).
- Frontend: `EntraMemberSearch.tsx` currently reads `useEntraAccessUsers()` data without checking `.success` or
  `isError` at all — see Open Questions.

## Dependencies and scope

**In scope:** `GraphService.GetAppRoleMembersAsync`, `IGraphService` xmldoc, `GetEntraAccessUsersHandler`, and the
unit tests covering both (`GraphServiceTests.cs`, `GetEntraAccessUsersHandlerTests.cs`).

**Explicitly out of scope:**
- `SearchUsersAsync` in the same file has the identical swallow-to-empty-list pattern at lines 208-212 and 261-264.
  It is not mentioned in the finding and has different callers/semantics (interactive search — an empty result on
  failure is a worse UX bug there than a correctness bug); leave it untouched, but it's a natural follow-up
  arch-review item.
- The internal non-success-HTTP branches inside `GetAppRoleMembersAsync` itself (FR-3) — preserved intentionally,
  not overlooked.
- Frontend changes to `EntraMemberSearch.tsx` to surface `isError`/`success:false` to the admin user. The component
  today silently shows an empty picker either way; this task fixes the backend signal, not the UI's consumption of
  it. Flagged as a follow-up, not blocking this fix (the API contract change alone is a strict improvement: the
  failure is now observable in logs/network tab instead of nowhere).

## Rough plan

1. `GraphService.cs`: narrow the token-acquisition catch to `MsalException` → `GraphServiceAuthException` (FR-1);
   delete the outer catch-all (FR-2).
2. `IGraphService.cs`: add the `GraphServiceAuthException` xmldoc to `GetAppRoleMembersAsync` (FR-4).
3. `GetEntraAccessUsersHandler.cs`: add try/catch mapping `GraphServiceAuthException`/`GraphServiceException` to
   `ErrorCodes.ConfigurationError`/`ExternalServiceError` (FR-5).
4. `GraphServiceTests.cs`: rewrite `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError` to assert
   a thrown exception where it's actually testing the outer catch-all path; add a token-acquisition-failure test
   mirroring `GetGroupMembersAsync_TokenAcquisitionMsalException_Throws`; add/keep a test confirming SP-lookup and
   assignment-page HTTP failures still return empty list per FR-3.
5. `GetEntraAccessUsersHandlerTests.cs`: add the auth-failure-maps-to-ConfigurationError test (FR-5 acceptance).
6. Run `dotnet build` + `dotnet format` + the touched test projects; no FE or E2E changes required for this task.

## Open questions

1. **Does `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError` actually exercise the outer
   catch-all, or the batch-response non-success branch (line 411-415, preserved per FR-3)?** Reading the test
   (`GraphServiceTests.cs:281-320`), it drives an HTTP 500 *response* from the fake handler for the `$batch` call —
   that's the FR-3-preserved early-return branch, not an exception. **Default taken:** this specific test's
   assertion (`result.Should().BeEmpty()`) does **not** change; only its name/comment should clarify it's testing
   the intentionally-preserved HTTP-failure path. The FR-2 rewrite instead needs a *new* test that forces the outer
   catch-all specifically (e.g., the fake handler throwing `HttpRequestException` from `SendAsync`, or
   `JsonDocument.Parse` failing on malformed JSON) to prove it now propagates. Flagging so the dev step doesn't
   just rename the existing test and declare FR-2 covered.
2. **Should `GraphServiceException` (not just `GraphServiceAuthException`) also be thrown for the FR-3-preserved
   HTTP-failure branches, as a larger follow-up?** Not resolved here — default is no, keep FR-3 scope as-is, per the
   `GetGroupMembersAsync` precedent argument above. If the architecture step disagrees, treat it as scope expansion
   requiring its own review, since it touches five branches with existing passing tests asserting empty-list
   behavior.
3. **Is a bare 500 (no `GraphServiceAuthException` handling in `GetEntraAccessUsersHandler`) acceptable instead of
   FR-5's typed-error mapping?** The finding's suggested fix explicitly allows "let it propagate — either way the
   caller has a choice." **Default taken:** add the typed mapping (FR-5) for consistency with `GetGroupMembersHandler`
   and the rest of the Authorization controller's error contract, rather than introducing the module's first
   unhandled-exception-to-bare-500 admin endpoint. Revisit if the dev step finds this out of proportion to the fix.
