# Specification: Remove dead `RunExpeditionListPrintFixResult` type and enforce return-type contract on `useRunExpeditionListPrintFix`

## Summary
`useExpeditionListArchive.ts` exports an interface, `RunExpeditionListPrintFixResult`, that has zero importers anywhere in the codebase and describes a hook (`useRunExpeditionListPrintFix`) that actually lives in a different file, `useExpeditionList.ts`. This spec covers deleting the orphaned type and adding an explicit, enforced return-type annotation to the real hook so a future backend contract change (e.g. renaming `totalCount`) is caught by TypeScript instead of silently passing through as `any`.

## Background
`ExpeditionListArchivePage.tsx` calls `useRunExpeditionListPrintFix()` (defined in `frontend/src/api/hooks/useExpeditionList.ts`) and reads `result.totalCount` off the mutation result. That hook's `mutationFn` returns `await response.json()` with no type annotation, so `useMutation`'s generic resolves to `any` — the `.totalCount` access is completely unchecked by the compiler.

Meanwhile, `useExpeditionListArchive.ts` — a different, unrelated hooks module for the archive/reprint feature — separately declares an interface named `RunExpeditionListPrintFixResult` that models the exact same `{ totalCount: number }` shape, but it is never imported or referenced by `useRunExpeditionListPrintFix` or anything else (confirmed via project-wide grep). This is dead code that misleadingly implies the archive module owns the print-fix feature, and it does nothing to enforce the actual contract at its real call site.

This is a small, mechanical cleanup with no behavioral or API changes: move the type definition to where it's actually used, and wire it into the mutation's generic type parameters.

## Functional Requirements

### FR-1: Delete the orphaned `RunExpeditionListPrintFixResult` interface from `useExpeditionListArchive.ts`
Remove lines 32–34 of `frontend/src/api/hooks/useExpeditionListArchive.ts`:

```typescript
export interface RunExpeditionListPrintFixResult {
  totalCount: number;
}
```

No other code in `useExpeditionListArchive.ts` references this interface, so no other changes are needed in that file.

**Acceptance criteria:**
- `RunExpeditionListPrintFixResult` no longer appears anywhere in `useExpeditionListArchive.ts`.
- A project-wide search (e.g. `grep -r "RunExpeditionListPrintFixResult" frontend/src`) after the change returns matches only in the new location defined by FR-2 (i.e., zero remaining references tied to the archive file).
- `frontend/src/api/hooks/useExpeditionListArchive.ts` otherwise remains unchanged (no reordering, no formatting churn beyond removing the dead block and the blank line it leaves behind, consistent with surrounding style).

### FR-2: Define and apply an explicit return-type contract for `useRunExpeditionListPrintFix`
In `frontend/src/api/hooks/useExpeditionList.ts`:

1. Add an exported interface (reusing the deleted name so any future search for it resolves at its true home) directly above `useRunExpeditionListPrintFix`:
   ```typescript
   export interface RunExpeditionListPrintFixResult {
     totalCount: number;
   }
   ```
2. Annotate the `useMutation` call with this type as its result generic, and annotate `mutationFn`'s return type:
   ```typescript
   export const useRunExpeditionListPrintFix = () => {
     return useMutation<RunExpeditionListPrintFixResult, Error, void>({
       mutationFn: async (): Promise<RunExpeditionListPrintFixResult> => {
         const apiClient = getAuthenticatedApiClient();
         const relativeUrl = `/api/expedition-list/run-fix`;
         const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

         const response = await (apiClient as any).http.fetch(fullUrl, {
           method: "POST",
           headers: { "Content-Type": "application/json" },
         });

         if (!response.ok) {
           const errorData = await response.json().catch(() => null);
           throw new Error(
             errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
           );
         }

         return await response.json();
       },
     });
   };
   ```
   The exact shape of the `useMutation` generic parameters (`TData, TError, TVariables`) must match the existing pattern used elsewhere in the same file (see `usePrintExpeditionOrder`, which uses `useMutation<BaseResponse, Error, { orderCode: string }>`) — `useRunExpeditionListPrintFix` takes no variables, so `TVariables` is `void`.
3. Do not change the runtime behavior, the HTTP call, error handling, or the endpoint path — this is a type-only change.

**Acceptance criteria:**
- `useRunExpeditionListPrintFix`'s `useMutation` call has an explicit `RunExpeditionListPrintFixResult` (or equivalent) type parameter — no more implicit `any` return.
- `result.totalCount` in `ExpeditionListArchivePage.tsx` (wherever it consumes this hook's mutation result) type-checks against `totalCount: number` with no `any` involved.
- If the backend response shape ever omits or renames `totalCount`, this is a type-only assertion at the JS/TS boundary (since `response.json()` is inherently untyped at runtime) — TypeScript enforcement is at the call-site usage of the typed result, not at deserialization. Note this limitation explicitly in code only if a comment already exists nearby; otherwise no new comment is required.
- `npm run build` and `npm run lint` in `frontend/` succeed with no new errors or warnings introduced by this change.
- No other file that imports from `useExpeditionList.ts` or `useExpeditionListArchive.ts` breaks as a result of this change (verified by build).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a compile-time-only type change with zero runtime behavior difference. No performance impact.

### NFR-2: Security
Not applicable — no change to authentication, authorization, data handling, or the API surface. The HTTP call, headers, and endpoint remain identical.

## Data Model
No backend or persisted data model changes. The only "data model" affected is the TypeScript-side shape of the print-fix mutation result:

```typescript
interface RunExpeditionListPrintFixResult {
  totalCount: number;
}
```

This mirrors the existing (already correct) runtime shape returned by `POST /api/expedition-list/run-fix` — no backend changes are required or implied.

## API / Interface Design
No HTTP API changes. This spec only touches TypeScript module boundaries:
- **Removed export:** `RunExpeditionListPrintFixResult` from `frontend/src/api/hooks/useExpeditionListArchive.ts`.
- **Added export:** `RunExpeditionListPrintFixResult` from `frontend/src/api/hooks/useExpeditionList.ts`.
- **Changed signature:** `useRunExpeditionListPrintFix()`'s returned `UseMutationResult` now has `TData = RunExpeditionListPrintFixResult` instead of the previously implicit `any`.

Any file currently importing `RunExpeditionListPrintFixResult` from `useExpeditionListArchive.ts` would need updating — but per the brief and a confirmed project-wide grep, no such importer exists today, so this is a non-breaking move in practice.

## Dependencies
- `@tanstack/react-query` (`useMutation` generics) — already a dependency, no version change.
- No new libraries, no backend/API changes, no other feature dependencies.

## Out of Scope
- Any change to the actual HTTP call, error handling, or endpoint (`/api/expedition-list/run-fix`) in `useRunExpeditionListPrintFix`.
- Fixing the pre-existing pattern of casting `apiClient` to `any` to access `.baseUrl` and `.http.fetch` (used identically in `usePrintExpeditionOrder` in the same file) — this is a separate, broader pattern not called out in the brief.
- Runtime validation/parsing of the JSON response (e.g. via a schema validator) to guard against a backend contract change at runtime — the brief's concern is strictly about compile-time type enforcement.
- Any change to `ExpeditionListArchivePage.tsx` itself, since the type change is expected to be a drop-in compatible narrowing of what was previously `any`.
- Renaming or restructuring `useExpeditionListArchive.ts` or `useExpeditionList.ts` beyond the specific additions/deletions described above.

## Open Questions
None.

## Status: COMPLETE
