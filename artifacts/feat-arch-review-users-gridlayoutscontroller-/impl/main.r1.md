# Implementation: Remove unused ICurrentUserService from GridLayoutsController

## What was implemented

Surgical dead-code removal in `GridLayoutsController.cs`:
- Removed `using Anela.Heblo.Domain.Features.Users;` (only existed to resolve `ICurrentUserService`)
- Removed `private readonly ICurrentUserService _currentUserService;` field
- Removed `ICurrentUserService currentUserService` constructor parameter
- Removed `_currentUserService = currentUserService;` assignment

The controller's action methods (`Get`, `Save`, `Reset`), class attributes (`[Authorize]`, `[ApiController]`, `[Route]`), and all handler files are untouched. Identity resolution continues inside `GetGridLayoutHandler`, `SaveGridLayoutHandler`, and `ResetGridLayoutHandler` which each inject `ICurrentUserService` themselves.

## Files created/modified

- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — removed dead dependency (4 deletions, 1 insertion net)

## Tests

No new tests written. The spec explicitly states no controller tests are required for this pure removal:
- No `GridLayoutsController` tests existed before this change.
- The existing 4,341 handler/integration tests all pass unchanged.

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-users-gridlayoutscontroller-

# Confirm one file changed
git diff --stat 856bdc040b83a9f5361b96a897cd9a11dae2b34b..0d0d6d0c114812c98c4785e002e5e6f612c95372

# Confirm no _currentUserService reference remains
grep -n "_currentUserService" backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs || echo "none"

# Build
dotnet build backend/Anela.Heblo.sln

# Format
dotnet format backend/Anela.Heblo.sln --verify-no-changes

# Tests
dotnet test backend/Anela.Heblo.sln --no-build
```

## Notes

No deviations from spec. The implementation is exactly what FR-1 through FR-5 specify. Both spec compliance and code quality reviewers gave ✅ / "Ready to merge: Yes" with no issues found.

## PR Summary

Removes a dead constructor dependency from `GridLayoutsController`. The controller declared and stored an `ICurrentUserService` instance that none of its three action methods (`Get`, `Save`, `Reset`) ever read — identity resolution already lives inside `GetGridLayoutHandler`, `SaveGridLayoutHandler`, and `ResetGridLayoutHandler`, which each inject `ICurrentUserService` on their own. Keeping the unused dependency mislead readers and forced DI to resolve an unnecessary service on every controller instantiation.

The fix removes the field, constructor parameter, assignment, and the now-unused `using Anela.Heblo.Domain.Features.Users;` directive. The HTTP contract, `[Authorize]` attribute, DI registration, and all handler signatures are unchanged.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — removed `_currentUserService` field, constructor parameter, assignment, and unused `using` directive

## Status
DONE
