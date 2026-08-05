import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export type PackerStatsDto = {
  packerId?: string;
  packerName: string;
  orderCount: number;
};

export type GetPackingDashboardResponse = {
  ordersBeingPackedCount: number | null;
  ordersBeingProcessedCount: number | null;
  ordersBeingPackedCountLastSync: string | null;
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
      const apiClient = getAuthenticatedApiClient(false);
      const response = await apiClient.packaging_GetDashboard();

      return {
        ordersBeingPackedCount: response.ordersBeingPackedCount ?? null,
        ordersBeingProcessedCount: response.ordersBeingProcessedCount ?? null,
        ordersBeingPackedCountLastSync: response.ordersBeingPackedCountLastSync
          ? response.ordersBeingPackedCountLastSync.toISOString()
          : null,
        totalOrdersPackedToday: response.totalOrdersPackedToday ?? 0,
        packedByPacker: (response.packedByPacker ?? []).map((p) => ({
          packerId: p.packerId,
          packerName: p.packerName ?? '',
          orderCount: p.orderCount ?? 0,
        })),
      };
    },
    staleTime: 60_000,
    refetchInterval: 60_000,
    refetchIntervalInBackground: true,
  });
