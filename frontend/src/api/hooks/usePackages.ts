import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

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
};

export type GetPackagesRequest = {
  orderCode?: string;
  customerName?: string;
  packageNumber?: string;
  shippingProviderCode?: string;
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

export const packageKeys = {
  all: ["packages"] as const,
  list: (req: GetPackagesRequest) => [...packageKeys.all, "list", req] as const,
};

export const usePackagesQuery = (request: GetPackagesRequest) =>
  useQuery({
    queryKey: packageKeys.list(request),
    queryFn: async (): Promise<GetPackagesResponse> => {
      const apiClient = getAuthenticatedApiClient() as any;
      const relativeUrl = "/api/packaging/packages";
      const params = new URLSearchParams();

      if (request.orderCode) params.append("OrderCode", request.orderCode);
      if (request.customerName)
        params.append("CustomerName", request.customerName);
      if (request.packageNumber)
        params.append("PackageNumber", request.packageNumber);
      if (request.shippingProviderCode)
        params.append("ShippingProviderCode", request.shippingProviderCode);
      if (request.fromDate) params.append("FromDate", request.fromDate);
      if (request.toDate) params.append("ToDate", request.toDate);
      if (request.pageNumber)
        params.append("PageNumber", request.pageNumber.toString());
      if (request.pageSize)
        params.append("PageSize", request.pageSize.toString());
      if (request.sortBy) params.append("SortBy", request.sortBy);
      if (request.sortDescending !== undefined)
        params.append("SortDescending", request.sortDescending.toString());

      const queryString = params.toString();
      const fullUrl = `${apiClient.baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;

      const response = await apiClient.http.fetch(fullUrl, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetPackagesResponse>;
    },
    staleTime: 1000 * 30,
  });

export const useDeletePackageMutation = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const apiClient = getAuthenticatedApiClient() as any;
      const relativeUrl = `/api/packaging/packages/${id}`;
      const fullUrl = `${apiClient.baseUrl}${relativeUrl}`;

      const response = await apiClient.http.fetch(fullUrl, {
        method: "DELETE",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json();
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: packageKeys.all });
    },
  });
};
