import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface Department {
  id: string;
  name: string;
}

export const useDepartments = () => {
  return useQuery({
    queryKey: ['departments'],
    queryFn: async (): Promise<Department[]> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = '/api/departments';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json'
        }
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch departments: ${response.statusText}`);
      }

      return await response.json();
    },
    staleTime: 5 * 60 * 1000, // 5 minutes - departments don't change often
  });
};