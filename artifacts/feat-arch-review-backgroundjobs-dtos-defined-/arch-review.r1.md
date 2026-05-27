# Architecture Review: Move BackgroundJobs Request Body DTOs to Application Layer

## Skip Design: true

This is a backend-only mechanical relocation. No new components, screens, or visual changes.

## Architectural Fit Assessment

The proposed change directly enforces a rule documented in `docs/architecture/development_guidelines.md`:

> *"API project never defines or owns DTOs — it only uses them"*
> *"DTOs defined in API or Xcc — Breaks ownership, violates boundaries"*

The fix aligns perfectly with the existing pattern. The target folder already exists at `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/` and already houses a sibling DTO (`RecurringJobDto.cs`) under the namespace `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`. The controller already imports that namespace (line 2 of `RecurringJobsController.cs`), so post-move type resolution is automatic.

**Integration points verified:**
- Test file `RecurringJobsControllerTests.cs` already has `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;` (line 2) — unqualified references on lines 115, 153, 183, 221 will continue to resolve.
- Frontend client (`frontend/src/api/generated/api-client.ts`) consumes only the type names (`UpdateJobStatusRequestBody`, `UpdateJobCronRequestBody`) — schema names are namespace-independent in NSwag output, so regeneration produces identical artifacts.
- The `using System.ComponentModel.DataAnnotations;` directive in the controller becomes dead code after the move (only `[Required]` on `UpdateJobCronRequestBody` uses it) and must be removed.

## Proposed Architecture

### Component Overview

```
backend/src/Anela.Heblo.API/Controllers/
└── RecurringJobsController.cs                         # HTTP shell only, no DTO definitions

backend/src/Anela.Heblo.Application/Features/BackgroundJobs/
├── Contracts/
│   ├── RecurringJobDto.cs                             # (existing)
│   ├── UpdateJobStatusRequestBody.cs                  # NEW — relocated
│   └── UpdateJobCronRequestBody.cs                    # NEW — relocated
├── UseCases/
│   ├── UpdateRecurringJobStatus/                      # MediatR Request/Response/Handler
│   └── UpdateRecurringJobCron/                        # MediatR Request/Response/Handler
└── BackgroundJobsModule.cs
```

Data flow is unchanged:
```
HTTP Body → [FromBody] *RequestBody (Contracts) → Controller → MediatR Request → Handler → Response
```

### Key Design Decisions

#### Decision 1: One file per DTO, mirror existing convention

**Options considered:**
- A. One file per DTO (`UpdateJobStatusRequestBody.cs`, `UpdateJobCronRequestBody.cs`).
- B. Combined file (`UpdateRecurringJobRequestBodies.cs`).

**Chosen approach:** A — one file per DTO.

**Rationale:** Matches the existing convention in the same folder (`RecurringJobDto.cs` is solo) and the project-wide rule that "files [are] aligned with the primary type they define" (csharp-coding-style). Also keeps `dotnet format` diffs minimal and makes git history easier to follow when DTOs evolve independently.

#### Decision 2: Keep DTOs as `public class` with mutable properties, not `record`

**Options considered:**
- A. Preserve current shape: `public class` + public mutable properties.
- B. Convert to `record` / `init`-only.

**Chosen approach:** A — preserve current shape.

**Rationale:** CLAUDE.md and `development_guidelines.md` are explicit: *"DTOs are classes, never C# records"* because OpenAPI client generators (NSwag) mishandle record parameter order. The frontend depends on the generated TypeScript class shape; switching to records would silently regenerate a different client. NFR-2 also requires the DTOs to follow the same conventions as `RecurringJobDto`, which is a `public class`.

#### Decision 3: Do not co-locate DTOs inside their UseCase folder

**Options considered:**
- A. Put `UpdateJobCronRequestBody` next to `UpdateRecurringJobCron` use case under `UseCases/UpdateRecurringJobCron/`.
- B. Put both in the shared `Contracts/` folder alongside `RecurringJobDto.cs`.

**Chosen approach:** B — shared `Contracts/` folder.

**Rationale:** `development_guidelines.md` is explicit that DTOs live in `Contracts/` of the specific module. The `UseCases/` subfolder owns MediatR `Request`/`Response`/`Handler` triples (the internal CQRS contract); the `Contracts/` folder owns the externally consumable DTOs (the cross-boundary contract). The brief and the spec both target `Contracts/`, and this matches the precedent set by `RecurringJobDto.cs`.

## Implementation Guidance

### Directory / Module Structure

**Create exactly two new files:**

1. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs`
2. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs`

**Modify exactly one existing file:**

3. `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` — delete lines 125–146 (both DTO classes + their XML doc comments must move with them) and remove line 1 (`using System.ComponentModel.DataAnnotations;`) because no remaining symbol in the file requires it.

**Do not touch:**
- `RecurringJobsControllerTests.cs` (already correctly imports `Contracts`).
- `useRecurringJobs.ts` (consumes regenerated client by type name).
- Any `UseCases/` files.
- `RecurringJobDto.cs`.

### Interfaces and Contracts

Both files must have this exact shape (preserving XML docs verbatim from the source):

```csharp
// UpdateJobStatusRequestBody.cs
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

/// <summary>
/// Request body for updating recurring job status
/// </summary>
public class UpdateJobStatusRequestBody
{
    /// <summary>
    /// Whether the job should be enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
```

```csharp
// UpdateJobCronRequestBody.cs
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

/// <summary>
/// Request body for updating recurring job CRON expression
/// </summary>
public class UpdateJobCronRequestBody
{
    /// <summary>
    /// The new CRON expression (e.g. "0 3 * * *")
    /// </summary>
    [Required]
    public string CronExpression { get; set; } = string.Empty;
}
```

**Critical invariants** (any deviation breaks the OpenAPI contract):
- Type names: `UpdateJobStatusRequestBody`, `UpdateJobCronRequestBody` — exact.
- Property names: `IsEnabled`, `CronExpression` — exact (JSON serializer emits camelCase).
- `[Required]` data annotation on `CronExpression` — preserved.
- `= string.Empty` default on `CronExpression` — preserved.
- No constructors, no `init` accessors, no `required` keyword.

### Data Flow

Unchanged. The HTTP request lifecycle for the two affected endpoints:

```
PUT /api/RecurringJobs/{jobName}/status
  → ASP.NET Core model binder deserializes body into Contracts.UpdateJobStatusRequestBody
  → RecurringJobsController.UpdateJobStatus maps to UseCases.UpdateRecurringJobStatus.UpdateRecurringJobStatusRequest
  → MediatR dispatch → Handler → UpdateRecurringJobStatusResponse → HandleResponse → 200 OK

PUT /api/RecurringJobs/{jobName}/cron
  → ASP.NET Core model binder deserializes body into Contracts.UpdateJobCronRequestBody
  → ModelState validation enforces [Required] CronExpression
  → RecurringJobsController.UpdateJobCron maps to UseCases.UpdateRecurringJobCron.UpdateRecurringJobCronRequest
  → MediatR dispatch → Handler → UpdateRecurringJobCronResponse → HandleResponse → 200 OK
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| OpenAPI schema name changes (e.g. NSwag prepends namespace prefix) — would silently break frontend types | HIGH | After build, diff `frontend/src/api/generated/api-client.ts` for the two type names and the two operation methods (`recurringJobs_UpdateJobStatus`, `recurringJobs_UpdateJobCron`). Lines 10008, 10070, 34172, 34253 must remain byte-identical except for line numbers. |
| Forgetting to delete the in-controller class definitions → compile error from duplicate type names | LOW | Single file edit; `dotnet build` will fail loudly. |
| Removing `using System.ComponentModel.DataAnnotations;` while another usage remains | LOW | Grep the controller for `[Required]`, `[Range]`, `[StringLength]`, etc. before removal. Current file confirms none remain after the DTOs leave. |
| Test file relies on `Anela.Heblo.API.Controllers.UpdateJobStatusRequestBody` via FQN | LOW | Verified via grep — all four test-side references (lines 115, 153, 183, 221) are unqualified and resolve through the existing `using Contracts;`. |
| `dotnet format` rewrites unrelated lines on the controller (because the file changed) | LOW | NFR-3 requires surgical scope. Run `dotnet format --include backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` and `--include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJob*.cs` only. |

## Specification Amendments

The spec is implementation-ready. Two minor reinforcements to add to the implementer's checklist:

1. **FR-3 cleanup verification:** After removing `using System.ComponentModel.DataAnnotations;`, explicitly grep the controller for any remaining data-annotation attribute usage to confirm the using is truly orphan. (The current file shows none, but make this a step rather than an assumption.)

2. **FR-5 OpenAPI diff check:** Add an explicit verification step that after `npm run build` (which triggers client regeneration), `git diff frontend/src/api/generated/api-client.ts` shows zero changes to the type-name lines, interface lines, and operation signatures for these two DTOs. If any shape change appears, treat it as a CRITICAL blocker — NSwag may have changed schema naming and the frontend hook will break.

No other amendments. The spec correctly excludes record conversion, FluentValidation introduction, controller splitting, and frontend manual edits.

## Prerequisites

None. No migrations, no config, no infrastructure, no new packages, no project reference changes. The `Anela.Heblo.Application` project already has access to `System.ComponentModel.DataAnnotations` (transitive via the SDK). Implementation can start immediately.