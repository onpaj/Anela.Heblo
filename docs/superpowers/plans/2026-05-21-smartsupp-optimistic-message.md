# Smartsupp Optimistic Message Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the operator's sent message appear instantly in the Smartsupp chat window with a pending spinner, switching to a checkmark on success and disappearing on failure.

**Architecture:** The optimistic update code already exists in `useSendMessage.ts` but is immediately destroyed by `onSettled` calling `invalidateQueries`, which triggers a refetch that returns stale data (the backend DB hasn't synced the new message from Smartsupp yet). The fix: return the send response from `mutationFn`, add `onSuccess` to promote the optimistic entry to a confirmed one, add `deliveryStatus: "pending"` to the optimistic message, and remove `onSettled` entirely — the 30-second `refetchInterval` in `useSmartsuppConversation` handles eventual sync.

**Tech Stack:** React, TanStack Query (React Query v5), TypeScript, Jest + React Testing Library

---

## Files

| Action | Path |
|--------|------|
| Modify | `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts` |
| Modify | `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts` |

No other files need changes. `MessageDeliveryIcon` already renders `"pending"` as a spinner and `"sent"` as a checkmark.

---

### Task 1: Write two failing tests

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts`

- [ ] **Step 1: Add the two new tests inside the existing `describe("useSendMessage")` block, after the last existing test**

  ```typescript
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

    await waitFor(() => expect(result.current.isPending).toBe(true));

    const cached = queryClient.getQueryData<{ messages: Array<{ id: string; deliveryStatus?: string }> }>(
      ["smartsupp", "conversation", "conv1"],
    );
    expect(cached?.messages).toHaveLength(1);
    expect(cached?.messages[0].id).toMatch(/^optimistic-/);
    expect(cached?.messages[0].deliveryStatus).toBe("pending");

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
    expect(cached?.messages[1]).toMatchObject({
      id: "ms-real-123",
      content: "Nová zpráva",
      deliveryStatus: "sent",
    });
    expect(cached?.messages.some((m) => m.id.startsWith("optimistic-"))).toBe(false);
  });
  ```

---

### Task 2: Run the new tests — expect FAIL

**Files:**
- Test: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts`

- [ ] **Step 1: Run only the new tests**

  ```bash
  cd frontend && npx jest --testPathPattern="useSendMessage" --watchAll=false --no-coverage
  ```

  Expected: the two new tests **fail** (the optimistic message won't have `deliveryStatus: "pending"` and `onSuccess` doesn't replace the ID yet). All 5 existing tests should still pass.

---

### Task 3: Implement the fix in `useSendMessage.ts`

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts`

Replace the entire file content with:

- [ ] **Step 1: Rewrite `useSendMessage.ts`**

  ```typescript
  import { useMutation, useQueryClient } from "@tanstack/react-query";
  import { getClientAndBaseUrl, apiPost } from "../../../../api/smartsuppClient";
  import {
    SMARTSUPP_QUERY_KEYS,
    type GetConversationResponse,
    type MessageDto,
  } from "../../../../api/hooks/useSmartsupp";

  interface SendMessageApiResponse {
    success: boolean;
    errorCode?: string;
    messageId?: string;
    createdAt?: string;
  }

  const SEND_ERROR_MESSAGES: Record<string, string> = {
    SmartsuppSendMessageUnavailable: "Nepodařilo se odeslat zprávu — služba je nedostupná. Zkuste to prosím znovu.",
    SmartsuppConversationNotFound: "Konverzace nebyla nalezena.",
  };

  function messageForSendError(code?: string): string {
    if (code && SEND_ERROR_MESSAGES[code]) return SEND_ERROR_MESSAGES[code];
    return "Nepodařilo se odeslat zprávu.";
  }

  interface UseSendMessageResult {
    send: (content: string) => void;
    isPending: boolean;
    error: string | null;
    justSent: boolean;
    clearSent: () => void;
  }

  type SendMessageContext = { previous?: GetConversationResponse };

  export function useSendMessage(conversationId: string | null): UseSendMessageResult {
    const queryClient = useQueryClient();

    const mutation = useMutation<SendMessageApiResponse, Error, string, SendMessageContext>({
      mutationFn: async (content) => {
        if (!conversationId) {
          throw new Error("Není vybrána konverzace.");
        }

        const { apiClient, baseUrl } = getClientAndBaseUrl();
        const response = await apiPost(
          apiClient,
          `${baseUrl}/api/smartsupp/conversations/${conversationId}/messages`,
          { content },
        );

        if (!response.ok) {
          const errData = await response.json().catch(() => ({})) as Partial<SendMessageApiResponse>;
          throw new Error(messageForSendError(errData?.errorCode));
        }

        const data = (await response.json()) as SendMessageApiResponse;
        if (!data.success) {
          throw new Error(messageForSendError(data?.errorCode));
        }

        return data;
      },
      onMutate: async (content) => {
        if (!conversationId) return {};
        await queryClient.cancelQueries({
          queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        });
        const previous = queryClient.getQueryData<GetConversationResponse>(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        );
        const optimisticMsg: MessageDto = {
          id: `optimistic-${Date.now()}`,
          authorType: "agent",
          content,
          createdAt: new Date().toISOString(),
          isFirstReply: false,
          deliveryStatus: "pending",
        };
        queryClient.setQueryData<GetConversationResponse>(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          (old) => (old ? { ...old, messages: [...old.messages, optimisticMsg] } : old),
        );
        return { previous };
      },
      onSuccess: (data) => {
        if (!conversationId) return;
        queryClient.setQueryData<GetConversationResponse>(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          (current) => {
            if (!current) return current;
            return {
              ...current,
              messages: current.messages.map((m) =>
                m.id.startsWith("optimistic-")
                  ? {
                      ...m,
                      id: data.messageId ?? m.id,
                      createdAt: data.createdAt ?? m.createdAt,
                      deliveryStatus: "sent",
                    }
                  : m,
              ),
            };
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
      send: (content: string) => mutation.mutate(content),
      isPending: mutation.isPending,
      error: mutation.error ? mutation.error.message : null,
      justSent: mutation.isSuccess,
      clearSent: mutation.reset,
    };
  }
  ```

  Key changes from the original:
  - `useMutation<void, …>` → `useMutation<SendMessageApiResponse, …>`
  - `mutationFn` now returns `data` instead of void
  - `optimisticMsg` gains `deliveryStatus: "pending"`
  - New `onSuccess` replaces the optimistic entry with real `messageId`/`createdAt` and `deliveryStatus: "sent"`
  - `onSettled` removed — 30 s `refetchInterval` handles sync

---

### Task 4: Run all tests — expect all pass

- [ ] **Step 1: Run the full test suite for this file**

  ```bash
  cd frontend && npx jest --testPathPattern="useSendMessage" --watchAll=false --no-coverage
  ```

  Expected output: **7 tests pass**, 0 failures.

  If a test fails:
  - "shows optimistic message…" failing → check `onMutate` adds `deliveryStatus: "pending"` and the seeded queryClient data is read correctly
  - "replaces optimistic message…" failing → check `onSuccess` maps correctly and `m.id.startsWith("optimistic-")` matches

---

### Task 5: Build + lint check

- [ ] **Step 1: TypeScript build**

  ```bash
  cd frontend && npm run build
  ```

  Expected: no errors. If TypeScript complains about `conversationId` being `string | null` passed to `SMARTSUPP_QUERY_KEYS.conversation` inside `onSuccess`, add a guard: `if (!conversationId) return;` (already present in the plan's code).

- [ ] **Step 2: Lint**

  ```bash
  cd frontend && npm run lint
  ```

  Expected: no errors.

---

### Task 6: Commit

- [ ] **Step 1: Stage and commit**

  ```bash
  git add frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts \
          frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts
  git commit -m "fix(smartsupp): show optimistic message instantly with delivery status

  The previous onSettled invalidateQueries immediately refetched from the
  backend DB, which does not contain the new message yet (Smartsupp syncs
  on polling). This wiped the optimistic entry before it was visible.

  Fix: return SendMessageApiResponse from mutationFn, promote the optimistic
  message to a confirmed one in onSuccess (real ID + deliveryStatus 'sent'),
  and remove onSettled — the 30 s refetchInterval handles eventual sync."
  ```
