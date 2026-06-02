# Architecture Review: Relocate BackgroundRefresh DTOs to Application Layer

## Skip Design: true

Pure backend refactor — moving three DTO files and updating their namespace + one controller's `using` directive. No UI components, screens, or visual design decisions involved.

## Architectural Fit Assessment

The proposal aligns perfectly with the project's mandatory rules. `docs/architecture/development_guidelines.md` is unambiguous: *"`API` project never defines or owns DTOs – it only uses them"* and *"DTO objects for API (`Request`, `Response`) live in `contracts/` of the specific module"*. The current state of these three files is a direct violation of both rules.

The destination folder already exists and already follows the convention:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/
├── RecurringJobDto.cs                  // namespace: ...BackgroundJobs.Contracts
├── UpdateJobCronRequestBody.cs
└── UpdateJobStatusRequestBody.cs
```

The exact same relocation was performed on `UpdateJobStatusRequestBody` / `UpdateJobCronRequestBody` two days ago (see `docs/superpowers/plans/2026-05-27-relocate-backgroundjobs-request-body-dtos.md`). That precedent confirms the chosen pattern and exposes no architectural surprises.

Integration points:
1. **`BackgroundRefreshController`** — the single backend consumer of the three DTO symbols (sole controller using them, no tests reference them).
2. **NSwag/OpenAPI pipeline** (`nswag.frontend.json`) — generates the TypeScript client. The current TS client emits unqualified names (`export class RefreshTaskDto`), and NSwag's default `schemaNameGenerator` is type-name-based, not CLR-namespace-based, so the generated TS shape should be byte-identical after the move.
3. **Project references** — `Anela.Heblo.API.csproj` already declares `<ProjectReference Include="../Anela.Heblo.Application/..."/>`. No new wiring is required.

## Proposed Architecture

### Component Overview

```
Before:                                After:
─────────────────────────              ─────────────────────────
Anela.Heblo.API                        Anela.Heblo.API
└── Controllers/                       └── Controllers/
    ├── BackgroundRefreshController        └── BackgroundRefreshController
    │       ▲                                      │ using
    │       │ same namespace                       ▼
    ├── RefreshTaskDto              ─►    Anela.Heblo.Application
    ├── RefreshTaskStatusDto        ─►    └── Features/BackgroundJobs/
    └── RefreshTaskExecutionLogDto  ─►        └── Contracts/
                                                  ├── RefreshTaskDto
                                                  ├── RefreshTaskStatusDto
                                                  └── RefreshTaskExecutionLogDto
                                                  (siblings of existing
                                                   RecurringJobDto, etc.)
```

Dependency direction after the move points the correct way: API → Application (one-way), matching every other controller-to-contract relationship in the codebase.

### Key Design Decisions

#### Decision 1: Target namespace
**Options considered:**
- (a) `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` — matches `RecurringJobDto` and the request-body DTOs already there.
- (b) A new sub-namespace like `...BackgroundJobs.Contracts.BackgroundRefresh` — visually groups the refresh-related DTOs.

**Chosen approach:** (a) — `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`.

**Rationale:** The module's existing convention is a flat `Contracts/` folder with one namespace per module (verified by `RecurringJobDto.cs`). Introducing a sub-namespace for three small DTOs would diverge from the established module shape and risks namespace churn if more refresh DTOs appear later. Keep it flat; rely on filename clarity.

#### Decision 2: Use `git mv` to preserve history
**Options considered:**
- (a) Delete + create new file content (loses `git log --follow`).
- (b) `git mv` followed by the namespace edit in a separate commit.
- (c) `git mv` plus the namespace edit in the same commit.

**Chosen approach:** (c) — `git mv` and the one-line namespace edit in a single commit per file (or one commit for all three).

**Rationale:** Git's rename detection works on similarity, so editing one line in the same commit as the rename still produces a clean rename diff. This keeps the working tree consistent (no intermediate uncompilable state) and yields the smallest reviewer-visible diff.

#### Decision 3: Do not touch DTO shape
**Options considered:**
- (a) Move only; keep all properties, attributes, and ordering verbatim.
- (b) Take the opportunity to drop `init`-only setters or rename properties for consistency.

**Chosen approach:** (a).

**Rationale:** Any property change would alter the generated OpenAPI schema and force a TS client regeneration that touches more than `$ref` paths, expanding the blast radius beyond the architectural cleanup. Spec FR-4 already mandates this; the architecture concurs.

## Implementation Guidance

### Directory / Module Structure

Final file layout after the change:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/
├── RecurringJobDto.cs                  (unchanged)
├── UpdateJobCronRequestBody.cs         (unchanged)
├── UpdateJobStatusRequestBody.cs       (unchanged)
├── RefreshTaskDto.cs                   ← moved, namespace updated
├── RefreshTaskStatusDto.cs             ← moved, namespace updated
└── RefreshTaskExecutionLogDto.cs       ← moved, namespace updated

backend/src/Anela.Heblo.API/Controllers/
└── BackgroundRefreshController.cs      ← single `using` added
    (no other change)
```

### Interfaces and Contracts

The three DTOs preserve their public surface verbatim. Each must declare:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

public class RefreshTaskDto                 // unchanged body
public class RefreshTaskStatusDto           // unchanged body
public class RefreshTaskExecutionLogDto     // unchanged body
```

`BackgroundRefreshController.cs` adds exactly one `using` directive (preserve alphabetical ordering of existing usings):

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;   // new
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

No `[FromBody]`, action signature, or mapper-method change is required.

### Data Flow

Wire contract (HTTP request/response JSON) is unchanged. Only the C# type's residence changes:

```
HTTP request → BackgroundRefreshController action
                  │
                  ├── reads RefreshTaskConfiguration / RefreshTaskExecutionLog
                  │   from IBackgroundRefreshTaskRegistry (Xcc)
                  │
                  └── MapToDto(...) →  RefreshTaskDto / RefreshTaskStatusDto /
                                       RefreshTaskExecutionLogDto
                                       (now resolved from Application.Contracts)
                                              │
                                              ▼
                                       JSON serialization (System.Text.Json)
                                              │
                                              ▼
                                       HTTP response
```

The mapper methods (`MapToDto(RefreshTaskConfiguration, ...)` and `MapToDto(RefreshTaskExecutionLog)`) remain on the controller. Moving them is **out of scope** per spec; leave them where they are.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| NSwag emits CLR namespace into schema names, breaking TS client | Low | Inspect current `frontend/src/api/generated/api-client.ts` — exported class is `RefreshTaskDto`, not `Anela_Heblo_API_Controllers_RefreshTaskDto`. NSwag config uses defaults. Regenerate after the move and diff the file; if zero meaningful changes, do not commit a regenerated client. |
| Hidden test consumer breaks | Low | Repo-wide grep across `backend/test/` for the three symbol names returned zero hits. Run full `dotnet test` after the change as a backstop. |
| Other code references `Anela.Heblo.API.Controllers` for these DTOs | Low | The 77 files importing that namespace do so for *other* DTOs that remain there. Grep specifically for the three symbol names anchored to a FQN form (`Anela\.Heblo\.API\.Controllers\.RefreshTask`) — must return zero matches after the change. |
| `dotnet format` rewrites unrelated files | Low | Run with `--include` scoped to the four touched files (mirrors the precedent in the prior plan). Use `--verify-no-changes` to confirm clean state. |
| Generated TS client subtly drifts (`$ref` ordering, JSON Schema component order) | Low | NSwag schema components are keyed by type name. After running `npm run build` in `frontend/`, diff `api-client.ts`; any non-cosmetic delta is unexpected and should be investigated, not silently committed. |

## Specification Amendments

None substantive; the spec is accurate. Two minor clarifications worth recording in the implementation plan:

1. **FR-3 phrasing — "fully-qualified references"**: A repo-wide grep for `Anela\.Heblo\.API\.Controllers\.RefreshTask` (anchored on type name) is the precise check; the broader grep for `Anela.Heblo.API.Controllers` will surface 77 unrelated files and is not the relevant signal. The plan should anchor the grep to the three symbol names.

2. **FR-5 phrasing**: NSwag's default `schemaNameGenerator` uses the CLR *type name*, not the namespace, so generated TS client output should be unchanged. The plan should still regenerate and diff (per the precedent for `UpdateJob*RequestBody`), but the expected outcome is **zero TS file changes**. Treat any non-empty diff as a flag to investigate, not as expected churn.

3. **`dotnet format` scope**: Run `dotnet format --include` scoped to the four touched files (mirrors the precedent). The spec's blanket "`dotnet format` reports no changes required" should be qualified to the touched set.

## Prerequisites

None. Infrastructure already in place:

- `Anela.Heblo.API.csproj` references `Anela.Heblo.Application.csproj` ✓ (verified)
- `Anela.Heblo.Application/Features/BackgroundJobs/Contracts/` folder exists ✓ (verified — already contains 3 DTO siblings)
- DTOs are already POCO classes with no problematic attributes ✓ (no `[Required]`, no `System.ComponentModel.DataAnnotations` baggage that complicated the earlier `UpdateJobStatusRequestBody` move)
- Namespace target collision check: no existing type in `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` shares any of the three names ✓ (verified by directory listing)

Implementation can start immediately.