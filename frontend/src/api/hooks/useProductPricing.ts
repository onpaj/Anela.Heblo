import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { readApiErrorEnvelope } from "../apiErrorEnvelope";
import {
  PriceDivergenceRowDto,
  PriceDivergenceSummaryDto,
  PriceDivergenceKind,
  SetProductPriceRequest,
  SetProductPriceResponse,
} from "../generated/api-client";

export { PriceDivergenceRowDto, PriceDivergenceSummaryDto, PriceDivergenceKind };

const QUERY_KEYS = {
  divergence: ["product-pricing", "divergence"] as const,
};

// Fetches the divergence report. `useSetProductPrice` below invalidates this query on a
// successful write so an edited row reloads from live Shoptet/Flexi data instead of
// trusting the value the operator just typed.
export const usePriceDivergenceReport = () =>
  useQuery({
    queryKey: QUERY_KEYS.divergence,
    queryFn: async () => {
      const response = await getAuthenticatedApiClient().productPricing_GetDivergenceReport();
      return {
        rows: response.rows ?? [],
        summary: response.summary,
      };
    },
  });

export interface SetProductPriceInput {
  productCode: string;
  priceWithVat: number;
}

const GENERIC_SET_PRICE_ERROR = "Cenu se nepodařilo uložit.";

/**
 * Writes straight through to the live Shoptet store and the live ABRA Flexi ERP — there is
 * no sandbox and no server-side price ceiling. The thrown `Error` always carries the
 * backend's `errorCode` (a string) as a property so the caller can translate it; a
 * `ProductPriceFlexiWriteFailed` means Shoptet WAS updated but Flexi was not, so the two
 * systems now hold different prices.
 */
const setProductPrice = async ({
  productCode,
  priceWithVat,
}: SetProductPriceInput): Promise<SetProductPriceResponse> => {
  try {
    const response = await getAuthenticatedApiClient().productPricing_SetPrice(
      productCode,
      new SetProductPriceRequest({ productCode, priceWithVat }),
    );
    if (response.success === false) {
      throw Object.assign(new Error(GENERIC_SET_PRICE_ERROR), { errorCode: response.errorCode });
    }
    return response;
  } catch (error) {
    if (error instanceof Error && (error as { errorCode?: string }).errorCode) {
      throw error;
    }
    const envelope = readApiErrorEnvelope(error);
    throw Object.assign(new Error(GENERIC_SET_PRICE_ERROR), { errorCode: envelope?.errorCode });
  }
};

export const useSetProductPrice = () => {
  const queryClient = useQueryClient();

  return useMutation<SetProductPriceResponse, Error, SetProductPriceInput>({
    mutationFn: setProductPrice,
    onSuccess: () => {
      // Reload from live Shoptet/Flexi data rather than trusting the local value.
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.divergence });
    },
  });
};
