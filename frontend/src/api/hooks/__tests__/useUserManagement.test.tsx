import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactNode } from 'react';
import { useResponsiblePersonsQuery } from '../useUserManagement';

// Mock the entire API client module
jest.mock('../../client', () => ({
    getAuthenticatedApiClient: jest.fn(() => ({
        baseUrl: 'http://localhost:5000',
        http: {
            fetch: jest.fn()
        }
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
        // Mock fetch globally
        global.fetch = jest.fn();
    });

    afterEach(() => {
        jest.restoreAllMocks();
    });

    it('should show loading state initially', () => {
        (global.fetch as jest.Mock).mockImplementation(() => new Promise(() => {})); // Never resolves

        const { result } = renderHook(() => useResponsiblePersonsQuery(), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.data).toBeUndefined();
        expect(result.current.isError).toBe(false);
    });

    it('should have correct initial state', () => {
        (global.fetch as jest.Mock).mockResolvedValue({
            ok: true,
            json: () => Promise.resolve({ success: true, members: [] })
        });

        const { result } = renderHook(() => useResponsiblePersonsQuery(), {
            wrapper: createWrapper(),
        });

        // Check initial query state
        expect(result.current.failureCount).toBe(0);
        expect(result.current.dataUpdatedAt).toBeDefined();
    });

    it('should be a query hook with proper structure', () => {
        (global.fetch as jest.Mock).mockResolvedValue({
            ok: true,
            json: () => Promise.resolve({ success: true, members: [] })
        });

        const { result } = renderHook(() => useResponsiblePersonsQuery(), {
            wrapper: createWrapper(),
        });

        // Check that it has the expected React Query structure
        expect(typeof result.current.isLoading).toBe('boolean');
        expect(typeof result.current.isError).toBe('boolean');
        expect(typeof result.current.isSuccess).toBe('boolean');
        expect(typeof result.current.refetch).toBe('function');
    });

    it('should handle hook cleanup properly', () => {
        (global.fetch as jest.Mock).mockResolvedValue({
            ok: true,
            json: () => Promise.resolve({ success: true, members: [] })
        });

        const { unmount } = renderHook(() => useResponsiblePersonsQuery(), {
            wrapper: createWrapper(),
        });

        // Test that unmounting doesn't throw errors
        expect(() => unmount()).not.toThrow();
    });
});