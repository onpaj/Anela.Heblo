import { renderHook, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  useProductStatistics,
  getMonthRangeError,
  isValidMonthRange,
  HISTORY_FLOOR_MONTH,
  MAX_RANGE_MONTHS,
} from "../useProductStatistics";
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

describe("getMonthRangeError", () => {
  test("returns null for a range inside the supported window", () => {
    expect(getMonthRangeError("2024-01", "2024-12")).toBeNull();
  });

  test("returns null when the range is a single month", () => {
    expect(getMonthRangeError("2024-03", "2024-03")).toBeNull();
  });

  test.each(["2025-1", "2025-13", "202-01", "", "03/2025"])(
    "rejects the malformed month %s",
    (month) => {
      expect(getMonthRangeError(month, "2025-06")).toBe(
        "Zadejte období ve formátu RRRR-MM.",
      );
    },
  );

  test("rejects an inverted range", () => {
    expect(getMonthRangeError("2025-06", "2025-01")).toBe(
      'Datum "Od" musí být dříve než "Do".',
    );
  });

  test("rejects a range ending before the first month with history", () => {
    expect(getMonthRangeError("2015-01", "2019-12")).toBe(
      `Data jsou k dispozici až od ${HISTORY_FLOOR_MONTH}.`,
    );
  });

  test("accepts a range ending exactly at the first month with history", () => {
    expect(getMonthRangeError("2019-01", HISTORY_FLOOR_MONTH)).toBeNull();
  });

  test("accepts a span of exactly the maximum months", () => {
    // 2020-01..2029-12 inclusive is 120 months.
    expect(getMonthRangeError("2020-01", "2029-12")).toBeNull();
  });

  test("rejects a span one month past the maximum", () => {
    expect(getMonthRangeError("2020-01", "2030-01")).toBe(
      `Rozsah nesmí přesáhnout ${MAX_RANGE_MONTHS} měsíců.`,
    );
  });

  test("isValidMonthRange agrees with getMonthRangeError", () => {
    expect(isValidMonthRange("2024-01", "2024-12")).toBe(true);
    expect(isValidMonthRange("2025-1", "2025-06")).toBe(false);
    expect(isValidMonthRange("2020-01", "2030-01")).toBe(false);
  });
});
