import React from "react";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useCloseConversation } from "../useSmartsupp";

const mockFetch = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: () => ({
    baseUrl: "http://localhost:5001",
    http: { fetch: mockFetch },
  }),
  QUERY_KEYS: {
    smartsupp: ["smartsupp"],
  },
}));

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  jest.clearAllMocks();
});

describe("useCloseConversation", () => {
  it("calls POST to the close endpoint with the conversation id", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ success: true }),
    });

    const { result } = renderHook(() => useCloseConversation(), { wrapper });

    act(() => {
      result.current.mutate("conv-1");
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFetch).toHaveBeenCalledWith(
      "http://localhost:5001/api/smartsupp/conversations/conv-1/close",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("sets error message when API returns non-ok with SmartsuppCloseConversationUnavailable", async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      json: async () => ({ errorCode: "SmartsuppCloseConversationUnavailable" }),
    });

    const { result } = renderHook(() => useCloseConversation(), { wrapper });

    act(() => {
      result.current.mutate("conv-1");
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error?.message).toContain("nedostupná");
  });

  it("sets generic error message when API returns non-ok with no error code", async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      json: async () => ({}),
    });

    const { result } = renderHook(() => useCloseConversation(), { wrapper });

    act(() => {
      result.current.mutate("conv-2");
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error?.message).toBeTruthy();
  });
});
