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
      .mockReturnValueOnce(slowRefetch)
      // The make-good refetch the sync schedules because it cancelled one. It reads Flexi's
      // cache AFTER the sync force-reloaded it, so it agrees with the synced rows.
      .mockResolvedValue(reportWith(PriceDivergenceKind.FlexiDiffers, 260));
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

  /**
   * The mirror of the case above, and the one the cancel alone gets wrong. Here the operator
   * saves WHILE the sync is in flight, so the save's refetch carries a price the sync's
   * backend read never saw. Cancelling it and marking the merged report fresh would hide the
   * operator's own write for the whole five-minute staleTime — no remount and no window focus
   * would correct it, because the cache believes it is current.
   */
  it("does not strand a save that landed while the sync was in flight", async () => {
    // Arrange
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    let resolveSync: (value: SyncProductPricesResponse) => void = () => {};
    const pendingSync = new Promise<SyncProductPricesResponse>((resolve) => {
      resolveSync = resolve;
    });

    // The save's refetch never resolves on its own: it has to be genuinely in flight when the
    // sync cancels it, otherwise it would land first and the assertion below would hold
    // however the hook behaves.
    const neverResolves = new Promise<GetPriceDivergenceReportResponse>(() => {});

    const productPricing_GetDivergenceReport = jest
      .fn()
      .mockResolvedValueOnce(reportWith(PriceDivergenceKind.InAgreement, 200))
      .mockReturnValueOnce(neverResolves)
      // Only the make-good refetch can produce this: it is the third call, and it reads the
      // price the operator actually saved.
      .mockResolvedValue(reportWith(PriceDivergenceKind.FlexiDiffers, 999));
    const productPricing_SetPrice = jest
      .fn()
      .mockResolvedValue(SetProductPriceResponse.fromJS({ success: true, priceWithVat: 210 }));
    const productPricing_Sync = jest.fn().mockReturnValue(pendingSync);

    mockGetAuthenticatedApiClient.mockReturnValue({
      productPricing_GetDivergenceReport,
      productPricing_SetPrice,
      productPricing_Sync,
    } as unknown as ReturnType<typeof getAuthenticatedApiClient>);

    const wrapper = ({ children }: { children: ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    render(<PriceDivergenceReport canWrite />, { wrapper });
    await screen.findByText("Alpha");

    // Act — start the sync, and while it hangs, save a price. The save's invalidate starts the
    // refetch that the sync's onSuccess is about to cancel.
    await userEvent.click(screen.getByTestId("sync-prices-button"));
    await waitFor(() => expect(productPricing_Sync).toHaveBeenCalledTimes(1));

    await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
    const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
    await userEvent.clear(input);
    await userEvent.type(input, "210");
    await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));
    await waitFor(() => expect(productPricing_GetDivergenceReport).toHaveBeenCalledTimes(2));

    // ...now the sync resolves, carrying rows read BEFORE that save.
    resolveSync(syncResponse);
    await waitFor(() => expect(queryClient.isFetching({ queryKey: ["product-pricing", "divergence"] })).toBe(0));

    // Assert — the cancelled refetch is made good, so the report ends on the post-save truth
    // (999) rather than on the sync's pre-save rows (260 Kc), which the cache would otherwise
    // treat as fresh for five minutes with nothing left to correct it.
    expect(productPricing_GetDivergenceReport).toHaveBeenCalledTimes(3);
    await waitFor(() =>
      expect(screen.getByTestId("divergence-flexi-price-A")).toHaveTextContent("999"),
    );
  });
});
