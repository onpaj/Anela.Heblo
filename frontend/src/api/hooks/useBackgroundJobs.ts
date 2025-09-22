import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface BackgroundJobInfo {
  id: string;
  method: string;
  state: string;
  enqueuedAt?: string;
  scheduledAt?: string;
  arguments?: string;
  exception?: string;
}

export interface QueuedJobsResult {
  jobs: BackgroundJobInfo[];
  totalCount: number;
}

export interface GetQueuedJobsRequest {
  offset?: number;
  count?: number;
  queue?: string;
  state?: string;
}

export interface GetScheduledJobsRequest {
  offset?: number;
  count?: number;
  fromDate?: string;
  toDate?: string;
}

export interface GetJobRequest {
  jobId: string;
  includeHistory?: boolean;
}

export interface GetFailedJobsRequest {
  offset?: number;
  count?: number;
  fromDate?: string;
  toDate?: string;
}

const backgroundJobsApi = {
  async getQueuedJobs(request: GetQueuedJobsRequest): Promise<QueuedJobsResult> {
    const apiClient = await getAuthenticatedApiClient();
    const params = new URLSearchParams();
    
    if (request.offset !== undefined) params.append('offset', request.offset.toString());
    if (request.count !== undefined) params.append('count', request.count.toString());
    if (request.queue) params.append('queue', request.queue);
    if (request.state) params.append('state', request.state);

    const url = `${(apiClient as any).baseUrl}/api/backgroundjobs/queued?${params.toString()}`;
    const response = await (apiClient as any).http.fetch(url, {
      method: 'GET'
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch queued jobs: ${response.statusText}`);
    }

    return response.json();
  },

  async getScheduledJobs(request: GetScheduledJobsRequest): Promise<QueuedJobsResult> {
    const apiClient = await getAuthenticatedApiClient();
    const params = new URLSearchParams();
    
    if (request.offset !== undefined) params.append('offset', request.offset.toString());
    if (request.count !== undefined) params.append('count', request.count.toString());
    if (request.fromDate) params.append('fromDate', request.fromDate);
    if (request.toDate) params.append('toDate', request.toDate);

    const url = `${(apiClient as any).baseUrl}/api/backgroundjobs/scheduled?${params.toString()}`;
    const response = await (apiClient as any).http.fetch(url, {
      method: 'GET'
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch scheduled jobs: ${response.statusText}`);
    }

    return response.json();
  },

  async getJob(request: GetJobRequest): Promise<BackgroundJobInfo> {
    const apiClient = await getAuthenticatedApiClient();
    const params = new URLSearchParams();
    
    if (request.includeHistory !== undefined) {
      params.append('includeHistory', request.includeHistory.toString());
    }

    const url = `${(apiClient as any).baseUrl}/api/backgroundjobs/${request.jobId}?${params.toString()}`;
    const response = await (apiClient as any).http.fetch(url, {
      method: 'GET'
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch job: ${response.statusText}`);
    }

    return response.json();
  },

  async getFailedJobs(request: GetFailedJobsRequest): Promise<QueuedJobsResult> {
    const apiClient = await getAuthenticatedApiClient();
    const params = new URLSearchParams();
    
    if (request.offset !== undefined) params.append('offset', request.offset.toString());
    if (request.count !== undefined) params.append('count', request.count.toString());
    if (request.fromDate) params.append('fromDate', request.fromDate);
    if (request.toDate) params.append('toDate', request.toDate);

    const url = `${(apiClient as any).baseUrl}/api/backgroundjobs/failed?${params.toString()}`;
    const response = await (apiClient as any).http.fetch(url, {
      method: 'GET'
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch failed jobs: ${response.statusText}`);
    }

    return response.json();
  }
};

export const useQueuedJobs = (request: GetQueuedJobsRequest) => {
  return useQuery({
    queryKey: ['background-jobs', 'queued', request],
    queryFn: () => backgroundJobsApi.getQueuedJobs(request),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Auto-refresh every minute
  });
};

export const useScheduledJobs = (request: GetScheduledJobsRequest) => {
  return useQuery({
    queryKey: ['background-jobs', 'scheduled', request],
    queryFn: () => backgroundJobsApi.getScheduledJobs(request),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Auto-refresh every minute
  });
};

export const useFailedJobs = (request: GetFailedJobsRequest) => {
  return useQuery({
    queryKey: ['background-jobs', 'failed', request],
    queryFn: () => backgroundJobsApi.getFailedJobs(request),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Auto-refresh every minute
  });
};

export const useJob = (request: GetJobRequest) => {
  return useQuery({
    queryKey: ['background-jobs', 'job', request.jobId, request.includeHistory],
    queryFn: () => backgroundJobsApi.getJob(request),
    enabled: !!request.jobId,
    staleTime: 30 * 1000, // 30 seconds
  });
};