import { renderHook, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useProductStatistics } from "../useProductStatistics";
import { ProductStatisticsMetric } from "../../generated/api-client";

const mockGetProductStatistics = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: () => ({
    catalog_GetProductStatistics: mockGetProductStatistics,
  }),
  QUERY_KEYS: { catalog: ["catalog"] },
}));

const wrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("useProductStatistics", () => {
  beforeEach(() => {
    mockGetProductStatistics.mockReset();
  });

  test("does not fetch when no products are selected", () => {
    const { result } = renderHook(
      () =>
        useProductStatistics(
          [],
          ProductStatisticsMetric.Sales,
          "2025-01",
          "2025-06",
        ),
      { wrapper },
    );

    expect(result.current.fetchStatus).toBe("idle");
    expect(mockGetProductStatistics).not.toHaveBeenCalled();
  });

  test("does not fetch when the range is inverted", () => {
    const { result } = renderHook(
      () =>
        useProductStatistics(
          ["PROD-A"],
          ProductStatisticsMetric.Sales,
          "2025-06",
          "2025-01",
        ),
      { wrapper },
    );

    expect(result.current.fetchStatus).toBe("idle");
    expect(mockGetProductStatistics).not.toHaveBeenCalled();
  });

  test("fetches and returns the response when inputs are valid", async () => {
    mockGetProductStatistics.mockResolvedValue({
      months: ["2025-01"],
      products: [{ productCode: "PROD-A", productName: "Krém", values: [5] }],
    });

    const { result } = renderHook(
      () =>
        useProductStatistics(
          ["PROD-A"],
          ProductStatisticsMetric.Sales,
          "2025-01",
          "2025-01",
        ),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetProductStatistics).toHaveBeenCalledTimes(1);
    expect(result.current.data?.products).toHaveLength(1);
  });

  test("refetches when the metric changes", async () => {
    mockGetProductStatistics.mockResolvedValue({ months: [], products: [] });

    const { rerender, result } = renderHook(
      ({ metric }: { metric: ProductStatisticsMetric }) =>
        useProductStatistics(["PROD-A"], metric, "2025-01", "2025-01"),
      {
        wrapper,
        initialProps: { metric: ProductStatisticsMetric.Sales },
      },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    rerender({ metric: ProductStatisticsMetric.Purchase });

    await waitFor(() => expect(mockGetProductStatistics).toHaveBeenCalledTimes(2));
  });
});
