import React, { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSetProductPrice } from "../useProductPricing";
import { getAuthenticatedApiClient } from "../../client";
import { SwaggerException, SetProductPriceResponse } from "../../generated/api-client";

// Mock the API client module but preserve QUERY_KEYS and other real exports.
jest.mock("../../client", () => ({
  ...jest.requireActual("../../client"),
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

const DIVERGENCE_QUERY_KEY = ["product-pricing", "divergence"];

// Mirrors how the generated client actually fails: any non-200 status throws a
// SwaggerException whose `.response` carries the raw JSON envelope.
const rejectionWithErrorCode = (errorCode: string) =>
  new SwaggerException(
    "An unexpected server error occurred.",
    409,
    JSON.stringify({ success: false, errorCode }),
    {},
    null,
  );

describe("useSetProductPrice", () => {
  let queryClient: QueryClient;
  let productPricing_SetPrice: jest.Mock;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    productPricing_SetPrice = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      productPricing_SetPrice,
    } as unknown as ReturnType<typeof getAuthenticatedApiClient>);
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it("invalidates the divergence report after a successful save", async () => {
    // Arrange — built via `fromJS`, not the constructor: `SetProductPriceResponse` extends
    // BaseResponse, and constructing it directly loses `priceWithVat` under this project's
    // Babel class-fields transform (see PriceDivergenceReport.refetch.test.tsx for the full
    // writeup).
    productPricing_SetPrice.mockResolvedValue(
      SetProductPriceResponse.fromJS({ success: true, priceWithVat: 210 }),
    );
    const invalidateSpy = jest.spyOn(queryClient, "invalidateQueries");

    // Act
    const { result } = renderHook(() => useSetProductPrice(), { wrapper });
    result.current.mutate({ productCode: "A", priceWithVat: 210 });

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: DIVERGENCE_QUERY_KEY });
  });

  it("invalidates the divergence report when Shoptet was written but the Flexi write failed", async () => {
    // Arrange — Shoptet WAS updated for this error code even though the mutation rejects,
    // so the report must refetch instead of continuing to show the stale (and now false)
    // "in agreement" row.
    productPricing_SetPrice.mockRejectedValue(rejectionWithErrorCode("ProductPriceFlexiWriteFailed"));
    const invalidateSpy = jest.spyOn(queryClient, "invalidateQueries");

    // Act
    const { result } = renderHook(() => useSetProductPrice(), { wrapper });
    result.current.mutate({ productCode: "A", priceWithVat: 210 });

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: DIVERGENCE_QUERY_KEY });
  });

  it.each([
    "ProductPriceNotFoundInShoptet",
    "ProductPriceFlexiItemIdUnknown",
    "ProductPriceFlexiPriceTypeUnsupported",
    "ProductPriceShoptetWriteFailed",
  ])(
    "does not invalidate the divergence report when nothing was written anywhere (%s)",
    async (errorCode) => {
      // Arrange
      productPricing_SetPrice.mockRejectedValue(rejectionWithErrorCode(errorCode));
      const invalidateSpy = jest.spyOn(queryClient, "invalidateQueries");

      // Act
      const { result } = renderHook(() => useSetProductPrice(), { wrapper });
      result.current.mutate({ productCode: "A", priceWithVat: 210 });

      // Assert
      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(invalidateSpy).not.toHaveBeenCalled();
    },
  );
});
