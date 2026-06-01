---

# Implementation: Relocate BackgroundRefresh DTOs to Application Layer

## What was implemented
Moved three DTO files from `Anela.Heblo.API/Controllers/` into `Anela.Heblo.Application/Features/BackgroundJobs/Contracts/` using `git mv` (preserving history). Updated the namespace declaration in each file from `Anela.Heblo.API.Controllers` to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`. Added one `using` directive to `BackgroundRefreshController.cs`. Pure architectural cleanup — no behavior, wire contract, or TS client shape changed.

## Files created/modified
- `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` — added `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;` (alphabetically ordered)
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs` — moved from API/Controllers, namespace updated
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs` — moved from API/Controllers, namespace updated
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs` — moved from API/Controllers, namespace updated

## Tests
No new tests written (pure relocation, no behavioral change per spec NFR-2). Backend test suite ran and passed. No test consumers of the moved DTOs existed.

## How to verify
```bash
# Namespaces correct
grep '^namespace ' backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTask*.cs

# No Dto.cs under API project
find backend/src/Anela.Heblo.API -name '*Dto.cs' -not -path '*/bin/*' -not -path '*/obj/*'

# History preserved
git log --follow --oneline -3 backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs

# Build clean
dotnet build backend/Anela.Heblo.sln -nologo -v quiet
```

## Notes
- Git rename detection confirmed for all three files (commit shows `{.../Controllers → .../Contracts}` paths)
- TS client checksum identical before/after (NSwag uses type name, not CLR namespace)
- 237 pre-existing build warnings and 144 pre-existing lint issues are unrelated to this change
- Pre-commit hooks passed

## PR Summary
Relocates three BackgroundRefresh DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) from the API project's `Controllers/` folder into the Application layer's `BackgroundJobs/Contracts/` folder, restoring full compliance with the architectural rule that the API project never defines or owns DTOs. The controller is updated with a single `using` directive. No behavior, HTTP wire contract, or generated TypeScript client shape changes.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` — added one `using` directive for the new namespace
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs` — relocated (git mv), namespace updated
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs` — relocated (git mv), namespace updated
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs` — relocated (git mv), namespace updated

## Status
DONE