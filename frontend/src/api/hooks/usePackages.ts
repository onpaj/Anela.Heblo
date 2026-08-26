import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import type { Carriers, PackageDto as GeneratedPackageDto } from "../generated/api-client";
import { getErrorMessage } from "../../utils/errorHandler";
import { callApi } from "../apiErrorEnvelope";

const GENERIC_DELETE_ERROR = "Nepodařilo se smazat balík.";

export type PackageDto = {
  id: number;
  orderCode: string;
  customerName: string;
  packageNumber: string;
  trackingNumber?: string;
  shippingProviderCode: string;
  shippingProviderName?: string;
  packedAt: string;
  packedBy?: string;
  packedByUserId?: string;
};

export type GetPackagesRequest = {
  orderCode?: string;
  customerName?: string;
  packageNumber?: string;
  carrier?: string;
  fromDate?: string;
  toDate?: string;
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
};

export type GetPackagesResponse = {
  items: PackageDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
};

const toPackageDto = (dto: GeneratedPackageDto): PackageDto => ({
  id: dto.id ?? 0,
  orderCode: dto.orderCode ?? '',
  customerName: dto.customerName ?? '',
  packageNumber: dto.packageNumber ?? '',
  trackingNumber: dto.trackingNumber,
  shippingProviderCode: dto.shippingProviderCode ?? '',
  shippingProviderName: dto.shippingProviderName,
  packedAt: dto.packedAt ? dto.packedAt.toISOString() : '',
  packedBy: dto.packedBy,
  packedByUserId: dto.packedByUserId,
});

export const packageKeys = {
  all: ["packages"] as const,
  list: (req: GetPackagesRequest) => [...packageKeys.all, "list", req] as const,
};

export const usePackagesQuery = (request: GetPackagesRequest) =>
  useQuery({
    queryKey: packageKeys.list(request),
    queryFn: async (): Promise<GetPackagesResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.packaging_GetPackages(
        request.orderCode || undefined,
        request.customerName || undefined,
        request.packageNumber || undefined,
        (request.carrier || undefined) as Carriers | undefined,
        request.fromDate ? new Date(request.fromDate) : undefined,
        request.toDate ? new Date(request.toDate) : undefined,
        request.pageNumber,
        request.pageSize,
        request.sortBy,
        request.sortDescending,
      );

      return {
        items: (response.items ?? []).map(toPackageDto),
        totalCount: response.totalCount ?? 0,
        pageNumber: response.pageNumber ?? request.pageNumber,
        pageSize: response.pageSize ?? request.pageSize,
      };
    },
    staleTime: 1000 * 30,
  });

export const useDeletePackageMutation = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number): Promise<void> => {
      const apiClient = getAuthenticatedApiClient();
      await callApi(
        () => apiClient.packaging_DeletePackage(id),
        ({ errorCode, params }) =>
          errorCode ? getErrorMessage(errorCode, params) : GENERIC_DELETE_ERROR,
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: packageKeys.all });
    },
  });
};
