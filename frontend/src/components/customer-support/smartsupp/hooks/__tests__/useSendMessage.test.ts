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

  it("shows generic error message when API returns non-JSON error body", async () => {
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: "http://api.test",
      http: {
        fetch: jest.fn().mockResolvedValue({
          ok: false,
          status: 503,
          json: async () => { throw new SyntaxError("Unexpected token"); },
        }),
      },
    });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toBe("Nepodařilo se odeslat zprávu.");
  });

  it("does nothing when conversationId is null", async () => {
    const { result } = renderHook(() => useSendMessage(null), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("rolls back optimistic update when API call fails", async () => {
    setApiResponse(503, { success: false, errorCode: "SmartsuppSendMessageUnavailable" });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const existingMessages = [
      { id: "existing-1", authorType: "contact", content: "Existující zpráva", createdAt: "2026-01-01T00:00:00Z", isFirstReply: false },
    ];
    queryClient.setQueryData(["smartsupp", "conversation", "conv1"], {
      success: true,
      messages: existingMessages,
    });

    function seededWrapper({ children }: { children: React.ReactNode }) {
      return React.createElement(QueryClientProvider, { client: queryClient }, children);
    }

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper: seededWrapper });
    act(() => result.current.send("Zpráva, která selže"));

    await waitFor(() => expect(result.current.error).not.toBeNull());

    const cached = queryClient.getQueryData<{ messages: unknown[] }>(["smartsupp", "conversation", "conv1"]);
    expect(cached?.messages).toHaveLength(1);
    expect(cached?.messages[0]).toMatchObject({ id: "existing-1" });
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

  it("shows optimistic message with pending delivery status while request is in flight", async () => {
    let resolvePromise!: (v: unknown) => void;
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: "http://api.test",
      http: {
        fetch: () => new Promise((res) => { resolvePromise = res; }),
      },
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    queryClient.setQueryData(["smartsupp", "conversation", "conv1"], {
      success: true,
      messages: [],
    });

    function seededWrapper({ children }: { children: React.ReactNode }) {
      return React.createElement(QueryClientProvider, { client: queryClient }, children);
    }

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper: seededWrapper });
    act(() => result.current.send("Ahoj"));

    await waitFor(() => {
      const cached = queryClient.getQueryData<{ messages: Array<{ id: string; deliveryStatus?: string; content?: string }> }>(
        ["smartsupp", "conversation", "conv1"],
      );
      expect(cached?.messages).toHaveLength(1);
    });

    const cached = queryClient.getQueryData<{ messages: Array<{ id: string; deliveryStatus?: string; content?: string }> }>(
      ["smartsupp", "conversation", "conv1"],
    );
    expect(cached?.messages[0].id).toMatch(/^optimistic-/);
    expect(cached?.messages[0].deliveryStatus).toBe("pending");
    expect(cached?.messages[0].content).toBe("Ahoj");

    // resolve so the hook can settle and avoid act() warnings
    resolvePromise({
      ok: true,
      status: 200,
      json: async () => ({ success: true, messageId: "ms999", createdAt: "2026-05-20T10:00:00Z" }),
    });
  });

  it("replaces optimistic message with real messageId and sent delivery status on success", async () => {
    setApiResponse(200, { success: true, messageId: "ms-real-123", createdAt: "2026-05-20T10:00:00Z" });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    queryClient.setQueryData(["smartsupp", "conversation", "conv1"], {
      success: true,
      messages: [
        {
          id: "existing-1",
          authorType: "contact",
          content: "Původní zpráva",
          createdAt: "2026-01-01T00:00:00Z",
          isFirstReply: false,
        },
      ],
    });

    function seededWrapper({ children }: { children: React.ReactNode }) {
      return React.createElement(QueryClientProvider, { client: queryClient }, children);
    }

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper: seededWrapper });
    act(() => result.current.send("Nová zpráva"));

    await waitFor(() => expect(result.current.justSent).toBe(true));

    const cached = queryClient.getQueryData<{
      messages: Array<{ id: string; content?: string | null; deliveryStatus?: string | null }>;
    }>(["smartsupp", "conversation", "conv1"]);

    expect(cached?.messages).toHaveLength(2);
    const sentMessage = cached?.messages.find((m) => m.id === "ms-real-123");
    expect(sentMessage).toMatchObject({
      content: "Nová zpráva",
      deliveryStatus: "sent",
    });
    expect(cached?.messages.some((m) => m.id.startsWith("optimistic-"))).toBe(false);
  });
});
