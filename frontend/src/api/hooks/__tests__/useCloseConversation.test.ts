import React from "react";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useCloseConversation } from "../useSmartsupp";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { smartsupp: ["smartsupp"] },
}));

const mockCloseConversation = jest.fn();

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  jest.clearAllMocks();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    smartsupp_CloseConversation: mockCloseConversation,
  });
});

describe("useCloseConversation", () => {
  it("calls the typed client with the conversation id", async () => {
    mockCloseConversation.mockResolvedValue({ success: true });

    const { result } = renderHook(() => useCloseConversation(), { wrapper });

    act(() => {
      result.current.mutate("conv-1");
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockCloseConversation).toHaveBeenCalledWith("conv-1");
  });

  it("sets error message when the typed response carries SmartsuppCloseConversationUnavailable", async () => {
    mockCloseConversation.mockResolvedValue({
      success: false,
      errorCode: "SmartsuppCloseConversationUnavailable",
    });

    const { result } = renderHook(() => useCloseConversation(), { wrapper });

    act(() => {
      result.current.mutate("conv-1");
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error?.message).toContain("nedostupná");
  });

  it("sets a generic error message when the call throws (untyped 404/503)", async () => {
    mockCloseConversation.mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useCloseConversation(), { wrapper });

    act(() => {
      result.current.mutate("conv-2");
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error?.message).toBeTruthy();
  });
});
