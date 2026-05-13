# Meeting Tasks Frontend — Hooks, List, Detail, Config — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the user-facing UI for the Meeting Task Validation Checkpoint feature: React Query hooks, a list page with filters/pagination, a detail/validation page (edit/approve/reject/add/submit), routing + sidebar wiring, and backend config.

**Architecture:** Hand-written DTOs + raw-fetch hooks (no OpenAPI regen) following the existing `useBackgroundRefresh.ts` / `useAsyncInvoiceImport.ts` pattern. Pages live under `frontend/src/components/pages/automation/` next to `BackgroundTasks.tsx`. Mutations invalidate `MEETING_TASKS_KEYS.detail(id)` and (for status/submit) `MEETING_TASKS_KEYS.list`. Status types use string-literal unions. Backend config gains a `MeetingTasks` section containing only `TodoListName` (the `ApiKey` field is deferred to the Plaud-ingest subtask per arch review amendment #1).

**Tech Stack:** React 18, TypeScript (strict), `@tanstack/react-query`, `react-router-dom`, `lucide-react`, Tailwind CSS, Jest + React Testing Library (for hook tests). .NET 8 / ASP.NET Core for backend config.

---

## File Structure

**Frontend — Create**
- `frontend/src/api/hooks/useMeetingTasks.ts` — DTOs, query keys, raw-fetch client, list/detail/mutation hooks.
- `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts` — hook unit tests covering fetch URLs, query keys, mutation invalidation, error contract.
- `frontend/src/components/pages/automation/MeetingTasksPage.tsx` — list view with status filter, table, pagination.
- `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` — detail/validation view: header, summary, task cards, inline edit, approve/reject, bulk approve, add task form, submit-to-TODO modal.

**Frontend — Modify**
- `frontend/src/App.tsx` — register two new routes and imports.
- `frontend/src/components/Layout/Sidebar.tsx` — add "Meeting Tasks" nav item under "Automatizace".

**Backend — Modify**
- `backend/src/Anela.Heblo.API/appsettings.json` — add a `MeetingTasks` section with `TodoListName` only.

---

## Task 1: Add `MeetingTasks` section to backend `appsettings.json`

Per arch review amendment #1, commit only `TodoListName` (matches `MeetingTasksOptions.cs` which currently exposes that property only). The `ApiKey` field belongs to the Plaud-ingest subtask.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Read current appsettings.json**

Run: open `backend/src/Anela.Heblo.API/appsettings.json` and confirm the top-level structure (object with sibling keys `ManufactureGroupId`, `MarketingCalendar`, `AzureAd`, …).

- [ ] **Step 2: Insert the `MeetingTasks` section**

Add the following JSON block at the top level of the file, placed adjacent to other feature-config sections (e.g. directly after `"MarketingCalendar": { … }`). Preserve trailing commas to keep valid JSON:

```json
"MeetingTasks": {
  "TodoListName": "Meeting Actions"
},
```

- [ ] **Step 3: Verify build + format**

Run from repo root:

```
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: build succeeds (zero errors); format reports no diffs. If `dotnet format` rewrites the JSON's whitespace, accept the rewrite and re-run until clean.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add MeetingTasks config section (TodoListName)"
```

---

## Task 2: Create `useMeetingTasks.ts` — types + query keys + raw-fetch client (TDD)

This task brings the hooks file from empty to fully tested. We write a failing test first, then add types + client + hooks just to make tests pass.

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts`
- Create: `frontend/src/api/hooks/useMeetingTasks.ts`

- [ ] **Step 1: Write the failing test file**

Create `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts` with this content. It exercises the list query (GET URL, query key shape), the detail query (`enabled: false` when id falsy), and one mutation invalidation behavior:

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import {
  useMeetingTasksList,
  useMeetingTaskDetail,
  useUpdateProposedTaskStatus,
  MEETING_TASKS_KEYS,
  TranscriptListResponse,
  TranscriptDetailResponse,
} from '../useMeetingTasks';
import {
  createMockApiClient,
  mockAuthenticatedApiClient,
  createQueryClientWrapper,
} from '../../testUtils';

jest.mock('../../client');

describe('useMeetingTasks', () => {
  let mockFetch: jest.Mock;
  let mockClient: ReturnType<typeof createMockApiClient>['mockClient'];

  beforeEach(() => {
    const mock = createMockApiClient();
    mockClient = mock.mockClient;
    mockFetch = mock.mockFetch;
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('MEETING_TASKS_KEYS', () => {
    it('exposes stable list and detail key factories', () => {
      expect(MEETING_TASKS_KEYS.list).toEqual(['meetingTasks']);
      expect(MEETING_TASKS_KEYS.detail('abc')).toEqual(['meetingTasks', 'abc']);
    });
  });

  describe('useMeetingTasksList', () => {
    it('GETs /api/meeting-tasks with statusFilter, pageNumber, pageSize', async () => {
      const payload: TranscriptListResponse = {
        success: true,
        items: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 0,
      };
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });

      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(
        () => useMeetingTasksList('PendingReview', 2, 20),
        { wrapper },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks?statusFilter=PendingReview&pageNumber=2&pageSize=20`,
        expect.objectContaining({ method: 'GET' }),
      );
    });

    it('omits statusFilter param when filter is undefined', async () => {
      mockFetch.mockResolvedValue({
        ok: true, status: 200,
        json: () => Promise.resolve({ success: true, items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }),
      });
      const { wrapper } = createQueryClientWrapper();
      renderHook(() => useMeetingTasksList(undefined, 1, 20), { wrapper });
      await waitFor(() => expect(mockFetch).toHaveBeenCalled());
      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks?pageNumber=1&pageSize=20`,
        expect.any(Object),
      );
    });

    it('throws "API error: {status}" on non-2xx', async () => {
      mockFetch.mockResolvedValue({ ok: false, status: 500, json: () => Promise.resolve({}) });
      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(() => useMeetingTasksList(undefined, 1, 20), { wrapper });
      await waitFor(() => expect(result.current.isError).toBe(true));
      expect((result.current.error as Error).message).toBe('API error: 500');
    });
  });

  describe('useMeetingTaskDetail', () => {
    it('does not fetch when id is empty', () => {
      const { wrapper } = createQueryClientWrapper();
      renderHook(() => useMeetingTaskDetail(''), { wrapper });
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it('GETs /api/meeting-tasks/{id} when id is provided', async () => {
      const payload: TranscriptDetailResponse = {
        success: true,
        transcript: {
          id: 'abc',
          subject: 'Test',
          summary: '',
          plaudRecordingId: 'r',
          plaudCreatedAt: '2026-05-13T08:00:00Z',
          status: 'PendingReview',
          receivedAt: '2026-05-13T08:00:00Z',
          reviewedAt: null,
          reviewedByUser: null,
          taskCount: 0,
          approvedTaskCount: 0,
          rejectedTaskCount: 0,
          tasks: [],
        },
      };
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });
      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(() => useMeetingTaskDetail('abc'), { wrapper });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks/abc`,
        expect.objectContaining({ method: 'GET' }),
      );
    });
  });

  describe('useUpdateProposedTaskStatus', () => {
    it('invalidates detail and list on success', async () => {
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({}) });
      const { wrapper, queryClient } = createQueryClientWrapper();
      const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');

      const { result } = renderHook(() => useUpdateProposedTaskStatus(), { wrapper });
      await result.current.mutateAsync({ transcriptId: 'tid', taskId: 'k', status: 'Approved' });

      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: MEETING_TASKS_KEYS.detail('tid') });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: MEETING_TASKS_KEYS.list });
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `frontend/`:

```
npx jest src/api/hooks/__tests__/useMeetingTasks.test.ts
```

Expected: FAIL — `Cannot find module '../useMeetingTasks'`.

- [ ] **Step 3: Create the hook file with types, keys, client, and hooks**

Create `frontend/src/api/hooks/useMeetingTasks.ts` with the following complete content:

```typescript
// TODO: migrate to generated client when /api/meeting-tasks is added to NSwag.
// Pattern matches useBackgroundRefresh.ts / useAsyncInvoiceImport.ts.
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// --- DTOs ---

export type ProposedTaskStatus = "Pending" | "Approved" | "Rejected";
export type TranscriptStatus = "PendingReview" | "Approved" | "PartiallyApproved";

export interface ProposedTaskDto {
  id: string;
  title: string;
  description: string;
  assignee: string;
  dueDate: string | null;
  status: ProposedTaskStatus;
  externalTaskId: string | null;
  isManuallyAdded: boolean;
}

export interface MeetingTranscriptDto {
  id: string;
  subject: string;
  summary: string;
  plaudRecordingId: string;
  plaudCreatedAt: string;
  status: TranscriptStatus;
  receivedAt: string;
  reviewedAt: string | null;
  reviewedByUser: string | null;
  taskCount: number;
  approvedTaskCount: number;
  rejectedTaskCount: number;
  tasks: ProposedTaskDto[];
}

export interface TranscriptListResponse {
  success: boolean;
  items: MeetingTranscriptDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface TranscriptDetailResponse {
  success: boolean;
  transcript: MeetingTranscriptDto;
}

export interface SubmitToTodoResponse {
  success: boolean;
  successCount: number;
  failedCount: number;
  errors: string[];
}

export interface AddProposedTaskResponse {
  success: boolean;
  task: ProposedTaskDto;
}

export interface TaskFormData {
  title: string;
  description: string;
  assignee: string;
  dueDate: string | null;
}

// --- Query keys ---

export const MEETING_TASKS_KEYS = {
  all: ["meetingTasks"] as const,
  list: ["meetingTasks"] as const,
  detail: (id: string) => ["meetingTasks", id] as const,
} as const;

// --- Raw-fetch client helper ---

async function fetchJson<T>(path: string, init: RequestInit): Promise<T> {
  const apiClient = await getAuthenticatedApiClient();
  const url = `${(apiClient as any).baseUrl}${path}`;
  const response = await (apiClient as any).http.fetch(url, init);
  if (!response.ok) {
    throw new Error(`API error: ${response.status}`);
  }
  return response.json() as Promise<T>;
}

// --- Queries ---

export function useMeetingTasksList(
  statusFilter?: string,
  page: number = 1,
  pageSize: number = 20,
) {
  return useQuery<TranscriptListResponse>({
    queryKey: [...MEETING_TASKS_KEYS.list, statusFilter ?? "", page, pageSize],
    queryFn: () => {
      const params = new URLSearchParams();
      if (statusFilter) params.append("statusFilter", statusFilter);
      params.append("pageNumber", String(page));
      params.append("pageSize", String(pageSize));
      return fetchJson<TranscriptListResponse>(
        `/api/meeting-tasks?${params.toString()}`,
        { method: "GET", headers: { Accept: "application/json" } },
      );
    },
  });
}

export function useMeetingTaskDetail(id: string) {
  return useQuery<TranscriptDetailResponse>({
    queryKey: MEETING_TASKS_KEYS.detail(id),
    enabled: !!id,
    queryFn: () =>
      fetchJson<TranscriptDetailResponse>(
        `/api/meeting-tasks/${encodeURIComponent(id)}`,
        { method: "GET", headers: { Accept: "application/json" } },
      ),
  });
}

// --- Mutations ---

export interface UpdateProposedTaskInput {
  transcriptId: string;
  taskId: string;
  data: TaskFormData;
}

export function useUpdateProposedTask() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: UpdateProposedTaskInput) =>
      fetchJson<{ success: boolean }>(
        `/api/meeting-tasks/${encodeURIComponent(input.transcriptId)}/tasks/${encodeURIComponent(input.taskId)}`,
        {
          method: "PUT",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({
            title: input.data.title,
            description: input.data.description,
            assignee: input.data.assignee,
            dueDate: input.data.dueDate || null,
          }),
        },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(vars.transcriptId) });
    },
  });
}

export interface UpdateProposedTaskStatusInput {
  transcriptId: string;
  taskId: string;
  status: ProposedTaskStatus;
}

export function useUpdateProposedTaskStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: UpdateProposedTaskStatusInput) =>
      fetchJson<{ success: boolean }>(
        `/api/meeting-tasks/${encodeURIComponent(input.transcriptId)}/tasks/${encodeURIComponent(input.taskId)}/status`,
        {
          method: "PUT",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ status: input.status }),
        },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(vars.transcriptId) });
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
}

export interface AddProposedTaskInput {
  transcriptId: string;
  data: TaskFormData;
}

export function useAddProposedTask() {
  const qc = useQueryClient();
  return useMutation<AddProposedTaskResponse, Error, AddProposedTaskInput>({
    mutationFn: async (input) =>
      fetchJson<AddProposedTaskResponse>(
        `/api/meeting-tasks/${encodeURIComponent(input.transcriptId)}/tasks`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({
            title: input.data.title,
            description: input.data.description,
            assignee: input.data.assignee,
            dueDate: input.data.dueDate || null,
          }),
        },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(vars.transcriptId) });
    },
  });
}

export function useSubmitToTodo() {
  const qc = useQueryClient();
  return useMutation<SubmitToTodoResponse, Error, string>({
    mutationFn: async (transcriptId) =>
      fetchJson<SubmitToTodoResponse>(
        `/api/meeting-tasks/${encodeURIComponent(transcriptId)}/submit`,
        { method: "POST", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, transcriptId) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(transcriptId) });
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run from `frontend/`:

```
npx jest src/api/hooks/__tests__/useMeetingTasks.test.ts
```

Expected: all tests PASS.

- [ ] **Step 5: Typecheck the new file**

Run from `frontend/`:

```
npx tsc --noEmit
```

Expected: zero TypeScript errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useMeetingTasks.ts frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts
git commit -m "feat: add meeting tasks API hooks (list/detail/mutations)"
```

---

## Task 3: Create `MeetingTasksPage.tsx` — list view

Page lives at `/automation/meeting-tasks`. Header + filter pills + table + pagination. Loading and empty states. Filter change resets page to 1.

**Files:**
- Create: `frontend/src/components/pages/automation/MeetingTasksPage.tsx`

- [ ] **Step 1: Create the page file with full content**

Create `frontend/src/components/pages/automation/MeetingTasksPage.tsx`:

```typescript
import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight } from "lucide-react";
import {
  MeetingTranscriptDto,
  TranscriptStatus,
  useMeetingTasksList,
} from "../../../api/hooks/useMeetingTasks";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";

const PAGE_SIZE = 20;

type StatusBadgeProps = { status: string };

function StatusBadge({ status }: StatusBadgeProps) {
  const colorMap: Record<string, string> = {
    PendingReview: "bg-yellow-100 text-yellow-800",
    Approved: "bg-green-100 text-green-800",
    PartiallyApproved: "bg-blue-100 text-blue-800",
  };
  const labelMap: Record<string, string> = {
    PendingReview: "Ke kontrole",
    Approved: "Schvaleno",
    PartiallyApproved: "Castecne",
  };
  const iconMap: Record<string, React.ReactNode> = {
    PendingReview: <Clock className="w-3.5 h-3.5 mr-1" />,
    Approved: <CheckCircle className="w-3.5 h-3.5 mr-1" />,
    PartiallyApproved: <CheckCircle2 className="w-3.5 h-3.5 mr-1" />,
  };
  const color = colorMap[status] ?? "bg-gray-100 text-gray-800";
  const label = labelMap[status] ?? status;
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${color}`}>
      {iconMap[status]}
      {label}
    </span>
  );
}

const MeetingTasksPage: React.FC = () => {
  const navigate = useNavigate();
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const [page, setPage] = useState(1);
  const { data, isLoading } = useMeetingTasksList(statusFilter, page, PAGE_SIZE);

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  const handleFilter = (next: string | undefined) => {
    setStatusFilter(next);
    setPage(1);
  };

  const filterButton = (label: string, value: string | undefined) => {
    const active = statusFilter === value;
    return (
      <button
        key={label}
        type="button"
        onClick={() => handleFilter(value)}
        className={`px-3 py-1.5 rounded-md text-sm font-medium border transition-colors ${
          active
            ? "bg-indigo-600 text-white border-indigo-600"
            : "bg-white text-gray-700 border-gray-300 hover:bg-gray-50"
        }`}
      >
        {label}
      </button>
    );
  };

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold text-gray-900">Meeting Tasks</h1>
        <p className="mt-2 text-gray-600">Validace AI-extrahovanych ukolu ze schuzek pred odeslanim do Microsoft TODO</p>
      </div>

      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8 flex gap-2">
        {filterButton("Vse", undefined)}
        {filterButton("Ke kontrole", "PendingReview" as TranscriptStatus)}
        {filterButton("Schvaleno", "Approved" as TranscriptStatus)}
      </div>

      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        <div className="bg-white shadow-sm rounded-lg border border-gray-200 overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Predmet</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Prijato</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Ulohy</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Stav</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {isLoading && (
                <tr>
                  <td colSpan={4} className="px-4 py-6 text-center text-sm text-gray-500">Nacitani...</td>
                </tr>
              )}
              {!isLoading && items.length === 0 && (
                <tr>
                  <td colSpan={4} className="px-4 py-6 text-center text-sm text-gray-500">Zadne zaznamy</td>
                </tr>
              )}
              {!isLoading && items.map((row: MeetingTranscriptDto) => (
                <tr
                  key={row.id}
                  onClick={() => navigate(`/automation/meeting-tasks/${row.id}`)}
                  className={`cursor-pointer hover:bg-gray-50 ${row.status === "PendingReview" ? "bg-yellow-50" : ""}`}
                >
                  <td className="px-4 py-2 text-sm text-gray-900">{row.subject}</td>
                  <td className="px-4 py-2 text-sm text-gray-700">
                    {new Date(row.receivedAt).toLocaleDateString("cs-CZ")}
                  </td>
                  <td className="px-4 py-2 text-sm text-gray-700">
                    {row.taskCount}
                    {row.approvedTaskCount > 0 && (
                      <span className="ml-1 text-xs text-gray-500">
                        ({row.approvedTaskCount} schvaleno)
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-2"><StatusBadge status={row.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="flex items-center justify-between mt-3 text-sm text-gray-700">
            <div>Strana {page} z {totalPages} ({totalCount} celkem)</div>
            <div className="flex gap-2">
              <button
                type="button"
                title="Predchozi strana"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="inline-flex items-center px-2 py-1 border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <button
                type="button"
                title="Dalsi strana"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="inline-flex items-center px-2 py-1 border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default MeetingTasksPage;
```

- [ ] **Step 2: Typecheck**

Run from `frontend/`:

```
npx tsc --noEmit
```

Expected: zero TypeScript errors. If TS complains about `PAGE_CONTAINER_HEIGHT`, confirm the import path matches existing usage in `BackgroundTasks.tsx` (`../../../constants/layout`).

- [ ] **Step 3: Lint**

Run from `frontend/`:

```
npm run lint -- --max-warnings=0 src/components/pages/automation/MeetingTasksPage.tsx
```

Expected: no errors. (If the lint script doesn't accept a path argument in this repo, just run `npm run lint` and verify the new file doesn't introduce errors.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/automation/MeetingTasksPage.tsx
git commit -m "feat: add MeetingTasksPage list view with filters and pagination"
```

---

## Task 4: Create `MeetingTaskDetailPage.tsx` — detail/validation view

Single-file page (≤ 800 lines per project budget). Contains: back link, header, summary, task cards with inline edit, approve/reject, bulk approve, add-task form, submit-to-TODO confirmation modal.

**Files:**
- Create: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

- [ ] **Step 1: Create the page file with full content**

Create `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`:

```typescript
import React, { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
} from "lucide-react";
import {
  ProposedTaskDto,
  ProposedTaskStatus,
  TaskFormData,
  TranscriptStatus,
  useAddProposedTask,
  useMeetingTaskDetail,
  useSubmitToTodo,
  useUpdateProposedTask,
  useUpdateProposedTaskStatus,
} from "../../../api/hooks/useMeetingTasks";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";

const EMPTY_FORM: TaskFormData = { title: "", description: "", assignee: "", dueDate: null };

function TranscriptStatusBadge({ status }: { status: string }) {
  const colorMap: Record<string, string> = {
    PendingReview: "bg-yellow-100 text-yellow-800",
    Approved: "bg-green-100 text-green-800",
    PartiallyApproved: "bg-blue-100 text-blue-800",
  };
  const labelMap: Record<string, string> = {
    PendingReview: "Ke kontrole",
    Approved: "Schvaleno",
    PartiallyApproved: "Castecne",
  };
  const iconMap: Record<string, React.ReactNode> = {
    PendingReview: <Clock className="w-3.5 h-3.5 mr-1" />,
    Approved: <CheckCircle className="w-3.5 h-3.5 mr-1" />,
    PartiallyApproved: <CheckCircle2 className="w-3.5 h-3.5 mr-1" />,
  };
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${colorMap[status] ?? "bg-gray-100 text-gray-800"}`}>
      {iconMap[status]}
      {labelMap[status] ?? status}
    </span>
  );
}

const MeetingTaskDetailPage: React.FC = () => {
  const { id = "" } = useParams<{ id: string }>();
  const detail = useMeetingTaskDetail(id);
  const updateTask = useUpdateProposedTask();
  const updateStatus = useUpdateProposedTaskStatus();
  const addTask = useAddProposedTask();
  const submitToTodo = useSubmitToTodo();

  const [editingTaskId, setEditingTaskId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<TaskFormData>(EMPTY_FORM);
  const [addingTask, setAddingTask] = useState(false);
  const [addForm, setAddForm] = useState<TaskFormData>(EMPTY_FORM);
  const [submitOpen, setSubmitOpen] = useState(false);

  if (detail.isLoading) {
    return <div className="p-8 text-gray-500">Nacitani...</div>;
  }
  const transcript = detail.data?.transcript;
  if (!transcript) {
    return <div className="p-8 text-gray-500">Zaznam nenalezen</div>;
  }

  const tasks: ProposedTaskDto[] = transcript.tasks;
  const pendingTasks = tasks.filter((t) => t.status === "Pending");
  const approvedCount = tasks.filter((t) => t.status === "Approved").length;

  const beginEdit = (t: ProposedTaskDto) => {
    setEditingTaskId(t.id);
    setEditForm({
      title: t.title,
      description: t.description,
      assignee: t.assignee,
      dueDate: t.dueDate,
    });
  };

  const cancelEdit = () => {
    setEditingTaskId(null);
    setEditForm(EMPTY_FORM);
  };

  const saveEdit = async (taskId: string) => {
    await updateTask.mutateAsync({
      transcriptId: id,
      taskId,
      data: { ...editForm, dueDate: editForm.dueDate || null },
    });
    cancelEdit();
  };

  const changeStatus = (taskId: string, status: ProposedTaskStatus) =>
    updateStatus.mutateAsync({ transcriptId: id, taskId, status });

  const approveAll = async () => {
    for (const t of pendingTasks) {
      await updateStatus.mutateAsync({ transcriptId: id, taskId: t.id, status: "Approved" });
    }
  };

  const handleAddTask = async () => {
    await addTask.mutateAsync({
      transcriptId: id,
      data: { ...addForm, dueDate: addForm.dueDate || null },
    });
    setAddForm(EMPTY_FORM);
    setAddingTask(false);
  };

  const confirmSubmit = async () => {
    await submitToTodo.mutateAsync(id);
    setSubmitOpen(false);
  };

  return (
    <div className="flex flex-col w-full overflow-auto" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="px-4 sm:px-6 lg:px-8 py-3">
        <Link to="/automation/meeting-tasks" className="inline-flex items-center text-sm text-indigo-700 hover:underline">
          <ArrowLeft className="w-4 h-4 mr-1" /> Zpet na seznam
        </Link>
      </div>

      <div className="px-4 sm:px-6 lg:px-8 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">{transcript.subject}</h1>
          <p className="mt-1 text-sm text-gray-600">
            {new Date(transcript.plaudCreatedAt).toLocaleString("cs-CZ")} · {transcript.plaudRecordingId}
          </p>
        </div>
        <TranscriptStatusBadge status={transcript.status} />
      </div>

      <div className="px-4 sm:px-6 lg:px-8 mt-4">
        <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900 whitespace-pre-wrap">
          {transcript.summary}
        </div>
      </div>

      <div className="px-4 sm:px-6 lg:px-8 mt-6 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-gray-900">
          Navrhovane ulohy ({tasks.length})
        </h2>
        <div className="flex gap-2">
          {pendingTasks.length > 0 && (
            <button
              type="button"
              onClick={approveAll}
              className="inline-flex items-center px-3 py-1.5 rounded-md text-sm font-medium bg-green-600 text-white hover:bg-green-700"
            >
              <CheckCheck className="w-4 h-4 mr-1" /> Schvalit vse ({pendingTasks.length})
            </button>
          )}
          <button
            type="button"
            onClick={() => setAddingTask((v) => !v)}
            className="inline-flex items-center px-3 py-1.5 rounded-md text-sm font-medium bg-white text-gray-700 border border-gray-300 hover:bg-gray-50"
          >
            <Plus className="w-4 h-4 mr-1" /> Pridat ulohu
          </button>
        </div>
      </div>

      {addingTask && (
        <div className="px-4 sm:px-6 lg:px-8 mt-3">
          <div className="bg-white border border-gray-200 rounded-md p-3 space-y-2">
            <input
              type="text"
              placeholder="Nazev ulohy"
              value={addForm.title}
              onChange={(e) => setAddForm({ ...addForm, title: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
            />
            <textarea
              placeholder="Popis"
              value={addForm.description}
              onChange={(e) => setAddForm({ ...addForm, description: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
            />
            <div className="flex gap-2">
              <input
                type="text"
                placeholder="Resitel"
                value={addForm.assignee}
                onChange={(e) => setAddForm({ ...addForm, assignee: e.target.value })}
                className="flex-1 border border-gray-300 rounded-md px-2 py-1 text-sm"
              />
              <input
                type="date"
                value={addForm.dueDate ?? ""}
                onChange={(e) => setAddForm({ ...addForm, dueDate: e.target.value || null })}
                className="border border-gray-300 rounded-md px-2 py-1 text-sm"
              />
            </div>
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => { setAddingTask(false); setAddForm(EMPTY_FORM); }}
                className="px-2 py-1 text-sm text-gray-700 hover:underline"
              >
                Zrusit
              </button>
              <button
                type="button"
                onClick={handleAddTask}
                disabled={!addForm.title || !addForm.assignee || addTask.isPending}
                className="px-3 py-1 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Pridat
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="px-4 sm:px-6 lg:px-8 mt-3 space-y-2">
        {tasks.map((t) => {
          const isEditing = editingTaskId === t.id;
          const cardClass = t.status === "Approved"
            ? "bg-green-50 border-green-200"
            : t.status === "Rejected"
              ? "bg-gray-50 border-gray-200 opacity-60"
              : "bg-white border-gray-200";
          return (
            <div key={t.id} className={`border rounded-md p-3 ${cardClass}`}>
              {!isEditing ? (
                <div
                  className="flex justify-between gap-3"
                  onClick={() => { if (t.status === "Pending") beginEdit(t); }}
                >
                  <div className={t.status === "Pending" ? "cursor-pointer flex-1" : "flex-1"}>
                    <div className={`font-medium ${t.status === "Rejected" ? "line-through" : ""}`}>
                      {t.title}
                      {t.isManuallyAdded && (
                        <span className="ml-2 text-xs text-gray-500">(rucne pridano)</span>
                      )}
                      {t.externalTaskId && (
                        <span className="ml-2 text-xs text-green-700">(odeslano do TODO)</span>
                      )}
                    </div>
                    {t.description && <div className="text-sm text-gray-700 mt-1">{t.description}</div>}
                    <div className="text-xs text-gray-500 mt-1">
                      {t.assignee}{t.dueDate ? ` · ${new Date(t.dueDate).toLocaleDateString("cs-CZ")}` : ""}
                    </div>
                  </div>
                  {t.status === "Pending" && (
                    <div className="flex gap-1 shrink-0">
                      <button
                        type="button"
                        title="Schvalit"
                        onClick={(e) => { e.stopPropagation(); changeStatus(t.id, "Approved"); }}
                        className="p-1 rounded-md text-green-700 hover:bg-green-100"
                      >
                        <Check className="w-4 h-4" />
                      </button>
                      <button
                        type="button"
                        title="Zamitnout"
                        onClick={(e) => { e.stopPropagation(); changeStatus(t.id, "Rejected"); }}
                        className="p-1 rounded-md text-red-700 hover:bg-red-100"
                      >
                        <X className="w-4 h-4" />
                      </button>
                    </div>
                  )}
                </div>
              ) : (
                <div className="space-y-2">
                  <input
                    type="text"
                    value={editForm.title}
                    onChange={(e) => setEditForm({ ...editForm, title: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
                  />
                  <textarea
                    value={editForm.description}
                    onChange={(e) => setEditForm({ ...editForm, description: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
                  />
                  <div className="flex gap-2">
                    <input
                      type="text"
                      placeholder="Resitel"
                      value={editForm.assignee}
                      onChange={(e) => setEditForm({ ...editForm, assignee: e.target.value })}
                      className="flex-1 border border-gray-300 rounded-md px-2 py-1 text-sm"
                    />
                    <input
                      type="date"
                      value={editForm.dueDate ?? ""}
                      onChange={(e) => setEditForm({ ...editForm, dueDate: e.target.value || null })}
                      className="border border-gray-300 rounded-md px-2 py-1 text-sm"
                    />
                  </div>
                  <div className="flex justify-end gap-2">
                    <button type="button" onClick={cancelEdit} className="px-2 py-1 text-sm text-gray-700 hover:underline">
                      Zrusit
                    </button>
                    <button
                      type="button"
                      onClick={() => saveEdit(t.id)}
                      disabled={updateTask.isPending}
                      className="px-3 py-1 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                      Ulozit
                    </button>
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      <div className="sticky bottom-0 mt-6 px-4 sm:px-6 lg:px-8 py-3 bg-white border-t border-gray-200 flex justify-end">
        <button
          type="button"
          disabled={approvedCount === 0}
          onClick={() => setSubmitOpen(true)}
          className="inline-flex items-center px-4 py-2 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Send className="w-4 h-4 mr-1" /> Odeslat do TODO ({approvedCount})
        </button>
      </div>

      {submitOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-lg p-5 max-w-md w-full">
            <h3 className="text-lg font-semibold text-gray-900">Odeslat schvalene ulohy do Microsoft TODO?</h3>
            <p className="text-sm text-gray-600 mt-2">
              Odeslete se schvalene ulohy ({approvedCount}). Tato akce je nevratna.
            </p>
            {submitToTodo.isError && (
              <p className="text-sm text-red-600 mt-2">
                Odeslani selhalo: {(submitToTodo.error as Error).message}
              </p>
            )}
            <div className="flex justify-end gap-2 mt-4">
              <button
                type="button"
                onClick={() => setSubmitOpen(false)}
                disabled={submitToTodo.isPending}
                className="px-3 py-1.5 rounded-md text-sm text-gray-700 hover:bg-gray-100"
              >
                Zrusit
              </button>
              <button
                type="button"
                onClick={confirmSubmit}
                disabled={submitToTodo.isPending}
                className="px-3 py-1.5 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {submitToTodo.isPending ? "Odesilam..." : "Odeslat"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

// Ensure module is treated as referencing exported types (silences ts unused-import warnings for narrow types):
export type _MeetingTaskDetailTypes = TranscriptStatus;

export default MeetingTaskDetailPage;
```

> Note on the trailing `_MeetingTaskDetailTypes` export: only needed if TS lint flags `TranscriptStatus` as unused. If lint passes without it, delete that line and re-run lint.

- [ ] **Step 2: Typecheck**

Run from `frontend/`:

```
npx tsc --noEmit
```

Expected: zero TypeScript errors.

- [ ] **Step 3: Lint**

Run from `frontend/`:

```
npm run lint
```

Expected: zero new errors/warnings on the new file. If the `TranscriptStatus` import is reported as unused, remove the unused import and the `_MeetingTaskDetailTypes` export line and re-run.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx
git commit -m "feat: add MeetingTaskDetailPage with validation and submit-to-TODO flow"
```

---

## Task 5: Wire routes into `App.tsx`

**Files:**
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Add imports next to the existing automation imports**

Edit `frontend/src/App.tsx`. Locate the existing imports for automation pages (around lines 30–31):

```typescript
import InvoiceImportStatistics from "./components/pages/automation/InvoiceImportStatistics";
import BackgroundTasks from "./components/pages/automation/BackgroundTasks";
```

Add these two lines immediately after:

```typescript
import MeetingTasksPage from "./components/pages/automation/MeetingTasksPage";
import MeetingTaskDetailPage from "./components/pages/automation/MeetingTaskDetailPage";
```

- [ ] **Step 2: Register routes next to `/automation/background-tasks`**

Locate the route block (around lines 386–387):

```tsx
<Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
<Route path="/automation/background-tasks" element={<BackgroundTasks />} />
```

Insert two new routes immediately after the `background-tasks` line:

```tsx
<Route path="/automation/meeting-tasks" element={<MeetingTasksPage />} />
<Route path="/automation/meeting-tasks/:id" element={<MeetingTaskDetailPage />} />
```

- [ ] **Step 3: Typecheck**

Run from `frontend/`:

```
npx tsc --noEmit
```

Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "feat: register /automation/meeting-tasks routes"
```

---

## Task 6: Add sidebar nav entry

Place the new item directly after `hangfire` (i.e. between `hangfire` and `data-quality`) per spec FR-4 and arch review item #6.

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Insert the new nav item**

Edit `frontend/src/components/Layout/Sidebar.tsx`. Locate the `automatizace` section's `items` array (around lines 282–308). Between the `hangfire` block (ends `onClick: openHangfireDashboard,`) and the `data-quality` block, insert:

```typescript
{
  id: "meeting-tasks",
  name: "Meeting Tasks",
  href: "/automation/meeting-tasks",
},
```

Final order of the `automatizace` items must be:
1. `background-tasks`
2. `stock-operations`
3. `recurring-jobs`
4. `hangfire`
5. **`meeting-tasks`** ← new
6. `data-quality`

- [ ] **Step 2: Typecheck + lint**

Run from `frontend/`:

```
npx tsc --noEmit && npm run lint
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: add Meeting Tasks sidebar item under Automatizace"
```

---

## Task 7: Final build + lint + test verification

This task runs the full validation gates defined in spec FR-6 and CLAUDE.md.

**Files:**
- None modified.

- [ ] **Step 1: Frontend production build**

Run from `frontend/`:

```
npm run build
```

Expected: build succeeds, no TypeScript errors, no unresolved imports.

- [ ] **Step 2: Frontend lint**

Run from `frontend/`:

```
npm run lint
```

Expected: zero new errors. If pre-existing warnings appear, they are unrelated to this PR — do not "fix" them.

- [ ] **Step 3: Frontend hook tests**

Run from `frontend/`:

```
npx jest src/api/hooks/__tests__/useMeetingTasks.test.ts
```

Expected: all tests PASS.

- [ ] **Step 4: Backend build**

Run from repo root:

```
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds (zero errors).

- [ ] **Step 5: Backend MeetingTasks tests**

Run from repo root:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTasks" --no-build
```

Expected: all MeetingTasks tests pass. (If `--no-build` complains about missing artifacts, drop the flag and let it rebuild.)

- [ ] **Step 6: Backend format check**

Run from repo root:

```
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: zero diffs. If diffs appear, run without `--verify-no-changes`, commit the formatting, and re-run the check.

- [ ] **Step 7: Manual UI smoke test (dev server)**

Run from `frontend/`:

```
npm run dev
```

Open `http://localhost:3001/automation/meeting-tasks` in a browser logged into the dev tenant. Verify:
- Sidebar shows "Meeting Tasks" under Automatizace, between Hangfire and Kvalita dat.
- List page renders header, filter pills, table, and either a loading row, an empty row, or transcript rows.
- Clicking a row navigates to `/automation/meeting-tasks/<id>`.
- Detail page renders header, blue summary block, task cards.
- For a `Pending` task: clicking the body opens inline edit; Approve / Reject buttons mutate status and refresh the view.
- "Pridat ulohu" reveals the inline form; submit disabled until title + assignee filled.
- "Schvalit vse" appears only when there is ≥ 1 pending task.
- "Odeslat do TODO ({approvedCount})" disabled until at least one approved task; opens the modal; modal disables Submit while pending and shows red error on `isError`.

If any of the above fails, fix in-place before committing.

- [ ] **Step 8: Final commit (only if Step 7 produced changes)**

If any Step 7 fix needed an edit, commit it:

```bash
git add -A
git commit -m "fix: address meeting-tasks UI smoke-test findings"
```

If no changes were needed, skip this step — the prior commits already cover the feature.

---

## Notes & Follow-ups (not required for this PR)

- The `ApiKey` field for `MeetingTasksOptions` is intentionally **not** added to `appsettings.json` in this PR (arch review amendment #1). It will land with the Plaud-ingest subtask which adds the consuming code path.
- Sub-component extraction (`StatusBadge.tsx`, `TaskCard.tsx`, etc.) is fine as a follow-up when either page exceeds the 800-line file budget. Both pages here are well under it.
- Submit-modal a11y (focus trap) is intentionally not added — matches existing modal patterns across the repo. A repo-wide modal a11y pass is a separate effort.
- Centralized error toast (from `getAuthenticatedApiClient`) and the inline red error in the submit modal will both fire on submit failure. If product owner reports the double-notification, switch to `getAuthenticatedApiClient(false)` inside the submit mutation only — keep the rest of the file on the default behavior for consistency with other hooks.

## Self-Review Summary

- **Spec coverage:** FR-1 → Task 2; FR-2 → Task 3; FR-3 → Task 4; FR-4 → Tasks 5 & 6; FR-5 → Task 1 (per amendment, `TodoListName` only); FR-6 → Task 7. NFR-1 invalidation contract honored in `useMeetingTasks.ts`. NFR-2 — auth via `getAuthenticatedApiClient` + absolute URL; no secrets committed. NFR-3 Czech strings without diacritics. NFR-4 `title` attributes on icon buttons; modal has no focus trap (documented).
- **Placeholders:** none — every step contains the exact file change or command needed.
- **Type consistency:** `TaskFormData`, `ProposedTaskStatus`, `TranscriptStatus`, `MEETING_TASKS_KEYS`, mutation input interfaces are defined once in Task 2 and reused verbatim in Task 4.
- **Routing path consistency:** `/automation/meeting-tasks` and `/automation/meeting-tasks/:id` appear identically in Tasks 4 (navigation `Link`), 5 (route definitions), and 6 (sidebar `href`).
