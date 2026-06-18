import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactNode } from 'react';
import { useResponsiblePersonsQuery } from '../useUserManagement';
import { SwaggerException } from '../../generated/api-client';

const mockGetGroupMembers = jest.fn();

jest.mock('../../client', () => ({
    getAuthenticatedApiClient: jest.fn(() => ({
        userManagement_GetGroupMembers: mockGetGroupMembers,
    })),
    QUERY_KEYS: {
        userManagement: ['user-management']
    }
}));

const createWrapper = () => {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
            },
        },
    });

    return ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
};

describe('useResponsiblePersonsQuery', () => {
    beforeEach(() => {
        jest.clearAllMocks();
        mockGetGroupMembers.mockResolvedValue({ success: true, members: [] });
    });

    afterEach(() => {
        jest.restoreAllMocks();
    });

    it('should show loading state initially', () => {
        mockGetGroupMembers.mockImplementation(() => new Promise(() => {})); // Never resolves

        const { result } = renderHook(() => useResponsiblePersonsQuery('test-group-id'), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.data).toBeUndefined();
        expect(result.current.isError).toBe(false);
    });

    it('should have correct initial state', () => {
        const { result } = renderHook(() => useResponsiblePersonsQuery('test-group-id'), {
            wrapper: createWrapper(),
        });

        expect(result.current.failureCount).toBe(0);
        expect(result.current.dataUpdatedAt).toBeDefined();
    });

    it('should be a query hook with proper structure', () => {
        const { result } = renderHook(() => useResponsiblePersonsQuery('test-group-id'), {
            wrapper: createWrapper(),
        });

        expect(typeof result.current.isLoading).toBe('boolean');
        expect(typeof result.current.isError).toBe('boolean');
        expect(typeof result.current.isSuccess).toBe('boolean');
        expect(typeof result.current.refetch).toBe('function');
    });

    it('should handle hook cleanup properly', () => {
        const { unmount } = renderHook(() => useResponsiblePersonsQuery('test-group-id'), {
            wrapper: createWrapper(),
        });

        expect(() => unmount()).not.toThrow();
    });
});

describe('retry behavior', () => {
    const createWrapperWithRetry = () => {
        const queryClient = new QueryClient({
            defaultOptions: {
                queries: {
                    retryDelay: 0,
                },
            },
        });

        return ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        );
    };

    beforeEach(() => {
        jest.clearAllMocks();
        // resetMocks: true clears jest.fn() implementations between tests;
        // re-wire getAuthenticatedApiClient so the hook can reach the mock.
        const { getAuthenticatedApiClient } = require('../../client');
        (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
            userManagement_GetGroupMembers: mockGetGroupMembers,
        });
    });

    afterEach(() => {
        jest.restoreAllMocks();
    });

    it('should NOT retry on 403 SwaggerException (called exactly 1 time)', async () => {
        mockGetGroupMembers.mockRejectedValue(
            new SwaggerException('Forbidden', 403, '', {}, null)
        );

        const { result } = renderHook(() => useResponsiblePersonsQuery('test-group-id'), {
            wrapper: createWrapperWithRetry(),
        });

        await waitFor(() => {
            expect(result.current.isError).toBe(true);
        });

        expect(mockGetGroupMembers).toHaveBeenCalledTimes(1);
    });

    it('should retry up to 2 times on 500 error (called exactly 3 times total)', async () => {
        mockGetGroupMembers.mockRejectedValue(new Error('server error'));

        const { result } = renderHook(() => useResponsiblePersonsQuery('test-group-id'), {
            wrapper: createWrapperWithRetry(),
        });

        await waitFor(() => {
            expect(result.current.isError).toBe(true);
        }, { timeout: 5000 });

        expect(mockGetGroupMembers).toHaveBeenCalledTimes(3);
    });
});
