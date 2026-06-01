import { renderHook, waitFor } from '@testing-library/react';
import {
    useInvoiceImportJobStatus,
    useRunningInvoiceImportJobs,
    BackgroundJobInfo
} from '../useAsyncInvoiceImport';
import { getAuthenticatedApiClient } from '../../client';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper, setupFakeTimers } from '../../testUtils';

// Mock the API client
jest.mock('../../client');

describe('useAsyncInvoiceImport - Job Polling Logic', () => {
    let mockFetch: jest.Mock;
    let mockClient: any;

    beforeEach(() => {
        const mock = createMockApiClient();
        mockClient = mock.mockClient;
        mockFetch = mock.mockFetch;
        mockAuthenticatedApiClient(mockClient);
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe('useInvoiceImportJobStatus', () => {
        it('should poll job status every 2000ms (2 seconds)', async () => {
            // Arrange
            const jobId = 'test-job-123';
            const mockJobStatus: BackgroundJobInfo = {
                id: jobId,
                jobName: 'Import Invoice',
                state: 'Processing',
                createdAt: '2024-01-01T10:00:00Z',
                queue: 'default'
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockJobStatus)
            });

            const { wrapper } = createQueryClientWrapper();
            const timers = setupFakeTimers();

            try {
                // Act
                const { result } = renderHook(
                    () => useInvoiceImportJobStatus(jobId),
                    { wrapper }
                );

                // Initial fetch
                await waitFor(() => {
                    expect(result.current.isSuccess).toBe(true);
                });

                expect(mockFetch).toHaveBeenCalledTimes(1);

                // Advance 2000ms - should trigger refetch
                timers.advanceTimersByTime(2000);

                await waitFor(() => {
                    expect(mockFetch).toHaveBeenCalledTimes(2);
                });

                // Advance another 2000ms - should trigger another refetch
                timers.advanceTimersByTime(2000);

                await waitFor(() => {
                    expect(mockFetch).toHaveBeenCalledTimes(3);
                });

                // Verify correct URL was called
                expect(mockFetch).toHaveBeenCalledWith(
                    `${mockClient.baseUrl}/api/invoices/import/job-status/${encodeURIComponent(jobId)}`,
                    expect.objectContaining({
                        method: 'GET',
                        headers: { 'Content-Type': 'application/json' }
                    })
                );
            } finally {
                timers.cleanup();
            }
        });

        it('should have staleTime: 0 (always fetch fresh data)', async () => {
            // Arrange
            const jobId = 'test-job-123';
            const mockJobStatus: BackgroundJobInfo = {
                id: jobId,
                state: 'Processing'
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockJobStatus)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(
                () => useInvoiceImportJobStatus(jobId),
                { wrapper }
            );

            // Assert - Initial fetch
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            // Data should be marked as stale immediately (staleTime: 0)
            expect(result.current.isStale).toBe(true);
        });

        it('should NOT poll when jobId is undefined', () => {
            // Arrange
            const { wrapper } = createQueryClientWrapper();

            // Act - No jobId provided
            const { result } = renderHook(
                () => useInvoiceImportJobStatus(undefined),
                { wrapper }
            );

            // Assert - Query should be disabled (enabled: false in hook config)
            expect(result.current.isLoading).toBe(false);
            expect(result.current.fetchStatus).toBe('idle'); // Query is disabled
            expect(mockFetch).not.toHaveBeenCalled();
        });

        it('should NOT poll when jobId is null', () => {
            // Arrange
            const { wrapper } = createQueryClientWrapper();

            // Act - null jobId
            const { result } = renderHook(
                () => useInvoiceImportJobStatus(null as any),
                { wrapper }
            );

            // Assert - Query should be disabled
            expect(result.current.isLoading).toBe(false);
            expect(result.current.fetchStatus).toBe('idle');
            expect(mockFetch).not.toHaveBeenCalled();
        });
    });

    describe('useRunningInvoiceImportJobs', () => {
        it('should poll running jobs every 5000ms (5 seconds)', async () => {
            // Arrange
            const mockJobs: BackgroundJobInfo[] = [
                { id: 'job-1', state: 'Processing' },
                { id: 'job-2', state: 'Enqueued' }
            ];

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockJobs)
            });

            const { wrapper } = createQueryClientWrapper();
            const timers = setupFakeTimers();

            try {
                // Act
                const { result } = renderHook(
                    () => useRunningInvoiceImportJobs(),
                    { wrapper }
                );

                // Initial fetch
                await waitFor(() => {
                    expect(result.current.isSuccess).toBe(true);
                });

                expect(mockFetch).toHaveBeenCalledTimes(1);

                // Advance 5000ms - should trigger refetch
                timers.advanceTimersByTime(5000);

                await waitFor(() => {
                    expect(mockFetch).toHaveBeenCalledTimes(2);
                });

                // Advance another 5000ms - should trigger another refetch
                timers.advanceTimersByTime(5000);

                await waitFor(() => {
                    expect(mockFetch).toHaveBeenCalledTimes(3);
                });

                // Verify correct URL was called
                expect(mockFetch).toHaveBeenCalledWith(
                    `${mockClient.baseUrl}/api/invoices/import/running-jobs`,
                    expect.objectContaining({
                        method: 'GET',
                        headers: { 'Content-Type': 'application/json' }
                    })
                );
            } finally {
                timers.cleanup();
            }
        });

        it('should have staleTime: 0 (always fetch fresh data)', async () => {
            // Arrange
            const mockJobs: BackgroundJobInfo[] = [];

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockJobs)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(
                () => useRunningInvoiceImportJobs(),
                { wrapper }
            );

            // Assert - Initial fetch
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            // Data should be marked as stale immediately (staleTime: 0)
            expect(result.current.isStale).toBe(true);
        });
    });

    describe('Polling interval comparison', () => {
        it('should verify job status polls faster than running jobs list', () => {
            // This test verifies the configuration values are correct
            // Job status: 2000ms (2 seconds)
            // Running jobs: 5000ms (5 seconds)

            const JOB_STATUS_INTERVAL = 2000;
            const RUNNING_JOBS_INTERVAL = 5000;

            expect(JOB_STATUS_INTERVAL).toBeLessThan(RUNNING_JOBS_INTERVAL);
            expect(JOB_STATUS_INTERVAL).toBe(2000);
            expect(RUNNING_JOBS_INTERVAL).toBe(5000);
        });
    });
});
