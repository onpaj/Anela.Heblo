import React from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSetNd } from "../useNd";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { catalog: ["catalog"] },
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.Mock;
const catalog_SetNd = jest.fn();

function createWrapper(queryClient: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe("useSetNd", () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { mutations: { retry: false } },
    });
    mockGetAuthenticatedApiClient.mockResolvedValue({ catalog_SetNd });
  });

  it("sends the product code and flag to the API", async () => {
    catalog_SetNd.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useSetNd(), {
      wrapper: createWrapper(queryClient),
    });

    act(() => {
      result.current.mutate({ productCode: "ABC", isNd: true });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(catalog_SetNd).toHaveBeenCalledTimes(1);
    const [productCode, request] = catalog_SetNd.mock.calls[0];
    expect(productCode).toBe("ABC");
    expect(request).toMatchObject({ productCode: "ABC", isNd: true });
  });

  it("refreshes catalog and manufacturing stock analysis data after saving", async () => {
    catalog_SetNd.mockResolvedValue({ success: true });
    const invalidateQueries = jest.spyOn(queryClient, "invalidateQueries");
    const { result } = renderHook(() => useSetNd(), {
      wrapper: createWrapper(queryClient),
    });

    act(() => {
      result.current.mutate({ productCode: "ABC", isNd: false });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ["catalog"] });
    expect(invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["manufacturing-stock-analysis"],
    });
  });

  it("does not refresh data and reports the error when saving fails", async () => {
    catalog_SetNd.mockRejectedValue(new Error("Flexi unavailable"));
    const invalidateQueries = jest.spyOn(queryClient, "invalidateQueries");
    const { result } = renderHook(() => useSetNd(), {
      wrapper: createWrapper(queryClient),
    });

    act(() => {
      result.current.mutate({ productCode: "ABC", isNd: true });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(invalidateQueries).not.toHaveBeenCalled();
  });
});
