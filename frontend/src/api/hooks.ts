// TanStack Query hooks for API calls
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient, QUERY_KEYS, DEFAULT_QUERY_OPTIONS } from './client';

// Example hook for weather data
export const useWeatherQuery = () => {
  return useQuery({
    queryKey: QUERY_KEYS.weather,
    queryFn: () => apiClient.weatherForecast(),
    ...DEFAULT_QUERY_OPTIONS,
  });
};

// Example mutation hook (for future use)
// export const useCreateItemMutation = () => {
//   const queryClient = useQueryClient();
//   
//   return useMutation({
//     mutationFn: (data: CreateItemRequest) => apiClient.createItem(data),
//     onSuccess: () => {
//       // Invalidate and refetch relevant queries
//       queryClient.invalidateQueries({ queryKey: QUERY_KEYS.items });
//     },
//   });
// };

// Generic error handler
export const handleApiError = (error: unknown): string => {
  if (error instanceof Error) {
    return error.message;
  }
  return 'An unexpected error occurred';
};