import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { 
  useGiftPackageManufactureJobStatus, 
  useRunningJobsForGiftPackage,
  useEnqueueGiftPackageManufacture 
} from '../useGiftPackageManufacturing';

// Mock the API client
const mockApiClient = {
  logistics_GetGiftPackageManufactureJobStatus: jest.fn(),
  logistics_GetRunningJobsForGiftPackage: jest.fn(),
  logistics_EnqueueGiftPackageManufacture: jest.fn(),
};

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => mockApiClient,
  QUERY_KEYS: {
    giftPackages: ['giftPackages']
  }
}));

const createTestQueryClient = () => new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
    },
  },
});

const createWrapper = () => {
  const queryClient = createTestQueryClient();
  // eslint-disable-next-line react/display-name
  return ({ children }: { children: React.ReactNode }) => (
    React.createElement(QueryClientProvider, { client: queryClient }, children)
  );
};

describe('useGiftPackageManufacturingJobStatus', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should fetch job status successfully', async () => {
    const mockResponse = {
      isSuccess: true,
      jobStatus: {
        jobId: 'test-job-123',
        status: 'Processing',
        isRunning: true,
        createdAt: new Date(),
        startedAt: new Date(),
        completedAt: null,
        errorMessage: null
      }
    };

    mockApiClient.logistics_GetGiftPackageManufactureJobStatus.mockResolvedValue(mockResponse);

    const { result } = renderHook(
      () => useGiftPackageManufactureJobStatus('test-job-123'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    expect(result.current.data).toEqual(mockResponse);
    expect(mockApiClient.logistics_GetGiftPackageManufactureJobStatus).toHaveBeenCalledWith('test-job-123');
  });

  it('should not fetch when jobId is not provided', () => {
    const { result } = renderHook(
      () => useGiftPackageManufactureJobStatus(undefined),
      { wrapper: createWrapper() }
    );

    expect(result.current.isFetching).toBe(false);
    expect(mockApiClient.logistics_GetGiftPackageManufactureJobStatus).not.toHaveBeenCalled();
  });
});

describe('useRunningJobsForGiftPackage', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should fetch running jobs successfully', async () => {
    const mockResponse = {
      isSuccess: true,
      hasRunningJobs: true,
      runningJobs: [
        {
          jobId: 'job-1',
          status: 'Processing',
          isRunning: true,
          createdAt: new Date()
        }
      ]
    };

    mockApiClient.logistics_GetRunningJobsForGiftPackage.mockResolvedValue(mockResponse);

    const { result } = renderHook(
      () => useRunningJobsForGiftPackage('TEST001'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    expect(result.current.data).toEqual(mockResponse);
    expect(mockApiClient.logistics_GetRunningJobsForGiftPackage).toHaveBeenCalledWith('TEST001');
  });

  it('should not fetch when giftPackageCode is not provided', () => {
    const { result } = renderHook(
      () => useRunningJobsForGiftPackage(undefined),
      { wrapper: createWrapper() }
    );

    expect(result.current.isFetching).toBe(false);
    expect(mockApiClient.logistics_GetRunningJobsForGiftPackage).not.toHaveBeenCalled();
  });
});

describe('useEnqueueGiftPackageManufacture', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should enqueue manufacture successfully', async () => {
    const mockResponse = {
      jobId: 'new-job-456',
      message: 'Manufacturing queued successfully'
    };

    mockApiClient.logistics_EnqueueGiftPackageManufacture.mockResolvedValue(mockResponse);

    const { result } = renderHook(
      () => useEnqueueGiftPackageManufacture(),
      { wrapper: createWrapper() }
    );

    const request = {
      giftPackageCode: 'TEST001',
      quantity: 5,
      allowStockOverride: false
    };

    await result.current.mutateAsync(request);

    expect(mockApiClient.logistics_EnqueueGiftPackageManufacture).toHaveBeenCalledWith(request);
  });
});