import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useCreateShipment } from "../useCreateShipment";
import * as clientModule from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
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
    baseUrl: "http://localhost:5001",
    http: { fetch: fetchMock },
  } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);
  return fetchMock;
};

describe("useCreateShipment", () => {
  it("returns label data on success", async () => {
    setFetch({
      json: async () => ({
        success: true,
        shipmentGuid: "abc-guid",
        labelReady: true,
        labels: [{ shipmentGuid: "abc-guid", packageName: "P1", labelUrl: "https://x.com/label.pdf" }],
        existingShipmentFound: false,
      }),
    });

    const { result } = renderHook(() => useCreateShipment(), { wrapper: createWrapper });
    result.current.mutate({ orderCode: "0001234", forceCreate: false });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.labelReady).toBe(true);
    expect(result.current.data?.labels).toHaveLength(1);
    expect(result.current.data?.existingShipmentFound).toBe(false);
  });

  it("returns existingShipmentFound=true for ShipmentAlreadyExists (no throw)", async () => {
    setFetch({
      json: async () => ({
        success: false,
        errorCode: "ShipmentAlreadyExists",
        labels: [{ shipmentGuid: "old-guid", packageName: "P1", labelUrl: "https://x.com/old.pdf" }],
        existingShipmentFound: true,
      }),
    });

    const { result } = renderHook(() => useCreateShipment(), { wrapper: createWrapper });
    result.current.mutate({ orderCode: "0001234", forceCreate: false });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.existingShipmentFound).toBe(true);
    expect(result.current.data?.labels).toHaveLength(1);
  });

  it("throws an error for ShipmentCarrierNotResolved", async () => {
    setFetch({
      json: async () => ({ success: false, errorCode: "ShipmentCarrierNotResolved" }),
    });

    const { result } = renderHook(() => useCreateShipment(), { wrapper: createWrapper });
    result.current.mutate({ orderCode: "0001234", forceCreate: false });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect((result.current.error as Error).message).toBe(
      "Dopravce se nepodařilo určit pro tuto objednávku"
    );
  });

  it("throws a generic error for unknown error codes", async () => {
    setFetch({
      json: async () => ({ success: false, errorCode: "SomeUnknownCode" }),
    });

    const { result } = renderHook(() => useCreateShipment(), { wrapper: createWrapper });
    result.current.mutate({ orderCode: "0001234", forceCreate: false });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect((result.current.error as Error).message).toBe("Zásilku se nepodařilo vytvořit");
  });

  it("sends POST request with correct URL and body", async () => {
    const fetchMock = setFetch({
      json: async () => ({
        success: true,
        shipmentGuid: "xyz-guid",
        labelReady: false,
        labels: [],
        existingShipmentFound: false,
      }),
    });

    const { result } = renderHook(() => useCreateShipment(), { wrapper: createWrapper });
    result.current.mutate({ orderCode: "0009999", forceCreate: true });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://localhost:5001/api/shipment-labels/create");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({ orderCode: "0009999", forceCreate: true });
  });
});
