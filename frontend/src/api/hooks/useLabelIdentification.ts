import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { IdentifyLabelResponse } from "../generated/api-client";

/**
 * Identifies a product from a photo of its etiquette.
 *
 * Labels print only the INCI composition, which identifies a product FAMILY — size
 * variants share the same artwork text. A family with two sizes returns both variants
 * so the operator can pick the one in hand.
 */
export const useIdentifyLabelMutation = () =>
  useMutation<IdentifyLabelResponse, Error, File>({
    mutationFn: async (photo: File) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.labelIdentification_Identify({
        data: photo,
        fileName: photo.name,
      });
    },
  });
