import { useQuery } from "@tanstack/react-query";
import { getApiBaseUrl, getAuthenticatedFetch } from "../client";

export type PackerStatsDto = {
  packerId?: string;
  packerName: string;
  orderCount: number;
};

export type GetPackingDashboardResponse = {
  ordersBeingPackedCount: number | null;
  totalOrdersPackedToday: number;
  packedByPacker: PackerStatsDto[];
};

export const packingDashboardKeys = {
  all: ["packingDashboard"] as const,
};

export const usePackingDashboard = () =>
  useQuery({
    queryKey: packingDashboardKeys.all,
    queryFn: async (): Promise<GetPackingDashboardResponse> => {
      const url = `${getApiBaseUrl()}/api/packaging/dashboard`;
      const response = await getAuthenticatedFetch()(url, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetPackingDashboardResponse>;
    },
    staleTime: 60_000,
    refetchInterval: 60_000,
    refetchIntervalInBackground: true,
  });
