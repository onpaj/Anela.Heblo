import React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import InvoiceImportJobTracker from '../InvoiceImportJobTracker';
import * as useAsyncInvoiceImportHooks from '../../../api/hooks/useAsyncInvoiceImport';
import { setupFakeTimers } from '../../../api/testUtils';

// Mock the hooks
jest.mock('../../../api/hooks/useAsyncInvoiceImport');

describe('InvoiceImportJobTracker - Job Completion Auto-Dismiss', () => {
    let queryClient: QueryClient;
    const mockUseInvoiceImportJobStatus = jest.fn();
    const mockOnJobCompleted = jest.fn();

    beforeEach(() => {
        queryClient = new QueryClient({
            defaultOptions: {
                queries: { retry: false },
                mutations: { retry: false }
            }
        });

        (useAsyncInvoiceImportHooks as any).useInvoiceImportJobStatus = mockUseInvoiceImportJobStatus;

        jest.clearAllMocks();
        jest.spyOn(console, 'log').mockImplementation(() => {});
    });

    afterEach(() => {
        (console.log as jest.Mock).mockRestore();
    });

    const renderComponent = (jobId: string) => {
        return render(
            <QueryClientProvider client={queryClient}>
                <InvoiceImportJobTracker
                    jobId={jobId}
                    onJobCompleted={mockOnJobCompleted}
                />
            </QueryClientProvider>
        );
    };

    it('should start 5-second timer when job status changes to "Succeeded"', async () => {
        // Arrange
        const jobId = 'test-job-success';
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Succeeded', jobName: 'Import Invoice' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act
            renderComponent(jobId);

            // Wait for component to render with Succeeded status
            await waitFor(() => {
                expect(screen.getByText('Import dokončen')).toBeInTheDocument();
            });

            // Assert - Callback should NOT be called immediately
            expect(mockOnJobCompleted).not.toHaveBeenCalled();

            // Advance time by 4999ms - still should not fire
            act(() => {
                timers.advanceTimersByTime(4999);
            });

            expect(mockOnJobCompleted).not.toHaveBeenCalled();

            // Advance time by 1ms more (total 5000ms) - should fire now
            act(() => {
                timers.advanceTimersByTime(1);
            });

            // Assert - Callback should be called after 5 seconds
            await waitFor(() => {
                expect(mockOnJobCompleted).toHaveBeenCalledTimes(1);
            });
        } finally {
            timers.cleanup();
        }
    });

    it('should start 5-second timer when job status changes to "Failed"', async () => {
        // Arrange
        const jobId = 'test-job-failed';
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Failed', jobName: 'Import Invoice' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act
            renderComponent(jobId);

            // Wait for component to render with Failed status
            await waitFor(() => {
                expect(screen.getByText('Import selhal')).toBeInTheDocument();
            });

            // Assert - Callback should NOT be called immediately
            expect(mockOnJobCompleted).not.toHaveBeenCalled();

            // Advance time by 5 seconds
            act(() => {
                timers.advanceTimersByTime(5000);
            });

            // Assert - Callback should be called after 5 seconds
            await waitFor(() => {
                expect(mockOnJobCompleted).toHaveBeenCalledTimes(1);
            });
        } finally {
            timers.cleanup();
        }
    });

    it('should call onJobCompleted callback when timer fires', async () => {
        // Arrange
        const jobId = 'test-job-callback';
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Succeeded' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act
            renderComponent(jobId);

            await waitFor(() => {
                expect(screen.getByText('Import dokončen')).toBeInTheDocument();
            });

            // Advance timer
            act(() => {
                timers.advanceTimersByTime(5000);
            });

            // Assert - Verify callback was called
            await waitFor(() => {
                expect(mockOnJobCompleted).toHaveBeenCalledTimes(1);
            });
        } finally {
            timers.cleanup();
        }
    });

    it('should start timer only once (hasStartedCompletionTimer flag prevents duplicates)', async () => {
        // Arrange
        const jobId = 'test-job-once';
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Succeeded' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act
            const { rerender } = renderComponent(jobId);

            await waitFor(() => {
                expect(screen.getByText('Import dokončen')).toBeInTheDocument();
            });

            // Force re-render multiple times (simulating React updates)
            rerender(
                <QueryClientProvider client={queryClient}>
                    <InvoiceImportJobTracker
                        jobId={jobId}
                        onJobCompleted={mockOnJobCompleted}
                    />
                </QueryClientProvider>
            );

            rerender(
                <QueryClientProvider client={queryClient}>
                    <InvoiceImportJobTracker
                        jobId={jobId}
                        onJobCompleted={mockOnJobCompleted}
                    />
                </QueryClientProvider>
            );

            // Advance timer
            act(() => {
                timers.advanceTimersByTime(5000);
            });

            // Assert - Callback should be called EXACTLY once, not multiple times
            await waitFor(() => {
                expect(mockOnJobCompleted).toHaveBeenCalledTimes(1);
            });

            // Advance more time to verify no additional timers fire
            act(() => {
                timers.advanceTimersByTime(10000);
            });

            // Still only called once
            expect(mockOnJobCompleted).toHaveBeenCalledTimes(1);
        } finally {
            timers.cleanup();
        }
    });

    it('should NOT start timer for "Processing" status', async () => {
        // Arrange
        const jobId = 'test-job-processing';
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Processing' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act
            renderComponent(jobId);

            await waitFor(() => {
                expect(screen.getByText('Importuje se')).toBeInTheDocument();
            });

            // Advance time by 10 seconds
            act(() => {
                timers.advanceTimersByTime(10000);
            });

            // Assert - Callback should NOT be called (job still running)
            expect(mockOnJobCompleted).not.toHaveBeenCalled();
        } finally {
            timers.cleanup();
        }
    });

    it('should NOT start timer for "Enqueued" status', async () => {
        // Arrange
        const jobId = 'test-job-enqueued';
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Enqueued' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act
            renderComponent(jobId);

            await waitFor(() => {
                expect(screen.getByText('Ve frontě')).toBeInTheDocument();
            });

            // Advance time by 10 seconds
            act(() => {
                timers.advanceTimersByTime(10000);
            });

            // Assert - Callback should NOT be called (job still enqueued)
            expect(mockOnJobCompleted).not.toHaveBeenCalled();
        } finally {
            timers.cleanup();
        }
    });

    it('should handle transition from Processing to Succeeded correctly', async () => {
        // Arrange
        const jobId = 'test-job-transition';

        // Start with Processing status
        mockUseInvoiceImportJobStatus.mockReturnValue({
            data: { id: jobId, state: 'Processing' },
            isLoading: false,
            error: null
        });

        const timers = setupFakeTimers();

        try {
            // Act - Render with Processing status
            const { rerender } = renderComponent(jobId);

            await waitFor(() => {
                expect(screen.getByText('Importuje se')).toBeInTheDocument();
            });

            // Advance time - should NOT trigger timer
            act(() => {
                timers.advanceTimersByTime(5000);
            });

            expect(mockOnJobCompleted).not.toHaveBeenCalled();

            // Transition to Succeeded
            mockUseInvoiceImportJobStatus.mockReturnValue({
                data: { id: jobId, state: 'Succeeded' },
                isLoading: false,
                error: null
            });

            // Rerender to trigger update
            rerender(
                <QueryClientProvider client={queryClient}>
                    <InvoiceImportJobTracker
                        jobId={jobId}
                        onJobCompleted={mockOnJobCompleted}
                    />
                </QueryClientProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('Import dokončen')).toBeInTheDocument();
            });

            // NOW timer should start - advance 5 seconds
            act(() => {
                timers.advanceTimersByTime(5000);
            });

            // Assert - Callback should be called after transition to Succeeded
            await waitFor(() => {
                expect(mockOnJobCompleted).toHaveBeenCalledTimes(1);
            });
        } finally {
            timers.cleanup();
        }
    });
});
