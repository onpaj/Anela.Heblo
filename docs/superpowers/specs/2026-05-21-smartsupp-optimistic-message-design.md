# Smartsupp Optimistic Message — Design Spec

## Context

When an operator sends a message in the Smartsupp chat window, there is currently no immediate feedback in the message list. The message only appears after the next 30-second polling sync.

Root cause: `useSendMessage.ts` already contains optimistic-update code in `onMutate`, but `onSettled` immediately calls `queryClient.invalidateQueries`, which triggers a refetch. The backend's GET `/conversations/{id}` endpoint reads from the local DB, which does not yet contain the new message (it lives in Smartsupp's system and is synced only via polling). The refetch returns stale data without the new message and overwrites the optimistic entry.

## Goal

The operator's sent message appears instantly in the chat window and shows a delivery-status indicator (spinner while in-flight, checkmark on success, rollback on failure).

## Design

### Data flow after the fix

```
mutate(content)
  → onMutate:  add optimistic msg  { id: "optimistic-…", deliveryStatus: "pending" }
  → mutationFn: POST /messages  → returns { messageId, createdAt }
  → onSuccess: replace optimistic msg  { id: messageId, deliveryStatus: "sent" }
  → onError:   restore previous cache  (rollback)
  → onSettled: (nothing — 30 s refetchInterval handles eventual sync)
```

### Changes — `useSendMessage.ts`

All changes are confined to this single file.

**1. `mutationFn` — return response instead of void**

Change the mutation type parameter from `void` to `SendMessageApiResponse`.  
Return `data` at the end of `mutationFn` so `onSuccess` receives it.

**2. `onMutate` — add `deliveryStatus: "pending"`**

```ts
const optimisticMsg: MessageDto = {
  id: `optimistic-${Date.now()}`,
  authorType: "agent",
  content,
  createdAt: new Date().toISOString(),
  isFirstReply: false,
  deliveryStatus: "pending",   // ← new
};
```

**3. `onSuccess` — replace optimistic entry with confirmed data**

```ts
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
            ? { ...m, id: data.messageId ?? m.id, createdAt: data.createdAt ?? m.createdAt, deliveryStatus: "sent" }
            : m,
        ),
      };
    },
  );
},
```

**4. `onSettled` — remove `invalidateQueries`**

The 30-second `refetchInterval` in `useSmartsuppConversation` will pick up the real message once Smartsupp has synced it. No immediate invalidation is needed.

`onSettled` can be deleted entirely.

### No changes needed elsewhere

- `MessageBubble` / `MessageDeliveryIcon` already handle `"pending"` and `"sent"` statuses.
- `ConversationDetail` already re-renders when the cache changes.
- Backend unchanged.

### Rollback behaviour (unchanged)

`onError` already restores `context.previous`, removing the optimistic message and re-showing what was there before.

## Edge cases

| Scenario | Behaviour |
|---|---|
| Multiple rapid sends | Each `onSuccess` maps only messages whose ID starts with `"optimistic-"`. Because sends are sequential (one pending mutation at a time), only the matching one is replaced. |
| 30 s poll fires before onSuccess | `cancelQueries` in `onMutate` stops any in-flight refetch. The poll that fires after `onSuccess` will overwrite the confirmed message with the real server message (same `messageId`), which is identical content — no visible change. |
| `messageId` absent in response | Falls back to the optimistic fake ID. The message stays in the list with the fake ID until the next poll replaces the entire messages array. |
| Send fails | `onError` rolls back to `context.previous`; the optimistic message disappears. |

## Tests to update — `useSendMessage.test.ts`

- Existing success test: assert `invalidateQueries` is **not** called on success.
- New: assert optimistic message has `deliveryStatus: "pending"`.
- New: assert `onSuccess` replaces the optimistic message with `messageId`/`"sent"`.
- Existing rollback test: unchanged — still verifies `context.previous` is restored.

## Verification

1. Open a Smartsupp conversation with messages.
2. Type and send a message.
3. Message should appear instantly with a spinning loader icon.
4. Once the POST returns, the loader switches to a checkmark.
5. Simulate a send failure (offline / 503) — the message should disappear and the composer should show the error.
6. Wait 30 s for polling — the message should still be visible and should not duplicate.
