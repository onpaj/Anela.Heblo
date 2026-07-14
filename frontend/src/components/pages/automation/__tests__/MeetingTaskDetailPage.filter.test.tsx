import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

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
import { useExplainSelection } from '../explain/useExplainSelection';
import MeetingTaskDetailPage from '../MeetingTaskDetailPage';

// ---- Module mocks ----

jest.mock('react-markdown', () => ({ __esModule: true, default: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));

jest.mock('../../../../api/hooks/useMeetingTasks');
jest.mock('../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: () => false,
  }),
}));
let mockUsername: string | undefined = 'me@anela.cz';
jest.mock('../../../../auth/useAuth', () => ({
  useAuth: () => ({ account: mockUsername ? { username: mockUsername } : null }),
}));
jest.mock('../explain/useExplainSelection');
jest.mock('../explain/ExplainTooltip', () => ({ ExplainTooltip: () => null }));
jest.mock('../explain/ExplainModal', () => ({ ExplainModal: () => null }));
jest.mock('../access/ManageAccessModal', () => ({ ManageAccessModal: () => null }));

// ---- Helpers ----

const noopMutation = { mutate: jest.fn(), mutateAsync: jest.fn(), isPending: false, isError: false, error: null, reset: jest.fn() };

function buildTask(overrides: Record<string, unknown>) {
  return {
    id: 'task-x',
    title: 'Task',
    description: '',
    assignee: '',
    assigneeEmail: null,
    dueDate: null,
    status: 'Approved',
    externalTaskId: null,
    isManuallyAdded: false,
    ...overrides,
  };
}

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
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function setupHooks(transcriptOverrides: Record<string, unknown> = {}) {
  (useMeetingTaskDetail as jest.Mock).mockReturnValue({ isLoading: false, data: { transcript: buildTranscript(transcriptOverrides) } });
  (useUpdateProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useUpdateProposedTaskStatus as jest.Mock).mockReturnValue(noopMutation);
  (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
  (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
  (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
  (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
  (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
}

beforeEach(() => {
  jest.clearAllMocks();
  mockUsername = 'me@anela.cz';
});

describe('participants line', () => {
  it('lists the meeting participants', () => {
    setupHooks({ participants: ['Alice', 'Bob'] });
    renderPage();
    expect(screen.getByText('Účastníci:')).toBeInTheDocument();
    expect(screen.getByText('Alice, Bob')).toBeInTheDocument();
  });

  it('renders a dash when there are no participants', () => {
    setupHooks({ participants: [] });
    renderPage();
    expect(
      screen.getByText((_, element) => element?.tagName === 'P' && element.textContent === 'Účastníci: —'),
    ).toBeInTheDocument();
  });
});

describe('suggested tasks filter', () => {
  const tasks = [
    buildTask({ id: 't1', title: 'My Task', assignee: 'Já', assigneeEmail: 'me@anela.cz' }),
    buildTask({ id: 't2', title: 'Bob Task', assignee: 'Bob', assigneeEmail: 'bob@anela.cz' }),
  ];

  it('shows all tasks initially', () => {
    setupHooks({ tasks });
    renderPage();
    expect(screen.getByText('My Task')).toBeInTheDocument();
    expect(screen.getByText('Bob Task')).toBeInTheDocument();
  });

  it('filters to the selected person via the dropdown', () => {
    setupHooks({ tasks });
    renderPage();
    fireEvent.change(screen.getByLabelText('Filtrovat podle osoby'), { target: { value: 'bob@anela.cz' } });
    expect(screen.getByText('Bob Task')).toBeInTheDocument();
    expect(screen.queryByText('My Task')).not.toBeInTheDocument();
  });

  it('filters to my tasks when the Ja toggle is pressed', () => {
    setupHooks({ tasks });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Ja' }));
    expect(screen.getByText('My Task')).toBeInTheDocument();
    expect(screen.queryByText('Bob Task')).not.toBeInTheDocument();
  });

  it('hides the Ja toggle when there is no signed-in user', () => {
    mockUsername = undefined;
    setupHooks({ tasks });
    renderPage();
    expect(screen.queryByRole('button', { name: 'Ja' })).not.toBeInTheDocument();
  });

  it('shows an empty-state message when the filter matches no tasks', () => {
    setupHooks({ tasks: [buildTask({ id: 't2', title: 'Bob Task', assignee: 'Bob', assigneeEmail: 'bob@anela.cz' })] });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Ja' }));
    expect(screen.getByText(/nejsou prirazeny zadne ulohy/i)).toBeInTheDocument();
  });
});
