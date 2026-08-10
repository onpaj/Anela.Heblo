// TODO: migrate to generated client when /api/mind-maps is added to NSwag.
// Pattern matches useMeetingTasks.ts.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// --- Types (raw JSON: dates are ISO strings) ---

export type MindMapStatusValue = "Idle" | "Updating" | "Failed";

export interface MindMapListItem {
  id: string;
  name: string;
  description: string | null;
  status: MindMapStatusValue;
  meetingCount: number;
  updatedAt: string;
}

export interface MindMapListResponse {
  items: MindMapListItem[];
}

export interface AttachedMeeting {
  meetingTranscriptId: string;
  subject: string;
  plaudCreatedAt: string;
  attachedAt: string;
  processedAt: string | null;
}

export interface MindMapVersionInfo {
  versionNumber: number;
  createdAt: string;
  triggerMeetingId: string | null;
  triggerMeetingSubject: string | null;
}

export interface MindMapDetail {
  id: string;
  name: string;
  description: string | null;
  status: MindMapStatusValue;
  lastError: string | null;
  documentJson: string;
  meetings: AttachedMeeting[];
  versions: MindMapVersionInfo[];
}

// --- Query keys ---

export const MIND_MAPS_KEYS = {
  all: ["mindMaps"] as const,
  list: ["mindMaps"] as const,
  detail: (id: string) => ["mindMaps", id] as const,
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

const JSON_HEADERS = { "Content-Type": "application/json", Accept: "application/json" };

// --- Queries ---

export function useMindMapsList() {
  return useQuery<MindMapListResponse>({
    queryKey: MIND_MAPS_KEYS.list,
    queryFn: () =>
      fetchJson<MindMapListResponse>("/api/mind-maps", {
        method: "GET",
        headers: { Accept: "application/json" },
      }),
  });
}

const UPDATING_POLL_INTERVAL_MS = 3000;

export function useMindMapDetail(id: string) {
  return useQuery<MindMapDetail>({
    queryKey: MIND_MAPS_KEYS.detail(id),
    enabled: !!id,
    refetchInterval: (query) =>
      query.state.data?.status === "Updating" ? UPDATING_POLL_INTERVAL_MS : false,
    queryFn: () =>
      fetchJson<MindMapDetail>(`/api/mind-maps/${encodeURIComponent(id)}`, {
        method: "GET",
        headers: { Accept: "application/json" },
      }),
  });
}

// --- Mutations ---

export function useCreateMindMap() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; description: string | null }) =>
      fetchJson<{ id: string }>("/api/mind-maps", {
        method: "POST",
        headers: JSON_HEADERS,
        body: JSON.stringify(input),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.all }),
  });
}

export function useDeleteMindMap() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      fetchJson<{ success: boolean }>(`/api/mind-maps/${encodeURIComponent(id)}`, {
        method: "DELETE",
        headers: { Accept: "application/json" },
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.all }),
  });
}

export function useAttachMeeting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; meetingTranscriptId: string }) =>
      fetchJson<{ success: boolean }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/meetings`,
        {
          method: "POST",
          headers: JSON_HEADERS,
          body: JSON.stringify({ meetingTranscriptId: input.meetingTranscriptId }),
        },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) });
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.list });
    },
  });
}

export function useDetachMeeting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; meetingTranscriptId: string }) =>
      fetchJson<{ success: boolean }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/meetings/${encodeURIComponent(input.meetingTranscriptId)}`,
        { method: "DELETE", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) });
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.list });
    },
  });
}

export function useRegenerateMindMap() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      fetchJson<{ success: boolean }>(`/api/mind-maps/${encodeURIComponent(id)}/regenerate`, {
        method: "POST",
        headers: { Accept: "application/json" },
      }),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(id) });
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.list });
    },
  });
}

export function useSaveMindMapDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; documentJson: string }) =>
      fetchJson<{ documentJson: string }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/document`,
        {
          method: "PUT",
          headers: JSON_HEADERS,
          body: JSON.stringify({ documentJson: input.documentJson }),
        },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) });
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.list });
    },
  });
}

export function useRestoreMindMapVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; versionNumber: number }) =>
      fetchJson<{ documentJson: string }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/versions/${input.versionNumber}/restore`,
        { method: "POST", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) });
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.list });
    },
  });
}
