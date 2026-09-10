# Design: Extract ExpeditionJobControlsBar from ExpeditionListArchivePage

## Component Design

### `ExpeditionJobControlsBar` (new)

- **File:** `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`
- **Signature:** `const ExpeditionJobControlsBar: React.FC = () => { ... }; export default ExpeditionJobControlsBar;`
- **Props:** none. No `Props` interface is declared — matching `GroupsGrid`/`UsersGrid` in `frontend/src/components/pages/access/`. The component is a self-resolving leaf: it neither receives data/handlers from its parent nor exposes any callback prop, since every side effect it produces (toast, query invalidation) is fully contained within itself.
- **Responsibility:** owns the three "operational controls" concerns currently embedded in `ExpeditionListArchivePage` — recurring-job trigger/toggle, print-fix trigger, and ad-hoc order printing — plus the permission checks that gate them.

**Internal hooks it calls (module-scope constants and helper move here too):**

| Concern | Hook / helper | Source module |
|---|---|---|
| Permission gating | `usePermissionsContext().hasPermission(...)` for `TRIGGER_JOBS_PERMISSION` (`"jobs.trigger.read"`) and `DISABLE_JOBS_PERMISSION` (`"jobs.disable.read"`) | `../../../auth/PermissionsContext` |
| Job status read | `useRecurringJobQuery(PRINT_JOB_NAME, canTriggerJob \|\| canToggleJob)` → `RecurringJobDto \| null` | `../../../api/hooks/useRecurringJobs` |
| Job trigger | `useTriggerRecurringJobMutation()` → `UseMutationResult<TriggerRecurringJobResponse, unknown, string>` | `../../../api/hooks/useRecurringJobs` |
| Job enable/disable | `useUpdateRecurringJobStatusMutation()` → `UseMutationResult<UpdateRecurringJobStatusResponse, unknown, { jobName: string; isEnabled: boolean }>` | `../../../api/hooks/useRecurringJobs` |
| Print-fix trigger (cross-module, confined here per FR-4) | `useRunExpeditionListPrintFix()` → `UseMutationResult<RunExpeditionListPrintFixResult, Error, void>` | `../../../api/hooks/useExpeditionList` |
| Toasts | `useToast()` → `{ showSuccess, showError }` | `../../../contexts/ToastContext` |
| Cache invalidation | `useQueryClient()` | `@tanstack/react-query` |
| Local UI state | `useState<boolean>` for `isPrintOrderModalOpen` | React |

**Module-scope constants/helpers relocated into this file** (no longer in the page):
- `PRINT_JOB_NAME = "print-picking-list"`
- `TRIGGER_JOBS_PERMISSION = "jobs.trigger.read"`
- `DISABLE_JOBS_PERMISSION = "jobs.disable.read"`
- `formatDateTime(iso: string | null): string` — duplicated here (its "Další běh: …" caller); the page keeps its own copy for the items-table "Nahráno" column. Two independent 7-line copies, not a shared util (see arch-review Decision/Amendment 1 — no new `utils/` module, surgical refactor only).

**What it renders** (verbatim markup moved from the page's header-right `<div>`, no class/DOM changes):
- Conditionally (`canTriggerJob || canToggleJob`): the "Expediční robot" toggle switch (only if `canToggleJob`) + "Další běh: {formatDateTime(...)}" text.
- "Tisknout zakázku" button → opens `<PrintOrderModal isOpen onClose onSuccess={handlePrintOrderSuccess} />` (from `../../../components/modals/PrintOrderModal`).
- "Spustit tisk oprav" button (spinner while `runFixMutation.isPending`) → `handleRunFix`.
- Conditionally (`canTriggerJob`): "Spustit tisk" button (spinner while `triggerJobMutation.isPending`) → `handleRunJob`.

**Internal handlers** (moved verbatim, same success/error toast copy, same invalidation behavior):
- `handleRunJob` — `triggerJobMutation.mutateAsync(PRINT_JOB_NAME)`; no `expeditionListArchive` invalidation (job list is invalidated internally by the hook itself; preserves today's asymmetry, out of scope to fix).
- `handleToggleJob` — `updateStatusMutation.mutateAsync({ jobName: PRINT_JOB_NAME, isEnabled: !printJob.isEnabled })`; no-op if `printJob` is falsy.
- `handleRunFix` — `runFixMutation.mutateAsync()`; toast includes `result.totalCount`; no `expeditionListArchive` invalidation (preserves today's asymmetry).
- `handlePrintOrderSuccess(orderCode: string)` — closes the modal, shows success toast, calls `queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive })` (from `../../../api/client`). This is the one flow that must affect the page's data; it works with no props because `useQueryClient()` resolves the same client instance from the shared `QueryClientProvider` in `App.tsx` regardless of which component calls it — the page's `useExpeditionDates`/`useExpeditionListsByDate` queries refetch as a side effect of the shared cache, not a callback.

**Extension point:** if a future requirement needs the parent to react to a bar event, add an explicit `interface ExpeditionJobControlsBarProps { onXxx?: () => void }` at that time. Nothing in this refactor's scope needs one — do not add it pre-emptively (YAGNI).

### `ExpeditionListArchivePage` (reduced)

- **File:** `frontend/src/pages/ExpeditionListArchivePage.tsx` (path unchanged; `App.tsx`'s import and the `/logistics/expedition-archive` route are untouched).
- **Responsibility after the split:** archive browsing only.

**What stays:**
- `useScreenView('Logistics', 'ExpeditionArchive')` — fires exactly once, from the page, as before.
- Date pagination state (`page`) and `selectedDate` state, plus the auto-select-first-date `useEffect`.
- `useExpeditionDates(page, PAGE_SIZE)`, `useExpeditionListsByDate(selectedDate)`, `useReprintExpeditionList()`, `getExpeditionListDownloadUrl` — all from `../api/hooks/useExpeditionListArchive` (the page's only remaining feature-hook import).
- `getAuthenticatedFetch`, `QUERY_KEYS` from `../api/client`; `useQueryClient()` and `useToast()` (used only by `handleOpen`, `handleRefresh`, and `handleReprintConfirm` — archive-only flows).
- The "Obnovit" refresh button and `handleRefresh` (invalidates `QUERY_KEYS.expeditionListArchive`).
- The date sidebar (list + pagination controls), the items table (`formatFileSize`, page-local `formatDateTime` for the "Nahráno" column), `handleOpen` (blob fetch → open in new tab).
- The reprint confirmation dialog: `reprintConfirm` state, `handleReprintConfirm`.
- `<h1>Archiv expedičních listů</h1>` and the outer layout.

**What moves out:** all recurring-job hooks/handlers, `useRunExpeditionListPrintFix`, `usePermissionsContext`, `PrintOrderModal` and its trigger/state/success-handler, and the three constants — all into `ExpeditionJobControlsBar`.

**New render:** `<ExpeditionJobControlsBar />` (no props) is rendered in the header-right area, in the same DOM position it occupies today, immediately followed by the "Obnovit" button.

**Post-refactor import list for the page** (only feature-hook imports remaining):
```ts
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../api/hooks/useExpeditionListArchive";
import ExpeditionJobControlsBar from "../components/pages/ExpeditionListArchive/ExpeditionJobControlsBar";
```
No import from `../api/hooks/useExpeditionList`, `../api/hooks/useRecurringJobs`, `../auth/PermissionsContext`, or `../components/modals/PrintOrderModal` remains in the page (FR-2/FR-4 acceptance criteria).

## Data Schemas

No new schemas — this is a pure component-boundary refactor. The new component consumes the following pre-existing shapes unchanged; only their *import site* moves from the page to `ExpeditionJobControlsBar`.

**From `frontend/src/api/hooks/useRecurringJobs.ts`:**
```ts
RecurringJobDto                          // returned by useRecurringJobQuery(jobName, enabled) as { data: RecurringJobDto | null }
                                          // consumed fields: isEnabled: boolean, nextRunAt: Date | null
TriggerRecurringJobResponse              // useTriggerRecurringJobMutation()'s mutateAsync resolves to this (fields unused by the bar beyond success/failure)
UpdateRecurringJobStatusResponse         // useUpdateRecurringJobStatusMutation()'s mutateAsync resolves to this
// mutation input: { jobName: string; isEnabled: boolean }
```
`useRecurringJobQuery`/mutations invalidate their own `recurringJobs` query keys (list + per-job detail) internally on success — unrelated to `QUERY_KEYS.expeditionListArchive` (see Component Design's invalidation-asymmetry note).

**From `frontend/src/api/hooks/useExpeditionList.ts`:**
```ts
interface RunExpeditionListPrintFixResult {
  totalCount: number;
}
// useRunExpeditionListPrintFix(): UseMutationResult<RunExpeditionListPrintFixResult, Error, void>
// mutationFn calls client.expeditionList_RunFix() (generated API client) with no request body.
// No onSuccess invalidation defined on this hook today — preserved as-is.
```

**From `frontend/src/components/modals/PrintOrderModal.tsx`:**
```ts
interface PrintOrderModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (orderCode: string) => void;
}
```
The bar owns `isPrintOrderModalOpen` and wires `onClose={() => setIsPrintOrderModalOpen(false)}`, `onSuccess={handlePrintOrderSuccess}`. `PrintOrderModal` itself is unchanged; its own internal coupling to `usePrintExpeditionOrder` (from `useExpeditionList.ts`) is pre-existing and out of scope.

**From `frontend/src/auth/PermissionsContext.tsx`:**
```ts
interface PermissionsContextValue {
  hasPermission: (perm: string) => boolean;
  // (other fields present on the context but unused by this component)
}
```

**Query-key shape used for invalidation (from `frontend/src/api/client.ts`):**
```ts
QUERY_KEYS.expeditionListArchive   // array-form key prefix; invalidated by:
                                   //  - ExpeditionJobControlsBar.handlePrintOrderSuccess (new owner)
                                   //  - ExpeditionListArchivePage.handleRefresh (unchanged, page-owned)
                                   //  - useReprintExpeditionList's internal onSuccess (unchanged, page-owned hook)
```

**From `frontend/src/api/hooks/useExpeditionListArchive.ts`** (page-owned only, listed for completeness — the bar has zero dependency on these, per FR-2):
```ts
interface ExpeditionListItemDto { blobPath: string; fileName: string; listId: string; createdOn: string | null; contentLength: number | null; }
interface GetExpeditionDatesResponse { dates: string[]; totalCount: number; page: number; pageSize: number; }
interface GetExpeditionListsByDateResponse { items: ExpeditionListItemDto[]; }
interface ReprintExpeditionListResponse { success: boolean; errorCode: string | null; params: Record<string, string> | null; }
```
