### task: add-degraded-indicator-to-list-page

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTasksPage.tsx` (line 3 imports, and the "Ulohy" `<td>` at lines 191-198)
- Create: `frontend/src/components/pages/automation/__tests__/MeetingTasksPage.test.tsx`

Reference files read to produce this task (do not modify):
- `MeetingTasksPage.tsx` — confirmed the current `lucide-react` import (line 3) does **not**
  include `AlertTriangle` (unlike the detail page); confirmed the "Ulohy" `<td>` (lines 191-198)
  currently renders `{row.taskCount}` plus an optional `({row.approvedTaskCount} schvaleno)` span;
  confirmed the page's only hooks are `useNavigate` (react-router), `useScreenView` (telemetry),
  and `useMeetingTasksList` — no existing test file for this page exists yet (verified via file
  search), so this task creates the first one.
- `MeetingTaskDetailPage.reviewState.test.tsx` (already read for the previous task) and
  `JournalList.test.tsx` line 21-22 — confirmed the `useScreenView` mock pattern
  (`jest.mock('.../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }))`) needed since
  this page calls it directly (unlike the detail page, which doesn't).

Steps:

- [ ] **Step 1: Write a failing test file asserting the row-level pill's presence/absence.**
  Create `MeetingTasksPage.test.tsx`:
  ```tsx
  import React from 'react';
  import { render, screen } from '@testing-library/react';
  import { MemoryRouter } from 'react-router-dom';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

  import { useMeetingTasksList } from '../../../../api/hooks/useMeetingTasks';
  import MeetingTasksPage from '../MeetingTasksPage';

  jest.mock('../../../../api/hooks/useMeetingTasks');
  jest.mock('../../../../telemetry/useScreenView', () => ({
    useScreenView: jest.fn(),
  }));

  function buildRow(overrides: Record<string, unknown> = {}) {
    return {
      id: 'abc',
      subject: 'Schůzka',
      plaudRecordingId: 'plaud-1',
      plaudCreatedAt: '2026-05-19T10:00:00Z',
      status: 'PendingReview',
      receivedAt: '2026-05-19T10:00:00Z',
      taskCount: 3,
      approvedTaskCount: 0,
      rejectedTaskCount: 0,
      accessLevel: 'Private' as const,
      tasksExtractionDegraded: false,
      ...overrides,
    };
  }

  function mockList(rows: Record<string, unknown>[]) {
    (useMeetingTasksList as jest.Mock).mockReturnValue({
      data: { items: rows, totalCount: rows.length, pageNumber: 1, pageSize: 20, totalPages: 1 },
      isLoading: false,
      isFetching: false,
      refetch: jest.fn(),
    });
  }

  function renderPage() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <MeetingTasksPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  }

  describe('extraction-degraded row indicator', () => {
    it('shows a warning pill for a row with tasksExtractionDegraded set', () => {
      mockList([buildRow({ tasksExtractionDegraded: true })]);
      renderPage();
      expect(screen.getByTitle('extrakce může být neúplná')).toBeInTheDocument();
    });

    it('shows no warning pill for a row without the flag', () => {
      mockList([buildRow({ tasksExtractionDegraded: false })]);
      renderPage();
      expect(screen.queryByTitle('extrakce může být neúplná')).not.toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run the test and confirm it fails (RED — no such pill exists yet).**
  ```bash
  cd frontend
  npx react-scripts test --watchAll=false MeetingTasksPage.test
  ```

- [ ] **Step 3: Implement the row-level pill.**
  Edit `MeetingTasksPage.tsx` line 3 from:
  ```typescript
  import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight, RefreshCw } from "lucide-react";
  ```
  to:
  ```typescript
  import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight, RefreshCw, AlertTriangle } from "lucide-react";
  ```
  Edit the "Ulohy" `<td>` (lines 191-198) from:
  ```tsx
                  <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">
                    {row.taskCount}
                    {row.approvedTaskCount > 0 && (
                      <span className="ml-1 text-xs text-gray-500 dark:text-graphite-muted">
                        ({row.approvedTaskCount} schvaleno)
                      </span>
                    )}
                  </td>
  ```
  to:
  ```tsx
                  <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">
                    {row.taskCount}
                    {row.approvedTaskCount > 0 && (
                      <span className="ml-1 text-xs text-gray-500 dark:text-graphite-muted">
                        ({row.approvedTaskCount} schvaleno)
                      </span>
                    )}
                    {row.tasksExtractionDegraded && (
                      <span
                        title="extrakce může být neúplná"
                        className="ml-1 inline-flex items-center text-amber-700 bg-amber-100 dark:text-amber-300 dark:bg-amber-900/30 rounded-full px-1.5 py-0.5"
                      >
                        <AlertTriangle className="w-3 h-3" />
                      </span>
                    )}
                  </td>
  ```

- [ ] **Step 4: Run the test and confirm it passes (GREEN).**
  ```bash
  npx react-scripts test --watchAll=false MeetingTasksPage.test
  ```

- [ ] **Step 5: Build and lint.**
  ```bash
  npm run build
  npm run lint
  ```

- [ ] **Step 6: Commit.**
  ```bash
  git add frontend/src/components/pages/automation/MeetingTasksPage.tsx \
          frontend/src/components/pages/automation/__tests__/MeetingTasksPage.test.tsx
  git commit -m "Show a per-row warning pill on the meeting list page when task extraction is degraded"
  ```

---

## Self-review

- **FR-1 coverage:** `log-raw-response-and-flag-degraded-result` (structured `{RawResponse}`
  property, full untruncated text, `ex` still passed, dedicated unit test).
- **FR-2 coverage:** `add-partial-extraction-parser-primitives` (the scanner itself, with
  adversarial fixtures per the arch review's risk mitigation) and
  `wire-partial-recovery-into-extractor` (integration into the catch block; tests for (a) partial
  salvage with order preservation, (b) not-JSON-at-all fallback, (c) fully-valid `Degraded: false`
  — all three FR-2 acceptance-criteria scenarios are covered).
- **FR-3 coverage:** `add-tasksextractiondegraded-domain-and-migration` (entity + migration),
  `thread-degraded-flag-through-handlers-and-dto` (both handlers set it, both read handlers expose
  it, reimport overwrite-not-OR semantics tested in both directions),
  `regenerate-openapi-client-and-update-meeting-tasks-hook` (frontend type propagation, including
  the arch review's corrected understanding that the hand-written hook interface needs a manual
  edit), `add-degraded-warning-banner-to-detail-page` (unmissable banner near
  `TranscriptStatusBadge`, pointing at Reimport), `add-degraded-indicator-to-list-page` (row-level
  pill). No task filters out or hides degraded rows from review queues — FR-3's "informational
  only" constraint is respected by construction (no new filtering logic was added anywhere).
- **NFR-1 (no success-path cost):** `PartialExtractionParser.TrySalvage` is only ever invoked from
  inside the `catch (JsonException)` block, never on the success path; the happy-path return
  statement is untouched by any task.
- **NFR-2 (log sensitivity):** No redaction logic was added, matching the spec's explicit decision
  that no additional scrubbing is required.
- **Spec deviation flagged:** The FR-2 acceptance criteria's literal wording (`JsonDocument.Parse`
  in a "permissive mode") is explicitly called out as non-implementable and replaced with the
  arch-review-approved depth-aware scanner in both the Overview above and inline in the
  `add-partial-extraction-parser-primitives` task.
- **Type/method-name consistency check:** `MeetingExtractionResult(List<ExtractedTask> Tasks,
  List<string> Participants, bool Degraded = false)` is introduced once (first task) and used
  identically (including the named `Degraded:` argument) in every later task and test.
  `PartialExtractionParser.TrySalvage(string text, ILogger logger) -> (List<ExtractedTask> Tasks,
  List<string> Participants, bool LocatedAnyArray)` is introduced once (second task) and consumed
  identically in the third task. `TasksExtractionDegraded` (domain entity, DTO, and TypeScript
  interface) and `tasksExtractionDegraded` (TypeScript/JSON casing) are spelled consistently
  across every backend and frontend task. `ExtractedTask` and `NormalizeParticipants` are reused
  from the existing class rather than redefined.
- **Placeholder-language scan:** no "TBD", "similar to Task N", or undefined types/methods remain
  in any step; every code block above is complete and self-contained given the preceding tasks'
  changes.
