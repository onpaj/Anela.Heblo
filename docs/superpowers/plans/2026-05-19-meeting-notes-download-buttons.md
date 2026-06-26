# Meeting Notes Download Buttons — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add "Stáhnout souhrn" (.md) and "Stáhnout přepis" (.txt) download buttons to the meeting notes detail page header so users can export content for LLM use.

**Architecture:** Two pure-browser downloads — no API calls. A shared `downloadTextFile` utility handles the blob+anchor pattern. The existing `MeetingTaskDetailPage` header action bar gets two new conditional buttons that are hidden when the respective field is empty.

**Tech Stack:** React, TypeScript, lucide-react (`Download` icon), Jest + React Testing Library

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `frontend/src/utils/downloadTextFile.ts` | `sanitizeFilename` + `downloadTextFile` |
| Create | `frontend/src/utils/__tests__/downloadTextFile.test.ts` | Unit tests for both exports |
| Modify | `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` | Add two download buttons to header |
| Create | `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx` | Component tests for button visibility + click |

---

## Task 1: `downloadTextFile` utility — TDD

**Files:**
- Create: `frontend/src/utils/downloadTextFile.ts`
- Create: `frontend/src/utils/__tests__/downloadTextFile.test.ts`

### Step 1.1 — Write failing tests

Create `frontend/src/utils/__tests__/downloadTextFile.test.ts`:

```typescript
import { sanitizeFilename, downloadTextFile } from '../downloadTextFile';

describe('sanitizeFilename', () => {
  it('lowercases the subject', () => {
    expect(sanitizeFilename('Hello World')).toBe('hello-world');
  });

  it('collapses whitespace runs to a single hyphen', () => {
    expect(sanitizeFilename('foo  bar   baz')).toBe('foo-bar-baz');
  });

  it('trims leading and trailing whitespace', () => {
    expect(sanitizeFilename('  hello  ')).toBe('hello');
  });

  it('strips dangerous filesystem characters', () => {
    expect(sanitizeFilename('foo/bar\\baz:qux*?<>|"')).toBe('foobarbazqux');
  });

  it('preserves Czech diacritics', () => {
    expect(sanitizeFilename('Schůzka s týmem Q2')).toBe('schůzka-s-týmem-q2');
  });

  it('preserves hyphens already in the subject', () => {
    expect(sanitizeFilename('Stand-up meeting')).toBe('stand-up-meeting');
  });
});

describe('downloadTextFile', () => {
  const mockCreateObjectURL = jest.fn(() => 'blob:mock-url');
  const mockRevokeObjectURL = jest.fn();
  const mockClick = jest.fn();
  const mockAnchor = { href: '', download: '', click: mockClick } as unknown as HTMLAnchorElement;

  beforeEach(() => {
    jest.clearAllMocks();
    global.URL.createObjectURL = mockCreateObjectURL;
    global.URL.revokeObjectURL = mockRevokeObjectURL;
    jest.spyOn(document, 'createElement').mockReturnValueOnce(mockAnchor);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('creates a Blob with the given content and MIME type', () => {
    const BlobSpy = jest.spyOn(global, 'Blob');
    downloadTextFile('hello content', 'file.txt', 'text/plain');
    expect(BlobSpy).toHaveBeenCalledWith(['hello content'], { type: 'text/plain' });
    BlobSpy.mockRestore();
  });

  it('sets href, download filename, and triggers click on the anchor', () => {
    downloadTextFile('# Summary\nContent', 'meeting-summary.md', 'text/markdown');
    expect(mockAnchor.href).toBe('blob:mock-url');
    expect(mockAnchor.download).toBe('meeting-summary.md');
    expect(mockClick).toHaveBeenCalledTimes(1);
  });

  it('revokes the object URL after click', () => {
    downloadTextFile('content', 'file.txt', 'text/plain');
    expect(mockRevokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });
});
```

### Step 1.2 — Run tests to verify they fail

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="downloadTextFile" 2>&1 | tail -20
```

Expected: FAIL — `Cannot find module '../downloadTextFile'`

### Step 1.3 — Write implementation

Create `frontend/src/utils/downloadTextFile.ts`:

```typescript
export function sanitizeFilename(subject: string): string {
  return subject
    .trim()
    .replace(/\s+/g, '-')
    .replace(/[/\\:*?"<>|]/g, '')
    .toLowerCase();
}

export function downloadTextFile(content: string, filename: string, mimeType: string): void {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
```

### Step 1.4 — Run tests to verify they pass

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="downloadTextFile" 2>&1 | tail -20
```

Expected: PASS — all 9 tests green

### Step 1.5 — Commit

```bash
git add frontend/src/utils/downloadTextFile.ts frontend/src/utils/__tests__/downloadTextFile.test.ts
git commit -m "feat: add downloadTextFile utility with sanitizeFilename"
```

---

## Task 2: Download buttons in `MeetingTaskDetailPage` — TDD

**Files:**
- Create: `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx`
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

### Step 2.1 — Write failing component tests

Create `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx`:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import * as downloadUtils from '../../../../utils/downloadTextFile';

// ---- Module mocks ----

jest.mock('../../../../api/hooks/useMeetingTasks');
jest.mock('../../../../api/hooks/useMeetingManagerPermission');
jest.mock('../explain/useExplainSelection');
jest.mock('../explain/ExplainTooltip', () => ({ ExplainTooltip: () => null }));
jest.mock('../explain/ExplainModal', () => ({ ExplainModal: () => null }));
jest.mock('../access/ManageAccessModal', () => ({ ManageAccessModal: () => null }));
jest.mock('../../../../utils/downloadTextFile');

import {
  useMeetingTaskDetail,
  useUpdateProposedTask,
  useUpdateProposedTaskStatus,
  useAddProposedTask,
  useSubmitToTodo,
  useMeetingUsers,
  useReimportMeeting,
  useExplainMeetingSummary,
} from '../../../../api/hooks/useMeetingTasks';
import { useMeetingManagerPermission } from '../../../../api/hooks/useMeetingManagerPermission';
import { useExplainSelection } from '../explain/useExplainSelection';
import MeetingTaskDetailPage from '../MeetingTaskDetailPage';

// ---- Helpers ----

const noopMutation = { mutate: jest.fn(), mutateAsync: jest.fn(), isPending: false, isError: false, error: null, reset: jest.fn() };

function buildTranscript(overrides: Partial<{ summary: string; rawTranscript: string }> = {}) {
  return {
    id: 'abc',
    subject: 'Schůzka s týmem',
    summary: 'AI summary text',
    rawTranscript: 'Speaker: Hello world',
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
    accessLevel: 'Private' as const,
    accessGrants: [],
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
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function setupHooks(transcriptOverrides: Parameters<typeof buildTranscript>[0] = {}) {
  (useMeetingTaskDetail as jest.Mock).mockReturnValue({ isLoading: false, data: { transcript: buildTranscript(transcriptOverrides) } });
  (useUpdateProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useUpdateProposedTaskStatus as jest.Mock).mockReturnValue(noopMutation);
  (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
  (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
  (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
  (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
  (useMeetingManagerPermission as jest.Mock).mockReturnValue(false);
  (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
}

// ---- Tests ----

beforeEach(() => jest.clearAllMocks());

describe('download summary button', () => {
  it('is visible when summary is non-empty', () => {
    setupHooks({ summary: 'Some summary' });
    renderPage();
    expect(screen.getByRole('button', { name: /stáhnout souhrn/i })).toBeInTheDocument();
  });

  it('is hidden when summary is empty', () => {
    setupHooks({ summary: '' });
    renderPage();
    expect(screen.queryByRole('button', { name: /stáhnout souhrn/i })).not.toBeInTheDocument();
  });

  it('calls downloadTextFile with .md filename and text/markdown MIME type on click', () => {
    setupHooks({ summary: '# AI Summary\nContent here' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /stáhnout souhrn/i }));
    expect(downloadUtils.downloadTextFile).toHaveBeenCalledWith(
      '# AI Summary\nContent here',
      'schůzka-s-týmem-summary.md',
      'text/markdown',
    );
  });
});

describe('download transcript button', () => {
  it('is visible when rawTranscript is non-empty', () => {
    setupHooks({ rawTranscript: 'Speaker: Hello' });
    renderPage();
    expect(screen.getByRole('button', { name: /stáhnout přepis/i })).toBeInTheDocument();
  });

  it('is hidden when rawTranscript is empty', () => {
    setupHooks({ rawTranscript: '' });
    renderPage();
    expect(screen.queryByRole('button', { name: /stáhnout přepis/i })).not.toBeInTheDocument();
  });

  it('calls downloadTextFile with .txt filename and text/plain MIME type on click', () => {
    setupHooks({ rawTranscript: 'Speaker A: Hello\nSpeaker B: World' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /stáhnout přepis/i }));
    expect(downloadUtils.downloadTextFile).toHaveBeenCalledWith(
      'Speaker A: Hello\nSpeaker B: World',
      'schůzka-s-týmem-transcript.txt',
      'text/plain',
    );
  });
});
```

### Step 2.2 — Run tests to verify they fail

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="MeetingTaskDetailPage.download" 2>&1 | tail -20
```

Expected: FAIL — buttons not found / `downloadTextFile` not called

### Step 2.3 — Implement the buttons

In `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`:

**a) Add `Download` to the lucide-react import (line 8):**

```typescript
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
  ChevronDown, ChevronRight, AlertTriangle, RefreshCw, Download,
} from "lucide-react";
```

**b) Add utility import after the existing hook imports (after line 29):**

```typescript
import { downloadTextFile, sanitizeFilename } from "../../../utils/downloadTextFile";
```

**c) Replace the header action `<div>` (lines 217–249) with the version that includes the two new buttons. The div starts at `<div className="flex items-center gap-2 shrink-0">` — replace its entire contents:**

```tsx
<div className="flex items-center gap-2 shrink-0">
  <TranscriptStatusBadge status={transcript.status} />
  {transcript.accessLevel && (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
      transcript.accessLevel === 'Public'
        ? 'bg-green-100 text-green-800'
        : transcript.accessLevel === 'Restricted'
        ? 'bg-yellow-100 text-yellow-800'
        : 'bg-gray-100 text-gray-600'
    }`}>
      {transcript.accessLevel === 'Private' && 'Soukromé'}
      {transcript.accessLevel === 'Public' && 'Veřejné'}
      {transcript.accessLevel === 'Restricted' && 'Omezené'}
    </span>
  )}
  {transcript.summary?.trim() && (
    <button
      type="button"
      onClick={() =>
        downloadTextFile(
          transcript.summary,
          `${sanitizeFilename(transcript.subject)}-summary.md`,
          'text/markdown',
        )
      }
      className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
    >
      <Download className="w-4 h-4 mr-1" />
      Stáhnout souhrn
    </button>
  )}
  {transcript.rawTranscript?.trim() && (
    <button
      type="button"
      onClick={() =>
        downloadTextFile(
          transcript.rawTranscript,
          `${sanitizeFilename(transcript.subject)}-transcript.txt`,
          'text/plain',
        )
      }
      className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
    >
      <Download className="w-4 h-4 mr-1" />
      Stáhnout přepis
    </button>
  )}
  <button
    type="button"
    onClick={handleReimport}
    disabled={reimport.isPending}
    className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
  >
    <RefreshCw className={`w-4 h-4 mr-1 ${reimport.isPending ? 'animate-spin' : ''}`} />
    Reimport
  </button>
  {isMeetingManager && (
    <button
      onClick={() => setAccessModalOpen(true)}
      className="px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
    >
      Spravovat přístup
    </button>
  )}
</div>
```

### Step 2.4 — Run component tests to verify they pass

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="MeetingTaskDetailPage.download" 2>&1 | tail -20
```

Expected: PASS — all 6 tests green

### Step 2.5 — Commit

```bash
git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx \
        frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx
git commit -m "feat(meeting-tasks): add download summary and transcript buttons to detail page"
```

---

## Task 3: Build verification

### Step 3.1 — TypeScript build

```bash
cd frontend && npm run build 2>&1 | tail -30
```

Expected: compiled successfully, no TypeScript errors

### Step 3.2 — Lint

```bash
cd frontend && npm run lint 2>&1 | tail -20
```

Expected: no new lint errors

### Step 3.3 — Run full utility test suite

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="src/utils" 2>&1 | tail -20
```

Expected: all utility tests pass (including new `downloadTextFile` tests)

### Step 3.4 — Commit if build/lint required any fixes

Only commit if step 3.1 or 3.2 produced errors that needed fixing:

```bash
git add <changed files>
git commit -m "fix: address build/lint issues after download buttons"
```
