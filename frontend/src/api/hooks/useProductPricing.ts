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
// successful write, and also on a `ProductPriceFlexiWriteFailed` partial failure, so an
// edited row reloads from live Shoptet/Flexi data instead of trusting the value the
// operator just typed or silently showing a stale "in agreement" state.
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

// The one failure mode where Shoptet was already written even though the mutation as a
// whole failed. Without a refetch here, the row would keep rendering its pre-edit Shoptet
// price next to the pre-edit Flexi price — the two would still match, so the row would
// render InAgreement (green) even though the live systems have actually diverged. The
// persistent row-level alert would then be the only thing contradicting the row it sits in.
// The other four error codes all mean nothing was written anywhere, so invalidating on
// those would just be a pointless pair of live API calls against Shoptet and Flexi on every
// validation-style failure.
const FLEXI_WRITE_FAILED_ERROR_CODE = "ProductPriceFlexiWriteFailed";

export const useSetProductPrice = () => {
  const queryClient = useQueryClient();

  return useMutation<SetProductPriceResponse, Error, SetProductPriceInput>({
    mutationFn: setProductPrice,
    onSuccess: () => {
      // Reload from live Shoptet/Flexi data rather than trusting the local value.
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.divergence });
    },
    onError: (error) => {
      const errorCode = (error as { errorCode?: string })?.errorCode;
      if (errorCode === FLEXI_WRITE_FAILED_ERROR_CODE) {
        queryClient.invalidateQueries({ queryKey: QUERY_KEYS.divergence });
      }
    },
  });
};
