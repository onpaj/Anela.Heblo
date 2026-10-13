# Implementation: extract-resolve-service-principal

## What was implemented

Extracted Step 1 of `GraphService.GetAppRoleMembersAsync` (resolving the service
principal id and its configured app roles for the configured Azure AD app
registration client id) into a new private helper method,
`ResolveServicePrincipalAsync`. `GetAppRoleMembersAsync` now delegates to this
helper and returns early with an empty list when it comes back with a null
service principal id, exactly as the inlined code did before. Step 2 (finding
the matching `appRoleId`) stays inline in `GetAppRoleMembersAsync` for now (it
moves to its own helper in a later task) but was updated to read from the
`appRoles` tuple value returned by the helper instead of the now-removed local
`spDoc` variable.

This is a pure behavior-preserving refactor: no logic, error handling, or
logging changed — the exact same HTTP call, error checks, and log messages
were moved verbatim into the new method.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`
  — added the private `ResolveServicePrincipalAsync(string clientId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)` helper (returns `(string? SpId, JsonElement? AppRoles)`, cloning the `appRoles` JsonElement so it stays readable after the source `JsonDocument` is disposed); replaced the inlined Step 1 block in `GetAppRoleMembersAsync` with a call to it.

## Tests

No new tests were added — this task is a pure internal refactor with no
observable behavior change, and the existing test suite already exercises
`GetAppRoleMembersAsync`'s full happy path and the "service principal not
found" / "missing client id" paths through mocked HTTP responses.

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetAppRoleMembersTests.cs` (3 tests) — unchanged, all still pass.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` (18 tests) — unchanged, all still pass.

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln   # NOTE: sln is actually at the repo root — see Notes below
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```

Baseline (before this change): 21 passed, 0 failed.
After this change: 21 passed, 0 failed (identical count — no behavior change).

## Notes

- The task-context's Step 1/Step 4 instructions reference `dotnet build backend/Anela.Heblo.sln`, but the solution file is actually at the repo root (`Anela.Heblo.sln`, not `backend/Anela.Heblo.sln`). I built/verified via `dotnet test` on the test project directly instead (which transitively builds `GraphService.cs`'s project and would surface any compile error), and confirmed a full `dotnet build` from the correct root-level sln path also succeeds. This is a pre-existing inaccuracy in the task doc, not something introduced by this change.
- The actual baseline/post-change test count is 21 (GetAppRoleMembersTests: 3, GraphServiceTests: 18), not the "12" mentioned in the task doc's Step 1/Step 4 — again a stale figure in the task doc. What matters per the task's own acceptance criteria (Step 4: "same count as Step 1 baseline") is satisfied: 21 before, 21 after.
- No docs need updating — this is an internal, private-method-only refactor with no change to `IGraphService`'s public contract or any documented behavior.

## PR Summary

Extracted the service-principal-resolution step of `GraphService.GetAppRoleMembersAsync` into a new private `ResolveServicePrincipalAsync` helper, as the first step in breaking up that method's five inline sequential steps (arch-review issue #4219). Purely mechanical, behavior-preserving change — same HTTP call, same error handling, same logging, verified via the existing 21-test suite (`GetAppRoleMembersTests` + `GraphServiceTests`) before and after.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — added `ResolveServicePrincipalAsync`; `GetAppRoleMembersAsync` now calls it instead of inlining the service-principal lookup

## Status
DONE
