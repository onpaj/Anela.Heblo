import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// Response types for the new endpoints
interface GetTransportBoxByCodeResponse {
  transportBox?: {
    id: number;
    code: string;
    state: string;
    location?: string;
    description?: string;
    lastStateChanged?: string;
    items: Array<{
      id: number;
      productCode: string;
      productName: string;
      amount: number;
    }>;
  };
  success: boolean;
  errorMessage?: string;
}

interface ReceiveTransportBoxResponse {
  success: boolean;
  errorMessage?: string;
  boxId: number;
  boxCode?: string;
}

interface ReceiveTransportBoxRequest {
  boxId: number;
  userName: string;
}

export const useTransportBoxReceive = () => {
  const queryClient = useQueryClient();

  // Get box by code for scanning
  const getByCode = async (boxCode: string): Promise<GetTransportBoxByCodeResponse> => {
    const apiClient = getAuthenticatedApiClient();
    const relativeUrl = `/api/transport-boxes/by-code/${encodeURIComponent(boxCode)}`;
    const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
    
    const response = await (apiClient as any).http.fetch(fullUrl, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      if (response.status === 400) {
        // Bad request - business logic error (box not found, wrong state, etc.)
        return {
          success: false,
          errorMessage: errorData.errorMessage || 'Box nelze přijmout'
        };
      }
      throw new Error(`HTTP ${response.status}: ${errorData.message || response.statusText}`);
    }

    return await response.json();
  };

  // Receive transport box
  const receiveMutation = useMutation({
    mutationFn: async (params: { boxId: number; userName: string }): Promise<ReceiveTransportBoxResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/transport-boxes/${params.boxId}/receive`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      
      const request: ReceiveTransportBoxRequest = {
        boxId: params.boxId,
        userName: params.userName
      };

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        if (response.status === 400) {
          // Bad request - business logic error
          return {
            success: false,
            errorMessage: errorData.errorMessage || 'Chyba při příjmu boxu',
            boxId: params.boxId
          };
        }
        throw new Error(`HTTP ${response.status}: ${errorData.message || response.statusText}`);
      }

      return await response.json();
    },
    onSuccess: (data, variables) => {
      // Invalidate relevant queries after successful receive
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, "detail", variables.boxId],
      });
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.transportBox, "list"]
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, "summary"],
      });
    },
  });

  return {
    getByCode,
    receive: (boxId: number, userName: string) => receiveMutation.mutateAsync({ boxId, userName }),
    isReceiving: receiveMutation.isPending,
  };
};