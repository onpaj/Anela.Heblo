### task: wire-controls-bar-into-page

**Files:**
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:1-67` (imports, constants, hooks)
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:101-141` (handlers)
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:161-239` (header JSX)
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:364-368` (`<PrintOrderModal>` render)

- [ ] **Step 1: Replace the top-of-file imports and constants**

In `frontend/src/pages/ExpeditionListArchivePage.tsx`, replace lines 1–44 (imports through `formatDateTime`) with:

```tsx
import React, { useState, useEffect } from "react";
import { FileText, Printer, ExternalLink, ChevronLeft, ChevronRight, RefreshCw } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedFetch, QUERY_KEYS } from "../api/client";
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../api/hooks/useExpeditionListArchive";
import { useToast } from "../contexts/ToastContext";
import { useScreenView } from "../telemetry/useScreenView";
import ExpeditionJobControlsBar from "../components/pages/ExpeditionListArchive/ExpeditionJobControlsBar";

const PAGE_SIZE = 20;

const formatFileSize = (bytes: number | null): string => {
  if (bytes === null) return "–";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const formatDateTime = (iso: string | null): string => {
  if (!iso) return "–";
  return new Date(iso).toLocaleString("cs-CZ", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
};
```

Notes on this change: `Play` is removed from the `lucide-react` import (only used by the job-trigger/print-fix buttons, now in the bar); `formatDateTime` is kept here too (duplicated, not shared) because the items table's "Nahráno" column (line ~332) still calls it — per arch-review's Specification Amendment 1, do not extract a shared util for this 7-line formatter.

- [ ] **Step 2: Trim the component body's state, hooks, and handlers**

Replace the component body from the `const ExpeditionListArchivePage: React.FC = () => {` opening line through the `handlePrintOrderSuccess` handler (originally lines 46–141) with:

```tsx
const ExpeditionListArchivePage: React.FC = () => {
  const { showSuccess, showError } = useToast();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [selectedDate, setSelectedDate] = useState<string>("");
  const [reprintConfirm, setReprintConfirm] = useState<ExpeditionListItemDto | null>(null);

  useScreenView('Logistics', 'ExpeditionArchive');

  const { data: datesData, isLoading: datesLoading } = useExpeditionDates(page, PAGE_SIZE);
  const { data: itemsData, isLoading: itemsLoading } = useExpeditionListsByDate(selectedDate);
  const reprintMutation = useReprintExpeditionList();

  // Auto-select the first (most recent) date when dates load
  useEffect(() => {
    if (datesData?.dates?.length && !selectedDate) {
      setSelectedDate(datesData.dates[0]);
    }
  }, [datesData, selectedDate]);

  const totalPages = datesData ? Math.ceil(datesData.totalCount / PAGE_SIZE) : 0;

  const handleOpen = async (item: ExpeditionListItemDto) => {
    const url = getExpeditionListDownloadUrl(item.blobPath);
    try {
      const response = await getAuthenticatedFetch()(url, { method: 'GET' });
      if (!response.ok) {
        showError('Chyba', `Nepodařilo se otevřít soubor (${response.status}).`);
        return;
      }
      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
      window.open(blobUrl, '_blank', 'noopener,noreferrer');
      setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
    } catch {
      showError('Chyba', 'Nepodařilo se otevřít soubor.');
    }
  };

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
    setIsRefreshing(false);
  };

  const handleReprintConfirm = async () => {
    if (!reprintConfirm) return;
    try {
      await reprintMutation.mutateAsync({ blobPath: reprintConfirm.blobPath });
      showSuccess("Přetisk odeslán", `${reprintConfirm.fileName} byl odeslán na tiskárnu.`);
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.message
          : typeof err === 'object' && err !== null && 'message' in err
            ? String((err as { message: unknown }).message)
            : 'Nepodařilo se odeslat na tisk.';
      showError("Chyba tisku", msg);
    } finally {
      setReprintConfirm(null);
    }
  };
```

This removes: `isPrintOrderModalOpen` state, `triggerJobMutation`, `runFixMutation`, `usePermissionsContext`/`canTriggerJob`/`canToggleJob`, `printJob`/`updateStatusMutation`, and the `handleRunJob`/`handleToggleJob`/`handleRunFix`/`handlePrintOrderSuccess` handlers — all moved into `ExpeditionJobControlsBar`.

- [ ] **Step 3: Replace the header-right JSX**

Replace the header `<div className="flex items-center justify-between mb-6">...</div>` block (originally lines 163–239) with:

```tsx
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold text-gray-900 dark:text-graphite-text">Archiv expedičních listů</h1>
        <div className="flex items-center gap-4">
          <ExpeditionJobControlsBar />
          <button
            onClick={handleRefresh}
            disabled={isRefreshing}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-lg hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 transition-colors"
          >
            <RefreshCw size={14} className={isRefreshing ? "animate-spin" : ""} />
            Obnovit
          </button>
        </div>
      </div>
```

Note: `ExpeditionJobControlsBar` renders as one contiguous unit before "Obnovit" — see the "Note on header-row layout" section at the top of this plan for why "Obnovit" moves from before to after the bar's three buttons.

- [ ] **Step 4: Remove the now-unused `<PrintOrderModal>` render**

Remove these lines (originally 364–368, right after the closing `</div>` of the date-sidebar/items-table `flex gap-6` container and before the reprint confirmation dialog):

```tsx
      <PrintOrderModal
        isOpen={isPrintOrderModalOpen}
        onClose={() => setIsPrintOrderModalOpen(false)}
        onSuccess={handlePrintOrderSuccess}
      />

```

`PrintOrderModal` is now rendered exclusively inside `ExpeditionJobControlsBar`.

- [ ] **Step 5: Verify the resulting import list has no leftover cross-module imports**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
grep -n "useExpeditionList\"" frontend/src/pages/ExpeditionListArchivePage.tsx
grep -n "useRecurringJobs\|PermissionsContext\|PrintOrderModal" frontend/src/pages/ExpeditionListArchivePage.tsx
```

Expected: both commands print no matches (empty output) — confirms FR-2's and FR-4's import-confinement acceptance criteria.

- [ ] **Step 6: Type-check the page**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npx tsc --noEmit -p tsconfig.json
```

Expected: no errors. (The existing page test file will fail to compile/run at this point because it still mocks modules the page no longer imports — that's expected and fixed in the next task.)

- [ ] **Step 7: Commit**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
git add frontend/src/pages/ExpeditionListArchivePage.tsx
git commit -m "refactor: reduce ExpeditionListArchivePage to archive browsing only"
```

---
