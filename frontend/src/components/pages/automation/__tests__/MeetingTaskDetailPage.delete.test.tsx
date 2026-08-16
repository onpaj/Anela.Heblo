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
  useDeleteMeeting,
} from '../../../../api/hooks/useMeetingTasks';
import { useExplainSelection } from '../explain/useExplainSelection';
import MeetingTaskDetailPage from '../MeetingTaskDetailPage';

// ---- Module mocks ----

jest.mock('react-markdown', () => ({ __esModule: true, default: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

jest.mock('../../../../api/hooks/useMeetingTasks');
let mockHasPermission: (perm: string) => boolean = () => false;
jest.mock('../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
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

function buildTranscript() {
  return {
    id: 'abc',
    subject: 'Schůzka s týmem',
    summary: 'AI summary text',
    rawTranscript: 'Speaker: Hello world',
    plaudRecordingId: 'plaud-1',
    plaudCreatedAt: '2026-05-19T10:00:00Z',
    status: 'Approved',
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

function setupHooks(deleteMutation: Partial<typeof noopMutation> = {}) {
  (useMeetingTaskDetail as jest.Mock).mockReturnValue({ isLoading: false, data: { transcript: buildTranscript() } });
  (useUpdateProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useUpdateProposedTaskStatus as jest.Mock).mockReturnValue(noopMutation);
  (useUpdateTranscriptStatus as jest.Mock).mockReturnValue(noopMutation);
  (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
  (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
  (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
  (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
  (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
  (useDeleteMeeting as jest.Mock).mockReturnValue({ ...noopMutation, ...deleteMutation });
}

// ---- Tests ----

beforeEach(() => {
  jest.clearAllMocks();
  mockHasPermission = () => false;
});

describe('delete meeting button', () => {
  it('is hidden without the anela.meetings.write permission', () => {
    setupHooks();
    renderPage();
    expect(screen.queryByRole('button', { name: /^smazat$/i })).not.toBeInTheDocument();
  });

  it('is visible for a meeting manager', () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    setupHooks();
    renderPage();
    expect(screen.getByRole('button', { name: /^smazat$/i })).toBeInTheDocument();
  });

  it('opens the confirmation dialog instead of deleting immediately', () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    setupHooks({ mutateAsync });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));

    expect(screen.getByText('Smazat schůzku?')).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('deletes and navigates to the list when confirmed', async () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    setupHooks({ mutateAsync });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));
    fireEvent.click(screen.getAllByRole('button', { name: /^smazat$/i })[1]);

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith('abc'));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('/automation/meeting-tasks'));
  });

  it('keeps the dialog open and shows an error when deletion fails', async () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    const mutateAsync = jest.fn().mockRejectedValue(new Error('API error: 500'));
    setupHooks({ mutateAsync });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));
    fireEvent.click(screen.getAllByRole('button', { name: /^smazat$/i })[1]);

    expect(await screen.findByText(/nezdařilo/i)).toBeInTheDocument();
    expect(screen.getByText('Smazat schůzku?')).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});
