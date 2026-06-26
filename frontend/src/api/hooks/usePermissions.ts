import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export interface MePermissions {
  email?: string;
  displayName?: string;
  isSuperUser: boolean;
  permissions: string[];
  groups: string[];
}

export const permissionsQueryKey = ["auth", "me"] as const;

export const usePermissions = (enabled: boolean) => {
  return useQuery({
    queryKey: permissionsQueryKey,
    enabled,
    staleTime: 5 * 60 * 1000, // align with backend ~5 min TTL
    queryFn: async (): Promise<MePermissions> => {
      const client = getAuthenticatedApiClient();
      const res = await client.auth_Me();
      return {
        email: res?.email ?? undefined,
        displayName: res?.displayName ?? undefined,
        isSuperUser: res?.isSuperUser ?? false,
        permissions: res?.permissions ?? [],
        groups: res?.groups ?? [],
      };
    },
  });
};
