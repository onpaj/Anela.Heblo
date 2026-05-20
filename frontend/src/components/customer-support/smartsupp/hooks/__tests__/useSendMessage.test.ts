import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSendMessage } from "../useSendMessage";
import { getAuthenticatedApiClient } from "../../../../../api/client";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();

function setApiResponse(status: number, body: unknown): void {
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    baseUrl: "http://api.test",
    http: { fetch: mockFetch },
  });
  mockFetch.mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  });
}

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockFetch.mockReset();
  jest.restoreAllMocks();
});

describe("useSendMessage", () => {
  it("calls the correct endpoint and returns messageId on success", async () => {
    setApiResponse(200, { success: true, messageId: "ms123", createdAt: "2026-05-20T10:00:00Z" });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Dobrý den!"));

    await waitFor(() => expect(result.current.justSent).toBe(true));

    expect(mockFetch).toHaveBeenCalledWith(
      "http://api.test/api/smartsupp/conversations/conv1/messages",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ content: "Dobrý den!" }),
      }),
    );
  });

  it("sets error message on API failure", async () => {
    setApiResponse(503, { success: false, errorCode: "SmartsuppSendMessageUnavailable" });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo|nedostupn/i);
  });

  it("does nothing when conversationId is null", async () => {
    const { result } = renderHook(() => useSendMessage(null), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("isPending is true while request is in flight", async () => {
    let resolvePromise!: (v: unknown) => void;
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: "http://api.test",
      http: {
        fetch: () => new Promise((res) => { resolvePromise = res; }),
      },
    });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.isPending).toBe(true));
    resolvePromise({ ok: true, status: 200, json: async () => ({ success: true }) });
  });
});
