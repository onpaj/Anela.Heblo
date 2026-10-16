import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSendMessage } from "../useSendMessage";
import { getAuthenticatedApiClient } from "../../../../../api/client";
import { ErrorCodes, GetConversationResponse } from "../../../../../api/generated/api-client";
import { SMARTSUPP_QUERY_KEYS } from "../../../../../api/hooks/useSmartsupp";
import { getErrorMessage } from "../../../../../utils/errorHandler";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockSendMessage = jest.fn();

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockSendMessage.mockReset();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    smartsupp_SendMessage: mockSendMessage,
  });
});

describe("useSendMessage", () => {
  it("calls the typed client and returns messageId on success", async () => {
    mockSendMessage.mockResolvedValue({
      success: true,
      messageId: "ms123",
      createdAt: new Date("2026-05-20T10:00:00Z"),
    });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Dobrý den!"));

    await waitFor(() => expect(result.current.justSent).toBe(true));

    expect(mockSendMessage).toHaveBeenCalledWith(
      "conv1",
      expect.objectContaining({ content: "Dobrý den!" }),
    );
  });

  it("forwards draftLogId when the message came from an AI draft", async () => {
    mockSendMessage.mockResolvedValue({
      success: true,
      messageId: "ms123",
      createdAt: new Date("2026-05-20T10:00:00Z"),
    });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Upravený návrh", "log-abc"));

    await waitFor(() => expect(result.current.justSent).toBe(true));

    expect(mockSendMessage).toHaveBeenCalledWith(
      "conv1",
      expect.objectContaining({ content: "Upravený návrh", draftLogId: "log-abc" }),
    );
  });

  it("sets error message on typed API failure", async () => {
    mockSendMessage.mockResolvedValue({
      success: false,
      errorCode: "SmartsuppSendMessageUnavailable",
    });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo|nedostupn/i);
  });

  it("shows generic error message when the call throws (untyped 400/404/503)", async () => {
    mockSendMessage.mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toBe("Nepodařilo se odeslat zprávu.");
  });

  it("does nothing when conversationId is null", async () => {
    const { result } = renderHook(() => useSendMessage(null), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(mockSendMessage).not.toHaveBeenCalled();
  });

  it("rolls back optimistic update when API call fails", async () => {
    mockSendMessage.mockResolvedValue({
      success: false,
      errorCode: "SmartsuppSendMessageUnavailable",
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const existingMessages = [
      {
        id: "existing-1",
        authorType: "contact",
        content: "Existující zpráva",
        createdAt: "2026-01-01T00:00:00Z",
        isFirstReply: false,
      },
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
    mockSendMessage.mockReturnValue(new Promise((res) => { resolvePromise = res; }));

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.isPending).toBe(true));
    resolvePromise({ success: true, messageId: "ms1", createdAt: new Date() });
  });

  it("shows optimistic message with pending delivery status while request is in flight", async () => {
    let resolvePromise!: (v: unknown) => void;
    mockSendMessage.mockReturnValue(new Promise((res) => { resolvePromise = res; }));

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

    resolvePromise({ success: true, messageId: "ms999", createdAt: new Date("2026-05-20T10:00:00Z") });
  });

  it("replaces optimistic message with real messageId and sent delivery status on success", async () => {
    mockSendMessage.mockResolvedValue({
      success: true,
      messageId: "ms-real-123",
      createdAt: new Date("2026-05-20T10:00:00Z"),
    });

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

describe("useSendMessage with attachments", () => {
  const mockFetch = jest.fn();
  const createObjectURL = jest.fn();
  const revokeObjectURL = jest.fn();

  beforeEach(() => {
    mockFetch.mockReset();
    let urlCounter = 0;
    createObjectURL.mockReset().mockImplementation(() => `blob:test-url-${++urlCounter}`);
    revokeObjectURL.mockReset();
    (URL as any).createObjectURL = createObjectURL;
    (URL as any).revokeObjectURL = revokeObjectURL;
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: "https://api.test",
      http: { fetch: mockFetch },
      smartsupp_SendMessage: mockSendMessage,
    });
  });

  const files = () => [
    new File(["a"], "a.png", { type: "image/png" }),
    new File(["b"], "b.pdf", { type: "application/pdf" }),
  ];

  it("posts multipart to the absolute with-attachments URL without a Content-Type header", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ success: true, messageId: "m1", createdAt: "2026-05-20T10:00:00Z" }),
    });

    const { result } = renderHook(() => useSendMessage("c1"), { wrapper });
    act(() => result.current.send("Ahoj", "log-1", files()));

    await waitFor(() => expect(result.current.justSent).toBe(true));

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, init] = mockFetch.mock.calls[0];
    expect(url).toBe("https://api.test/api/smartsupp/conversations/c1/messages/with-attachments");
    expect(init.method).toBe("POST");
    expect(init.headers).toBeUndefined();
    const body = init.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect(body.get("content")).toBe("Ahoj");
    expect(body.get("draftLogId")).toBe("log-1");
    expect(body.getAll("files")).toHaveLength(2);
    expect(mockSendMessage).not.toHaveBeenCalled();
  });

  it("maps a 503 error code to its i18n text", async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({ errorCode: "SmartsuppAttachmentUploadFailed" }),
    });

    const { result } = renderHook(() => useSendMessage("c1"), { wrapper });
    act(() => result.current.send("", null, files()));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toBe(getErrorMessage(ErrorCodes.SmartsuppAttachmentUploadFailed));
    expect(result.current.justSent).toBe(false);
  });

  it("falls back to the generic message on a network exception", async () => {
    mockFetch.mockRejectedValue(new TypeError("network"));

    const { result } = renderHook(() => useSendMessage("c1"), { wrapper });
    act(() => result.current.send("x", null, files()));

    await waitFor(() => expect(result.current.error).toBe("Nepodařilo se odeslat zprávu."));
  });

  it("falls back to the generic message when the error body is not JSON", async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 502,
      json: async () => {
        throw new Error("not json");
      },
    });

    const { result } = renderHook(() => useSendMessage("c1"), { wrapper });
    act(() => result.current.send("x", null, files()));

    await waitFor(() => expect(result.current.error).toBe("Nepodařilo se odeslat zprávu."));
  });

  it("adds an optimistic message with blob attachments and revokes the URLs on rollback", async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const key = SMARTSUPP_QUERY_KEYS.conversation("c1");
    queryClient.setQueryData(key, GetConversationResponse.fromJS({ messages: [] }));
    const localWrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    let rejectFetch: (e: Error) => void = () => undefined;
    mockFetch.mockReturnValue(new Promise((_, reject) => (rejectFetch = reject)));

    const { result } = renderHook(() => useSendMessage("c1"), { wrapper: localWrapper });
    act(() => result.current.send("x", null, files()));

    await waitFor(() => {
      const data = queryClient.getQueryData<GetConversationResponse>(key);
      expect(data?.messages).toHaveLength(1);
    });
    const optimistic = queryClient.getQueryData<GetConversationResponse>(key)!.messages![0];
    expect(optimistic.id).toMatch(/^optimistic-/);
    expect(optimistic.attachments).toHaveLength(2);
    expect(optimistic.attachments![0]).toMatchObject({
      type: "image",
      fileName: "a.png",
      fileType: "image/png",
      url: "blob:test-url-1",
    });
    expect(optimistic.attachments![1].type).toBe("file");
    expect(revokeObjectURL).not.toHaveBeenCalled();

    act(() => rejectFetch(new Error("boom")));
    await waitFor(() => expect(result.current.error).not.toBeNull());

    expect(queryClient.getQueryData<GetConversationResponse>(key)?.messages).toHaveLength(0);
    expect(revokeObjectURL).toHaveBeenCalledTimes(2);
  });

  it("keeps the attachments on the swapped message and revokes nothing until unmount", async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const key = SMARTSUPP_QUERY_KEYS.conversation("c1");
    queryClient.setQueryData(key, GetConversationResponse.fromJS({ messages: [] }));
    const invalidate = jest.spyOn(queryClient, "invalidateQueries");
    const localWrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ success: true, messageId: "m1" }),
    });

    const { result, unmount } = renderHook(() => useSendMessage("c1"), { wrapper: localWrapper });
    act(() => result.current.send("x", null, files()));

    await waitFor(() => expect(result.current.justSent).toBe(true));
    const sent = queryClient.getQueryData<GetConversationResponse>(key)!.messages![0];
    expect(sent.id).toBe("m1");
    expect(sent.attachments).toHaveLength(2);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: key });
    expect(revokeObjectURL).not.toHaveBeenCalled();

    unmount();
    expect(revokeObjectURL).toHaveBeenCalledTimes(2);
  });

  it("revokes pending optimistic URLs on unmount", async () => {
    mockFetch.mockReturnValue(new Promise(() => undefined));

    const { result, unmount } = renderHook(() => useSendMessage("c1"), { wrapper });
    act(() => result.current.send("x", null, files()));
    await waitFor(() => expect(createObjectURL).toHaveBeenCalledTimes(2));

    unmount();
    expect(revokeObjectURL).toHaveBeenCalledTimes(2);
  });
});
