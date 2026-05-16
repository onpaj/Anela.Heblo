import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useOpenOrResumeBox, useAddBoxItem } from "../useBoxFill";
import * as clientModule from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { manufacturedProductInventory: ["manufactured-product-inventory"] },
}));

const mockGetClient = clientModule.getAuthenticatedApiClient as jest.MockedFunction<
  typeof clientModule.getAuthenticatedApiClient
>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const setFetch = (response: Partial<Response> & { json: () => Promise<unknown> }) => {
  mockGetClient.mockReturnValue({
    baseUrl: "http://test",
    http: { fetch: jest.fn().mockResolvedValue(response) },
  } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);
};

describe("useBoxFill", () => {
  it("useOpenOrResumeBox returns the parsed success body", async () => {
    setFetch({
      ok: true,
      json: async () => ({ success: true, resumed: true, transportBox: { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] } }),
    });

    const { result } = renderHook(() => useOpenOrResumeBox(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync("B001");

    expect(res.success).toBe(true);
    expect(res.resumed).toBe(true);
    expect(res.transportBox?.code).toBe("B001");
  });

  it("useAddBoxItem surfaces a failure body returned with HTTP 400", async () => {
    setFetch({
      ok: false,
      json: async () => ({ success: false, errorCode: 1404 }),
    });

    const { result } = renderHook(() => useAddBoxItem(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync({
      boxId: 1, productCode: "P-1", productName: "Product 1", amount: 2,
    });

    await waitFor(() => expect(res.success).toBe(false));
    expect(res.errorCode).toBe(1404);
  });
});
