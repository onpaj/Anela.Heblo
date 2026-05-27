Final reviewer: **APPROVED**. Writing the output artifact:

---

# Implementation: Move BackgroundJobs Request Body DTOs to Application Layer

## What was implemented

Mechanical architectural fix: two request-body DTOs (`UpdateJobStatusRequestBody`, `UpdateJobCronRequestBody`) were relocated from the bottom of `RecurringJobsController.cs` into the BackgroundJobs module's `Contracts/` folder. This enforces the project rule that the API project must not define or own DTOs. Zero behavioural change.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs` — new file: `public class` DTO with `bool IsEnabled { get; set; }`, XML doc comments preserved
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs` — new file: `public class` DTO with `[Required] string CronExpression { get; set; }`, `using System.ComponentModel.DataAnnotations;`, XML doc comments preserved
- `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` — removed both inline DTO class definitions (lines 125–146) and the now-orphan `using System.ComponentModel.DataAnnotations;` directive (line 1)

## Tests

- `RecurringJobsControllerTests.cs` — **unchanged** (per spec). All 5 existing tests pass. They already imported `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` and used unqualified type names — resolution is automatic.

## How to verify

```bash
# Backend build
dotnet build backend/Anela.Heblo.sln

# BackgroundJobs controller tests
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.RecurringJobsControllerTests"

# Format check
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs \
  --verify-no-changes

# OpenAPI schema names (should find exactly 4 matches)
grep -nE 'class UpdateJobStatusRequestBody|class UpdateJobCronRequestBody|recurringJobs_UpdateJobStatus|recurringJobs_UpdateJobCron' frontend/src/api/generated/api-client.ts
```

## Notes

- OpenAPI schema names verified unchanged after `npm run build` — `git diff frontend/src/api/generated/api-client.ts` shows zero changes. NSwag schema naming is namespace-independent, as confirmed by the architecture review's risk assessment.
- 151 pre-existing lint errors in the frontend test suite are unrelated to this change; no new errors were introduced.
- Commit `40a95873`: 3 files changed, 27 insertions(+), 24 deletions(−).

## PR Summary

Relocates `UpdateJobStatusRequestBody` and `UpdateJobCronRequestBody` from `RecurringJobsController.cs` into the BackgroundJobs module's `Contracts/` folder. This enforces the project rule that the API project never defines or owns DTOs.

The controller already imported `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;`, so all consumer references (controller `[FromBody]` parameters, test file, generated TypeScript client) continue to resolve automatically without modification. The `using System.ComponentModel.DataAnnotations;` directive in the controller became orphan after the move and was removed.

Wire format, OpenAPI schema names (`UpdateJobStatusRequestBody`, `UpdateJobCronRequestBody`), HTTP status codes, and the generated TypeScript client are byte-identical before and after the move.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs` — new DTO file, relocated from controller
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs` — new DTO file with `[Required]` annotation, relocated from controller
- `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` — removed inline DTO definitions and orphan `using` directive

## Status
DONE