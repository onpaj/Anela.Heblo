import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// Types matching backend DTOs
export interface AuditLogDto {
  id: string;
  timestamp: string;
  dataType: string;
  source: string;
  success: boolean;
  recordCount: number;
  duration: string;
  errorMessage?: string;
  metadata?: { [key: string]: any };
}

export interface GetAuditLogsRequest {
  limit?: number;
  fromDate?: string;
  toDate?: string;
}

export interface GetAuditLogsResponse {
  count: number;
  logs: AuditLogDto[];
}

export interface AuditSummaryItemDto {
  dataType: string;
  source: string;
  totalRequests: number;
  successfulRequests: number;
  failedRequests: number;
  totalRecords: number;
  averageDuration: number;
  lastSuccessfulLoad?: string;
  lastFailedLoad?: string;
}

export interface GetAuditSummaryRequest {
  fromDate?: string;
  toDate?: string;
}

export interface GetAuditSummaryResponse {
  periodFrom?: string;
  periodTo?: string;
  summary: AuditSummaryItemDto[];
}

// API function to fetch audit logs
const fetchAuditLogs = async (params: GetAuditLogsRequest = {}): Promise<GetAuditLogsResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const searchParams = new URLSearchParams();
  
  if (params.limit !== undefined) {
    searchParams.append('limit', params.limit.toString());
  }
  if (params.fromDate) {
    searchParams.append('fromDate', params.fromDate);
  }
  if (params.toDate) {
    searchParams.append('toDate', params.toDate);
  }

  const url = `/api/audit/data-loads${searchParams.toString() ? `?${searchParams.toString()}` : ''}`;
  
  const response = await (apiClient as any).http.fetch(url, {
    method: 'GET',
  });
  
  if (!response.ok) {
    throw new Error(`Failed to fetch audit logs: ${response.status} ${response.statusText}`);
  }
  
  return response.json();
};

// API function to fetch audit summary
const fetchAuditSummary = async (params: GetAuditSummaryRequest = {}): Promise<GetAuditSummaryResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const searchParams = new URLSearchParams();
  
  if (params.fromDate) {
    searchParams.append('fromDate', params.fromDate);
  }
  if (params.toDate) {
    searchParams.append('toDate', params.toDate);
  }

  const url = `/api/audit/summary${searchParams.toString() ? `?${searchParams.toString()}` : ''}`;
  
  const response = await (apiClient as any).http.fetch(url, {
    method: 'GET',
  });
  
  if (!response.ok) {
    throw new Error(`Failed to fetch audit summary: ${response.status} ${response.statusText}`);
  }
  
  return response.json();
};

// React Query hook for audit logs
export const useAuditLogs = (params: GetAuditLogsRequest = {}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.audit, 'logs', params],
    queryFn: () => fetchAuditLogs(params),
    staleTime: 2 * 60 * 1000, // 2 minutes
    gcTime: 5 * 60 * 1000, // 5 minutes
  });
};

// React Query hook for audit summary
export const useAuditSummary = (params: GetAuditSummaryRequest = {}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.audit, 'summary', params],
    queryFn: () => fetchAuditSummary(params),
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
  });
};

// Hook for recent audit logs (last 24 hours)
export const useRecentAuditLogs = (limit: number = 50) => {
  const yesterday = new Date();
  yesterday.setDate(yesterday.getDate() - 1);
  
  const params: GetAuditLogsRequest = {
    limit,
    fromDate: yesterday.toISOString(),
  };

  return useQuery({
    queryKey: [...QUERY_KEYS.audit, 'recent', limit],
    queryFn: () => fetchAuditLogs(params),
    staleTime: 1 * 60 * 1000, // 1 minute for recent data
    gcTime: 5 * 60 * 1000, // 5 minutes
    refetchInterval: 30 * 1000, // Auto-refresh every 30 seconds
  });
};

// Hook for audit summary with default period (last 7 days)
export const useRecentAuditSummary = () => {
  const weekAgo = new Date();
  weekAgo.setDate(weekAgo.getDate() - 7);
  
  const params: GetAuditSummaryRequest = {
    fromDate: weekAgo.toISOString(),
  };

  return useQuery({
    queryKey: [...QUERY_KEYS.audit, 'recent-summary'],
    queryFn: () => fetchAuditSummary(params),
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
    refetchInterval: 60 * 1000, // Auto-refresh every minute
  });
};