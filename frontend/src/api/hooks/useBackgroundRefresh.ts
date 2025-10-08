import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  RefreshTaskDto,
  RefreshTaskExecutionLogDto,
  RefreshTaskStatusDto,
} from "../generated/api-client";

/**
 * Fetch all registered background refresh tasks
 */
const fetchBackgroundTasks = async (): Promise<RefreshTaskDto[]> => {
  const apiClient = await getAuthenticatedApiClient();
  const relativeUrl = `/api/backgroundrefresh/tasks`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: "GET",
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch background tasks: ${response.status}`);
  }

  return response.json();
};

/**
 * Fetch task execution history
 */
const fetchTaskHistory = async (
  taskId: string,
  maxRecords: number = 50,
): Promise<RefreshTaskExecutionLogDto[]> => {
  const apiClient = await getAuthenticatedApiClient();
  const relativeUrl = `/api/backgroundrefresh/tasks/${encodeURIComponent(taskId)}/history?maxRecords=${maxRecords}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: "GET",
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch task history: ${response.status}`);
  }

  return response.json();
};

/**
 * Fetch all execution history (all tasks)
 */
const fetchAllHistory = async (
  maxRecords: number = 100,
): Promise<RefreshTaskExecutionLogDto[]> => {
  const apiClient = await getAuthenticatedApiClient();
  const relativeUrl = `/api/backgroundrefresh/history?maxRecords=${maxRecords}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: "GET",
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch all history: ${response.status}`);
  }

  return response.json();
};

/**
 * Fetch task status
 */
const fetchTaskStatus = async (
  taskId: string,
): Promise<RefreshTaskStatusDto> => {
  const apiClient = await getAuthenticatedApiClient();
  const relativeUrl = `/api/backgroundrefresh/tasks/${encodeURIComponent(taskId)}/status`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: "GET",
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch task status: ${response.status}`);
  }

  return response.json();
};

/**
 * Force refresh a task
 */
const forceRefreshTask = async (taskId: string): Promise<void> => {
  const apiClient = await getAuthenticatedApiClient();
  const relativeUrl = `/api/backgroundrefresh/tasks/${encodeURIComponent(taskId)}/force-refresh`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: "POST",
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || `Failed to force refresh task: ${response.status}`);
  }
};

/**
 * Hook to fetch all background refresh tasks
 */
export const useBackgroundTasks = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.backgroundRefresh, "tasks"],
    queryFn: fetchBackgroundTasks,
    refetchInterval: 30000, // Refresh every 30 seconds
    refetchOnWindowFocus: true,
    staleTime: 15000, // Consider data stale after 15 seconds
  });
};

/**
 * Hook to fetch task execution history
 */
export const useTaskHistory = (taskId: string | null, maxRecords: number = 50) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.backgroundRefresh, "history", taskId, maxRecords],
    queryFn: () => fetchTaskHistory(taskId!, maxRecords),
    enabled: !!taskId, // Only fetch when taskId is provided
    refetchInterval: 10000, // Refresh every 10 seconds when modal is open
    staleTime: 5000,
  });
};

/**
 * Hook to fetch all execution history
 */
export const useAllHistory = (maxRecords: number = 100) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.backgroundRefresh, "history", "all", maxRecords],
    queryFn: () => fetchAllHistory(maxRecords),
    refetchInterval: 30000,
    staleTime: 15000,
  });
};

/**
 * Hook to fetch task status
 */
export const useTaskStatus = (taskId: string | null) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.backgroundRefresh, "status", taskId],
    queryFn: () => fetchTaskStatus(taskId!),
    enabled: !!taskId,
    refetchInterval: 5000, // Refresh every 5 seconds when monitoring a task
    staleTime: 2000,
  });
};

/**
 * Hook to force refresh a task
 */
export const useForceRefreshTask = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: forceRefreshTask,
    onSuccess: () => {
      // Invalidate all background refresh queries to fetch fresh data
      queryClient.invalidateQueries({
        queryKey: QUERY_KEYS.backgroundRefresh,
      });
    },
  });
};
