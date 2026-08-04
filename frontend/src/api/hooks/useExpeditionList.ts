import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { PrintExpeditionOrderRequest } from "../generated/api-client";
import { BaseResponse } from "../../types/errors";

export interface RunExpeditionListPrintFixResult {
  totalCount: number;
}

export const useRunExpeditionListPrintFix = () => {
  return useMutation<RunExpeditionListPrintFixResult, Error, void>({
    mutationFn: async (): Promise<RunExpeditionListPrintFixResult> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionList_RunFix();
      return { totalCount: response.totalCount ?? 0 };
    },
  });
};

export const usePrintExpeditionOrder = () => {
  return useMutation<BaseResponse, Error, { orderCode: string }>({
    mutationFn: async ({ orderCode }): Promise<BaseResponse> => {
      const client = getAuthenticatedApiClient();
      const request = new PrintExpeditionOrderRequest({ orderCode });
      const response = await client.expeditionList_PrintOrder(request);
      return {
        success: response.success ?? true,
        errorCode: response.errorCode ?? undefined,
        params: response.params ?? undefined,
      };
    },
  });
};
