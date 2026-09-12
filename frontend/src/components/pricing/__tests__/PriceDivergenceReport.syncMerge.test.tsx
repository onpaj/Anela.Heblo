import React, { ReactNode } from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import PriceDivergenceReport from "../PriceDivergenceReport";
import { getAuthenticatedApiClient } from "../../../api/client";
import {
  GetPriceDivergenceReportResponse,
  PriceDivergenceKind,
  SwaggerException,
  SyncProductPricesResponse,
} from "../../../api/generated/api-client";

// Like PriceDivergenceReport.refetch.test.tsx, this file does NOT mock the hooks module: it
// drives the real useSyncProductPrices against one shared QueryClient, so the cache merge
// that turns a sync response into rendered rows is actually exercised. Only the generated
// API client is mocked.
jest.mock("../../../api/client", () => ({
  ...jest.requireActual("../../../api/client"),
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

// `fromJS`, never the constructor — these DTOs extend BaseResponse and lose their own fields
// when constructed directly under this project's class-fields transform.
const initialReport = GetPriceDivergenceReportResponse.fromJS({
  success: true,
  rows: [
    {
      productCode: "A",
      productName: "Alpha",
      shoptetPriceWithVat: 200,
      flexiPriceWithVat: 200,
      differenceWithVat: 0,
      differencePercent: 0,
      kind: PriceDivergenceKind.InAgreement,
    },
    {
      productCode: "B",
      productName: "Beta",
      shoptetPriceWithVat: 300,
      flexiPriceWithVat: 300,
      differenceWithVat: 0,
      differencePercent: 0,
      kind: PriceDivergenceKind.InAgreement,
    },
  ],
  summary: {
    totalInScope: 2,
    inAgreementCount: 2,
    flexiDiffersCount: 0,
    missingInShoptetCount: 0,
    missingInFlexiCount: 0,
    flexiPriceTypeUnknownCount: 0,
  },
});

// Someone changed Alpha's price in Flexi since the report loaded; the sync is how the
// operator finds out.
const syncResponse = SyncProductPricesResponse.fromJS({
  success: true,
  rows: [
    {
      productCode: "A",
      productName: "Alpha",
      shoptetPriceWithVat: 200,
      flexiPriceWithVat: 250,
      differenceWithVat: 50,
      differencePercent: 25,
      kind: PriceDivergenceKind.FlexiDiffers,
    },
  ],
});

describe("PriceDivergenceReport sync merge", () => {
  let queryClient: QueryClient;
  let productPricing_GetDivergenceReport: jest.Mock;
  let productPricing_Sync: jest.Mock;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    productPricing_GetDivergenceReport = jest.fn().mockResolvedValue(initialReport);
    productPricing_Sync = jest.fn().mockResolvedValue(syncResponse);
    mockGetAuthenticatedApiClient.mockReturnValue({
      productPricing_GetDivergenceReport,
      productPricing_Sync,
      productPricing_SetPrice: jest.fn(),
    } as unknown as ReturnType<typeof getAuthenticatedApiClient>);
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it("shows the synced row's new state and re-tallies the tiles, without refetching the whole report", async () => {
    // Arrange
    render(<PriceDivergenceReport canWrite />, { wrapper });
    await screen.findByText("Alpha");
    expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Ve shodě");

    // Act — filter to Alpha, then sync just that one product
    await userEvent.type(screen.getByPlaceholderText("Kód produktu..."), "A{Enter}");
    await userEvent.click(screen.getByTestId("sync-prices-button"));

    // Assert — the synced row now reports the divergence the live ERP holds...
    await waitFor(() =>
      expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Flexi se liší"),
    );
    expect(productPricing_Sync).toHaveBeenCalledTimes(1);
    expect(productPricing_Sync.mock.calls[0][0].productCodes).toEqual(["A"]);

    // ...the tiles agree with it rather than still counting the pre-sync state...
    const summary = within(screen.getByTestId("divergence-summary"));
    expect(summary.getByTestId("summary-flexi-differs")).toHaveTextContent("1");
    expect(summary.getByTestId("summary-in-agreement")).toHaveTextContent("1");
    expect(summary.getByTestId("summary-total-in-scope")).toHaveTextContent("2");

    // ...and Beta, which the filter excluded, was neither re-read nor disturbed.
    expect(productPricing_GetDivergenceReport).toHaveBeenCalledTimes(1);
    await userEvent.clear(screen.getByPlaceholderText("Kód produktu..."));
    await userEvent.type(screen.getByPlaceholderText("Kód produktu..."), "{Enter}");
    expect(screen.getByTestId("divergence-kind-B")).toHaveTextContent("Ve shodě");
  });

  // The generated client throws a SwaggerException carrying the raw transport error, which
  // is not a sentence anyone should be shown. This is the only test that exercises the real
  // hook's catch, so it is the only place the message the operator reads is pinned down.
  it("shows a readable message and leaves the report untouched when the sync fails", async () => {
    // Arrange
    productPricing_Sync.mockRejectedValue(
      new SwaggerException("An unexpected server error occurred.", 500, "", {}, null),
    );
    render(<PriceDivergenceReport canWrite />, { wrapper });
    await screen.findByText("Alpha");

    // Act
    await userEvent.click(screen.getByTestId("sync-prices-button"));

    // Assert
    expect(await screen.findByRole("alert")).toHaveTextContent("Ceny se nepodařilo synchronizovat.");
    expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Ve shodě");
    expect(screen.getByTestId("divergence-kind-B")).toHaveTextContent("Ve shodě");
  });
});
