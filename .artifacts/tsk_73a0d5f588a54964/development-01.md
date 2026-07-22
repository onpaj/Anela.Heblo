# Development: Remove dead `IGraphService.SearchUsersAsync`

## Summary

Implemented Option A exactly as specified by plan-01.md / design-01.md / architecture-01.md: deleted the dead `SearchUsersAsync` method from `IGraphService`, its two implementations (`GraphService`, `MockGraphService`), the now-unused `SearchResultLimit` constant, and the dedicated unit test file. Pure subtraction, no behavior change to any live endpoint/handler/MCP tool.

## Files changed

1. **`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`**
   Removed the `SearchUsersAsync` declaration (was line 14). Interface now has exactly two members: `GetGroupMembersAsync`, `GetAppRoleMembersAsync`.

2. **`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`**
   - Removed the `SearchUsersAsync` method body (~75 lines: `$search` query construction, quote-stripping, error handling).
   - Removed `private const int SearchResultLimit = 25;` — re-verified at edit time it had exactly one other reference (inside the deleted method itself), so it's fully dead now.
   - `GetGroupMembersAsync` and `GetAppRoleMembersAsync` untouched.

3. **`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`**
   Removed the `SearchUsersAsync` mock override.

4. **`backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`**
   Deleted the file in full (120 lines, 5 facts, all exclusively covering the removed method).

## Files verified unaffected

- `MockGraphServiceTests.cs`, `GraphServiceTests.cs`, `PhotobankGraphServiceThumbnailTests.cs` — re-grepped at implementation time, zero references to `SearchUsersAsync`/`SearchResultLimit`, no edits needed.
- DI registration (`Microsoft365AdapterServiceCollectionExtensions.cs`) — registers `IGraphService` as a whole; no per-method wiring, so nothing to change.

## Verification performed

- **Repo-wide grep** for `SearchUsersAsync|SearchResultLimit` across `backend/` and `frontend/` after all edits: **zero matches**. Only non-code hits are in the pre-existing planning artifacts (`.artifacts/...`) and two unrelated historical docs under `docs/superpowers/plans/` that reference an old design doc, not this codebase's live source.
- **Structural read-back** of the edited region in `GraphService.cs` confirms clean splice — `GetGroupMembersAsync`'s catch block is immediately followed by `GetAppRoleMembersAsync`, no orphaned braces or dangling code.

## Verification NOT performed (environment limitation)

This machine has **no .NET SDK installed** (`dotnet` command not found, no SDK directory located, podman/docker also unavailable to run the build in a container). I could not run:
- `dotnet build`
- `dotnet format` / `dotnet format --verify-no-changes`
- `dotnet test` (full `Anela.Heblo.Tests` suite)

The change is a pure deletion of a method that was statically verified (by this step and the two prior review steps) to have zero callers anywhere in the codebase — the only route to a build break would be a missed caller, and the repo-wide grep above confirms none exists. Still, **please run `dotnet build && dotnet test` in an environment with the .NET 8 SDK before merging**, per the repo's stated validation rules, since this step could not do so directly.

## How to verify

```bash
# Confirm zero remaining references
grep -rn "SearchUsersAsync\|SearchResultLimit" backend/ frontend/
# Expect: no output

# Build and test (requires .NET 8 SDK)
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test
```
