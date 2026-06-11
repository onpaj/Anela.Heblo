import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../api/client";

export interface PackingUser {
  id: string;
  displayName: string;
}

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export const packingUsersKey = ["packing-users"] as const;

export function usePackingUsers() {
  return useQuery({
    queryKey: packingUsersKey,
    queryFn: async (): Promise<PackingUser[]> => {
      const apiClient = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
      const url = `${apiClient.baseUrl}/api/packaging/packing-users`;
      const response = await apiClient.http.fetch(url, {
        method: "GET",
        headers: { Accept: "application/json" },
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      const data = (await response.json()) as { users?: PackingUser[] };
      return data.users ?? [];
    },
    staleTime: 60 * 1000,
  });
}
