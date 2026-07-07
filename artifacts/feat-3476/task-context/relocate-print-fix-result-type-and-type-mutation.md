### task: relocate-print-fix-result-type-and-type-mutation
Perform a type-only, mechanical refactor across two files in `frontend/src/api/hooks/`. No runtime/behavioral changes, no backend changes, no UI changes.

**1. `frontend/src/api/hooks/useExpeditionListArchive.ts`**
- Delete the orphaned interface (lines ~32-34):
  ```typescript
  export interface RunExpeditionListPrintFixResult {
    totalCount: number;
  }
  ```
- Also remove the blank line it leaves behind so there isn't a stray double-blank-line between `ReprintExpeditionListResponse` and the `// --- Query Keys ---` section comment. No other changes to this file — no reordering, no unrelated formatting.

**2. `frontend/src/api/hooks/useExpeditionList.ts`**
- Add this interface directly above `useRunExpeditionListPrintFix`:
  ```typescript
  export interface RunExpeditionListPrintFixResult {
    totalCount: number;
  }
  ```
- Update the `useRunExpeditionListPrintFix` hook to add explicit generics to `useMutation` and an explicit return type on `mutationFn`, matching the existing `usePrintExpeditionOrder` pattern (`useMutation<TData, TError, TVariables>`) in the same file:
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
- Do not touch the `apiClient as any` casts, the HTTP call, error handling, or the endpoint path — this is purely adding type annotations. `TVariables` is `void` since the hook takes no arguments.

**Explicitly out of scope (do not touch):**
- `ExpeditionListArchivePage.tsx` — should remain a drop-in-compatible consumer; no edits expected there. If `npm run build` surfaces a new type error at its `result.totalCount` usage (~line 131), that indicates a genuine pre-existing backend/frontend contract drift — do not silently "fix" it by loosening types; report it rather than papering over it, per the arch review's noted risk.
- The `apiClient as any` cast pattern (unrelated, pre-existing, used identically elsewhere in the file).
- Any runtime validation of the JSON response.

**Acceptance criteria (from spec):**
- `RunExpeditionListPrintFixResult` no longer appears anywhere in `useExpeditionListArchive.ts`.
- `RunExpeditionListPrintFixResult` is now exported from `useExpeditionList.ts`, directly above `useRunExpeditionListPrintFix`.
- `useRunExpeditionListPrintFix`'s `useMutation` call has explicit generics `<RunExpeditionListPrintFixResult, Error, void>` — no more implicit `any` return.
- `result.totalCount` in `ExpeditionListArchivePage.tsx` type-checks against `totalCount: number` with no `any` involved.
- No behavioral change: HTTP method, headers, endpoint path, and error handling are byte-for-byte identical to before.

**Verification steps:**
1. `grep -r "RunExpeditionListPrintFixResult" frontend/src` — confirm the only match is in `useExpeditionList.ts` (zero remaining references in `useExpeditionListArchive.ts`).
2. `cd frontend && npm run build` — must succeed with no new errors introduced by this change (including in `ExpeditionListArchivePage.tsx`).
3. `cd frontend && npm run lint` — must succeed with no new warnings/errors, and no stray blank-line/formatting diff artifacts in `useExpeditionListArchive.ts`.
4. Manually diff both changed files to confirm the edits are minimal and match exactly what's described above (no unrelated reordering or formatting churn).
