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
