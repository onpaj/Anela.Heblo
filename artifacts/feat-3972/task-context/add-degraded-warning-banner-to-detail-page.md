### task: add-degraded-warning-banner-to-detail-page

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` (insert after line 393)
- Create: `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.degraded.test.tsx`

Reference files read to produce this task (do not modify):
- `MeetingTaskDetailPage.tsx` — confirmed `AlertTriangle` is already imported at line 7; confirmed
  the header block (holding `TranscriptStatusBadge`) closes with `</div>` at line 393, immediately
  followed by the `{reimportError && (...)}` block at lines 395-399; confirmed the existing
  "neznámý uživatel" amber-pill idiom at lines 579-583 (`text-amber-700 bg-amber-100
  dark:text-amber-300 dark:bg-amber-900/30` + `AlertTriangle`) as the color/icon precedent to
  reuse verbatim, per the design doc.
- `MeetingTaskDetailPage.reviewState.test.tsx` — confirmed the full test harness pattern (module
  mocks for `react-markdown`, `remark-gfm`, `useMeetingTasks` hooks, `PermissionsContext`,
  `useAuth`, `explain/*`, `access/ManageAccessModal`; a `buildTranscript()`/`setupHooks()`/`renderPage()`
  helper trio) that this new test file will replicate, per the existing per-concern convention
  (`.filter.`, `.download.`, `.delete.`, `.reviewState.` test files each keep their own copy of
  this harness).

Steps:

- [ ] **Step 1: Write a failing test file asserting the banner's presence/absence.**
  Create `MeetingTaskDetailPage.degraded.test.tsx`:
  ```tsx
  import React from 'react';
  import { render, screen } from '@testing-library/react';
  import { MemoryRouter, Route, Routes } from 'react-router-dom';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

  import {
    useMeetingTaskDetail,
    useUpdateProposedTask,
    useUpdateProposedTaskStatus,
    useUpdateTranscriptStatus,
    useAddProposedTask,
    useSubmitToTodo,
    useMeetingUsers,
    useReimportMeeting,
    useExplainMeetingSummary,
    useDeleteMeeting,
  } from '../../../../api/hooks/useMeetingTasks';
  import { useExplainSelection } from '../explain/useExplainSelection';
  import MeetingTaskDetailPage from '../MeetingTaskDetailPage';

  // ---- Module mocks ----

  jest.mock('react-markdown', () => ({ __esModule: true, default: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
  jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));

  jest.mock('../../../../api/hooks/useMeetingTasks');
  jest.mock('../../../../auth/PermissionsContext', () => ({
    usePermissionsContext: () => ({
      permissions: [],
      isSuperUser: true,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    }),
  }));
  jest.mock('../../../../auth/useAuth', () => ({
    useAuth: () => ({ account: { username: 'me@anela.cz' } }),
  }));
  jest.mock('../explain/useExplainSelection');
  jest.mock('../explain/ExplainTooltip', () => ({ ExplainTooltip: () => null }));
  jest.mock('../explain/ExplainModal', () => ({ ExplainModal: () => null }));
  jest.mock('../access/ManageAccessModal', () => ({ ManageAccessModal: () => null }));

  // ---- Helpers ----

  const noopMutation = { mutate: jest.fn(), mutateAsync: jest.fn(), isPending: false, isError: false, error: null, reset: jest.fn() };

  function buildTranscript(overrides: Record<string, unknown> = {}) {
    return {
      id: 'abc',
      subject: 'Schůzka',
      summary: 'AI summary text',
      rawTranscript: 'Speaker: Hello',
      plaudRecordingId: 'plaud-1',
      plaudCreatedAt: '2026-05-19T10:00:00Z',
      status: 'PendingReview',
      receivedAt: '2026-05-19T10:00:00Z',
      reviewedAt: null,
      reviewedByUser: null,
      taskCount: 0,
      approvedTaskCount: 0,
      rejectedTaskCount: 0,
      tasks: [],
      participants: [],
      accessLevel: 'Private' as const,
      accessGrants: [],
      tasksExtractionDegraded: false,
      ...overrides,
    };
  }

  function renderPage() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/automation/meeting-tasks/abc']}>
          <Routes>
            <Route path="/automation/meeting-tasks/:id" element={<MeetingTaskDetailPage />} />
            <Route path="/automation/meeting-tasks" element={<div>SEZNAM PORAD</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  }

  function setupHooks(transcriptOverrides: Record<string, unknown> = {}) {
    (useMeetingTaskDetail as jest.Mock).mockReturnValue({ isLoading: false, data: { transcript: buildTranscript(transcriptOverrides) } });
    (useUpdateProposedTask as jest.Mock).mockReturnValue(noopMutation);
    (useUpdateProposedTaskStatus as jest.Mock).mockReturnValue(noopMutation);
    (useUpdateTranscriptStatus as jest.Mock).mockReturnValue(noopMutation);
    (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
    (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
    (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
    (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
    (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
    (useDeleteMeeting as jest.Mock).mockReturnValue(noopMutation);
    (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
  }

  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('extraction-degraded warning banner', () => {
    it('renders a warning banner when tasksExtractionDegraded is true', () => {
      setupHooks({ tasksExtractionDegraded: true });
      renderPage();
      expect(screen.getByText(/Extrakce úkolů může být neúplná/i)).toBeInTheDocument();
    });

    it('renders no banner when tasksExtractionDegraded is false', () => {
      setupHooks({ tasksExtractionDegraded: false });
      renderPage();
      expect(screen.queryByText(/Extrakce úkolů může být neúplná/i)).not.toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run the test and confirm it fails (RED — the page renders no such banner yet).**
  ```bash
  cd frontend
  npx react-scripts test --watchAll=false MeetingTaskDetailPage.degraded
  ```

- [ ] **Step 3: Implement the banner.**
  Edit `MeetingTaskDetailPage.tsx` — insert immediately after line 393 (the header row's closing
  `</div>`) and before line 395 (`{reimportError && (`):
  ```tsx
        </div>
      </div>

      {transcript.tasksExtractionDegraded && (
        <div className="px-4 sm:px-6 lg:px-8 mt-2">
          <div className="flex items-start gap-2 rounded-md border border-amber-200 dark:border-amber-900/40 bg-amber-100 dark:bg-amber-900/30 px-3 py-2 text-sm text-amber-700 dark:text-amber-300">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" aria-hidden="true" />
            <span>
              Extrakce úkolů může být neúplná — nepodařilo se zpracovat celou odpověď AI.
              Zkontrolujte přepis ručně, nebo použijte tlačítko "Reimport" výše.
            </span>
          </div>
        </div>
      )}

      {reimportError && (
  ```
  (The first two lines above, `</div>` / `</div>`, are the existing lines 392-393 shown for
  placement context — do not duplicate them; only the new `{transcript.tasksExtractionDegraded &&
  (...)}` block is new content, inserted between the existing line 393 and line 395.)

- [ ] **Step 4: Run the test and confirm it passes (GREEN).**
  ```bash
  npx react-scripts test --watchAll=false MeetingTaskDetailPage.degraded
  ```

- [ ] **Step 5: Run the full existing detail-page test suite to confirm no regressions.**
  ```bash
  npx react-scripts test --watchAll=false MeetingTaskDetailPage
  ```

- [ ] **Step 6: Build and lint.**
  ```bash
  npm run build
  npm run lint
  ```

- [ ] **Step 7: Commit.**
  ```bash
  git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx \
          frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.degraded.test.tsx
  git commit -m "Show a warning banner on the meeting detail page when task extraction is degraded"
  ```

---
