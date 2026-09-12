import React, { ReactNode } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import PriceDivergenceReport from "../PriceDivergenceReport";
import { getAuthenticatedApiClient } from "../../../api/client";
import {
  GetPriceDivergenceReportResponse,
  PriceDivergenceKind,
  SwaggerException,
} from "../../../api/generated/api-client";

// Unlike PriceDivergenceReport.test.tsx, this file does NOT mock
// ../../../api/hooks/useProductPricing — it exercises the real
// usePriceDivergenceReport/useSetProductPrice hooks against one shared QueryClient, so a
// query invalidation triggered by a failed save actually refetches through this component,
// the same as it would in the browser. Only the generated API client is mocked.
jest.mock("../../../api/client", () => ({
  ...jest.requireActual("../../../api/client"),
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

// Built via the generated client's own `fromJS` (not `new GetPriceDivergenceReportResponse(...)`):
// this DTO extends BaseResponse, and constructing it directly loses every field beyond
// `success`/`errorCode`/`params` under this project's Babel class-fields transform (the
// subclass's own field initializers run right after `super()` and clobber whatever the base
// constructor's for-in copy just set). `fromJS` populates fields via `init()` after
// construction finishes, so it isn't affected. See memory/gotchas for the full writeup.
const inAgreementReport = GetPriceDivergenceReportResponse.fromJS({
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
  ],
  summary: {
    totalInScope: 1,
    inAgreementCount: 1,
    flexiDiffersCount: 0,
    missingInShoptetCount: 0,
    missingInFlexiCount: 0,
    flexiPriceTypeUnknownCount: 0,
  },
});

// What the report looks like once it refetches after the (failed) save: Shoptet now holds
// the new price the operator submitted, Flexi still holds the old one — the row is now
// genuinely divergent.
const divergentReport = GetPriceDivergenceReportResponse.fromJS({
  success: true,
  rows: [
    {
      productCode: "A",
      productName: "Alpha",
      shoptetPriceWithVat: 210,
      flexiPriceWithVat: 200,
      differenceWithVat: 10,
      differencePercent: 5,
      kind: PriceDivergenceKind.FlexiDiffers,
    },
  ],
  summary: {
    totalInScope: 1,
    inAgreementCount: 0,
    flexiDiffersCount: 1,
    missingInShoptetCount: 0,
    missingInFlexiCount: 0,
    flexiPriceTypeUnknownCount: 0,
  },
});

describe("PriceDivergenceReport refetch behavior", () => {
  let queryClient: QueryClient;
  let productPricing_GetDivergenceReport: jest.Mock;
  let productPricing_SetPrice: jest.Mock;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    productPricing_GetDivergenceReport = jest
      .fn()
      .mockResolvedValueOnce(inAgreementReport)
      .mockResolvedValueOnce(divergentReport);
    productPricing_SetPrice = jest.fn().mockRejectedValue(
      new SwaggerException(
        "An unexpected server error occurred.",
        409,
        JSON.stringify({ success: false, errorCode: "ProductPriceFlexiWriteFailed" }),
        {},
        null,
      ),
    );
    mockGetAuthenticatedApiClient.mockReturnValue({
      productPricing_GetDivergenceReport,
      productPricing_SetPrice,
    } as unknown as ReturnType<typeof getAuthenticatedApiClient>);
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it("replaces the stale in-agreement row with the true divergence once the Flexi write failure refetches", async () => {
    // Arrange
    render(<PriceDivergenceReport canWrite />, { wrapper });
    await screen.findByText("Alpha");
    expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Ve shodě");

    // Act — edit and save; Shoptet accepts the write but Flexi rejects it.
    await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
    const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
    await userEvent.clear(input);
    await userEvent.type(input, "210");
    await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

    // Assert — the persistent alert appears immediately...
    expect(await screen.findByRole("alert")).toHaveTextContent(/Flexi/);

    // ...and the row itself refetches and stops asserting agreement: it must not keep
    // showing "Ve shodě" once the true, now-divergent state has loaded.
    await waitFor(() =>
      expect(screen.getByTestId("divergence-kind-A")).toHaveTextContent("Flexi se liší"),
    );
    expect(screen.getByTestId("divergence-kind-A")).not.toHaveTextContent("Ve shodě");

    // The refetch was actually driven by the failed save, not a coincidental extra poll.
    expect(productPricing_GetDivergenceReport).toHaveBeenCalledTimes(2);

    // The alert stays visible after the refetch — it explains *why* the row changed, the
    // refetched row explains *what* changed. Neither replaces the other.
    expect(screen.getByRole("alert")).toHaveTextContent(/Flexi/);
  });
});
