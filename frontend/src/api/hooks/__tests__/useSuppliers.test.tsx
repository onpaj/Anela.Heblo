import React, { ReactNode } from "react";
import { renderHook, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSupplierSearch } from "../useSuppliers";

import * as clientModule from "../../client";

const mockApiClient = {
  suppliers_SearchSuppliers: jest.fn(),
};

jest.mock("../../client");

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe("useSupplierSearch", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(
      mockApiClient,
    );
  });

  it("does not fetch and returns an empty list for terms shorter than 2 characters", async () => {
    const { result } = renderHook(() => useSupplierSearch("a"), {
      wrapper: createWrapper(),
    });

    // Wait past the debounce window to confirm no fetch is ever triggered.
    await act(() => new Promise((resolve) => setTimeout(resolve, 400)));

    expect(mockApiClient.suppliers_SearchSuppliers).not.toHaveBeenCalled();
    expect(result.current.suppliers).toEqual([]);
    expect(result.current.isLoading).toBe(false);
  });

  it("debounces the search term before fetching", async () => {
    mockApiClient.suppliers_SearchSuppliers.mockResolvedValue({
      suppliers: [{ id: 1, name: "Acme" }],
    });

    const { result, rerender } = renderHook(
      ({ term }) => useSupplierSearch(term),
      { wrapper: createWrapper(), initialProps: { term: "" } },
    );

    rerender({ term: "acm" });

    // Immediately after the keystroke, the request must not have fired yet.
    expect(mockApiClient.suppliers_SearchSuppliers).not.toHaveBeenCalled();

    await waitFor(
      () =>
        expect(
          mockApiClient.suppliers_SearchSuppliers,
        ).toHaveBeenCalledTimes(1),
      { timeout: 1000 },
    );
    expect(mockApiClient.suppliers_SearchSuppliers).toHaveBeenCalledWith(
      "acm",
      10,
    );
    await waitFor(() =>
      expect(result.current.suppliers).toEqual([{ id: 1, name: "Acme" }]),
    );
  });

  it("clears suppliers immediately when the raw term drops below 2 characters, without waiting for the debounce", async () => {
    mockApiClient.suppliers_SearchSuppliers.mockResolvedValue({
      suppliers: [{ id: 1, name: "Acme" }],
    });

    const { result, rerender } = renderHook(
      ({ term }) => useSupplierSearch(term),
      { wrapper: createWrapper(), initialProps: { term: "ac" } },
    );

    await waitFor(() =>
      expect(result.current.suppliers).toEqual([{ id: 1, name: "Acme" }]),
    );

    rerender({ term: "a" });

    expect(result.current.suppliers).toEqual([]);
    expect(result.current.isLoading).toBe(false);
  });

  it("does not re-fetch for a repeated identical search term (cache de-duplication)", async () => {
    mockApiClient.suppliers_SearchSuppliers.mockResolvedValue({
      suppliers: [{ id: 1, name: "Acme" }],
    });

    const { rerender } = renderHook(({ term }) => useSupplierSearch(term), {
      wrapper: createWrapper(),
      initialProps: { term: "acme" },
    });

    await waitFor(() =>
      expect(
        mockApiClient.suppliers_SearchSuppliers,
      ).toHaveBeenCalledTimes(1),
    );

    rerender({ term: "" });
    rerender({ term: "acme" });

    // Wait past the debounce window; the identical term should be served from cache.
    await act(() => new Promise((resolve) => setTimeout(resolve, 400)));

    expect(mockApiClient.suppliers_SearchSuppliers).toHaveBeenCalledTimes(1);
  });

  it("surfaces query errors via the error field with a fallback message", async () => {
    mockApiClient.suppliers_SearchSuppliers.mockRejectedValue(
      new Error("boom"),
    );

    const { result } = renderHook(() => useSupplierSearch("acme"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.error).toBe("boom"));
  });
});
