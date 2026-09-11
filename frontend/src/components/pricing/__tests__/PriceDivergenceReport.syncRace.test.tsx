import React, { ReactNode } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import PriceDivergenceReport from "../PriceDivergenceReport";
import { getAuthenticatedApiClient } from "../../../api/client";
import {
  GetPriceDivergenceReportResponse,
  PriceDivergenceKind,
  SetProductPriceResponse,
  SyncProductPricesResponse,
} from "../../../api/generated/api-client";

jest.mock("../../../api/client", () => ({
  ...jest.requireActual("../../../api/client"),
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

const reportWith = (kind: PriceDivergenceKind, flexiPriceWithVat: number) =>
  GetPriceDivergenceReportResponse.fromJS({
    success: true,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 200,
        flexiPriceWithVat,
        differenceWithVat: flexiPriceWithVat - 200,
        differencePercent: 0,
        kind,
      },
    ],
    summary: {
      totalInScope: 1,
      inAgreementCount: kind === PriceDivergenceKind.InAgreement ? 1 : 0,
      flexiDiffersCount: kind === PriceDivergenceKind.FlexiDiffers ? 1 : 0,
      missingInShoptetCount: 0,
      missingInFlexiCount: 0,
      flexiPriceTypeUnknownCount: 0,
    },
  });

const syncResponse = SyncProductPricesResponse.fromJS({
  success: true,
  rows: [
    {
      productCode: "A",
      productName: "Alpha",
      shoptetPriceWithVat: 210,
      flexiPriceWithVat: 260,
      differenceWithVat: 50,
      differencePercent: 23.81,
      kind: PriceDivergenceKind.FlexiDiffers,
    },
  ],
});

/**
 * A price save invalidates the divergence query, which starts a whole-catalogue refetch
 * across two live systems — slow. If the operator syncs while that refetch is still in
 * flight, the sync's merged rows must survive: the refetch reads Flexi from its five-minute
 * cache, so letting it land last would replace the freshly force-reloaded prices with
 * exactly the stale ones the sync exists to defeat, silently.
 */
describe("PriceDivergenceReport sync racing an in-flight report refetch", () => {
  it("keeps the synced rows when a slower report refetch resolves afterwards", async () => {
    // Arrange
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    let resolveSlowRefetch: (value: GetPriceDivergenceReportResponse) => void = () => {};
    const slowRefetch = new Promise<GetPriceDivergenceReportResponse>((resolve) => {
      resolveSlowRefetch = resolve;
    });

    const productPricing_GetDivergenceReport = jest
      .fn()
      .mockResolvedValueOnce(reportWith(PriceDivergenceKind.InAgreement, 200))
      .mockReturnValueOnce(slowRefetch);
    const productPricing_SetPrice = jest
      .fn()
      .mockResolvedValue(SetProductPriceResponse.fromJS({ success: true, priceWithVat: 210 }));
    const productPricing_Sync = jest.fn().mockResolvedValue(syncResponse);

    mockGetAuthenticatedApiClient.mockReturnValue({
      productPricing_GetDivergenceReport,
      productPricing_SetPrice,
      productPricing_Sync,
    } as unknown as ReturnType<typeof getAuthenticatedApiClient>);

    const wrapper = ({ children }: { children: ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    render(<PriceDivergenceReport canWrite />, { wrapper });
    await screen.findByText("Alpha");

    // Act — save a price, which invalidates and starts the slow refetch...
    await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
    const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
    await userEvent.clear(input);
    await userEvent.type(input, "210");
    await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));
    await waitFor(() => expect(productPricing_GetDivergenceReport).toHaveBeenCalledTimes(2));

    // ...then sync while it is still in flight; the scoped sync comes back first.
    await userEvent.click(screen.getByTestId("sync-prices-button"));
    await waitFor(() =>
      expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Flexi se liší"),
    );

    // ...and only now does the stale, cache-served refetch land. Wait for the query to be
    // genuinely settled, not merely for the promise to have been resolved — asserting before
    // React Query has had the chance to apply the result would pass however this behaves.
    resolveSlowRefetch(reportWith(PriceDivergenceKind.InAgreement, 200));
    await waitFor(() => expect(queryClient.isFetching({ queryKey: ["product-pricing", "divergence"] })).toBe(0));

    // Assert — the synced state survives; it is not overwritten by the staler read.
    expect(productPricing_Sync).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Flexi se liší");
    expect(screen.getByTestId("divergence-kind-A")).not.toHaveTextContent("Ve shodě");
  });
});
