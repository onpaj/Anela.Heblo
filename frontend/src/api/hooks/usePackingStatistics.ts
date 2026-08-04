import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import type {
  DailyThroughputDto,
  HourBucketDto,
  PackerThroughputDto,
  CarrierMixDto,
  PackagesPerOrderBucketDto,
  PackingStatisticsSummaryDto,
} from "../generated/api-client";

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

const toDailyThroughput = (dto: DailyThroughputDto): DailyThroughput => ({
  date: dto.date ? dto.date.toISOString() : '',
  orderCount: dto.orderCount ?? 0,
  packageCount: dto.packageCount ?? 0,
});

const toHourBucket = (dto: HourBucketDto): HourBucket => ({
  dayOfWeek: dto.dayOfWeek ?? 0,
  hour: dto.hour ?? 0,
  packageCount: dto.packageCount ?? 0,
});

const toPackerThroughput = (dto: PackerThroughputDto): PackerThroughput => ({
  packerId: dto.packerId ?? null,
  packerName: dto.packerName ?? '',
  orderCount: dto.orderCount ?? 0,
  packageCount: dto.packageCount ?? 0,
});

const toCarrierMix = (dto: CarrierMixDto): CarrierMix => ({
  code: dto.code ?? '',
  name: dto.name ?? '',
  packageCount: dto.packageCount ?? 0,
});

const toPackagesPerOrderBucket = (dto: PackagesPerOrderBucketDto): PackagesPerOrderBucket => ({
  packageCount: dto.packageCount ?? 0,
  orderCount: dto.orderCount ?? 0,
});

const toPackingStatisticsSummary = (dto: PackingStatisticsSummaryDto): PackingStatisticsSummary => ({
  totalPackages: dto.totalPackages ?? 0,
  totalOrders: dto.totalOrders ?? 0,
  distinctPackers: dto.distinctPackers ?? 0,
  averagePackagesPerOrder: dto.averagePackagesPerOrder ?? 0,
  trackingCoveragePercent: dto.trackingCoveragePercent ?? 0,
  busiestDay: dto.busiestDay ? toDailyThroughput(dto.busiestDay) : null,
  busiestHour: dto.busiestHour ? toHourBucket(dto.busiestHour) : null,
});

export const packingStatisticsKeys = {
  all: ["packingStatistics"] as const,
  detail: (params: PackingStatisticsParams) =>
    [...packingStatisticsKeys.all, params] as const,
};

export const usePackingStatistics = (params: PackingStatisticsParams = {}) =>
  useQuery({
    queryKey: packingStatisticsKeys.detail(params),
    queryFn: async (): Promise<PackingStatisticsResponse> => {
      const apiClient = getAuthenticatedApiClient(false);
      const response = await apiClient.packaging_GetStatistics(
        params.fromDate ? new Date(params.fromDate) : undefined,
        params.toDate ? new Date(params.toDate) : undefined,
      );

      return {
        fromDate: response.fromDate ? response.fromDate.toISOString() : '',
        toDate: response.toDate ? response.toDate.toISOString() : '',
        packerAttributionSince: response.packerAttributionSince
          ? response.packerAttributionSince.toISOString()
          : null,
        summary: toPackingStatisticsSummary(response.summary!),
        throughputDaily: (response.throughputDaily ?? []).map(toDailyThroughput),
        hourHeatmap: (response.hourHeatmap ?? []).map(toHourBucket),
        byPacker: (response.byPacker ?? []).map(toPackerThroughput),
        byCarrier: (response.byCarrier ?? []).map(toCarrierMix),
        packagesPerOrder: (response.packagesPerOrder ?? []).map(toPackagesPerOrderBucket),
      };
    },
    staleTime: 5 * 60_000,
  });
