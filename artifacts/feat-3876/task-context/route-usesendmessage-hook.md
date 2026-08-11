### task: route-usesendmessage-hook

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts`

#### Step 1: Rewrite `useSendMessage.ts`

Replace the entire file with:

```ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  SendMessageBody,
  GetConversationResponse,
  MessageDto,
  type SendMessageResponse,
} from "../../../../api/generated/api-client";
import { SMARTSUPP_QUERY_KEYS } from "../../../../api/hooks/useSmartsupp";

const SEND_ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppSendMessageUnavailable]:
    "Nepodařilo se odeslat zprávu — služba je nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForSendError(code?: ErrorCodes): string {
  if (code && SEND_ERROR_MESSAGES[code]) return SEND_ERROR_MESSAGES[code]!;
  return "Nepodařilo se odeslat zprávu.";
}

interface SendMessageVariables {
  content: string;
  /** Set when the message was composed from an AI draft, to link the sent text to that draft's log. */
  draftLogId?: string | null;
}

interface UseSendMessageResult {
  send: (content: string, draftLogId?: string | null) => void;
  isPending: boolean;
  error: string | null;
  justSent: boolean;
  clearSent: () => void;
}

type SendMessageContext = { previous?: GetConversationResponse; optimisticId?: string };

export function useSendMessage(conversationId: string | null): UseSendMessageResult {
  const queryClient = useQueryClient();

  const mutation = useMutation<SendMessageResponse, Error, SendMessageVariables, SendMessageContext>({
    mutationFn: async ({ content, draftLogId }) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      let data: SendMessageResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_SendMessage(
          conversationId,
          new SendMessageBody({ content, draftLogId: draftLogId ?? undefined }),
        );
      } catch {
        // 400/404/503 are all untyped ProducesResponseType on this controller action.
        throw new Error(messageForSendError(undefined));
      }
      if (!data.success) {
        throw new Error(messageForSendError(data.errorCode));
      }

      return data;
    },
    onMutate: async ({ content }) => {
      if (!conversationId) return {};
      await queryClient.cancelQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      const previous = queryClient.getQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      );
      const optimisticId = `optimistic-${Date.now()}`;
      const optimisticMsg = new MessageDto({
        id: optimisticId,
        authorType: "agent",
        content,
        createdAt: new Date(),
        isFirstReply: false,
        deliveryStatus: "pending",
      });
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (old) =>
          old
            ? new GetConversationResponse({
                ...old,
                messages: [...(old.messages ?? []), optimisticMsg],
              })
            : old,
      );
      return { previous, optimisticId };
    },
    onSuccess: (data, _variables, context) => {
      const optimisticId = context?.optimisticId;
      if (!conversationId || !optimisticId) return;
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (current) => {
          if (!current) return current;
          if (!data.messageId) {
            return new GetConversationResponse({
              ...current,
              messages: (current.messages ?? []).filter((m) => m.id !== optimisticId),
            });
          }
          return new GetConversationResponse({
            ...current,
            messages: (current.messages ?? []).map((m) =>
              m.id === optimisticId
                ? new MessageDto({
                    ...m,
                    id: data.messageId!,
                    createdAt: data.createdAt ?? m.createdAt,
                    deliveryStatus: "sent",
                  })
                : m,
            ),
          });
        },
      );
    },
    onError: (_err, _content, context) => {
      if (context?.previous !== undefined && conversationId) {
        queryClient.setQueryData(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          context.previous,
        );
      }
    },
  });

  return {
    send: (content: string, draftLogId?: string | null) =>
      mutation.mutate({ content, draftLogId }),
    isPending: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    justSent: mutation.isSuccess,
    clearSent: mutation.reset,
  };
}
```

Two things worth flagging explicitly since they're easy to get wrong:

- `GetConversationResponse` and `MessageDto` are imported here as **values** (not `type`-only), because they're constructed with `new` in the cache-update code — importing them `type`-only would compile-error at those call sites.
- Every cache-update spot that used to spread a plain object (`{...old, messages: [...]}`) is now wrapped in `new GetConversationResponse({...})` / `new MessageDto({...})`. This is required, not stylistic: `GetConversationResponse`/`MessageDto` are NSwag-generated classes with `init()`/`toJSON()` methods, and a plain object literal does not structurally satisfy a class type in TypeScript (confirmed by compiling both forms against this repo's `tsconfig.json` — the plain-spread form fails with "missing `init`, `toJSON`"). The generated class's constructor copies every property from the object you pass it (via `BaseResponse`'s constructor loop), so `new GetConversationResponse({...old, messages: [...]})` produces a real, correctly-populated instance.

#### Step 2: Rewrite `useSendMessage.test.ts`

Replace `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts` with:

```ts
import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSendMessage } from "../useSendMessage";
import { getAuthenticatedApiClient } from "../../../../../api/client";

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
```

#### Step 3: Run the test and full type-check

```bash
cd frontend
CI=true npx react-scripts test src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts --watchAll=false
npm run build
```

Both should be clean.

#### Step 4: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts \
  frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts
git commit -m "Route useSendMessage through the generated typed API client"
```

---
