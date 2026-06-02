## Module
BackgroundJobs

## Finding
Three DTOs used exclusively by `BackgroundRefreshController` are defined inside the API project's `Controllers/` folder:

- `backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs` — namespace `Anela.Heblo.API.Controllers`
- `backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs` — namespace `Anela.Heblo.API.Controllers`
- `backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs` — namespace `Anela.Heblo.API.Controllers`

The development guidelines state: *"API project never defines or owns DTOs – it only uses them."*

## Why it matters
DTOs owned by the API project are invisible to the Application layer and cannot be reused by other consumers (e.g. tests, other handlers). It violates the ownership rule that keeps the API layer a thin host/composition boundary and is the exact anti-pattern the guidelines were written to prevent.

## Suggested fix
Move all three files to the BackgroundJobs module contracts folder:
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/`

Update namespaces from `Anela.Heblo.API.Controllers` to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` and fix the `using` in `BackgroundRefreshController`.

---
_Filed by daily arch-review routine on 2026-05-28._