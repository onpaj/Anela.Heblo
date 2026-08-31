# Architecture Review: Extract ExpeditionJobControlsBar from ExpeditionListArchivePage

## Skip Design: true

Pure frontend refactor: no new visual components, no layout change, no route change. `ExpeditionJobControlsBar` reproduces existing DOM/markup/CSS classes verbatim in a new file. `docs/design/ui_design_document.md` and `docs/design/layout_definition.md` do not need to be consulted because nothing visual is being decided — this is a component-boundary move, not a UI change.

## Architectural Fit Assessment

This fits an established codebase pattern precisely, not a novel one. I read `frontend/src/pages/AccessManagementPage.tsx` and `frontend/src/components/pages/access/GroupsGrid.tsx`/`UsersGrid.tsx`:

- `AccessManagementPage.tsx` is a thin shell (tab routing + `<h1>` + nav) that renders `<GroupsGrid />` or `<UsersGrid />` with **zero props**.
- `GroupsGrid.tsx` is a `React.FC` that calls its own data hooks (`useGroups`, `useCatalogue`, `useDeleteGroup` from `../../../api/hooks/useAccessManagement`), owns its own local state (`search`), and handles its own loading/error rendering — nothing is threaded down from the page.

`ExpeditionListArchivePage.tsx` today violates this convention: it is the "GroupsGrid" and the "page" collapsed into one 407-line file, resolving 6 hooks (2 archive, 3 recurring-job, 1 print-fix) plus permission checks and a modal, for four unrelated concerns. The fix is not a new pattern — it is applying the existing `GroupsGrid`/`UsersGrid` split to this page.

The one nuance specific to this feature (that access management doesn't have) is FR-4's cross-module import: `useRunExpeditionListPrintFix` belongs to the `ExpeditionList` module's hook file (`api/hooks/useExpeditionList.ts`), not `ExpeditionListArchive`'s own (`api/hooks/useExpeditionListArchive.ts`). The frontend has no enforced module-boundary mechanism analogous to the backend's `ModuleBoundariesTests` (that reflection-based test only scans `Anela.Heblo.Application`/`Domain` namespaces) — cross-feature imports between `pages/`/`components/pages/*` are conventional, not compiler-enforced. This refactor does not add such enforcement (out of scope per spec); it only reduces the blast radius of the existing coupling to one clearly-named file, which is the correct-sized fix for a "pure refactor" ticket.

Integration points:
- `App.tsx` — unaffected (page path, route, and default export unchanged).
- `frontend/src/api/hooks/useExpeditionListArchive.ts`, `useRecurringJobs.ts`, `useExpeditionList.ts` — unchanged, only their import sites move.
- `PrintOrderModal.tsx` — unchanged; its own pre-existing coupling to `useExpeditionList` (via `usePrintExpeditionOrder`) is untouched and out of scope.

## Proposed Architecture

### Component Overview

```
frontend/src/pages/ExpeditionListArchivePage.tsx        (route target, unchanged path)
│
│  owns: useScreenView, page/date state, useExpeditionDates,
│        useExpeditionListsByDate, useReprintExpeditionList,
│        getExpeditionListDownloadUrl, "Obnovit" button,
│        date sidebar, items table, reprint confirm dialog
│
└── renders ──────────────────────────────────────────────┐
                                                            ▼
                              frontend/src/components/pages/ExpeditionListArchive/
                                              ExpeditionJobControlsBar.tsx
                              (React.FC, NO PROPS — fully self-contained)
                              │
                              │  owns: usePermissionsContext (TRIGGER_JOBS_PERMISSION,
                              │        DISABLE_JOBS_PERMISSION), useRecurringJobQuery,
                              │        useTriggerRecurringJobMutation,
                              │        useUpdateRecurringJobStatusMutation,
                              │        useRunExpeditionListPrintFix (← the confined
                              │        cross-module import, ExpeditionList's hook),
                              │        useToast, useQueryClient, isPrintOrderModalOpen
                              │
                              └── renders <PrintOrderModal /> (../../../components/modals/PrintOrderModal)
```

`ExpeditionListArchivePage` and `ExpeditionJobControlsBar` share **no props, no lifted state, no callback wiring**. They are siblings composed by rendering, exactly like `AccessManagementPage` + `GroupsGrid`. The only thing they still share implicitly is the query cache: both invalidate `QUERY_KEYS.expeditionListArchive` independently (page on "Obnovit" and reprint-success; bar on print-order success) via their own `useQueryClient()` calls — this is normal React Query usage and requires no coordination since both live under the same `QueryClientProvider`.

### Key Design Decisions

#### Decision 1: New component lives at `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`; the page file does not move

**Options considered:**
- (A) `components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`, page stays at `pages/ExpeditionListArchivePage.tsx` — the `AccessManagementPage`/`GroupsGrid` precedent.
- (B) Move the page itself to `pages/ExpeditionListArchive/ExpeditionListArchivePage.tsx` with a co-located `components/` folder — the `InvoiceClassification` precedent (`pages/InvoiceClassification/InvoiceClassificationPage.tsx` + `pages/InvoiceClassification/components/`).

**Chosen approach:** (A).

**Rationale:** Both precedents genuinely exist in this codebase, but they answer different problems. `InvoiceClassification`'s page-folder pattern applies when a page has *multiple sibling page files* that all need the same feature-local components folder (it has `InvoiceClassificationPage.tsx` and `ClassificationHistoryPage.tsx` sharing `components/`). `ExpeditionListArchivePage` has exactly one page file — there is nothing to share a page-local folder with, so restructuring into a subfolder buys nothing and forces an unnecessary edit to `App.tsx`'s import path plus every other file that imports the page. `AccessManagementPage`/`GroupsGrid` is the closer analog: one page, one extracted operational sub-component, page path untouched. Given this ticket's stated framing as a "pure refactor" with "no behavior change" and the project's "surgical changes" rule (CLAUDE.md), minimizing the diff by not moving the page file is the correct call. This directly resolves spec Open Question 1 in favor of the spec's own stated assumption.

#### Decision 2: `ExpeditionJobControlsBar` is fully self-contained (no props)

**Options considered:**
- (A) Self-contained: the bar calls `useRecurringJobQuery`/mutations/`useRunExpeditionListPrintFix`/`usePermissionsContext`/`useToast`/`useQueryClient` itself, per the `GroupsGrid` model.
- (B) Prop-driven: the page resolves all hooks and passes data + handlers down as props, per the brief's literal wording ("receives the relevant hooks and handlers as props").

**Chosen approach:** (A).

**Rationale:** The brief's own stated goal — "archive browsing tests would then only mock the three archive-related hooks" — is unreachable under (B): if the page still calls `useRecurringJobQuery`, `useTriggerRecurringJobMutation`, `useUpdateRecurringJobStatusMutation`, `useRunExpeditionListPrintFix`, and `usePermissionsContext` in order to hand them down as props, `ExpeditionListArchivePage.test.tsx` must still mock every one of those modules — nothing is actually removed from the page's test dependency graph, only relocated to a prop list. (A) is the only option that satisfies FR-2's acceptance criterion that the new test file "must not need to mock `useExpeditionListArchive`" *and* the (implied, symmetric) requirement that the page's test no longer needs to mock `useExpeditionList`/`useRecurringJobs`/`PermissionsContext`. It is also literally the pattern already in production in this codebase (`GroupsGrid`/`UsersGrid` take no props and self-resolve). This resolves spec Open Question 2: self-contained, no props, confirmed correct reading.

#### Decision 3: The `useExpeditionList` cross-module import is relocated, not eliminated

**Options considered:**
- (A) Confine `useRunExpeditionListPrintFix` import to `ExpeditionJobControlsBar.tsx` only (spec's FR-4).
- (B) Invert the dependency now, per the `ILeafletKnowledgeSource` pattern in `development_guidelines.md` (consumer-owned contract + provider adapter) — i.e. have `ExpeditionListArchive` define its own contract for "run print fix" and have `ExpeditionList` implement it.
- (C) Leave the import where it is today (page-level), do nothing.

**Chosen approach:** (A).

**Rationale:** (B) is the architecturally "correct" long-term fix, but it is a backend/contract change (a new hook, or a new endpoint ownership boundary) that this ticket explicitly rules out — the spec's Out of Scope section says as much, and the brief frames this as a pure frontend extraction. The `ILeafletKnowledgeSource` inversion pattern is a *backend* Application-layer pattern (module-to-module contracts through `Contracts/` interfaces) and doesn't map cleanly onto two React hook files without inventing new backend surface area. (C) fails the brief's explicit finding. (A) is the right-sized fix: it doesn't pretend to solve module coupling it isn't chartered to solve, but it does shrink the violation from "leaks into the page that also does archive browsing" to "lives in the one file whose name says `ExpeditionJobControlsBar` and whose job is exactly these operational concerns." This resolves spec Open Question 3 by explicit agreement with the spec's own position: confine, don't eliminate, don't opportunistically fix the invalidation asymmetry either (see Risks).

## Implementation Guidance

### Directory / Module Structure

New files:
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` — new component.
- `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` — new test file, migrated from the "expedition robot toggle" and "permission gating" `describe` blocks in the current page test.

Modified files:
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — strip to archive-browsing only; render `<ExpeditionJobControlsBar />` in place of the removed header-right JSX.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — remove the two migrated `describe` blocks and their associated `jest.mock` calls for `useExpeditionList`, `useRecurringJobs`, `PermissionsContext`; keep the "refresh button" block as-is.

Unmodified (import site only moves, no internal changes):
- `frontend/src/api/hooks/useExpeditionListArchive.ts`
- `frontend/src/api/hooks/useExpeditionList.ts`
- `frontend/src/api/hooks/useRecurringJobs.ts`
- `frontend/src/components/modals/PrintOrderModal.tsx`
- `frontend/src/App.tsx` (route + page import path both unchanged)

No backend files are touched. No new directories beyond the one `ExpeditionListArchive/` folder (+ its `__tests__/`).

### Interfaces and Contracts

`ExpeditionJobControlsBar`:
```tsx
// frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx
const ExpeditionJobControlsBar: React.FC = () => { ... };
export default ExpeditionJobControlsBar;
```
No props interface — matches `GroupsGrid`/`UsersGrid`'s `React.FC` with no generic argument. If a future requirement needs the parent to react to a bar event (e.g. the page wants to know when print-fix ran), add an explicit `interface ExpeditionJobControlsBarProps { onXxx?: () => void }` at that time — do not pre-emptively add one now (YAGNI; nothing in this refactor's scope needs it, per spec's own API/Interface Design section).

Constants and helpers that move into the new file (their only remaining caller lives there):
- `PRINT_JOB_NAME = "print-picking-list"`
- `TRIGGER_JOBS_PERMISSION = "jobs.trigger.read"`
- `DISABLE_JOBS_PERMISSION = "jobs.disable.read"`
- `formatDateTime` (used only by the "Další běh: …" text) — moves to `ExpeditionJobControlsBar.tsx`. Note the items table in the page also calls `formatDateTime` for the "Nahráno" column (line 332 of the current file) — **this caller stays in the page**, so `formatDateTime` must be duplicated (or kept in the page and NOT moved) rather than deleted from the page. Re-reading FR-3's acceptance criteria: the spec's parenthetical about `formatDateTime` undersells this — the items table clearly still needs it. Resolution: keep `formatDateTime` defined in **both** files as a small private module-scope function (it's a 7-line pure function with no dependencies; duplicating it is cheaper and safer than extracting a shared util for a single 7-line formatter used by exactly two call sites in a "no behavior change" ticket). Do not create a new shared `utils/` module for this — that would be scope creep for a refactor ticket that must not touch unrelated files.
- `formatFileSize` stays only in the page (items table only) — unchanged from spec.

Imports that must appear only in `ExpeditionJobControlsBar.tsx` after the refactor (per FR-2/FR-4 acceptance criteria):
```ts
import { useRunExpeditionListPrintFix } from "../../../api/hooks/useExpeditionList";
import {
  useTriggerRecurringJobMutation,
  useRecurringJobQuery,
  useUpdateRecurringJobStatusMutation,
} from "../../../api/hooks/useRecurringJobs";
import { usePermissionsContext } from "../../../auth/PermissionsContext";
import { useToast } from "../../../contexts/ToastContext";
import { useQueryClient } from "@tanstack/react-query";
import { QUERY_KEYS } from "../../../api/client";
import PrintOrderModal from "../../../components/modals/PrintOrderModal";
```

Imports that must remain (and become the *only* feature-hook imports) in `ExpeditionListArchivePage.tsx`:
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

### Data Flow

**Job trigger flow (unchanged behavior, new owner):** user clicks "Spustit tisk" → `ExpeditionJobControlsBar`'s `handleRunJob` → `triggerJobMutation.mutateAsync(PRINT_JOB_NAME)` (from `useTriggerRecurringJobMutation`, whose own `onSuccess` invalidates the `recurringJobs` query key internally, per `useRecurringJobs.ts` line 117-120 — unrelated to `expeditionListArchive`) → bar's local success/error toast. No interaction with the page or its query keys.

**Print-fix flow (unchanged behavior, new owner):** user clicks "Spustit tisk oprav" → bar's `handleRunFix` → `runFixMutation.mutateAsync()` (from `useRunExpeditionListPrintFix`, which has no `onSuccess` invalidation today — confirmed in `useExpeditionList.ts`) → bar's own toast with `result.totalCount`. No `expeditionListArchive` invalidation, preserving today's asymmetry.

**Order-print flow (unchanged behavior, new owner):** user clicks "Tisknout zakázku" → bar opens `<PrintOrderModal>` (bar-local `isPrintOrderModalOpen` state) → on modal success, bar's `handlePrintOrderSuccess(orderCode)` closes the modal, shows the toast, and calls `queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive })` using the bar's **own** `useQueryClient()` instance. Because `useQueryClient()` returns the same client instance from context regardless of which component calls it (both page and bar sit under the same `QueryClientProvider` in `App.tsx`), this invalidation still causes the page's `useExpeditionDates`/`useExpeditionListsByDate` queries to refetch — cross-component invalidation via the shared cache, no prop/callback needed. This is the load-bearing reason the "no props" design still works end-to-end for the one flow that needs to affect the page's own data.

**Toggle flow (unchanged behavior, new owner):** user clicks the switch → bar's `handleToggleJob` → `updateStatusMutation.mutateAsync({...})` (from `useUpdateRecurringJobStatusMutation`, which invalidates `recurringJobs` list + detail internally) → bar's toast. Again no `expeditionListArchive` interaction.

**Archive flows (page-owned, unaffected):** date pagination, date selection, `handleOpen` (blob fetch + open), "Obnovit" refresh, and reprint confirm/execute all remain wired exactly as today inside `ExpeditionListArchivePage.tsx`, using only `useExpeditionListArchive.ts` hooks.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `formatDateTime` duplication (page + bar) drifts out of sync if one copy is edited later | Low | Both copies are trivial (7 lines, `Date.toLocaleString("cs-CZ", …)`), unlikely to need independent evolution; if it ever changes, a grep for `formatDateTime` in `pages/ExpeditionListArchivePage.tsx` and `components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` finds both call sites immediately. Do not "fix" this by extracting a shared util as part of this ticket — that's an unrelated change under the "surgical changes" rule. |
| Test migration silently drops an assertion during the split (spec FR-5 requires zero coverage reduction) | Medium | Move the two `describe` blocks ("expedition robot toggle", "permission gating") verbatim from `ExpeditionListArchivePage.test.tsx` into the new file, changing only the render target (`<ExpeditionJobControlsBar />` instead of `<ExpeditionListArchivePage />`) and the mock import paths (one extra `../` level, per FR-2's acceptance criterion). Diff the two test files against the original block-for-block before considering the split done. |
| `useQueryClient()` cross-component invalidation (bar invalidates, page re-fetches) is implicit and easy to break if a future change wraps the bar in its own isolated `QueryClientProvider` | Low | Document this coupling inline as a one-line comment above `useQueryClient()` in `ExpeditionJobControlsBar.tsx` (e.g. "invalidates the shared QueryClientProvider from App.tsx so the archive page's lists refetch after a successful print"). No test currently exercises this cross-component effect directly (FR-2's coverage list doesn't include an order-print invalidation test) — flag to the developer that adding one would strengthen the split's safety net, though it isn't required by the spec's acceptance criteria. |
| Frontend has no compiler/lint enforcement of the "cross-module import confined to one file" rule (FR-4), unlike the backend's `ModuleBoundariesTests` | Low (accepted, out of scope) | The spec already scopes this as confinement-not-elimination and gives a literal grep-based acceptance check (`grep -rn "useExpeditionList\"" frontend/src/pages/ExpeditionListArchivePage.tsx` returns no match). No architecture-level test is proposed here since the codebase has no established frontend-boundary-test pattern to extend; this is consistent with treating the ticket as a pure refactor rather than an opportunity to build new enforcement tooling. |

## Specification Amendments

1. **FR-3's `formatDateTime` note needs correcting, not just clarifying.** The spec's parenthetical ("keep it in the archive page only if some other archive-browsing usage needs it, otherwise move it") reads as if there's ambiguity, but there isn't: the items table's "Nahráno" column (`ExpeditionListArchivePage.tsx` line 332, `formatDateTime(item.createdOn)`) is a second, independent caller that has nothing to do with the job-controls "Další běh" text. Both callers survive the split, in different files. Amendment: **duplicate the 7-line `formatDateTime` function** into both `ExpeditionListArchivePage.tsx` (items table) and `ExpeditionJobControlsBar.tsx` ("Další běh" text) rather than moving-and-choosing-one. Do not extract a shared util — out of scope for a surgical refactor.
2. **Open Questions 1–3 are resolved** by Decisions 1–3 above: sub-component location is `components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` (page path unchanged); the component is fully self-contained with no props; the pre-existing invalidation asymmetry (job-trigger/print-fix don't invalidate `expeditionListArchive`, order-print/reprint do) is preserved as-is and not fixed opportunistically. No further spec changes needed on these points — proceed with the spec's own stated assumptions.
3. No other amendments. FR-1, FR-2 (aside from the `formatDateTime` correction above), FR-4, and FR-5 are architecturally sound as written and require no changes to proceed to implementation.

## Prerequisites

None. This is a self-contained frontend file move with no new dependencies, no backend changes, no migrations, no config, and no feature flags. Implementation can start immediately: create `frontend/src/components/pages/ExpeditionListArchive/` (and its `__tests__/`), move the operational-controls JSX/hooks/handlers into `ExpeditionJobControlsBar.tsx`, trim `ExpeditionListArchivePage.tsx`, split the test file, then run `npm run build` + `npm run lint` + the Jest suite per CLAUDE.md's validation checklist.
