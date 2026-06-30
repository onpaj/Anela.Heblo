import { useQuery } from "@tanstack/react-query";
import { getApiBaseUrl, getAuthenticatedFetch } from "../client";

export interface PackingStatisticsSummary {
  totalPackages: number;
  totalOrders: number;
  distinctPackers: number;
  averagePackagesPerOrder: number;
  trackingCoveragePercent: number;
  busiestDay: DailyThroughput | null;
  busiestHour: HourBucket | null;
}

export interface DailyThroughput {
  date: string;
  orderCount: number;
  packageCount: number;
}

export interface HourBucket {
  /** ISO weekday: 1 = Monday .. 7 = Sunday. */
  dayOfWeek: number;
  hour: number;
  packageCount: number;
}

export interface PackerThroughput {
  packerId: string | null;
  packerName: string;
  orderCount: number;
  packageCount: number;
}

export interface CarrierMix {
  code: string;
  name: string;
  packageCount: number;
}

export interface PackagesPerOrderBucket {
  /** Packages per order; 3 means "3 or more". */
  packageCount: number;
  orderCount: number;
}

export interface PackingStatisticsResponse {
  fromDate: string;
  toDate: string;
  packerAttributionSince: string | null;
  summary: PackingStatisticsSummary;
  throughputDaily: DailyThroughput[];
  hourHeatmap: HourBucket[];
  byPacker: PackerThroughput[];
  byCarrier: CarrierMix[];
  packagesPerOrder: PackagesPerOrderBucket[];
}

export interface PackingStatisticsParams {
  /** Inclusive start of the local-day window (YYYY-MM-DD). */
  fromDate?: string;
  /** Inclusive end of the local-day window (YYYY-MM-DD). */
  toDate?: string;
}

export const packingStatisticsKeys = {
  all: ["packingStatistics"] as const,
  detail: (params: PackingStatisticsParams) =>
    [...packingStatisticsKeys.all, params] as const,
};

export const usePackingStatistics = (params: PackingStatisticsParams = {}) =>
  useQuery({
    queryKey: packingStatisticsKeys.detail(params),
    queryFn: async (): Promise<PackingStatisticsResponse> => {
      const query = new URLSearchParams();
      if (params.fromDate) query.set("fromDate", params.fromDate);
      if (params.toDate) query.set("toDate", params.toDate);
      const suffix = query.toString() ? `?${query.toString()}` : "";

      const url = `${getApiBaseUrl()}/api/packaging/statistics${suffix}`;
      const response = await getAuthenticatedFetch()(url, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<PackingStatisticsResponse>;
    },
    staleTime: 5 * 60_000,
  });
