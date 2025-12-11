import React from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

/**
 * Creates a mock API client for testing
 * @param baseUrl - Base URL for the API client (default: http://localhost:5000)
 * @returns Object containing mock client and mock fetch function
 */
export const createMockApiClient = (baseUrl = 'http://localhost:5000') => {
    const mockFetch = jest.fn();
    const mockClient = {
        baseUrl,
        http: { fetch: mockFetch }
    };
    return { mockClient, mockFetch };
};

/**
 * Mocks the getAuthenticatedApiClient function to return a mock client
 * @param mockClient - The mock client to return
 */
export const mockAuthenticatedApiClient = (mockClient: any) => {
    const { getAuthenticatedApiClient } = require('./client');
    // Mock as function that returns a Promise (works with await)
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(Promise.resolve(mockClient));
};

/**
 * Creates a QueryClient wrapper for testing hooks with React Query
 * @returns Object containing wrapper component and query client instance
 */
export const createQueryClientWrapper = () => {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false }
        }
    });

    const wrapper = ({ children }: { children: React.ReactNode }) => (
        React.createElement(QueryClientProvider, { client: queryClient }, children)
    );

    return { wrapper, queryClient };
};

/**
 * Sets up fake timers for testing time-dependent behavior
 * @returns Object with helper functions for advancing timers
 */
export const setupFakeTimers = () => {
    jest.useFakeTimers();
    return {
        advanceTimersByTime: (ms: number) => jest.advanceTimersByTime(ms),
        runAllTimers: () => jest.runAllTimers(),
        cleanup: () => jest.useRealTimers()
    };
};
