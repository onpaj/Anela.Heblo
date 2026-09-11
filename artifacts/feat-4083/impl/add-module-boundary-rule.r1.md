# Implementation: add-module-boundary-rule

## What was implemented
Added a new architecture-boundary rule to `ModuleBoundariesTests.cs` enforcing that
`Anela.Heblo.Application.Shared.Users` never references Authorization-owned namespaces
(`Domain.Features.Authorization`, `Application.Features.Authorization`,
`Persistence.Features.Authorization`) directly. This locks in the migration done by the
prior tasks (`add-authorization-directory-adapter`, `update-user-display-name-resolver`),
which moved the sole implementer of `Shared.Users.Contracts.IUserDirectorySource`
(`AuthorizationUserDirectorySourceAdapter`) into `Features.Authorization.Infrastructure`,
outside the inspected namespace prefix. The allowlist for the new rule is intentionally
empty, per the task instructions — no exceptions are permitted.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added the empty
  `SharedUsersAuthorizationAllowlist` field (with rationale comment) immediately after
  `AuthorizationUserManagementAllowlist`, and added the `"Shared.Users -> Authorization"`
  `ModuleBoundaryRule` entry as the second entry in the `Rules()` `TheoryData`, immediately
  after the existing `"Authorization -> UserManagement"` entry. Syntax (named args, array
  literal style) matches the existing entries exactly.

## Tests
Build: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — succeeded,
0 errors (241 pre-existing warnings, unrelated to this change).

Test run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build -p:UseSharedCompilation=false`

Result:
```
Passed!  - Failed:     0, Passed:    36, Skipped:     0, Total:    36, Duration: 226 ms - Anela.Heblo.Tests.dll (net8.0)
```

All 36 theory cases passed, including the new `"Shared.Users -> Authorization"` case, with
zero violations found and no entries needed in `SharedUsersAuthorizationAllowlist`.

## How to verify
1. Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` and confirm
   the new allowlist field and `TheoryData` entry are present as described above.
2. From the repo root, run:
   ```
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build -p:UseSharedCompilation=false
   ```
3. Confirm `Failed: 0` and that the total test count includes the new rule (36 total vs. 35
   before this change).

## Notes
No deviations from the task spec. The allowlist was not populated — the migration from the
depended-on tasks already fully isolates `Shared.Users` from `Authorization`, so the rule
passed on first run with zero violations, as expected.

## PR Summary
This change adds a regression-guarding architecture test that locks in the module boundary
between `Application.Shared.Users` and `Authorization`. It depends on two already-completed
tasks on this branch that moved `AuthorizationUserDirectorySourceAdapter` (the sole
implementer of `Shared.Users.Contracts.IUserDirectorySource`) into
`Features.Authorization.Infrastructure`, outside the `Shared.Users` namespace prefix this
rule inspects. With that adapter relocated, nothing under `Shared.Users` needs to reference
Authorization-owned namespaces directly, so the new rule's allowlist is empty by design and
the full `ModuleBoundariesTests` suite (36 cases) passes with zero violations.

### Changes
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added the empty
  `SharedUsersAuthorizationAllowlist` and the `"Shared.Users -> Authorization"` boundary
  rule enforcing that `Application.Shared.Users` never references Authorization's domain,
  application, or persistence namespaces.

## Status
DONE
