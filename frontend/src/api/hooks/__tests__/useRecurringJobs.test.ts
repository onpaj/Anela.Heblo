import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useUpdateRecurringJobCronMutation } from '../useRecurringJobs';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    recurringJobs: ['recurring-jobs'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useUpdateRecurringJobCronMutation', () => {
  const mockApiClient = {
    recurringJobs_UpdateJobCron: jest.fn(),
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  it('calls recurringJobs_UpdateJobCron with correct arguments', async () => {
    mockApiClient.recurringJobs_UpdateJobCron.mockResolvedValue({
      success: true,
      jobName: 'test-job',
      cronExpression: '0 3 * * *',
      lastModifiedAt: new Date().toISOString(),
      lastModifiedBy: 'test-user',
    });

    const { result } = renderHook(() => useUpdateRecurringJobCronMutation(), {
      wrapper: createWrapper,
    });

    await act(async () => {
      await result.current.mutateAsync({
        jobName: 'test-job',
        cronExpression: '0 3 * * *',
      });
    });

    expect(mockApiClient.recurringJobs_UpdateJobCron).toHaveBeenCalledTimes(1);
    expect(mockApiClient.recurringJobs_UpdateJobCron).toHaveBeenCalledWith(
      'test-job',
      expect.objectContaining({ cronExpression: '0 3 * * *' })
    );
  });

  it('throws on API error', async () => {
    mockApiClient.recurringJobs_UpdateJobCron.mockRejectedValue(
      new Error('Invalid CRON')
    );

    const { result } = renderHook(() => useUpdateRecurringJobCronMutation(), {
      wrapper: createWrapper,
    });

    await expect(
      act(async () => {
        await result.current.mutateAsync({
          jobName: 'test-job',
          cronExpression: 'bad-cron',
        });
      })
    ).rejects.toThrow('Invalid CRON');
  });
});
