# Specification: Move BackgroundJobs Request Body DTOs to Application Layer

## Summary
Two request-body DTOs (`UpdateJobStatusRequestBody`, `UpdateJobCronRequestBody`) currently defined inside `RecurringJobsController.cs` violate the architectural rule that the API project must not own DTOs. This spec covers their relocation to the existing `BackgroundJobs` module `Contracts/` folder with no behavioural change.

## Background
The project's `development_guidelines.md` is explicit: *"API project never defines or owns DTOs"* and *"DTOs defined in API or Xcc — Breaks ownership, violates boundaries."* DTOs belong to their owning application module so contracts are self-contained and reusable by other consumers (tests, MCP, OpenAPI generation) without forcing a dependency on the API host project.

A daily architecture review on 2026-05-27 flagged this violation in `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` (lines 128–134 and 136–146). The fix is mechanical: relocate the two classes to the namespace and folder where sibling DTOs already live (`RecurringJobDto` is already at `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`).

## Functional Requirements

### FR-1: Relocate `UpdateJobStatusRequestBody` to BackgroundJobs Contracts
Move the class from the bottom of `RecurringJobsController.cs` into a new file owned by the application layer.

**Acceptance criteria:**
- New file exists at `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs`.
- Class is declared in namespace `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`.
- Class members and shape are unchanged: public class, single public property `bool IsEnabled { get; set; }`.
- Class is no longer declared inside `RecurringJobsController.cs`.
- The XML doc comment (`<summary>Request body for updating recurring job status</summary>` and the property summary) is preserved on the relocated class.

### FR-2: Relocate `UpdateJobCronRequestBody` to BackgroundJobs Contracts
Move the class from the bottom of `RecurringJobsController.cs` into a new file owned by the application layer.

**Acceptance criteria:**
- New file exists at `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs`.
- Class is declared in namespace `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`.
- Class members and shape are unchanged: public class, single public property `[Required] string CronExpression { get; set; } = string.Empty;`.
- The `[Required]` data annotation is preserved (file must `using System.ComponentModel.DataAnnotations;`).
- Class is no longer declared inside `RecurringJobsController.cs`.
- The XML doc comment (`<summary>Request body for updating recurring job CRON expression</summary>` and the property summary, including the example `"0 3 * * *"`) is preserved on the relocated class.

### FR-3: Update controller imports and remove orphan `using`s
The controller file already imports `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`, so the moved types resolve through the existing using. The controller file must be cleaned up so it no longer carries usings that exist solely for the now-moved DTOs.

**Acceptance criteria:**
- `RecurringJobsController.cs` no longer contains either DTO class definition.
- `RecurringJobsController.cs` no longer declares `using System.ComponentModel.DataAnnotations;` unless still required for other code in the file (it is not, after the move — verify and remove if unused).
- Controller continues to reference `UpdateJobStatusRequestBody` and `UpdateJobCronRequestBody` via the existing `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;` directive.
- Controller method signatures, attributes, response types, MediatR dispatch, and route templates are unchanged.

### FR-4: Preserve consumer compatibility
The existing controller test file `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTests.cs` already imports `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` and references both DTOs by their unqualified names. No source change is required there because the type names and member shapes remain identical.

**Acceptance criteria:**
- Test file is unchanged (no edits required).
- All references to `UpdateJobStatusRequestBody` (lines 115, 153, 183, 221) compile and resolve via the new namespace through the existing using.
- Existing `RecurringJobsControllerTests` pass with no modifications.

### FR-5: Validation
The change must be verifiable through standard project gates.

**Acceptance criteria:**
- `dotnet build` succeeds for the full solution.
- `dotnet format` reports no violations on the touched files.
- `dotnet test` for the BackgroundJobs controller tests passes.
- Generated OpenAPI client (`frontend/src/api/generated/api-client.ts`) regenerates to identical contract shapes (type names, properties, request bodies) — no frontend hook change required.

## Non-Functional Requirements

### NFR-1: Behavioural Parity
Zero runtime behavioural change. Wire-format (JSON property names), HTTP status codes, validation behaviour, and OpenAPI schema names must be byte-identical before and after the move.

### NFR-2: Architectural Conformance
After the change, the API project must contain no DTO type definitions. The relocated DTOs must live in the same `Contracts/` folder as `RecurringJobDto.cs` and follow the same conventions (public class, no `record`, public mutable properties — consistent with the project's DTO rule that DTOs are classes, never records, because OpenAPI client generators mishandle record parameter order).

### NFR-3: Surgical Change Scope
Only the controller file and the two new DTO files are touched. No reformatting, refactoring of unrelated code, or comment cleanups elsewhere.

## Data Model

No data model change. The two classes retain their shape:

| Class                        | Property        | Type    | Attributes  |
|------------------------------|-----------------|---------|-------------|
| `UpdateJobStatusRequestBody` | `IsEnabled`     | `bool`  | none        |
| `UpdateJobCronRequestBody`   | `CronExpression`| `string`| `[Required]`|

Both remain public classes with public mutable properties (the project-wide DTO convention).

## API / Interface Design

No external interface change.

- `PUT /api/RecurringJobs/{jobName}/status` continues to accept `{ "isEnabled": bool }`.
- `PUT /api/RecurringJobs/{jobName}/cron` continues to accept `{ "cronExpression": string }`.
- OpenAPI schema names `UpdateJobStatusRequestBody` and `UpdateJobCronRequestBody` are preserved.
- Generated TypeScript client and `frontend/src/api/hooks/useRecurringJobs.ts` need no edits because the regenerated client emits the same type names.

The only namespace-level change is from `Anela.Heblo.API.Controllers` to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` on the C# side.

## Dependencies

- Existing `BackgroundJobs` application module (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/`).
- `Anela.Heblo.Application` already references `System.ComponentModel.DataAnnotations` (transitively, used elsewhere).
- No new NuGet packages, no project reference changes.

## Out of Scope

- Refactoring sibling DTOs (`RecurringJobDto`) or the MediatR request/response classes under `UseCases/`.
- Splitting the controller file further or extracting other controllers.
- Adding FluentValidation, additional validation attributes, or changing nullability annotations on the relocated DTOs.
- Converting the relocated DTOs to `record` types — explicitly forbidden by project rule.
- Renaming the DTOs or changing their JSON wire shape.
- Touching the frontend generated client manually (it will regenerate on build).
- Any change to `RecurringJobsControllerTests.cs`.

## Open Questions

None.

## Status: COMPLETE