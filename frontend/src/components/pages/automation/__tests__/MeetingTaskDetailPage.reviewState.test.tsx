import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
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

let transcriptStatusMutation: {
  mutate: jest.Mock;
  mutateAsync: jest.Mock;
  isPending: boolean;
  isError: boolean;
  error: null;
  reset: jest.Mock;
};

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
  (useUpdateTranscriptStatus as jest.Mock).mockReturnValue(transcriptStatusMutation);
  (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
  (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
  (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
  (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
  (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
}

beforeEach(() => {
  jest.clearAllMocks();
  transcriptStatusMutation = {
    mutate: jest.fn(),
    mutateAsync: jest.fn().mockResolvedValue({ success: true }),
    isPending: false,
    isError: false,
    error: null,
    reset: jest.fn(),
  };
});

describe('review-state toggle', () => {
  it('marks a pending meeting as approved', () => {
    setupHooks({ status: 'PendingReview' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Označit jako schváleno/i }));
    expect(transcriptStatusMutation.mutate).toHaveBeenCalledWith({ transcriptId: 'abc', status: 'Approved' });
  });

  it('reverts an approved meeting back to pending review', () => {
    setupHooks({ status: 'Approved' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Vrátit ke kontrole/i }));
    expect(transcriptStatusMutation.mutate).toHaveBeenCalledWith({ transcriptId: 'abc', status: 'PendingReview' });
  });
});

describe('leave-detail review prompt', () => {
  it('prompts when leaving a meeting that is still pending review', () => {
    setupHooks({ status: 'PendingReview' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Zpet na seznam/i }));
    expect(screen.getByText('Porada je stále ke kontrole')).toBeInTheDocument();
    expect(screen.queryByText('SEZNAM PORAD')).not.toBeInTheDocument();
  });

  it('marks reviewed and leaves when choosing the approve option', async () => {
    setupHooks({ status: 'PendingReview' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Zpet na seznam/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Označit jako schváleno a odejít' }));
    await waitFor(() => expect(transcriptStatusMutation.mutateAsync).toHaveBeenCalledWith({ transcriptId: 'abc', status: 'Approved' }));
    expect(await screen.findByText('SEZNAM PORAD')).toBeInTheDocument();
  });

  it('leaves without changing status when choosing keep pending', async () => {
    setupHooks({ status: 'PendingReview' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Zpet na seznam/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Nechat ke kontrole a odejít' }));
    expect(await screen.findByText('SEZNAM PORAD')).toBeInTheDocument();
    expect(transcriptStatusMutation.mutate).not.toHaveBeenCalled();
    expect(transcriptStatusMutation.mutateAsync).not.toHaveBeenCalled();
  });

  it('stays on the page when choosing to stay', () => {
    setupHooks({ status: 'PendingReview' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Zpet na seznam/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Zůstat' }));
    expect(screen.queryByText('Porada je stále ke kontrole')).not.toBeInTheDocument();
    expect(screen.queryByText('SEZNAM PORAD')).not.toBeInTheDocument();
  });

  it('does not prompt when the meeting is already approved', async () => {
    setupHooks({ status: 'Approved' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Zpet na seznam/i }));
    expect(await screen.findByText('SEZNAM PORAD')).toBeInTheDocument();
    expect(screen.queryByText('Porada je stále ke kontrole')).not.toBeInTheDocument();
  });
});
