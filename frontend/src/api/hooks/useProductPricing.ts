import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  PriceDivergenceRowDto,
  PriceDivergenceSummaryDto,
  PriceDivergenceKind,
} from "../generated/api-client";

export { PriceDivergenceRowDto, PriceDivergenceSummaryDto, PriceDivergenceKind };

const QUERY_KEYS = {
  divergence: ["product-pricing", "divergence"] as const,
};

// Read-only: fetches the divergence report and never mutates anything. There is no
// invalidation on any other mutation here on purpose — this view is a dry-run comparison,
// not part of any sync/write workflow.
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
