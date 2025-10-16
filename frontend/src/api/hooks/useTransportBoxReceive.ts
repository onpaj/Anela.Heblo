import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { GetTransportBoxByCodeResponse, ReceiveTransportBoxResponse, ReceiveTransportBoxRequest } from "../generated/api-client";

export const useTransportBoxReceive = () => {
  const queryClient = useQueryClient();

  // Get box by code for scanning
  const getByCode = async (boxCode: string): Promise<GetTransportBoxByCodeResponse> => {
    const apiClient = getAuthenticatedApiClient();
    return await apiClient.transportBox_GetTransportBoxByCode(boxCode);
  };

  // Receive transport box
  const receiveMutation = useMutation({
    mutationFn: async (params: { boxId: number; userName: string }): Promise<ReceiveTransportBoxResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const request = new ReceiveTransportBoxRequest({
        boxId: params.boxId,
        userName: params.userName
      });
      return await apiClient.transportBox_ReceiveTransportBox(params.boxId, request);
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