import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

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

export interface BoxFillResult {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
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

const getInternals = (): ApiClientWithInternals =>
  getAuthenticatedApiClient() as unknown as ApiClientWithInternals;

// The add/remove/state endpoints return HTTP 400 with a JSON body on business
// failures, so always read the body and surface { success: false } from it.
const parseResult = async (response: Response): Promise<BoxFillResult> => {
  try {
    return (await response.json()) as BoxFillResult;
  } catch {
    return { success: false };
  }
};

export const useOpenOrResumeBox = () =>
  useMutation({
    mutationFn: async (boxCode: string): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(`${api.baseUrl}/api/transport-boxes/open-by-code`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ boxCode }),
      });
      return parseResult(response);
    },
  });

export const useAddBoxItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddBoxItemInput): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(`${api.baseUrl}/api/transport-boxes/${input.boxId}/items`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });
      return parseResult(response);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useRemoveBoxItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { boxId: number; itemId: number }): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(
        `${api.baseUrl}/api/transport-boxes/${input.boxId}/items/${input.itemId}`,
        { method: "DELETE" },
      );
      return parseResult(response);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useSendBoxToTransit = () =>
  useMutation({
    mutationFn: async (boxId: number): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(`${api.baseUrl}/api/transport-boxes/${boxId}/state`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ boxId, newState: "InTransit" }),
      });
      return parseResult(response);
    },
  });
