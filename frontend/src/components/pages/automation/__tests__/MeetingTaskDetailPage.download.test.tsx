import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import * as downloadUtils from '../../../../utils/downloadTextFile';

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

// ---- Module mocks ----

jest.mock('react-markdown', () => ({ __esModule: true, default: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));

jest.mock('../../../../api/hooks/useMeetingTasks');
jest.mock('../../../../api/hooks/useMeetingManagerPermission');
jest.mock('../explain/useExplainSelection');
jest.mock('../explain/ExplainTooltip', () => ({ ExplainTooltip: () => null }));
jest.mock('../explain/ExplainModal', () => ({ ExplainModal: () => null }));
jest.mock('../access/ManageAccessModal', () => ({ ManageAccessModal: () => null }));
jest.mock('../../../../utils/downloadTextFile', () => ({
  ...jest.requireActual('../../../../utils/downloadTextFile'),
  downloadTextFile: jest.fn(),
}));

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
