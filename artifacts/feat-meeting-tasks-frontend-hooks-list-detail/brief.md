# Subtask 6: Frontend — Hooks, List Page, Detail Page, Config

**Parent Epic:** Meeting Task Validation Checkpoint

CRITICAL - This is part of epic, you **MUST** use epic branch - feature/meeting_tasks

## Task 10: Frontend — API Hooks

**Files:**
- Create: `frontend/src/api/hooks/useMeetingTasks.ts`

- [ ] **Step 1: Create React Query hooks**

```typescript
// frontend/src/api/hooks/useMeetingTasks.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// Types
export interface ProposedTaskDto {
  id: string;
  title: string;
  description: string;
  assignee: string;
  dueDate: string | null;
  status: string;
  externalTaskId: string | null;
  isManuallyAdded: boolean;
}

export interface MeetingTranscriptDto {
  id: string;
  subject: string;
  summary: string;
  plaudRecordingId: string;
  plaudCreatedAt: string;
  status: string;
  receivedAt: string;
  reviewedAt: string | null;
  reviewedByUser: string | null;
  taskCount: number;
  approvedTaskCount: number;
  rejectedTaskCount: number;
  tasks: ProposedTaskDto[];
}

interface TranscriptListResponse {
  success: boolean;
  items: MeetingTranscriptDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

interface TranscriptDetailResponse {
  success: boolean;
  transcript: MeetingTranscriptDto;
}

interface SubmitToTodoResponse {
  success: boolean;
  successCount: number;
  failedCount: number;
  errors: string[];
}

interface AddProposedTaskResponse {
  success: boolean;
  task: ProposedTaskDto;
}

// API Client
class MeetingTasksApiClient {
  private async makeRequest<T>(url: string, options?: RequestInit): Promise<T> {
    const apiClient = await getAuthenticatedApiClient();
    const fullUrl = `${(apiClient as any).baseUrl}${url}`;
    const response = await (apiClient as any).http.fetch(fullUrl, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...options?.headers,
      },
    });
    if (!response.ok) {
      throw new Error(`API error: ${response.status}`);
    }
    return response.json();
  }

  getList(statusFilter?: string, page = 1, pageSize = 20) {
    const params = new URLSearchParams();
    if (statusFilter) params.set('statusFilter', statusFilter);
    params.set('pageNumber', page.toString());
    params.set('pageSize', pageSize.toString());
    return this.makeRequest<TranscriptListResponse>(`/api/meeting-tasks?${params}`);
  }

  getDetail(id: string) {
    return this.makeRequest<TranscriptDetailResponse>(`/api/meeting-tasks/${id}`);
  }

  updateTask(transcriptId: string, taskId: string, data: { title: string; description: string; assignee: string; dueDate: string | null }) {
    return this.makeRequest(`/api/meeting-tasks/${transcriptId}/tasks/${taskId}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    });
  }

  updateTaskStatus(transcriptId: string, taskId: string, status: string) {
    return this.makeRequest(`/api/meeting-tasks/${transcriptId}/tasks/${taskId}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status }),
    });
  }

  addTask(transcriptId: string, data: { title: string; description: string; assignee: string; dueDate: string | null }) {
    return this.makeRequest<AddProposedTaskResponse>(`/api/meeting-tasks/${transcriptId}/tasks`, {
      method: 'POST',
      body: JSON.stringify(data),
    });
  }

  submitToTodo(transcriptId: string) {
    return this.makeRequest<SubmitToTodoResponse>(`/api/meeting-tasks/${transcriptId}/submit`, {
      method: 'POST',
    });
  }
}

const apiClient = new MeetingTasksApiClient();

// Query Keys
export const MEETING_TASKS_KEYS = {
  list: ['meetingTasks'] as const,
  detail: (id: string) => ['meetingTasks', id] as const,
};

// Hooks
export const useMeetingTasksList = (statusFilter?: string, page = 1, pageSize = 20) => {
  return useQuery({
    queryKey: [...MEETING_TASKS_KEYS.list, statusFilter, page, pageSize],
    queryFn: () => apiClient.getList(statusFilter, page, pageSize),
  });
};

export const useMeetingTaskDetail = (id: string) => {
  return useQuery({
    queryKey: MEETING_TASKS_KEYS.detail(id),
    queryFn: () => apiClient.getDetail(id),
    enabled: !!id,
  });
};

export const useUpdateProposedTask = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ transcriptId, taskId, data }: { transcriptId: string; taskId: string; data: { title: string; description: string; assignee: string; dueDate: string | null } }) =>
      apiClient.updateTask(transcriptId, taskId, data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(variables.transcriptId) });
    },
  });
};

export const useUpdateProposedTaskStatus = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ transcriptId, taskId, status }: { transcriptId: string; taskId: string; status: string }) =>
      apiClient.updateTaskStatus(transcriptId, taskId, status),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(variables.transcriptId) });
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
};

export const useAddProposedTask = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ transcriptId, data }: { transcriptId: string; data: { title: string; description: string; assignee: string; dueDate: string | null } }) =>
      apiClient.addTask(transcriptId, data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(variables.transcriptId) });
    },
  });
};

export const useSubmitToTodo = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (transcriptId: string) => apiClient.submitToTodo(transcriptId),
    onSuccess: (_, transcriptId) => {
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(transcriptId) });
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
};
```

- [ ] **Step 2: Verify frontend compiles**

Run: `cd frontend && npm run build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useMeetingTasks.ts
git commit -m "feat(meeting-tasks): add React Query hooks for meeting tasks API"
```

---

## Task 11: Frontend — List Page

**Files:**
- Create: `frontend/src/pages/automation/MeetingTasksPage.tsx`
- Modify: `frontend/src/App.tsx` — add route
- Modify: `frontend/src/components/Layout/Sidebar.tsx` — add nav item

- [ ] **Step 1: Create MeetingTasksPage**

```tsx
// frontend/src/pages/automation/MeetingTasksPage.tsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight } from 'lucide-react';
import { useMeetingTasksList } from '../../api/hooks/useMeetingTasks';

const StatusBadge: React.FC<{ status: string }> = ({ status }) => {
  const colorMap: Record<string, string> = {
    PendingReview: 'bg-yellow-100 text-yellow-800',
    Approved: 'bg-green-100 text-green-800',
    PartiallyApproved: 'bg-blue-100 text-blue-800',
  };
  const iconMap: Record<string, React.ReactNode> = {
    PendingReview: <Clock className="w-3 h-3 mr-1" />,
    Approved: <CheckCircle className="w-3 h-3 mr-1" />,
    PartiallyApproved: <CheckCircle2 className="w-3 h-3 mr-1" />,
  };
  const labelMap: Record<string, string> = {
    PendingReview: 'Ke kontrole',
    Approved: 'Schvaleno',
    PartiallyApproved: 'Castecne',
  };
  const classes = colorMap[status] ?? 'bg-gray-100 text-gray-800';
  return (
    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${classes}`}>
      {iconMap[status]}{labelMap[status] ?? status}
    </span>
  );
};

const MeetingTasksPage: React.FC = () => {
  const navigate = useNavigate();
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const [page, setPage] = useState(1);

  const { data, isLoading } = useMeetingTasksList(statusFilter, page);

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Meeting Tasks</h1>
        <p className="mt-1 text-sm text-gray-500">Kontrola uloh z meetingu pred odeslanim do TODO</p>
      </div>

      {/* Filters */}
      <div className="mb-4 flex gap-2">
        <button
          onClick={() => { setStatusFilter(undefined); setPage(1); }}
          className={`px-3 py-1.5 text-sm rounded-md ${!statusFilter ? 'bg-indigo-100 text-indigo-800' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
        >
          Vse
        </button>
        <button
          onClick={() => { setStatusFilter('PendingReview'); setPage(1); }}
          className={`px-3 py-1.5 text-sm rounded-md ${statusFilter === 'PendingReview' ? 'bg-yellow-100 text-yellow-800' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
        >
          Ke kontrole
        </button>
        <button
          onClick={() => { setStatusFilter('Approved'); setPage(1); }}
          className={`px-3 py-1.5 text-sm rounded-md ${statusFilter === 'Approved' ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
        >
          Schvaleno
        </button>
      </div>

      {/* Table */}
      <div className="bg-white shadow-sm rounded-lg overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Predmet</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Prijato</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Ulohy</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Stav</th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {isLoading && (
              <tr><td colSpan={4} className="px-6 py-4 text-center text-gray-500">Nacitani...</td></tr>
            )}
            {data?.items.map((transcript) => (
              <tr
                key={transcript.id}
                onClick={() => navigate(`/automation/meeting-tasks/${transcript.id}`)}
                className={`cursor-pointer hover:bg-gray-50 ${transcript.status === 'PendingReview' ? 'bg-yellow-50' : ''}`}
              >
                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{transcript.subject}</td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                  {new Date(transcript.receivedAt).toLocaleDateString('cs-CZ')}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                  {transcript.taskCount}
                  {transcript.approvedTaskCount > 0 && (
                    <span className="ml-1 text-green-600">({transcript.approvedTaskCount} schvaleno)</span>
                  )}
                </td>
                <td className="px-6 py-4 whitespace-nowrap"><StatusBadge status={transcript.status} /></td>
              </tr>
            ))}
            {!isLoading && data?.items.length === 0 && (
              <tr><td colSpan={4} className="px-6 py-4 text-center text-gray-500">Zadne zaznamy</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {data && data.totalPages > 1 && (
        <div className="mt-4 flex items-center justify-between">
          <span className="text-sm text-gray-700">
            Strana {data.pageNumber} z {data.totalPages} ({data.totalCount} celkem)
          </span>
          <div className="flex gap-2">
            <button
              onClick={() => setPage(p => Math.max(1, p - 1))}
              disabled={page === 1}
              className="px-3 py-1 text-sm border rounded-md disabled:opacity-50"
            >
              <ChevronLeft className="w-4 h-4" />
            </button>
            <button
              onClick={() => setPage(p => Math.min(data.totalPages, p + 1))}
              disabled={page >= data.totalPages}
              className="px-3 py-1 text-sm border rounded-md disabled:opacity-50"
            >
              <ChevronRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default MeetingTasksPage;
```

- [ ] **Step 2: Add route to App.tsx**

Add after the `/automation/background-tasks` route (around line 435) in `frontend/src/App.tsx`:

```tsx
<Route
  path="/automation/meeting-tasks"
  element={<MeetingTasksPage />}
/>
<Route
  path="/automation/meeting-tasks/:id"
  element={<MeetingTaskDetailPage />}
/>
```

Add imports at the top:
```tsx
import MeetingTasksPage from './pages/automation/MeetingTasksPage';
import MeetingTaskDetailPage from './pages/automation/MeetingTaskDetailPage';
```

- [ ] **Step 3: Add nav item to Sidebar.tsx**

Add to the "automatizace" section items array (after "hangfire" item, around line 280) in `frontend/src/components/Layout/Sidebar.tsx`:

```tsx
,{
  id: "meeting-tasks",
  name: "Meeting Tasks",
  href: "/automation/meeting-tasks",
}
```

- [ ] **Step 4: Verify frontend compiles** (will fail until Task 12 creates MeetingTaskDetailPage — that's expected)

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/automation/MeetingTasksPage.tsx
git add frontend/src/App.tsx
git add frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat(meeting-tasks): add MeetingTasksPage list view with filters and pagination"
```

---

## Task 12: Frontend — Detail/Validation Page

**Files:**
- Create: `frontend/src/pages/automation/MeetingTaskDetailPage.tsx`

- [ ] **Step 1: Create MeetingTaskDetailPage**

```tsx
// frontend/src/pages/automation/MeetingTaskDetailPage.tsx
import React, { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Check, X, Plus, Send, CheckCheck } from 'lucide-react';
import {
  useMeetingTaskDetail,
  useUpdateProposedTask,
  useUpdateProposedTaskStatus,
  useAddProposedTask,
  useSubmitToTodo,
  ProposedTaskDto,
} from '../../api/hooks/useMeetingTasks';

interface EditingTask {
  title: string;
  description: string;
  assignee: string;
  dueDate: string;
}

const TaskStatusBadge: React.FC<{ status: string }> = ({ status }) => {
  const colorMap: Record<string, string> = {
    Pending: 'bg-gray-100 text-gray-800',
    Approved: 'bg-green-100 text-green-800',
    Rejected: 'bg-red-100 text-red-800',
  };
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${colorMap[status] ?? 'bg-gray-100 text-gray-800'}`}>
      {status === 'Approved' ? 'Schvaleno' : status === 'Rejected' ? 'Zamitnuto' : 'Ceka'}
    </span>
  );
};

const MeetingTaskDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { data, isLoading } = useMeetingTaskDetail(id!);
  const updateTask = useUpdateProposedTask();
  const updateStatus = useUpdateProposedTaskStatus();
  const addTask = useAddProposedTask();
  const submitToTodo = useSubmitToTodo();

  const [editingTaskId, setEditingTaskId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<EditingTask>({ title: '', description: '', assignee: '', dueDate: '' });
  const [showAddForm, setShowAddForm] = useState(false);
  const [newTask, setNewTask] = useState<EditingTask>({ title: '', description: '', assignee: '', dueDate: '' });
  const [showConfirmSubmit, setShowConfirmSubmit] = useState(false);

  const transcript = data?.transcript;

  const startEdit = (task: ProposedTaskDto) => {
    setEditingTaskId(task.id);
    setEditForm({
      title: task.title,
      description: task.description,
      assignee: task.assignee,
      dueDate: task.dueDate ? task.dueDate.split('T')[0] : '',
    });
  };

  const saveEdit = async () => {
    if (!editingTaskId || !id) return;
    await updateTask.mutateAsync({
      transcriptId: id,
      taskId: editingTaskId,
      data: { ...editForm, dueDate: editForm.dueDate || null },
    });
    setEditingTaskId(null);
  };

  const handleStatusChange = async (taskId: string, status: string) => {
    if (!id) return;
    await updateStatus.mutateAsync({ transcriptId: id, taskId, status });
  };

  const handleApproveAll = async () => {
    if (!id || !transcript) return;
    const pendingTasks = transcript.tasks.filter(t => t.status === 'Pending');
    for (const task of pendingTasks) {
      await updateStatus.mutateAsync({ transcriptId: id, taskId: task.id, status: 'Approved' });
    }
  };

  const handleAddTask = async () => {
    if (!id) return;
    await addTask.mutateAsync({
      transcriptId: id,
      data: { ...newTask, dueDate: newTask.dueDate || null },
    });
    setNewTask({ title: '', description: '', assignee: '', dueDate: '' });
    setShowAddForm(false);
  };

  const handleSubmit = async () => {
    if (!id) return;
    await submitToTodo.mutateAsync(id);
    setShowConfirmSubmit(false);
  };

  if (isLoading) return <div className="p-6 text-gray-500">Nacitani...</div>;
  if (!transcript) return <div className="p-6 text-gray-500">Zaznam nenalezen</div>;

  const approvedCount = transcript.tasks.filter(t => t.status === 'Approved').length;
  const pendingCount = transcript.tasks.filter(t => t.status === 'Pending').length;

  return (
    <div className="p-6 max-w-5xl mx-auto">
      {/* Header */}
      <button onClick={() => navigate('/automation/meeting-tasks')} className="flex items-center text-sm text-gray-500 hover:text-gray-700 mb-4">
        <ArrowLeft className="w-4 h-4 mr-1" /> Zpet na seznam
      </button>

      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">{transcript.subject}</h1>
        <p className="text-sm text-gray-500 mt-1">
          Prijato: {new Date(transcript.plaudCreatedAt).toLocaleString('cs-CZ')} | Plaud ID: {transcript.plaudRecordingId}
        </p>
      </div>

      {/* Summary */}
      <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 mb-6">
        <h2 className="text-sm font-semibold text-blue-900 mb-2">Shrnutí meetingu</h2>
        <p className="text-sm text-blue-800 whitespace-pre-wrap">{transcript.summary}</p>
      </div>

      {/* Tasks */}
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-gray-900">Navrhovane ulohy ({transcript.tasks.length})</h2>
        <div className="flex gap-2">
          {pendingCount > 0 && (
            <button onClick={handleApproveAll} className="inline-flex items-center px-3 py-1.5 text-sm bg-green-50 text-green-700 rounded-md hover:bg-green-100">
              <CheckCheck className="w-4 h-4 mr-1" /> Schvalit vse ({pendingCount})
            </button>
          )}
          <button onClick={() => setShowAddForm(true)} className="inline-flex items-center px-3 py-1.5 text-sm bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200">
            <Plus className="w-4 h-4 mr-1" /> Pridat ulohu
          </button>
        </div>
      </div>

      <div className="space-y-3">
        {transcript.tasks.map((task) => (
          <div
            key={task.id}
            className={`border rounded-lg p-4 ${
              task.status === 'Approved' ? 'border-green-200 bg-green-50' :
              task.status === 'Rejected' ? 'border-gray-200 bg-gray-50 opacity-60' :
              'border-gray-200 bg-white'
            }`}
          >
            {editingTaskId === task.id ? (
              /* Edit mode */
              <div className="space-y-3">
                <input value={editForm.title} onChange={e => setEditForm({ ...editForm, title: e.target.value })}
                  className="w-full border rounded px-3 py-1.5 text-sm" placeholder="Nazev" />
                <textarea value={editForm.description} onChange={e => setEditForm({ ...editForm, description: e.target.value })}
                  className="w-full border rounded px-3 py-1.5 text-sm" rows={2} placeholder="Popis" />
                <div className="flex gap-3">
                  <input value={editForm.assignee} onChange={e => setEditForm({ ...editForm, assignee: e.target.value })}
                    className="flex-1 border rounded px-3 py-1.5 text-sm" placeholder="Prirazeno" />
                  <input type="date" value={editForm.dueDate} onChange={e => setEditForm({ ...editForm, dueDate: e.target.value })}
                    className="border rounded px-3 py-1.5 text-sm" />
                </div>
                <div className="flex gap-2">
                  <button onClick={saveEdit} className="px-3 py-1 text-sm bg-indigo-600 text-white rounded hover:bg-indigo-700">Ulozit</button>
                  <button onClick={() => setEditingTaskId(null)} className="px-3 py-1 text-sm bg-gray-200 text-gray-700 rounded hover:bg-gray-300">Zrusit</button>
                </div>
              </div>
            ) : (
              /* View mode */
              <div className="flex items-start justify-between">
                <div className="flex-1" onClick={() => task.status === 'Pending' && startEdit(task)}>
                  <div className="flex items-center gap-2 mb-1">
                    <span className={`text-sm font-medium ${task.status === 'Rejected' ? 'line-through text-gray-400' : 'text-gray-900'}`}>
                      {task.title}
                    </span>
                    <TaskStatusBadge status={task.status} />
                    {task.isManuallyAdded && (
                      <span className="text-xs text-indigo-600 font-medium">rucne pridano</span>
                    )}
                  </div>
                  {task.description && <p className="text-sm text-gray-600 mb-1">{task.description}</p>}
                  <div className="flex gap-4 text-xs text-gray-500">
                    <span>Prirazeno: {task.assignee}</span>
                    {task.dueDate && <span>Termin: {new Date(task.dueDate).toLocaleDateString('cs-CZ')}</span>}
                    {task.externalTaskId && <span className="text-green-600">Odeslano do TODO</span>}
                  </div>
                </div>
                {task.status === 'Pending' && (
                  <div className="flex gap-1 ml-4">
                    <button onClick={() => handleStatusChange(task.id, 'Approved')}
                      className="p-1.5 rounded-md bg-green-100 text-green-700 hover:bg-green-200" title="Schvalit">
                      <Check className="w-4 h-4" />
                    </button>
                    <button onClick={() => handleStatusChange(task.id, 'Rejected')}
                      className="p-1.5 rounded-md bg-red-100 text-red-700 hover:bg-red-200" title="Zamitnout">
                      <X className="w-4 h-4" />
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}

        {/* Add task form */}
        {showAddForm && (
          <div className="border-2 border-dashed border-indigo-300 rounded-lg p-4 space-y-3">
            <input value={newTask.title} onChange={e => setNewTask({ ...newTask, title: e.target.value })}
              className="w-full border rounded px-3 py-1.5 text-sm" placeholder="Nazev ulohy" />
            <textarea value={newTask.description} onChange={e => setNewTask({ ...newTask, description: e.target.value })}
              className="w-full border rounded px-3 py-1.5 text-sm" rows={2} placeholder="Popis" />
            <div className="flex gap-3">
              <input value={newTask.assignee} onChange={e => setNewTask({ ...newTask, assignee: e.target.value })}
                className="flex-1 border rounded px-3 py-1.5 text-sm" placeholder="Prirazeno komu" />
              <input type="date" value={newTask.dueDate} onChange={e => setNewTask({ ...newTask, dueDate: e.target.value })}
                className="border rounded px-3 py-1.5 text-sm" />
            </div>
            <div className="flex gap-2">
              <button onClick={handleAddTask} disabled={!newTask.title || !newTask.assignee}
                className="px-3 py-1 text-sm bg-indigo-600 text-white rounded hover:bg-indigo-700 disabled:opacity-50">Pridat</button>
              <button onClick={() => setShowAddForm(false)} className="px-3 py-1 text-sm bg-gray-200 text-gray-700 rounded hover:bg-gray-300">Zrusit</button>
            </div>
          </div>
        )}
      </div>

      {/* Submit footer */}
      <div className="mt-8 flex justify-end gap-3">
        <button
          onClick={() => setShowConfirmSubmit(true)}
          disabled={approvedCount === 0}
          className="inline-flex items-center px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Send className="w-4 h-4 mr-2" /> Odeslat do TODO ({approvedCount})
        </button>
      </div>

      {/* Confirmation dialog */}
      {showConfirmSubmit && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 max-w-md w-full mx-4">
            <h3 className="text-lg font-semibold mb-2">Potvrdit odeslani</h3>
            <p className="text-sm text-gray-600 mb-4">
              Odeslat {approvedCount} schvalenych uloh do Microsoft TODO?
            </p>
            {submitToTodo.isError && (
              <p className="text-sm text-red-600 mb-4">Chyba pri odesilani. Zkuste to znovu.</p>
            )}
            <div className="flex justify-end gap-2">
              <button onClick={() => setShowConfirmSubmit(false)} className="px-4 py-2 text-sm bg-gray-200 rounded-md hover:bg-gray-300">Zrusit</button>
              <button onClick={handleSubmit} disabled={submitToTodo.isPending}
                className="px-4 py-2 text-sm bg-indigo-600 text-white rounded-md hover:bg-indigo-700 disabled:opacity-50">
                {submitToTodo.isPending ? 'Odesilam...' : 'Potvrdit'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default MeetingTaskDetailPage;
```

- [ ] **Step 2: Verify frontend compiles**

Run: `cd frontend && npm run build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/automation/MeetingTaskDetailPage.tsx
git commit -m "feat(meeting-tasks): add MeetingTaskDetailPage validation UI with edit, approve/reject, and submit"
```

---

## Task 13: Configuration & Final Wiring

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` — add MeetingTasks section

- [ ] **Step 1: Add MeetingTasks config section**

Add to `appsettings.json`:

```json
"MeetingTasks": {
  "ApiKey": "",
  "TodoListName": "Meeting Actions"
}
```

The actual API key value goes in secrets.json (local) or environment variables (production).

- [ ] **Step 2: Run full backend build**

Run: `dotnet build backend/`
Expected: Build succeeded

- [ ] **Step 3: Run full frontend build**

Run: `cd frontend && npm run build`
Expected: Build succeeded

- [ ] **Step 4: Run all backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~MeetingTasks"`
Expected: All tests PASS

- [ ] **Step 5: Run dotnet format**

Run: `dotnet format backend/`
Expected: No formatting issues (or auto-fixed)

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(meeting-tasks): add configuration section for API key and TODO list name"
```

---

## Verification

After all tasks are complete:

1. **Start the backend** locally: `dotnet run --project backend/src/Anela.Heblo.API/`
2. **POST a test transcript** via curl:
   ```bash
   curl -X POST https://localhost:5001/api/meeting-tasks \
     -H "X-Api-Key: YOUR_KEY" \
     -H "Content-Type: application/json" \
     -d '{"subject":"Test Meeting","summary":"We discussed testing.","plaudRecordingId":"test@example.com","tasks":[{"title":"Write tests","description":"For new feature","assignee":"Alice","dueDate":"2026-05-01"}]}'
   ```
3. **Open Heblo UI**: Navigate to `/automation/meeting-tasks` — verify the transcript appears
4. **Open detail**: Click the transcript, verify summary and tasks display
5. **Edit a task**: Click a task, modify title, save
6. **Approve/reject**: Use the approve/reject buttons
7. **Add a task**: Use "Pridat ulohu" button
8. **Submit**: Click "Odeslat do TODO" with at least one approved task (requires Graph API setup)

---

> **Integration:** Create your feature branch from `feat/meeting-task-validation-epic`. When done, open a PR targeting `feat/meeting-task-validation-epic` (not `main`).