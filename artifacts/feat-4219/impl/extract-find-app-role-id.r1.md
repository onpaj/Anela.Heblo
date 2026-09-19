# Implementation: extract-find-app-role-id

## What was implemented

Extracted Step 2 of `GraphService.GetAppRoleMembersAsync` (finding the
`appRoleId` matching the requested app role value within the already-fetched
`appRoles` array) into a new `private static` helper method, `FindAppRoleId`.
`GetAppRoleMembersAsync` now delegates to this helper instead of inlining the
`foreach`/`TryGetProperty` loop. This is a pure behavior-preserving refactor:
same null-check semantics, same early-exit-on-first-match behavior, same
`appRoleId` result — only the code's location changed.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`
  — added the private static `FindAppRoleId(JsonElement? appRoles, string appRoleValue)`
  helper (placed after `ResolveServicePrincipalAsync`, at the end of the class);
  replaced the inlined "Step 2" block in `GetAppRoleMembersAsync` with a call to it.

## Tests

No new tests were added — this task is a pure internal refactor with no
observable behavior change, and the existing test suite already exercises
`GetAppRoleMembersAsync`'s full happy path and the "role not found" path
through mocked HTTP responses.

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetAppRoleMembersTests.cs` (3 tests) — unchanged, all still pass.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` (18 tests) — unchanged, all still pass.

## How to verify

```bash
dotnet build Anela.Heblo.sln   # sln is at the repo root
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```

Result: `Passed! - Failed: 0, Passed: 21, Skipped: 0, Total: 21` (same count
as the task's own baseline note from the prior task — the task doc's "12
PASS" figure is stale, as already flagged by task 1's implementation notes).

`dotnet build` on the full solution: 0 errors (pre-existing nullable-reference
warnings elsewhere in the codebase, unrelated to this change).

## Notes

- Build/test commands in the task doc reference `backend/Anela.Heblo.sln`,
  but the solution file is at the repo root (`Anela.Heblo.sln`) — same stale
  path noted by the previous task's implementation notes.
- No docs need updating — this is an internal, private-method-only refactor
  with no change to `IGraphService`'s public contract or any documented
  behavior.

## PR Summary
Extracted the app-role-id-lookup step of `GraphService.GetAppRoleMembersAsync` into a new private `FindAppRoleId` helper, continuing the breakup of that method's five inline sequential steps (arch-review issue #4219). Purely mechanical, behavior-preserving change — same lookup logic, same early return on no match, verified via the existing 21-test suite (`GetAppRoleMembersTests` + `GraphServiceTests`) passing unchanged.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — added `FindAppRoleId`; `GetAppRoleMembersAsync` now calls it instead of inlining the app-role lookup loop

## Status
DONE
