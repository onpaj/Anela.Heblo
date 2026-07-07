# Architecture Review: Remove dead `RunExpeditionListPrintFixResult` type and enforce return-type contract on `useRunExpeditionListPrintFix`

## Skip Design: true

## Architectural Fit Assessment
This is a type-only cleanup confined to the frontend API-hooks layer (`frontend/src/api/hooks/`). It touches no runtime behavior, no HTTP contract, no UI, and no backend code. Verified directly:

- `useExpeditionListArchive.ts:32-34` declares `export interface RunExpeditionListPrintFixResult { totalCount: number }` and nothing in that file references it — it's orphaned.
- The actual mutation it was presumably meant to describe, `useRunExpeditionListPrintFix`, lives in the unrelated `useExpeditionList.ts` (line 5) and currently has an untyped `mutationFn` (`useMutation({...})` with no generics), so `useMutation`'s `TData` resolves to `any`.
- The sole consumer, `ExpeditionListArchivePage.tsx:61,130-131`, calls `runFixMutation.mutateAsync()` and reads `result.totalCount` — today with zero compiler enforcement since the type is `any`.
- Project-wide grep confirms `RunExpeditionListPrintFixResult` has no importers outside its declaring file, matching the spec's claim.

This fits the codebase's existing convention in the same file: `usePrintExpeditionOrder` (lines 29-59 of `useExpeditionList.ts`) already uses the `useMutation<TData, TError, TVariables>` pattern with an exported response interface (`BaseResponse` from `../../types/errors`) directly informing the mutation's generics. The proposed change simply brings `useRunExpeditionListPrintFix` in line with its sibling in the same file, using a locally-defined interface instead of a shared one (appropriate since `RunExpeditionListPrintFixResult`'s shape is specific to this one endpoint).

No architectural boundary is crossed: both files remain in `frontend/src/api/hooks/`, both are consumed only by page-level components, and the module each type lives in already matches the module owning the hook that returns it. There is no case for introducing a shared types file, a barrel export, or any structural change beyond what the spec describes.

## Proposed Architecture

### Component Overview
No new components. Existing structure:
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — archive/reprint feature hooks (`useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`). Loses its dead, misplaced interface.
- `frontend/src/api/hooks/useExpeditionList.ts` — print-fix and print-order mutations (`useRunExpeditionListPrintFix`, `usePrintExpeditionOrder`). Gains the interface it should have owned from the start, applied to its own mutation's generics.
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — sole consumer; unaffected in behavior, but `result.totalCount` (line 131) becomes type-checked instead of `any`-typed.

### Key Design Decisions
1. **Colocate the type with the hook that produces it, not with the module that happens to share naming similarity.** `RunExpeditionListPrintFixResult` belongs next to `useRunExpeditionListPrintFix`, consistent with every other response interface in both files (`GetExpeditionDatesResponse` next to `useExpeditionDates`, `ReprintExpeditionListResponse` next to `useReprintExpeditionList`, etc.).
2. **Reuse the existing generic-parameter pattern (`useMutation<TData, TError, TVariables>`) already established by `usePrintExpeditionOrder`** rather than inventing a new typing convention. `TVariables = void` since the hook takes no arguments — this is a standard, idiomatic choice already implied by the spec.
3. **No runtime validation is introduced.** `response.json()` remains an unchecked type assertion at the JS/TS boundary; this change only enforces the type at the point where the caller (`ExpeditionListArchivePage.tsx`) reads `.totalCount`. This is consistent with how every other hook in this file already behaves (e.g., `usePrintExpeditionOrder`, `useExpeditionDates`) — none of them do runtime schema validation of API responses. Introducing one here would be scope creep relative to the rest of the module.
4. **Do not touch the `apiClient as any` casts.** These exist identically in `usePrintExpeditionOrder` in the same file; fixing them is an unrelated, broader pattern change explicitly out of scope per the spec.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Both files stay exactly where they are:
```
frontend/src/api/hooks/
  useExpeditionListArchive.ts   (interface removed)
  useExpeditionList.ts          (interface added, mutation typed)
```

### Interfaces and Contracts
- Remove from `useExpeditionListArchive.ts` (lines 32-34):
  ```typescript
  export interface RunExpeditionListPrintFixResult {
    totalCount: number;
  }
  ```
  along with the blank line it leaves behind, to avoid a stray double-blank-line diff artifact between `ReprintExpeditionListResponse` and the `// --- Query Keys ---` section comment.

- Add to `useExpeditionList.ts`, directly above `useRunExpeditionListPrintFix`:
  ```typescript
  export interface RunExpeditionListPrintFixResult {
    totalCount: number;
  }
  ```
  This is a plain TypeScript interface for a frontend API-response shape, not a C# DTO — CLAUDE.md's "DTOs are classes, never records" rule governs backend/OpenAPI-generated contract types and does not apply here; `interface` is the file's existing convention for these shapes (every other response type in both files is an `interface`, not a `class` or `type` alias), so no deviation is warranted.

- Type the mutation itself:
  ```typescript
  export const useRunExpeditionListPrintFix = () => {
    return useMutation<RunExpeditionListPrintFixResult, Error, void>({
      mutationFn: async (): Promise<RunExpeditionListPrintFixResult> => {
        ...
        return await response.json();
      },
    });
  };
  ```
  This mirrors `usePrintExpeditionOrder`'s `useMutation<BaseResponse, Error, { orderCode: string }>` shape one-for-one, substituting `void` for the variables generic since this mutation takes no input.

### Data Flow
Unchanged. `ExpeditionListArchivePage.tsx` → `useRunExpeditionListPrintFix().mutateAsync()` → `POST /api/expedition-list/run-fix` → JSON body `{ totalCount: number }` → displayed in a success toast (line 131). The only difference is that `result` in `mutateAsync()`'s resolved value is now statically known to be `RunExpeditionListPrintFixResult` instead of `any`, so `.totalCount` is checked against a `number` field instead of being an unchecked property access.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A currently-hidden importer of `RunExpeditionListPrintFixResult` from `useExpeditionListArchive.ts` exists that grep missed (e.g. via a barrel/re-export file) | Low | `npm run build` will fail loudly with a missing-export error if so; the spec's acceptance criteria already requires a clean build as the verification gate. Grep across `frontend/src` in this review found only the two hook files and no re-export barrels for this module. |
| Adding an explicit `RunExpeditionListPrintFixResult` type surfaces a latent type mismatch if the backend's actual JSON shape has drifted from `{ totalCount: number }` (e.g. field renamed) | Low | This is precisely the intended benefit of the change (per spec Background) — if `npm run build`/`lint` surface a new error at `ExpeditionListArchivePage.tsx:131`, that is a legitimate, valuable finding, not a defect in this change. Out of scope to fix if it surfaces (per spec's "Out of Scope"); should be flagged separately if it happens. |
| Blank-line/formatting churn in `useExpeditionListArchive.ts` beyond the minimal deletion, tripping a stricter lint/format rule | Low | FR-1's acceptance criteria explicitly calls for removing only the dead block and its trailing blank line, "consistent with surrounding style" — a single, minimal diff hunk. `npm run lint`/`dotnet format`-equivalent (ESLint/Prettier for FE) will catch any stray formatting issues. |

## Specification Amendments
None. The spec is precise, self-contained, and already identifies the correct acceptance criteria (build/lint clean, no other importers, behavior-neutral). No gaps found during exploration — the referenced line numbers (32-34 in the archive file, `usePrintExpeditionOrder`'s generic pattern) match the actual source exactly.

## Prerequisites
None. This change has no dependency on other in-flight work, no backend coordination, and no ordering constraints — it can be implemented and merged independently.
