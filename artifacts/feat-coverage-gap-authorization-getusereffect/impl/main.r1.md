All 5 tasks complete. Here is the output artifact:

---

# Implementation: GetUserEffectivePermissionsHandler Unit Tests

## What was implemented

Four xUnit `[Fact]` tests that characterize and lock in all three branches of `GetUserEffectivePermissionsHandler`, plus a dedicated dedup edge case for `AccessRoles.Base`. These are characterization tests on existing production behavior — all four passed green on first run. No production code was changed.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs` — new test class with 4 `[Fact]` methods covering FR-2, FR-3 (security-critical), FR-4, and the Base-dedup edge case

## Tests

`backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`:
- `Handle_UserNotFound_ReturnsAuthorizationUserNotFoundAndDoesNotLoadGraph` — FR-2: null user → error code + graph never loaded
- `Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph` — FR-3 (security): inactive user → empty permissions, no `AccessRoles.Base`, graph never loaded (all three assertions required by NFR-2)
- `Handle_ActiveUser_ReturnsMergedDistinctSortedPermissionsIncludingBase` — FR-4: parent-edge graph resolves transitively, permissions merged with Base, sorted ascending
- `Handle_ActiveUser_WhenGroupAlsoGrantsBase_BaseAppearsOnlyOnce` — dedup edge: Base granted by group + union still yields single occurrence

## How to verify

```bash
# From repo root / worktree root
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetUserEffectivePermissionsHandlerTests" \
  --no-build -v minimal
# Expected: 4 passed in ~100ms

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Authorization" \
  --no-build -v minimal
# Expected: 111 passed (existing 107 + 4 new), 1 skipped, 0 failed
```

## Notes

- The `NotContain(AccessRoles.Base)` assertion in FR-3 is deliberately kept alongside `BeEmpty()` — the spec (NFR-2) explicitly requires all three security assertions present together, even though `BeEmpty()` logically subsumes it.
- No production code under `backend/src/` was modified (verified by `git diff --name-only main...HEAD -- backend/src/` returning empty).
- Two commits on the branch: initial test class + fix for missing `ErrorCode.Should().BeNull()` in the dedup fact.

## PR Summary

Adds a focused unit-test class for `GetUserEffectivePermissionsHandler` — the Authorization use case that resolves effective permissions for a given user. Before this change there were zero direct handler tests, meaning the security-critical `IsActive` short-circuit had no regression protection.

The four `[Fact]` methods lock in all three handler branches (user-not-found, inactive-user, active-user) and a dedup edge case for `AccessRoles.Base`. FR-3 (`Handle_InactiveUser_*`) is the load-bearing security test: it asserts empty permissions, absence of `AccessRoles.Base`, and that the group-permission graph is never loaded — any future refactor that moves the graph query above the `IsActive` guard will fail this test explicitly.

### Changes
- `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs` — new xUnit test class (147 lines) with 4 `[Fact]` methods; uses `Mock<IAuthorizationRepository>` with `Times.Never` for negative-interaction assertions; inline data construction matching sibling test style

## Status
DONE