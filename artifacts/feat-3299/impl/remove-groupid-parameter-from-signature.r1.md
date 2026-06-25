# Implementation: remove-groupid-parameter-from-signature

## What was implemented
Removed the unused `string groupId` parameter from `AcquireGraphTokenAsync` in `GraphService.cs` and updated the single call site to match. Both changes were made atomically since updating the signature without updating the call site would cause a compile error.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — removed `string groupId` from method signature on line 86; updated call site on line 119 to pass only `cancellationToken`

## Tests
No new tests required. The method is private and the behavioral change is zero — the same token is acquired via the same scope with the same logic. `dotnet build` is the sufficient validation gate.

## How to verify
- `dotnet build backend/src/Anela.Heblo.API/` completes with 0 errors ✓
- `grep -n "groupId" backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` returns no hits for the method signature or call site (groupId still appears as a parameter in the caller `GetGroupMembersAsync`, which is correct)

## Notes
Both FR-1 and FR-2 were implemented in a single atomic commit because removing the parameter without updating the call site would break compilation. The two pipeline tasks map to a single 2-line diff.

## PR Summary
Removed the dead `string groupId` parameter from `AcquireGraphTokenAsync` in `GraphService`. The parameter was never used inside the method body — the application-scoped token acquisition (`https://graph.microsoft.com/.default`) is not group-specific. The caller already logs the group ID before and after the call, so no logging context is lost.

### Changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — removed `string groupId` from `AcquireGraphTokenAsync` signature and updated its single call site

## Status
DONE
