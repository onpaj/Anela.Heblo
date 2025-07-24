// TanStack Query client configuration
import { QueryClient } from '@tanstack/react-query';
import { DEFAULT_QUERY_OPTIONS } from './client';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      ...DEFAULT_QUERY_OPTIONS,
      refetchOnWindowFocus: false,
      refetchOnMount: true,
      refetchOnReconnect: true,
    },
    mutations: {
      retry: 1,
    },
  },
});

// Global error handler
queryClient.setMutationDefaults(['mutation'], {
  onError: (error) => {
    console.error('Mutation error:', error);
    // Here you can add global error handling like toast notifications
  },
});

queryClient.setQueryDefaults(['query'], {
  onError: (error) => {
    console.error('Query error:', error);
    // Here you can add global error handling
  },
});