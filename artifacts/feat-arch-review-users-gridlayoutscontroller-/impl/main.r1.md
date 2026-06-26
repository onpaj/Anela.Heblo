Implementation complete. Here's the summary:

**Task:** Remove unused `ICurrentUserService` from `GridLayoutsController`

**What changed:** Single file edited — `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs`. Removed the `_currentUserService` field, the `ICurrentUserService currentUserService` constructor parameter, its assignment, and the now-unused `using Anela.Heblo.Domain.Features.Users;` directive. Action methods, `[Authorize]`, and all handlers are untouched.

**Validation:**
- `dotnet build` — 0 errors, 0 new warnings
- `dotnet format --verify-no-changes` — clean
- `dotnet test` — 4,341 tests, all green
- Diff scope — exactly 1 file, 4 deletions / 1 insertion

**Reviews:** Both spec compliance (✅ all FRs met) and code quality (✅ "Ready to merge: Yes", no issues) passed on first round.

**Commit:** `0d0d6d0c` on branch `feat-arch-review-users-gridlayoutscontroller-`.