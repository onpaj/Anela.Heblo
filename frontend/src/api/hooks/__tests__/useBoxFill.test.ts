import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useOpenOrResumeBox, useAddBoxItem, useRemoveBoxItem, useSendBoxToTransit } from "../useBoxFill";
import * as clientModule from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { manufacturedProductInventory: ["manufactured-product-inventory"], transportBox: ["transport-boxes"] },
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
  const fetchMock = jest.fn().mockResolvedValue(response);
  mockGetClient.mockReturnValue({
    baseUrl: "http://test",
    http: { fetch: fetchMock },
  } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);
  return fetchMock;
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

  it("useRemoveBoxItem issues a DELETE to the box/item URL", async () => {
    const fetchMock = setFetch({ ok: true, json: async () => ({ success: true }) });

    const { result } = renderHook(() => useRemoveBoxItem(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync({ boxId: 3, itemId: 9 });

    expect(res.success).toBe(true);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://test/api/transport-boxes/3/items/9");
    expect(init.method).toBe("DELETE");
  });

  it("useSendBoxToTransit PUTs the InTransit state", async () => {
    const fetchMock = setFetch({ ok: true, json: async () => ({ success: true }) });

    const { result } = renderHook(() => useSendBoxToTransit(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync(5);

    expect(res.success).toBe(true);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://test/api/transport-boxes/5/state");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(init.body)).toEqual({ boxId: 5, newState: "InTransit" });
  });

  it("collapses a thrown network error to a failure result", async () => {
    mockGetClient.mockReturnValue({
      baseUrl: "http://test",
      http: { fetch: jest.fn().mockRejectedValue(new Error("network down")) },
    } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);

    const { result } = renderHook(() => useOpenOrResumeBox(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync("B001");

    expect(res.success).toBe(false);
  });
});
