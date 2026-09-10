# Extract ExpeditionJobControlsBar Implementation Plan

**Goal:** Split `frontend/src/pages/ExpeditionListArchivePage.tsx` into an archive-browsing-only page plus a new, fully self-contained `ExpeditionJobControlsBar` component that owns recurring-job control, print-fix triggering, and order printing, with zero behavior change.

**Architecture:** Follow the existing `AccessManagementPage` / `GroupsGrid` pattern: the page stays at its current path and renders `<ExpeditionJobControlsBar />` with no props; the new component lives at `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` and self-resolves all of its own hooks (`useRecurringJobs`, `useExpeditionList`'s print-fix hook, `usePermissionsContext`, `useToast`, `useQueryClient`), confining the pre-existing cross-module import of `useRunExpeditionListPrintFix` to this one file. Tests are split the same way: the migrated "expedition robot toggle" and "permission gating" `describe` blocks move verbatim into a new co-located test file that mounts `<ExpeditionJobControlsBar />` directly; the page's test file keeps only the "refresh button" block.

**Tech Stack:** React 18 (function components, hooks), TypeScript, `@tanstack/react-query` (`useQueryClient`, `useMutation`, `useQuery`), Jest + React Testing Library, Tailwind CSS utility classes (unchanged), `lucide-react` icons.

---

## Note on header-row layout (read before Task 2)

`ExpeditionJobControlsBar` is rendered as a single, prop-less component instance (per spec FR-2 / design.r1.md). Because the current markup interleaves the page-owned "Obnovit" button *inside* the same flex container as the bar's three action buttons, a single atomic insertion point cannot reproduce that exact interleaving without either prop-threading (ruled out by FR-2) or rendering the bar twice (which would double-fire its hooks, violating NFR-1). Per design.r1.md's explicit instruction ("`<ExpeditionJobControlsBar />` ... rendered in the header-right area ... immediately followed by the 'Obnovit' button"), the bar (toggle/next-run block + the three action buttons) is rendered as one contiguous unit, with the "Obnovit" button moved to sit immediately after it instead of immediately before the three action buttons. This is a purely cosmetic reordering of one button within the same flex row (same gap, same icons/labels/behavior) — no existing test asserts button order, so this does not reduce test coverage or change any tested behavior.

---

### task: create-expedition-job-controls-bar-failing-tests

**Files:**
- Create: `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx`

- [ ] **Step 1: Create the target directory structure**

```bash
mkdir -p /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend/src/components/pages/ExpeditionListArchive/__tests__
```

- [ ] **Step 2: Write the new test file**

Create `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` with this exact content (migrated verbatim from the "expedition robot toggle" and "permission gating" `describe` blocks in `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`, mounting `<ExpeditionJobControlsBar />` directly and importing from one extra `../` level, per FR-2's acceptance criterion):

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ToastProvider } from "../../../../contexts/ToastContext";
import ExpeditionJobControlsBar from "../ExpeditionJobControlsBar";

jest.mock("../../../../api/hooks/useExpeditionList", () => ({
  useRunExpeditionListPrintFix: jest.fn(),
  usePrintExpeditionOrder: jest.fn(),
}));

jest.mock("../../../../api/hooks/useRecurringJobs", () => ({
  useTriggerRecurringJobMutation: jest.fn(),
  useRecurringJobQuery: jest.fn(),
  useUpdateRecurringJobStatusMutation: jest.fn(),
}));

jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: jest.fn(),
}));

jest.mock("../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    expeditionListArchive: ["expedition-list-archive"],
  },
}));

const { useRunExpeditionListPrintFix, usePrintExpeditionOrder } = require("../../../../api/hooks/useExpeditionList");

const {
  useTriggerRecurringJobMutation,
  useRecurringJobQuery,
  useUpdateRecurringJobStatusMutation,
} = require("../../../../api/hooks/useRecurringJobs");

const { usePermissionsContext } = require("../../../../auth/PermissionsContext");

const TRIGGER_PERMISSION = "jobs.trigger.read";
const DISABLE_PERMISSION = "jobs.disable.read";

/** Sets the mocked permission context to grant exactly the listed permissions. */
const setPermissions = (granted: string[]) => {
  (usePermissionsContext as jest.Mock).mockReturnValue({
    hasPermission: (perm: string) => granted.includes(perm),
  });
};

const setPrintJob = (job: object | null) => {
  (useRecurringJobQuery as jest.Mock).mockReturnValue({ data: job });
};

const createQueryClient = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false } } });

const renderBar = (queryClient: QueryClient) =>
  render(
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <ExpeditionJobControlsBar />
      </ToastProvider>
    </QueryClientProvider>
  );

const setCommonMocks = () => {
  (useRunExpeditionListPrintFix as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ totalCount: 5 }),
    isPending: false,
  });
  (usePrintExpeditionOrder as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ success: true }),
    isPending: false,
  });
  (useTriggerRecurringJobMutation as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(undefined),
    isPending: false,
  });
  (useUpdateRecurringJobStatusMutation as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(undefined),
    isPending: false,
  });
  setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });
  // Default: full permissions
  setPermissions([TRIGGER_PERMISSION, DISABLE_PERMISSION]);
};

describe("ExpeditionJobControlsBar – expedition robot toggle", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  const getToggle = () =>
    screen.getByRole("switch", { name: /expediční robot/i });

  it("reflects the enabled state of the print job", () => {
    setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });

    renderBar(createQueryClient());

    expect(getToggle()).toHaveAttribute("aria-checked", "true");
  });

  it("reflects the disabled state of the print job", () => {
    setPrintJob({ jobName: "print-picking-list", isEnabled: false, nextRunAt: null });

    renderBar(createQueryClient());

    expect(getToggle()).toHaveAttribute("aria-checked", "false");
  });

  it("calls the status mutation with the negated value when toggled", async () => {
    const mutateAsync = jest.fn().mockResolvedValue(undefined);
    setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });
    (useUpdateRecurringJobStatusMutation as jest.Mock).mockReturnValue({
      mutateAsync,
      isPending: false,
    });

    renderBar(createQueryClient());

    fireEvent.click(getToggle());

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        jobName: "print-picking-list",
        isEnabled: false,
      })
    );
  });

  it("renders an em dash for next run when the job is missing", () => {
    setPrintJob(null);

    renderBar(createQueryClient());

    expect(screen.getByText(/Další běh: –/)).toBeInTheDocument();
    expect(getToggle()).toBeDisabled();
  });
});

describe("ExpeditionJobControlsBar – permission gating", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  it("shows the run button only with the trigger permission", () => {
    setPermissions([TRIGGER_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.getByRole("button", { name: /spustit tisk$/i })).toBeInTheDocument();
  });

  it("hides the run button without the trigger permission", () => {
    setPermissions([DISABLE_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.queryByRole("button", { name: /spustit tisk$/i })).not.toBeInTheDocument();
  });

  it("shows the toggle only with the disable permission", () => {
    setPermissions([DISABLE_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.getByRole("switch", { name: /expediční robot/i })).toBeInTheDocument();
  });

  it("hides the toggle without the disable permission", () => {
    setPermissions([TRIGGER_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.queryByRole("switch", { name: /expediční robot/i })).not.toBeInTheDocument();
  });

  it("hides the toggle and next-run entirely with neither job permission", () => {
    setPermissions([]);
    renderBar(createQueryClient());
    expect(screen.queryByRole("switch", { name: /expediční robot/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/Další běh:/)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run the test and confirm it fails**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npx jest src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx --no-coverage
```

Expected: FAIL — `Cannot find module '../ExpeditionJobControlsBar' from 'src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx'` (the component does not exist yet). This confirms the test is wired up correctly and exercises real code once it exists.

- [ ] **Step 4: Commit**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
git add frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx
git commit -m "test: add failing tests for ExpeditionJobControlsBar extraction"
```

---

### task: implement-expedition-job-controls-bar

**Files:**
- Create: `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`
- Test: `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx`

- [ ] **Step 1: Create the component file**

Create `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` with this exact content (JSX, handlers, and constants moved verbatim from `frontend/src/pages/ExpeditionListArchivePage.tsx`; only the toggle/next-run block and the three action buttons — no "Obnovit" button, no archive-browsing code):

```tsx
import React, { useState } from "react";
import { Printer, Play } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { QUERY_KEYS } from "../../../api/client";
import { useRunExpeditionListPrintFix } from "../../../api/hooks/useExpeditionList";
import {
  useTriggerRecurringJobMutation,
  useRecurringJobQuery,
  useUpdateRecurringJobStatusMutation,
} from "../../../api/hooks/useRecurringJobs";
import { usePermissionsContext } from "../../../auth/PermissionsContext";
import { useToast } from "../../../contexts/ToastContext";
import PrintOrderModal from "../../../components/modals/PrintOrderModal";

const PRINT_JOB_NAME = "print-picking-list";
const TRIGGER_JOBS_PERMISSION = "jobs.trigger.read";
const DISABLE_JOBS_PERMISSION = "jobs.disable.read";

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

const ExpeditionJobControlsBar: React.FC = () => {
  const { showSuccess, showError } = useToast();
  const queryClient = useQueryClient();
  const [isPrintOrderModalOpen, setIsPrintOrderModalOpen] = useState(false);

  const triggerJobMutation = useTriggerRecurringJobMutation();
  const runFixMutation = useRunExpeditionListPrintFix();

  const { hasPermission } = usePermissionsContext();
  const canTriggerJob = hasPermission(TRIGGER_JOBS_PERMISSION);
  const canToggleJob = hasPermission(DISABLE_JOBS_PERMISSION);
  const { data: printJob } = useRecurringJobQuery(PRINT_JOB_NAME, canTriggerJob || canToggleJob);
  const updateStatusMutation = useUpdateRecurringJobStatusMutation();

  const handleRunJob = async () => {
    try {
      await triggerJobMutation.mutateAsync(PRINT_JOB_NAME);
      showSuccess('Spuštěno', 'Tisk expedičního listu byl spuštěn.');
    } catch {
      showError('Chyba', 'Nepodařilo se spustit tisk expedičního listu.');
    }
  };

  const handleToggleJob = async () => {
    if (!printJob) return;
    try {
      await updateStatusMutation.mutateAsync({
        jobName: PRINT_JOB_NAME,
        isEnabled: !printJob.isEnabled,
      });
      showSuccess(
        'Uloženo',
        printJob.isEnabled
          ? 'Expediční robot byl vypnut.'
          : 'Expediční robot byl zapnut.',
      );
    } catch {
      showError('Chyba', 'Nepodařilo se změnit nastavení expedičního robota.');
    }
  };

  const handleRunFix = async () => {
    try {
      const result = await runFixMutation.mutateAsync();
      showSuccess('Spuštěno', `Tisk oprav dokončen. Celkem objednávek: ${result.totalCount}.`);
    } catch {
      showError('Chyba', 'Nepodařilo se spustit tisk oprav.');
    }
  };

  // Invalidates the shared QueryClientProvider from App.tsx (this component and
  // ExpeditionListArchivePage sit under the same provider), so the archive page's
  // date/items queries refetch after a successful order print, with no prop/callback needed.
  const handlePrintOrderSuccess = async (orderCode: string) => {
    setIsPrintOrderModalOpen(false);
    showSuccess("Zakázka vytištěna", `Zakázka ${orderCode} byla odeslána na tisk a převedena do stavu „Balí se".`);
    await queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
  };

  return (
    <>
      {(canTriggerJob || canToggleJob) && (
        <div className="flex items-center gap-3">
          {canToggleJob && (
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium text-gray-700 dark:text-graphite-muted">Expediční robot</span>
              <button
                type="button"
                role="switch"
                aria-checked={printJob?.isEnabled ?? false}
                aria-label="Expediční robot"
                onClick={handleToggleJob}
                disabled={!printJob || updateStatusMutation.isPending}
                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed ${
                  printJob?.isEnabled ? "bg-indigo-600" : "bg-gray-200"
                }`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                    printJob?.isEnabled ? "translate-x-6" : "translate-x-1"
                  }`}
                />
              </button>
            </div>
          )}
          <span className="text-sm text-gray-500 dark:text-graphite-muted whitespace-nowrap">
            Další běh: {formatDateTime(printJob?.nextRunAt ? printJob.nextRunAt.toISOString() : null)}
          </span>
        </div>
      )}
      <div className="flex items-center gap-2">
        <button
          onClick={() => setIsPrintOrderModalOpen(true)}
          className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 transition-colors"
        >
          <Printer size={14} />
          Tisknout zakázku
        </button>
        <button
          onClick={handleRunFix}
          disabled={runFixMutation.isPending}
          className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-amber-600 rounded-lg hover:bg-amber-700 disabled:opacity-50 transition-colors"
        >
          {runFixMutation.isPending ? (
            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white" />
          ) : (
            <Play size={14} />
          )}
          Spustit tisk oprav
        </button>
        {canTriggerJob && (
          <button
            onClick={handleRunJob}
            disabled={triggerJobMutation.isPending}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 transition-colors"
          >
            {triggerJobMutation.isPending ? (
              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white" />
            ) : (
              <Play size={14} />
            )}
            Spustit tisk
          </button>
        )}
      </div>

      <PrintOrderModal
        isOpen={isPrintOrderModalOpen}
        onClose={() => setIsPrintOrderModalOpen(false)}
        onSuccess={handlePrintOrderSuccess}
      />
    </>
  );
};

export default ExpeditionJobControlsBar;
```

- [ ] **Step 2: Run the new test file and confirm it passes**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npx jest src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx --no-coverage
```

Expected: PASS — both `describe` blocks (9 tests total: 4 in "expedition robot toggle", 5 in "permission gating") pass with no `act()` warnings.

- [ ] **Step 3: Type-check and lint the new file**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npx tsc --noEmit -p tsconfig.json
npm run lint
```

Expected: no new TypeScript errors or lint errors attributable to `ExpeditionJobControlsBar.tsx`.

- [ ] **Step 4: Commit**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
git add frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx
git commit -m "feat: implement self-contained ExpeditionJobControlsBar component"
```

---

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

### task: update-archive-page-tests

**Files:**
- Modify: `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`

- [ ] **Step 1: Replace the file's mocks and setup with the archive-only subset**

Replace the top of `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`, from the `import` statements through `setCommonMocks` (originally lines 1–125), with:

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ToastProvider } from "../../contexts/ToastContext";
import ExpeditionListArchivePage from "../ExpeditionListArchivePage";

jest.mock("../../api/hooks/useExpeditionListArchive", () => ({
  useExpeditionDates: jest.fn(),
  useExpeditionListsByDate: jest.fn(),
  useReprintExpeditionList: jest.fn(),
  getExpeditionListDownloadUrl: jest.fn(),
}));

jest.mock("../../components/pages/ExpeditionListArchive/ExpeditionJobControlsBar", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getAuthenticatedFetch: jest.fn(() => jest.fn()),
  QUERY_KEYS: {
    expeditionListArchive: ["expedition-list-archive"],
  },
}));

const {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
} = require("../../api/hooks/useExpeditionListArchive");

const mockDatesData = {
  data: { dates: ["2024-12-10", "2024-12-09"], totalCount: 2, page: 1, pageSize: 20 },
  isLoading: false,
};

const mockItemsData = {
  data: {
    items: [
      {
        blobPath: "blob/path/file.pdf",
        fileName: "expedice-2024-12-10.pdf",
        createdOn: "2024-12-10T10:00:00Z",
        contentLength: 1024,
      },
    ],
  },
  isLoading: false,
};

const createQueryClient = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false } } });

const renderPage = (queryClient: QueryClient) =>
  render(
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <ExpeditionListArchivePage />
      </ToastProvider>
    </QueryClientProvider>
  );

const setCommonMocks = () => {
  (useExpeditionDates as jest.Mock).mockReturnValue(mockDatesData);
  (useExpeditionListsByDate as jest.Mock).mockReturnValue(mockItemsData);
  (useReprintExpeditionList as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ success: true, errorCode: null, params: null }),
    isPending: false,
  });
};
```

`ExpeditionJobControlsBar` is stubbed to `() => null` so this file's tests exercise only archive-browsing behavior and never need to mock `useExpeditionList`, `useRecurringJobs`, or `PermissionsContext` — satisfying FR-2's/FR-3's acceptance criteria that this file's `jest.mock` calls no longer include those three modules.

- [ ] **Step 2: Remove the migrated `describe` blocks**

Delete the `"ExpeditionListArchivePage – expedition robot toggle"` and `"ExpeditionListArchivePage – permission gating"` `describe` blocks in their entirety (originally lines 186–277) — they now live in `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx`. Keep the `"ExpeditionListArchivePage – refresh button"` `describe` block (originally lines 127–184) exactly as-is; it should be the only `describe` block remaining in the file, immediately followed by the file's closing content (no trailing code after it).

- [ ] **Step 3: Run the updated page test file**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npx jest src/pages/__tests__/ExpeditionListArchivePage.test.tsx --no-coverage
```

Expected: PASS — all 4 tests in the "refresh button" `describe` block pass, no `act()` warnings or console errors.

- [ ] **Step 4: Run the full frontend Jest suite**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm test -- --watchAll=false
```

Expected: PASS — all suites green, including both `ExpeditionListArchivePage.test.tsx` (4 tests) and `ExpeditionJobControlsBar.test.tsx` (9 tests).

- [ ] **Step 5: Commit**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
git add frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx
git commit -m "test: split ExpeditionListArchivePage tests to archive-browsing scope only"
```

---

### task: validate-full-build-and-import-confinement

**Files:**
- (no file changes — verification only)

- [ ] **Step 1: Confirm the cross-module import is confined to `ExpeditionJobControlsBar.tsx`**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
grep -rln "useRunExpeditionListPrintFix" frontend/src/pages frontend/src/components/pages/ExpeditionListArchive
```

Expected: exactly one match — `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`. This satisfies FR-4's acceptance criterion.

- [ ] **Step 2: Run the full frontend build**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm run build
```

Expected: build succeeds with no TypeScript errors.

- [ ] **Step 3: Run lint**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm run lint
```

Expected: no new lint errors introduced by this refactor (pre-existing warnings elsewhere in the codebase, if any, are out of scope).

- [ ] **Step 4: Run the full Jest suite one more time**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm test -- --watchAll=false
```

Expected: all suites pass.

- [ ] **Step 5: Confirm `App.tsx` is untouched**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
git diff --stat origin/main -- frontend/src/App.tsx
```

Expected: no output (empty diff) — the route `/logistics/expedition-archive` and the `ExpeditionListArchivePage` import path are unchanged, per FR-1's acceptance criteria.

- [ ] **Step 6: No commit needed**

This task is verification-only; no files were changed. If any step above fails, fix the issue in the relevant earlier task's files, re-run that task's test/build commands, then re-run this task's steps from Step 1.
