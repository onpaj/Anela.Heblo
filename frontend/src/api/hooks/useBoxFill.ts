import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { BaseResponse } from "../../types/errors";

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export interface TerminalBoxItem {
  id: number;
  productCode: string;
  productName: string;
  amount: number;
  lotNumber?: string;
}

export interface TerminalBox {
  id: number;
  code: string;
  state: string;
  itemCount: number;
  items: TerminalBoxItem[];
}

export interface BoxFillResult extends BaseResponse {
  transportBox?: TerminalBox;
  resumed?: boolean;
}

export interface AddBoxItemInput {
  boxId: number;
  productCode: string;
  productName: string;
  amount: number;
  sourceInventoryId?: number;
  lotNumber?: string;
  expirationDate?: string;
  allowNegativeStock?: boolean;
}

const JSON_HEADERS = { "Content-Type": "application/json" };

// Issues a box-fill request and always resolves to a BoxFillResult — it never
// rejects. Business failures arrive as HTTP 400 with a JSON body; network
// failures (a thrown fetch) and unparseable bodies collapse to { success: false }.
const boxFillRequest = async (path: string, init: RequestInit): Promise<BoxFillResult> => {
  try {
    const apiClient = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
    const response = await apiClient.http.fetch(`${apiClient.baseUrl}${path}`, init);
    return (await response.json()) as BoxFillResult;
  } catch {
    return { success: false };
  }
};

export const useOpenOrResumeBox = () =>
  useMutation({
    mutationFn: (boxCode: string): Promise<BoxFillResult> =>
      boxFillRequest("/api/transport-boxes/open-by-code", {
        method: "POST",
        headers: JSON_HEADERS,
        body: JSON.stringify({ boxCode }),
      }),
  });

export const useAddBoxItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: AddBoxItemInput): Promise<BoxFillResult> =>
      boxFillRequest(`/api/transport-boxes/${input.boxId}/items`, {
        method: "POST",
        headers: JSON_HEADERS,
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.transportBox });
    },
  });
};

export const useRemoveBoxItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { boxId: number; itemId: number }): Promise<BoxFillResult> =>
      boxFillRequest(`/api/transport-boxes/${input.boxId}/items/${input.itemId}`, {
        method: "DELETE",
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.transportBox });
    },
  });
};

export const useSendBoxToTransit = () =>
  useMutation({
    mutationFn: (boxId: number): Promise<BoxFillResult> =>
      boxFillRequest(`/api/transport-boxes/${boxId}/state`, {
        method: "PUT",
        headers: JSON_HEADERS,
        body: JSON.stringify({ boxId, newState: "InTransit" }),
      }),
  });
