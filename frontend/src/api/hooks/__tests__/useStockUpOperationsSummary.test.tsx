import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useStockUpOperationsSummary } from "../useStockUpOperations";
import { StockUpSourceType } from "../../generated/api-client";

const mockGetSummary = jest.fn().mockResolvedValue({ success: true });

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: () => ({
    stockUpOperations_GetSummary: mockGetSummary,
  }),
  QUERY_KEYS: { stockUpOperations: ["stock-up-operations"] },
}));

function makeWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe("useStockUpOperationsSummary", () => {
  beforeEach(() => {
    mockGetSummary.mockClear();
  });

  it("does NOT call the API when enabled is false", async () => {
    renderHook(
      () =>
        useStockUpOperationsSummary(StockUpSourceType.TransportBox, {
          enabled: false,
        }),
      { wrapper: makeWrapper() }
    );
    await new Promise((r) => setTimeout(r, 50));
    expect(mockGetSummary).not.toHaveBeenCalled();
  });

  it("calls the API when enabled defaults to true", async () => {
    renderHook(
      () => useStockUpOperationsSummary(StockUpSourceType.TransportBox),
      { wrapper: makeWrapper() }
    );
    await waitFor(() => expect(mockGetSummary).toHaveBeenCalledTimes(1));
  });
});
