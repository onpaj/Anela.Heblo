import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactNode } from 'react';
import { useResponsiblePersonsQuery } from '../useUserManagement';

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

    it('should not fire the query when options.enabled is false', () => {
        const { result } = renderHook(
            () => useResponsiblePersonsQuery('test-group-id', { enabled: false }),
            { wrapper: createWrapper() },
        );

        expect(result.current.isLoading).toBe(false);
        expect(result.current.fetchStatus).toBe('idle');
        expect(mockGetGroupMembers).not.toHaveBeenCalled();
    });

    it('should fire the query when options.enabled is true', async () => {
        const { result } = renderHook(
            () => useResponsiblePersonsQuery('test-group-id', { enabled: true }),
            { wrapper: createWrapper() },
        );

        expect(result.current.isLoading).toBe(true);
        expect(result.current.fetchStatus).toBe('fetching');
    });
});
