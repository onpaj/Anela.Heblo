import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

/**
 * React Query hook for fetching organizational chart data from the backend
 * @returns Query result with organization structure containing positions and employees
 */
export const useOrgChart = () => {
    return useQuery({
        queryKey: [...QUERY_KEYS.orgChart],
        queryFn: async () => {
            const apiClient = getAuthenticatedApiClient();
            const response = await apiClient.orgChart_GetOrganizationStructure();
            return response;
        },
        staleTime: 1000 * 60 * 30, // Data is fresh for 30 minutes
        gcTime: 1000 * 60 * 60, // Cache data for 1 hour
    });
};
