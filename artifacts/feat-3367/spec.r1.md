# Specification: Expose ArticleGenerationStepStatus as Typed Enum Through API Contract

## Summary

`ArticleGenerationStepDto.Status` is currently serialized as a plain `string`, causing NSwag to omit the `ArticleGenerationStepStatus` TypeScript enum from the generated client. The frontend consequently relies on magic string comparisons and a loosely typed `Record<string, string>` dictionary. This change replaces the `string` field with the domain enum type end-to-end — DTO, handler, OpenAPI schema, generated TypeScript client, and the `ArticleDebugPanel` component — bringing `ArticleGenerationStepStatus` in line with the existing `ArticleStatus` pattern.

## Background

The Article module exposes two status enums from the domain layer:

- `ArticleStatus` — flows through `GetArticleResponse.Status` as the enum type; NSwag correctly emits a `ArticleStatus` TypeScript enum that the frontend imports and uses type-safely.
- `ArticleGenerationStepStatus` — flows through `ArticleGenerationStepDto.Status` as `string` after a `.ToString()` call in the handler. NSwag sees only `string` and emits no enum. The frontend hard-codes the string values `"Running"`, `"Succeeded"`, and `"Failed"` in `STEP_STATUS_COLORS`, `STEP_STATUS_LABELS`, and two inline comparisons.

The inconsistency means that renaming an enum member or adding a new one (e.g. `Skipped`) requires a manual text search across both layers rather than following the compiler. It also suppresses autocomplete and exhaustive-switch checking in the frontend.

## Functional Requirements

### FR-1: Change DTO field type to ArticleGenerationStepStatus

`ArticleGenerationStepDto.Status` in `GetArticleTraceResponse.cs` must be changed from `string` to `ArticleGenerationStepStatus`. The field initializer `= string.Empty` must be removed. The file must gain a `using Anela.Heblo.Domain.Features.Article;` directive (if not already present).

**Acceptance criteria:**
- `ArticleGenerationStepDto.Status` is declared as `public ArticleGenerationStepStatus Status { get; set; }`.
- The file compiles without warnings.
- No `= string.Empty` default remains on the property.

### FR-2: Remove .ToString() call in the handler

`GetArticleTraceHandler.cs` line 44 currently sets `Status = s.Status.ToString()`. This must be changed to `Status = s.Status` (direct enum assignment).

**Acceptance criteria:**
- `Status = s.Status.ToString()` no longer appears in the handler.
- `Status = s.Status` is the assignment, with no cast or conversion.
- `dotnet build` succeeds with no errors or warnings on the Article feature project.

### FR-3: NSwag emits ArticleGenerationStepStatus TypeScript enum

Because the DTO field is now an enum type, the OpenAPI schema generated at build time must include `ArticleGenerationStepStatus` as an enum schema. NSwag must emit a corresponding TypeScript enum in the generated client file alongside the existing `ArticleStatus` enum.

**Acceptance criteria:**
- After `npm run build` (which triggers OpenAPI client generation), the generated file contains an `export enum ArticleGenerationStepStatus { Running = ... }` declaration.
- The `ArticleGenerationStepDto` TypeScript interface references `ArticleGenerationStepStatus` for its `status` field, not `string`.

### FR-4: Update ArticleDebugPanel to use the generated enum

`frontend/src/features/articles/ArticleDebugPanel.tsx` must be updated to import `ArticleGenerationStepStatus` from the generated API client and replace all magic string usages:

- `STEP_STATUS_COLORS` must be typed `Record<ArticleGenerationStepStatus, string>` with enum-keyed entries.
- `STEP_STATUS_LABELS` must be typed `Record<ArticleGenerationStepStatus, string>` with enum-keyed entries.
- The inline comparisons `step.status === 'Running'` and `step.status === 'Failed'` must use enum member references (`ArticleGenerationStepStatus.Running`, `ArticleGenerationStepStatus.Failed`).

**Acceptance criteria:**
- No string literal `'Running'`, `'Succeeded'`, or `'Failed'` remains in `ArticleDebugPanel.tsx`.
- `STEP_STATUS_COLORS` and `STEP_STATUS_LABELS` are typed with `ArticleGenerationStepStatus` as the key type.
- TypeScript compilation (`npm run build`) succeeds with no type errors.
- `npm run lint` passes with no new warnings.

### FR-5: No behavioral regression

The visual and functional behavior of the debug panel must remain identical: color badges, Czech labels, the spinner on Running steps, and the error message block on Failed steps must all render correctly.

**Acceptance criteria:**
- Existing E2E or unit tests (if any) for `ArticleDebugPanel` pass unchanged.
- Manual verification: all three status states render the correct color class, label, and icon as before.

## Non-Functional Requirements

### NFR-1: Consistency

The implementation must match the `ArticleStatus` pattern in `GetArticleResponse` exactly: domain enum used directly in the DTO, no `.ToString()` in the handler, no string fallback on the frontend.

### NFR-2: No breaking API change

The JSON wire format for `ArticleGenerationStepStatus` serialized as an enum is `"Running"` / `"Succeeded"` / `"Failed"` by default in .NET's `System.Text.Json` (string-named enum values). This is identical to what `.ToString()` previously produced, so no consumers are broken. If NSwag or the OpenAPI serializer config uses integer enum serialization, a `[JsonConverter(typeof(JsonStringEnumConverter))]` attribute or global policy must be confirmed to be in place — consistent with how `ArticleStatus` is currently serialized.

### NFR-3: Build pipeline

Both `dotnet build` and `npm run build` must succeed cleanly after the change. The OpenAPI client regeneration happens as part of `npm run build`; no manual generation step should be required.

## Data Model

No schema or database changes. `ArticleGenerationStepStatus` is a read-only domain enum already persisted in the database as its underlying integer value (or string, depending on EF configuration). The change is purely in the application/presentation layer mapping.

Key types involved:

| Layer | Type | Location |
|---|---|---|
| Domain | `ArticleGenerationStepStatus` (enum) | `backend/.../Domain/Features/Article/ArticleGenerationStepStatus.cs` |
| Application DTO | `ArticleGenerationStepDto.Status` | `backend/.../GetArticleTrace/GetArticleTraceResponse.cs` |
| Application Handler | `GetArticleTraceHandler` | `backend/.../GetArticleTrace/GetArticleTraceHandler.cs` |
| Generated TS | `ArticleGenerationStepStatus` (enum) | `frontend/src/api/generated/api-client.ts` (auto-generated) |
| Frontend component | `ArticleDebugPanel` | `frontend/src/features/articles/ArticleDebugPanel.tsx` |

## API / Interface Design

No endpoint changes. The `GET /api/articles/{id}/trace` response shape is unchanged at the JSON level — `status` values remain the same strings. The only change is that NSwag now emits a typed schema for those strings, making them an enum in the OpenAPI document and in the generated TypeScript client.

OpenAPI schema change (illustrative):

```yaml
# Before
ArticleGenerationStepDto:
  properties:
    status:
      type: string

# After
ArticleGenerationStepDto:
  properties:
    status:
      $ref: '#/components/schemas/ArticleGenerationStepStatus'

ArticleGenerationStepStatus:
  type: string
  enum: [Running, Succeeded, Failed]
```

Frontend usage change (illustrative):

```ts
// Before
import { useArticleTraceQuery, ArticleGenerationStep } from '../../api/hooks/useArticleTrace';

const STEP_STATUS_COLORS: Record<string, string> = {
  Running: '...',
  ...
};

// After
import { ArticleGenerationStepStatus } from '../../api/generated/api-client';

const STEP_STATUS_COLORS: Record<ArticleGenerationStepStatus, string> = {
  [ArticleGenerationStepStatus.Running]: '...',
  [ArticleGenerationStepStatus.Succeeded]: '...',
  [ArticleGenerationStepStatus.Failed]: '...',
};
```

## Dependencies

- **NSwag / OpenAPI generation config** — must be confirmed to serialize enums as strings (not integers) consistently with `ArticleStatus`. Check the NSwag config file or global `JsonStringEnumConverter` registration. No change needed if it already matches `ArticleStatus` behavior.
- **Generated TypeScript client** — `frontend/src/api/generated/api-client.ts` is auto-generated on `npm run build`. No manual edit to the generated file is permitted; the change propagates automatically.
- **`useArticleTrace` hook** — re-exports `ArticleGenerationStep` type from the generated client. Verify that the `status` field type on the re-exported type also becomes `ArticleGenerationStepStatus` after regeneration; no manual hook changes should be needed unless the hook manually redeclares the type.

## Out of Scope

- Adding new members to `ArticleGenerationStepStatus` (e.g. `Skipped`, `Pending`). This spec fixes the type contract for the existing three members only.
- Changing how `ArticleGenerationStepStatus` is persisted in the database.
- Modifying any other DTO, handler, or frontend component beyond the four files listed in FR-1 through FR-4.
- Exhaustive-switch enforcement tooling (ESLint rules, Roslyn analyzers). That is a separate concern.
- Localisation of status labels beyond what already exists in `STEP_STATUS_LABELS`.

## Open Questions

None.

## Status: COMPLETE
