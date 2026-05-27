// TODO: migrate to generated client when /api/meeting-tasks is added to NSwag.
// Pattern matches useBackgroundRefresh.ts / useAsyncInvoiceImport.ts.
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// --- DTOs ---

export type ProposedTaskStatus = "Pending" | "Approved" | "Rejected";
export type TranscriptStatus = "PendingReview" | "Approved" | "PartiallyApproved";

export interface ProposedTaskDto {
  id: string;
  title: string;
  description: string;
  assignee: string;
  assigneeEmail: string | null;
  dueDate: string | null;
  status: ProposedTaskStatus;
  externalTaskId: string | null;
  isManuallyAdded: boolean;
}

export interface MeetingAccessGrantDto {
  userEmail: string;
  userDisplayName: string | null;
}

export interface MeetingTranscriptDto {
  id: string;
  subject: string;
  summary: string;
  rawTranscript: string;
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
  accessLevel: 'Private' | 'Public' | 'Restricted';
  accessGrants: MeetingAccessGrantDto[];
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
  assigneeEmail: string | null;
  dueDate: string | null;
}

export interface MeetingUserDto {
  email: string;
  displayName: string;
  aliases: string[];
}

interface MeetingUsersResponse {
  success: boolean;
  users: MeetingUserDto[];
}

export interface UpdateMeetingAccessPayload {
  accessLevel: 'Private' | 'Public' | 'Restricted';
  restrictedUserEmails: string[];
}

export interface UpdateMeetingAccessResponse {
  success: boolean;
  accessLevel: string;
  grants: MeetingAccessGrantDto[];
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
    queryKey: [...QUERY_KEYS.meetingTasks, statusFilter ?? "", page, pageSize],
    refetchOnMount: "always",
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

export function useMeetingUsers() {
  return useQuery<MeetingUserDto[]>({
    queryKey: [...QUERY_KEYS.meetingTasks, "users"],
    staleTime: 10 * 60 * 1000,
    queryFn: async () => {
      const response = await fetchJson<MeetingUsersResponse>(
        `/api/meeting-tasks/users`,
        { method: "GET", headers: { Accept: "application/json" } },
      );
      return response.users;
    },
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
            assigneeEmail: input.data.assigneeEmail || null,
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
            assigneeEmail: input.data.assigneeEmail || null,
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

export interface UpdateMeetingAccessInput extends UpdateMeetingAccessPayload {
  transcriptId: string;
}

export function useUpdateMeetingAccess() {
  const qc = useQueryClient();
  return useMutation<UpdateMeetingAccessResponse, Error, UpdateMeetingAccessInput>({
    mutationFn: async (input) =>
      fetchJson<UpdateMeetingAccessResponse>(
        `/api/meeting-tasks/${encodeURIComponent(input.transcriptId)}/access`,
        {
          method: "PUT",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({
            accessLevel: input.accessLevel,
            restrictedUserEmails: input.restrictedUserEmails,
          }),
        },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(vars.transcriptId) });
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
}

// --- Explain Summary ---

export interface ExplainSummaryResponse {
  success: boolean;
  relevantTranscript: string;
  explanation: string;
  errorCode?: string;
}

export interface ExplainSummaryInput {
  transcriptId: string;
  selectedText: string;
}

export function useExplainMeetingSummary() {
  return useMutation<ExplainSummaryResponse, Error, ExplainSummaryInput>({
    mutationFn: async (input) =>
      fetchJson<ExplainSummaryResponse>(
        `/api/meeting-tasks/${encodeURIComponent(input.transcriptId)}/explain`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ selectedText: input.selectedText }),
        },
      ),
  });
}

// --- Reimport ---

export interface ReimportMeetingResponse {
  success: boolean;
  errorCode?: string;
}

export function useReimportMeeting() {
  const qc = useQueryClient();
  return useMutation<ReimportMeetingResponse, Error, string>({
    mutationFn: async (transcriptId) =>
      fetchJson<ReimportMeetingResponse>(
        `/api/meeting-tasks/${encodeURIComponent(transcriptId)}/reimport`,
        { method: "POST", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, transcriptId) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(transcriptId) });
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
}
