# Implementation: update-call-site

## What was implemented
Updated the single call site of `AcquireGraphTokenAsync` inside `GetGroupMembersAsync` to omit the now-removed `groupId` argument. This was implemented atomically with the signature change (task `remove-groupid-parameter-from-signature`) in the same commit, as both changes are required together for the code to compile.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — call site on line 119 updated from `AcquireGraphTokenAsync(groupId, cancellationToken)` to `AcquireGraphTokenAsync(cancellationToken)`

## Tests
No new tests required. The call site change is a direct consequence of the signature change and introduces no behavioral differences.

## How to verify
- `dotnet build backend/src/Anela.Heblo.API/` completes with 0 errors ✓
- File line 119 now reads `var graphToken = await AcquireGraphTokenAsync(cancellationToken);`

## Notes
Implemented atomically with the signature removal. No other call sites exist — confirmed by inspection and by the fact that `dotnet build` succeeds with 0 errors.

## Status
DONE
