import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { readApiErrorEnvelope } from "../apiErrorEnvelope";
import {
  PriceDivergenceRowDto,
  PriceDivergenceSummaryDto,
  PriceDivergenceKind,
  SetProductPriceRequest,
  SetProductPriceResponse,
  SyncProductPricesRequest,
} from "../generated/api-client";
import { PriceDivergenceReportData, mergeSyncedRows } from "./priceDivergenceMerge";

export { PriceDivergenceRowDto, PriceDivergenceSummaryDto, PriceDivergenceKind };

const QUERY_KEYS = {
  divergence: ["product-pricing", "divergence"] as const,
};

// Fetches the divergence report. `useSetProductPrice` below invalidates this query on a
// successful write, and also on a `ProductPriceFlexiWriteFailed` partial failure, so an
// edited row reloads from live Shoptet/Flexi data instead of trusting the value the
// operator just typed or silently showing a stale "in agreement" state.
export const usePriceDivergenceReport = () =>
  useQuery<PriceDivergenceReportData>({
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

export const GENERIC_SYNC_ERROR = "Ceny se nepodařilo synchronizovat.";

/**
 * Re-reads Shoptet and Flexi for one selection of products and folds the fresh rows into the
 * cached report. A read: it writes to neither system, it only bypasses Flexi's five-minute
 * ceník cache so the operator sees the two systems as they stand right now.
 *
 * Deliberately `setQueryData` rather than `invalidateQueries` — invalidating would refetch
 * the whole catalogue from both live systems and throw away the very scoping the operator
 * asked for by filtering. The merged report is then marked fresh for the query's whole
 * `staleTime`, so rows outside the selection can be up to five minutes old while the cache
 * treats the report as current; syncing is how the operator refreshes what they care about,
 * a reload is how they refresh everything.
 */
export const useSyncProductPrices = () => {
  const queryClient = useQueryClient();

  return useMutation<PriceDivergenceRowDto[], Error, string[]>({
    mutationFn: async (productCodes) => {
      try {
        const response = await getAuthenticatedApiClient().productPricing_Sync(
          new SyncProductPricesRequest({ productCodes }),
        );
        return response.rows ?? [];
      } catch (error) {
        // The generated client throws a SwaggerException on any non-200, so its message is
        // the raw transport error; the operator gets a message they can read instead.
        const envelope = readApiErrorEnvelope(error);
        throw Object.assign(new Error(GENERIC_SYNC_ERROR), { errorCode: envelope?.errorCode });
      }
    },
    onSuccess: async (syncedRows) => {
      // A price save invalidates this query, and that whole-catalogue refetch across two live
      // systems is slow. React Query does not discard a resolved fetch because a
      // `setQueryData` happened meanwhile, so without this cancel the refetch lands last and
      // silently replaces the freshly force-reloaded prices with Flexi's five-minute cache —
      // exactly what the sync exists to defeat. The cancelled refetch is no loss: it would
      // carry staler data for the synced rows than what just arrived.
      await queryClient.cancelQueries({ queryKey: QUERY_KEYS.divergence });

      queryClient.setQueryData<PriceDivergenceReportData>(QUERY_KEYS.divergence, (current) =>
        current ? mergeSyncedRows(current, syncedRows) : current,
      );
    },
  });
};
