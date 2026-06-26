# Architecture Review: Expose ArticleGenerationStepStatus as Typed Enum Through API Contract

## Skip Design: true

## Architectural Fit Assessment

This is a pure type-hygiene refactor that brings `ArticleGenerationStepStatus` in line with the established `ArticleStatus` pattern. The existing infrastructure already supports this end-to-end:

- `JsonStringEnumConverter` is registered globally in `Program.cs` (`AddJsonOptions`) — the same converter that already serializes `ArticleStatus` as `"Generated"` etc. will do the same for `ArticleGenerationStepStatus` with zero additional configuration.
- NSwag's `enumStyle: "Enum"` in `nswag.frontend.json` instructs the generator to emit TypeScript `enum` declarations for any C# enum it encounters in the schema — it already does this for `ArticleStatus`. Changing `Status` from `string` to the domain enum type is the only gate.
- Wire format is unchanged. `ArticleGenerationStepStatus.Running.ToString()` currently produces `"Running"`, and `JsonStringEnumConverter` on the typed enum produces the identical JSON value. There is no breaking change.
- The frontend hook (`useArticleTrace.ts`) maintains its own `ArticleGenerationStep` interface with `status: string` and re-maps the generated DTO. This layer is the correct place to adopt the generated enum type.
- `ArticleDebugPanel.tsx` uses `Record<string, string>` lookup dictionaries keyed on magic strings (`"Running"`, `"Succeeded"`, `"Failed"`) and a direct string comparison for the spinner (`step.status === 'Running'`). These must be updated to reference the generated enum.

No new layers, abstractions, or service registrations are needed. The change is four files plus a regenerated client.

## Proposed Architecture

### Component Overview

```
Domain
  ArticleGenerationStepStatus (enum) — unchanged, source of truth

Application
  ArticleGenerationStepDto.Status: string  →  ArticleGenerationStepStatus
  GetArticleTraceHandler: s.Status.ToString()  →  s.Status

API / NSwag pipeline
  nswag.frontend.json — unchanged (enumStyle: "Enum" already correct)
  → regenerated api-client.ts now contains ArticleGenerationStepStatus enum
  → ArticleGenerationStepDto.status: string  →  ArticleGenerationStepStatus

Frontend
  useArticleTrace.ts — adopt ArticleGenerationStepStatus from generated client
  ArticleDebugPanel.tsx — replace magic-string keys with enum references
```

### Key Design Decisions

#### Decision 1: Where to apply the type change in the DTO

**Options considered:**
- (A) Change `ArticleGenerationStepDto.Status` to `ArticleGenerationStepStatus` and remove `.ToString()` in the handler.
- (B) Keep `string` on the DTO, add a `[JsonConverter]` attribute to force enum semantics.

**Chosen approach:** Option A.

**Rationale:** Option B would require a custom attribute just to work around a type that exists in the domain. Option A is consistent with how `GetArticleResponse.Status` is typed (`ArticleStatus` directly, no converter attribute). The global `JsonStringEnumConverter` handles serialization automatically.

#### Decision 2: Handling the frontend hook interface

**Options considered:**
- (A) Change `ArticleGenerationStep.status` in the hand-written hook interface from `string` to `ArticleGenerationStepStatus`.
- (B) Keep the hook's local `status: string` and absorb the enum at the mapping layer inside `useArticleTraceQuery`.

**Chosen approach:** Option A — change the interface to `ArticleGenerationStepStatus` and remove the re-export interface entirely in favour of re-exporting the generated type.

**Rationale:** The hand-written `ArticleGenerationStep` interface in `useArticleTrace.ts` duplicates the generated `IArticleGenerationStepDto`. Since we now have a proper typed enum, propagating it through the hook interface means `ArticleDebugPanel` receives compile-time safety. The mapping inside `useArticleTraceQuery` already does `step.status ?? ''` — replace with `step.status ?? ArticleGenerationStepStatus.Running` (or the appropriate fallback) and drop the `string` cast.

#### Decision 3: Updating ArticleDebugPanel lookup tables

**Options considered:**
- (A) Replace string literal keys in `STEP_STATUS_COLORS` and `STEP_STATUS_LABELS` with enum values.
- (B) Leave the dictionaries as `Record<string, string>` but feed enum values at call site.

**Chosen approach:** Option A — type the lookup tables as `Record<ArticleGenerationStepStatus, string>` (or `Partial<Record<...>>` if exhaustiveness is not guaranteed).

**Rationale:** Typed record keys give exhaustiveness checking for free and eliminate the possibility of a key typo introducing a silent miss. The inline comparison `step.status === 'Running'` becomes `step.status === ArticleGenerationStepStatus.Running`.

## Implementation Guidance

### Directory / Module Structure

All changes are confined to existing files — no new files are created:

```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/
  GetArticleTraceResponse.cs          ← change Status field type
  GetArticleTraceHandler.cs           ← remove .ToString()

frontend/src/api/generated/
  api-client.ts                       ← regenerated (do not edit manually)

frontend/src/api/hooks/
  useArticleTrace.ts                  ← adopt ArticleGenerationStepStatus, update mapping

frontend/src/features/articles/
  ArticleDebugPanel.tsx               ← use enum in lookup tables and comparisons
```

### Interfaces and Contracts

**`ArticleGenerationStepDto` (after change):**
```csharp
public sealed class ArticleGenerationStepDto
{
    // ...
    public ArticleGenerationStepStatus Status { get; set; }  // was: string
    // ...
}
```

**`GetArticleTraceHandler` (after change):**
```csharp
Status = s.Status,  // was: s.Status.ToString()
```

**Generated `IArticleGenerationStepDto` (after client regeneration):**
```typescript
export interface IArticleGenerationStepDto {
    status?: ArticleGenerationStepStatus;  // was: string
    // ...
}

export enum ArticleGenerationStepStatus {
    Running = "Running",
    Succeeded = "Succeeded",
    Failed = "Failed",
}
```

**`useArticleTrace.ts` (after change):**

The hand-written `ArticleGenerationStep` interface should replace `status: string` with `status: ArticleGenerationStepStatus`. Import `ArticleGenerationStepStatus` from the generated client. The mapping line becomes:
```typescript
status: step.status ?? ArticleGenerationStepStatus.Running,
```
(Choose the most defensive default; `Running` is reasonable for an in-progress trace with missing data, but the spec does not prescribe a fallback — pick any member, since `??` only fires when the server omits the field entirely, which cannot happen for a typed required property.)

**`ArticleDebugPanel.tsx` (after change):**
```typescript
import { ArticleGenerationStepStatus } from '../../api/generated/api-client';

const STEP_STATUS_COLORS: Record<ArticleGenerationStepStatus, string> = {
  [ArticleGenerationStepStatus.Running]:   'bg-blue-100 text-blue-700',
  [ArticleGenerationStepStatus.Succeeded]: 'bg-green-100 text-green-700',
  [ArticleGenerationStepStatus.Failed]:    'bg-red-100 text-red-700',
};

const STEP_STATUS_LABELS: Record<ArticleGenerationStepStatus, string> = {
  [ArticleGenerationStepStatus.Running]:   'Běží',
  [ArticleGenerationStepStatus.Succeeded]: 'Dokončeno',
  [ArticleGenerationStepStatus.Failed]:    'Chyba',
};

// Spinner guard:
{step.status === ArticleGenerationStepStatus.Running && <Loader2 ... />}
```
The `?? 'bg-gray-100 text-gray-700'` fallback on `colorClass` can be kept or dropped — with an exhaustive `Record` it will never fire at runtime.

The `ArticleGenerationStep` interface import from `useArticleTrace` is still used for the `StepCard` prop type — no change needed there unless you choose to re-export the generated type instead.

### Data Flow

```
DB (string column) → EF Core → Domain ArticleGenerationStepStatus enum
  → GetArticleTraceHandler maps to ArticleGenerationStepDto.Status (enum, no .ToString())
  → JsonStringEnumConverter serializes as "Running" / "Succeeded" / "Failed"
  → NSwag reads OpenAPI schema (string enum with enum values) → emits TS enum
  → api-client.ts: ArticleGenerationStepDto.status?: ArticleGenerationStepStatus
  → useArticleTraceQuery maps step.status to ArticleGenerationStep.status (typed enum)
  → ArticleDebugPanel uses enum for lookup and comparison
```

Wire bytes are identical before and after. The only observable change is that TypeScript now has a compile-time enum where it previously had `string`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| NSwag may not pick up the enum if the DTO property has a backing type that isn't directly visible from the schema | Low | The `ArticleStatus` field on `GetArticleResponse` already proves this path works — same project, same NSwag config, same global converter. Verify in generated client after `dotnet build`. |
| Frontend build breaks if generated client is stale when frontend code is updated | Low | Always regenerate the client (`dotnet build` triggers NSwag) before touching frontend files. CI and the validation checklist (`dotnet build` then `npm run build`) enforce this order. |
| `status: string` fallback in `useArticleTrace.ts` mapping uses `?? ''` — the empty string becomes invalid after the type tightens | Low | Replace `?? ''` with `?? ArticleGenerationStepStatus.Running` (or another member) when updating the hook. TypeScript will flag the empty-string assignment at compile time, so the risk is self-revealing. |
| Unit tests in `GetArticleTraceHandlerTests.cs` assert `Status` as string literals | Low | Update assertions to compare against enum members (e.g. `ArticleGenerationStepStatus.Running`) or their `.ToString()` equivalents. Check the test file for any `"Running"` string assertions before closing the task. |

## Specification Amendments

None. The spec is accurate and complete for this scope. One clarification worth noting: FR-4 ("Update `ArticleDebugPanel.tsx`") implicitly requires updating `useArticleTrace.ts` first — the hook is the intermediary between the generated client and the component. The implementation order should be:

1. DTO + handler (BE)
2. `dotnet build` (regenerates `api-client.ts`)
3. `useArticleTrace.ts` (adopt enum)
4. `ArticleDebugPanel.tsx` (use enum)
5. `npm run build`

## Prerequisites

- No migrations required (enum stored as string in DB, column unchanged).
- No new NuGet or npm packages.
- No feature-flag gating.
- `dotnet build` must run before frontend changes to ensure the generated client reflects the type change.
- Verify `GetArticleTraceHandlerTests.cs` for string-literal status assertions and update them to use the enum type.
