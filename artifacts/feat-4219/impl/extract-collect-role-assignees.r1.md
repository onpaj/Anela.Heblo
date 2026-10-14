# Implementation: extract-collect-role-assignees

## What was implemented

Extracted the inline "Step 3: get all principals assigned to this role (paginated)" block from
`GetAppRoleMembersAsync` into a new private helper method `CollectRoleAssigneesAsync`, matching
the pattern already established by the `ResolveServicePrincipalAsync` and `FindAppRoleId`
extractions from earlier rounds of this feature.

The helper walks the paginated `appRoleAssignedTo` Graph collection for a given service principal
and app role id, bucketing assignees into a `HashSet<string>` of direct user ids and a
`List<string>` of group ids to expand. On any page request failure it logs the error (same
message as before) and returns `(null, null)`; the caller now checks for that null sentinel and
returns an empty `List<UserDto>()` in that case, preserving the original early-return behavior.

`GetAppRoleMembersAsync` itself is now a single call to the helper followed by a null check,
replacing the ~38-line inline paging loop that used to sit between "Step 3" and "Step 4".

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` —
  added `private async Task<(HashSet<string>? DirectUserIds, List<string>? GroupIdsToExpand)> CollectRoleAssigneesAsync(...)`
  after `FindAppRoleId`; replaced the inline pagination block in `GetAppRoleMembersAsync` with a
  call to it plus a null-guard.

## Tests

No new test file was added — this is a pure extract-method refactor with no behavior change, and
the task context called for verifying via the existing test suite rather than new tests. The
existing `GetAppRoleMembersTests` / `GraphServiceTests` suite (21 tests, expanded from earlier
rounds of this same feature) exercises the paginated-assignment logic, including the failure path
that now flows through the helper's `(null, null)` return.

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```

Expected: build succeeds with 0 errors; all 21 matching tests pass.

## Notes

- The task-context's suggested solution path (`backend/Anela.Heblo.sln`) does not exist in this
  worktree — the solution file is actually at the repo root (`Anela.Heblo.sln`). Used the correct
  path to build/test; no functional deviation.
- The task-context expected "12 PASS" for the test filter; this worktree's suite currently has 21
  matching tests (grown from the two earlier extraction rounds — `extract-resolve-service-principal`
  and `extract-find-app-role-id` — in this same feature). All 21 pass; no regression.
- `dotnet format --verify-no-changes` on the touched file reported no formatting issues.
- No behavior change: the extracted method is a literal move of the existing loop body, with the
  early `return new List<UserDto>()` on failure replaced by `return (null, null)` inside the
  helper and a matching null-check at the call site.

## PR Summary
Extracted the paginated `appRoleAssignedTo` walk out of `GetAppRoleMembersAsync` into a new
`CollectRoleAssigneesAsync` helper on `GraphService`, continuing the same extract-method cleanup
already applied to service-principal resolution and app-role-id lookup earlier in this feature.
The helper returns `(null, null)` on a failed page request (logging the same error as before) so
the caller's existing empty-list fallback is preserved with no behavior change.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — added
  `CollectRoleAssigneesAsync`; `GetAppRoleMembersAsync` now delegates to it instead of inlining the
  pagination loop

## Status
DONE
