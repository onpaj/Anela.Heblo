import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { BaseResponse } from "../../types/errors";

export const useRunExpeditionListPrintFix = () => {
  return useMutation({
    mutationFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list/run-fix`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }

      return await response.json();
    },
  });
};

export const usePrintExpeditionOrder = () => {
  return useMutation<BaseResponse, Error, { orderCode: string }>({
    mutationFn: async ({ orderCode }): Promise<BaseResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list/print-order`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ orderCode }),
      });

      // The handler returns a BaseResponse body even for failures (mapped to
      // 4xx by the ErrorCodes HttpStatusCode attribute), so read the body first.
      const data = await response.json().catch(() => null);
      if (data && typeof data.success === "boolean") {
        return {
          success: data.success,
          errorCode: data.errorCode ?? undefined,
          params: data.params ?? undefined,
        };
      }

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      return { success: true };
    },
  });
};
