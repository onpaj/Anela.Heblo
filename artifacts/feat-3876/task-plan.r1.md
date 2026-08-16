# Implementation Plan: Route Smartsupp frontend hooks through the generated typed API client

Source docs: `artifacts/feat-3876/spec.r1.md`, `artifacts/feat-3876/arch-review.r1.md`, `artifacts/feat-3876/design.r1.md`

## Overview

All five Smartsupp frontend hook files bypass the NSwag-generated, typed API client (`frontend/src/api/generated/api-client.ts`) via a private-field-reaching escape hatch in `frontend/src/api/smartsuppClient.ts` (`asInternal()` casting the client to reach `.baseUrl`/`.http.fetch`), and hand-declare eleven DTO interfaces the generator already emits. This plan replaces every raw-fetch call site with calls to the generated `smartsupp_*` client methods and generated DTOs, deletes the hand-declared interfaces, and deletes `smartsuppClient.ts` once nothing imports it.

**A verified correction to the design doc, found by actually reading the generated client and running the real TypeScript compiler against this repo's `tsconfig.json` (not guessed):**

1. **Every field on every generated Smartsupp DTO is optional** (`field?: T`), not just the `Date`-typed fields the design's Decision 4 called out. `ConversationDto.status`, `.assignedAgentIds`, `.tags`, `.contactTags`, `.contactProperties`, `.variables`, `.otherConversations`; `MessageDto.authorType`, `.isFirstReply`; `ShoptetContactInfoDto.recentOrders`; `ConversationSummaryDto.id`/`.status` — all of these are optional on the generated class even though the old hand-declared interfaces marked them required. Confirmed via `npx -p typescript@4.9.5 tsc --noEmit -p tsconfig.json` against this repo: `message.authorType.toLowerCase()`, `conversation.assignedAgentIds.length`, and passing `conversation.status` into a `status: string` prop all produce real compile errors once the generated types are wired in. Task 2 below fixes every one of these, found by grep + compiler, not by inspection alone.
2. **Plain object spreads (`{...current, field: x}`) do not satisfy a generated response class as a type** — NSwag emits response DTOs as `class`es with `init()`/`toJSON()` methods, and TypeScript requires object-literal values to have every member (including methods) to satisfy a class type. Confirmed via compiler: `const x: GetConversationResponse = {...current, messages: []}` fails with "missing `init`, `toJSON`"; `new GetConversationResponse({...current, messages: []})` compiles clean. `useSendMessage.ts`'s optimistic-update cache writes (Task 4) use the constructor-wrap form for this reason.
3. **`err.status` is not reliably populated on exceptions thrown by the generated client for 400/403/404/409 branches.** NSwag gives every status code that appears in a `[ProducesResponseType]` attribute its own branch that parses a body object (the typed DTO if one is declared, `ProblemDetails` as a fallback if not) and throws that object directly — never a `SwaggerException` — for 400/403/404/409. Only status codes generated as the untyped catch-all (in this codebase, that's 503 specifically, since it has no typed/fallback branch) throw a real `SwaggerException`, which is the only exception type that carries a genuine `.status`. Verified by simulating `throwException`/`ProblemDetails.fromJS` in Node against the actual generated code, and by reading the codebase's own precedent (`useSubmitArticleFeedbackMutation` in `useArticles.ts`) — its `err.status === 409` check has the same latent gap, since Articles' 409 is also typed and NSwag routes it through the same `ProblemDetails`-throws-a-plain-object path. `ProblemDetails.fromJS`'s `init()` *does* blanket-copy every raw JSON property onto the thrown object (not just the four RFC7807 fields), so the backend's real `errorCode` field survives onto the caught exception even though `.status` does not. Task 5 (`useSubmitDraftReplyFeedback.ts`) is the only hook that branches on the thrown exception's identity (for the 409 "already submitted" case), so it uses `err.errorCode` instead of `err.status` — this is the one place this plan's code deliberately diverges from the arch-review's literal "mirror `useSubmitArticleFeedbackMutation` near-verbatim" instruction, because mirroring it verbatim would silently break the feature (the `alreadySubmitted` branch would never fire). Every other hook's catch block in this plan (`useCloseConversation`, `useGenerateDraftReply`, `useSendMessage`) does **not** branch on `.status`/`.errorCode` inside the exception handler — it just treats any thrown exception as the generic-message case, which is unaffected by this gap and matches what the design already specified.
4. Also confirmed (not assumed) via `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`'s `HandleResponse<T>`: a `success:false` response body is **never** returned with HTTP 200 — every `ErrorCodes` value carries an `HttpStatusCodeAttribute` that `HandleResponse` uses to pick the real status code (404/409/503/etc.), so the "resolved 200 response with `success:false`" branch the design asks for in `useCloseConversation`/`useGenerateDraftReply`/`useSendMessage` is defensive/currently-unreachable in production, not a live path. It's kept anyway (harmless, matches the design's explicit two-channel instruction, and future-proofs against a handler change), but should not be mistaken for a live code path when reviewing this plan's output.

None of this changes the *shape* of the plan — it still routes each hook through `getAuthenticatedApiClient()` or the `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch exactly as the design lays out — it just makes the code that lands actually correct and actually compile.

### Tasks

1. **route-usesmartsupp-core-hooks** — rewrite `frontend/src/api/hooks/useSmartsupp.ts` (FR-1, FR-2, FR-7 partially): conversations list/detail, close, presence heartbeat via the typed client; Shoptet/visitor info via the escape hatch; delete all hand-declared DTO interfaces; re-export the generated types consumers need; update the `MIGRATED_HOOKS` regression guard; rewrite the three existing unit tests for these hooks.
2. **fix-smartsupp-consumer-optionality-fallout** — fix the downstream components that read fields now typed as optional/`Date` on the generated DTOs (`ConversationListItem.tsx`, `MessageBubble.tsx`, `ContactDetailsPanel.tsx`, `ConversationDetail.tsx`, `ConversationList.tsx`, `ShoptetCustomerCard.tsx`, `StatusPill.tsx`, `DaySeparator.tsx`), verified against the real compiler.
3. **route-usegeneratedraftreply-hook** — rewrite `useGenerateDraftReply.ts` (FR-3) + its test.
4. **route-usesendmessage-hook** — rewrite `useSendMessage.ts` (FR-4) + its test.
5. **route-usesubmitdraftreplyfeedback-hook** — rewrite `useSubmitDraftReplyFeedback.ts` (FR-5), using `errorCode`-based 409 detection for the reason explained above.
6. **extract-shared-ragfeedback-mapper** — extract `useKnowledgeBase.ts`'s feedback-list mapper into `frontend/src/components/feedback/ragFeedbackMapping.ts` and rewrite `useSmartsuppDraftReplyFeedbackListQuery.ts` (FR-6) to reuse it.
7. **retire-smartsuppclient** — delete `frontend/src/api/smartsuppClient.ts` (FR-7), confirm no references remain, run the full verification pass (build/lint/tests) plus the NFR-3 compile-time spot-check.

Every task lands buildable and testable on its own except Task 1 and Task 2, which must land together (Task 1's DTO-shape change is what makes Task 2's fixes necessary — the app will not compile between them). Do Task 1 then Task 2 in immediate succession before committing either as "done", but keep them as two commits per the frequent-commits convention (Task 1's commit will not build `npm run build` clean in isolation; that's expected and resolved by Task 2's commit immediately after).

---

### task: route-usesmartsupp-core-hooks

**Files:**
- Modify: `frontend/src/api/hooks/useSmartsupp.ts`
- Modify: `frontend/src/api/hooks/__tests__/useCloseConversation.test.ts`
- Modify: `frontend/src/api/hooks/__tests__/usePresenceHeartbeat.test.ts`
- Modify: `frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts`
- Modify: `frontend/src/api/__tests__/authenticated-api-usage.test.ts`

#### Step 1: Rewrite `useSmartsupp.ts`

Replace the entire file with:

```ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import {
  getAuthenticatedApiClient,
  getApiBaseUrl,
  getAuthenticatedFetch,
  QUERY_KEYS,
} from "../client";
import {
  ErrorCodes,
  type ConversationDto,
  type ConversationPresenceDto,
  type ConversationSummaryDto,
  type MessageDto,
  type ListConversationsResponse,
  type GetConversationResponse,
  type GetSmartsuppContactShoptetInfoResponse,
  type GetVisitorInfoResponse,
  type CloseConversationResponse,
} from "../generated/api-client";

// Re-exported so sibling hooks under components/customer-support/smartsupp/hooks/ can keep
// importing these from this file's existing path instead of reaching into the generated client
// directly.
export type {
  ConversationDto,
  ConversationPresenceDto,
  ConversationSummaryDto,
  MessageDto,
  ListConversationsResponse,
  GetConversationResponse,
};

export const SMARTSUPP_QUERY_KEYS = {
  conversations: (status: string) => ["smartsupp", "conversations", status] as const,
  conversation: (id: string) => ["smartsupp", "conversation", id] as const,
  shoptetInfo: (id: string) => ["smartsupp", "shoptet-info", id] as const,
  visitorInfo: (id: string) => ["smartsupp", "visitor-info", id] as const,
};

export function useSmartsuppConversations(status: "Open" | "Resolved" = "Open") {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.conversations(status),
    queryFn: (): Promise<ListConversationsResponse> =>
      getAuthenticatedApiClient().smartsupp_GetConversations(status, 1, 100),
    refetchInterval: 10_000,
    staleTime: 10_000,
  });
}

export function useSmartsuppConversation(id: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.conversation(id ?? ""),
    queryFn: (): Promise<GetConversationResponse> =>
      getAuthenticatedApiClient().smartsupp_GetConversation(id!),
    enabled: !!id,
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}

// Shoptet/visitor info stay on the escape hatch permanently, not the typed client: the (currently
// unwired) NSwag template-override predicate that would let the generated client return a typed
// non-throwing 404 branch is hardcoded to fire only for HTTP 409 (see
// backend/src/Anela.Heblo.API/nswag-templates/README.md), so a typed try/catch on 404 here would
// buy nothing over the escape hatch. See docs/development/api-client-generation.md for the
// escape-hatch pattern this mirrors.
export function useSmartsuppShoptetInfo(conversationId: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.shoptetInfo(conversationId ?? ""),
    queryFn: async (): Promise<GetSmartsuppContactShoptetInfoResponse | null> => {
      const response = await getAuthenticatedFetch()(
        `${getApiBaseUrl()}/api/smartsupp/conversations/${conversationId}/shoptet-info`,
        { method: "GET" },
      );
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`Shoptet info error: ${response.status}`);
      return (await response.json()) as GetSmartsuppContactShoptetInfoResponse;
    },
    enabled: !!conversationId,
    staleTime: 300_000,
    retry: false,
  });
}

export function useSmartsuppVisitorInfo(conversationId: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.visitorInfo(conversationId ?? ""),
    queryFn: async (): Promise<GetVisitorInfoResponse | null> => {
      const response = await getAuthenticatedFetch()(
        `${getApiBaseUrl()}/api/smartsupp/conversations/${conversationId}/visitor-info`,
        { method: "GET" },
      );
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`Visitor info error: ${response.status}`);
      return (await response.json()) as GetVisitorInfoResponse;
    },
    enabled: !!conversationId,
    staleTime: 600_000,
    retry: false,
  });
}

const CLOSE_ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppCloseConversationUnavailable]:
    "Nepodařilo se uzavřít konverzaci — služba je nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForCloseError(code?: ErrorCodes): string {
  if (code && CLOSE_ERROR_MESSAGES[code]) return CLOSE_ERROR_MESSAGES[code]!;
  return "Nepodařilo se uzavřít konverzaci.";
}

export function useCloseConversation() {
  const queryClient = useQueryClient();
  return useMutation<CloseConversationResponse, Error, string>({
    mutationFn: async (conversationId: string) => {
      let data: CloseConversationResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_CloseConversation(conversationId);
      } catch {
        // The controller's 404/503 ProducesResponseType are both untyped, so the generated
        // client throws here without a usable errorCode — fall back to the generic message.
        throw new Error(messageForCloseError(undefined));
      }
      if (!data.success) {
        throw new Error(messageForCloseError(data.errorCode));
      }
      return data;
    },
    onSuccess: (_data, conversationId) => {
      queryClient.invalidateQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.smartsupp, "conversations"],
      });
    },
  });
}

const PRESENCE_HEARTBEAT_INTERVAL_MS = 20_000;

/**
 * While a conversation detail is open, periodically tell the backend the current operator is
 * viewing it, so other operators see a presence badge. Sends a best-effort "leave" on unmount;
 * the backend TTL cleans up if that never arrives.
 */
export function usePresenceHeartbeat(conversationId: string | null): void {
  useEffect(() => {
    if (!conversationId) return;

    let cancelled = false;
    const apiClient = getAuthenticatedApiClient();

    const beat = () => {
      apiClient.smartsupp_RecordPresence(conversationId).catch(() => {
        /* presence is best-effort; ignore transient failures */
      });
    };

    beat();
    const timer = window.setInterval(() => {
      if (!cancelled) beat();
    }, PRESENCE_HEARTBEAT_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
      // The generated smartsupp_RemovePresence method builds its RequestInit internally with no
      // way for a caller to pass `keepalive` through its public signature — every NSwag
      // Fetch-template method owns its own options object, so this is a structural gap, not a
      // "not wired yet" one. Stay on the escape hatch for this one call so the "leave" signal
      // survives page/tab unload; smartsupp_RecordPresence above has no such need and stays typed.
      getAuthenticatedFetch()(
        `${getApiBaseUrl()}/api/smartsupp/conversations/${conversationId}/presence`,
        { method: "DELETE", keepalive: true },
      ).catch(() => {
        /* best-effort leave; TTL cleans up otherwise */
      });
    };
  }, [conversationId]);
}

/** Active viewers of a conversation other than the current operator. */
export function otherActiveViewers(
  conversation: Pick<ConversationDto, "activeViewers">,
): ConversationPresenceDto[] {
  return (conversation.activeViewers ?? []).filter((v) => !v.isCurrentUser);
}
```

Notes on what changed and why, so the diff is legible when reviewed:
- `ConversationSummaryDto`, `ConversationPresenceDto`, `ConversationDto`, `MessageDto`, `ListConversationsResponse`, `GetConversationResponse`, `ShoptetCustomerSnapshotDto`, `ShoptetOrderSnapshotDto`, `ShoptetContactInfoDto`, `GetSmartsuppShoptetInfoResponse`, `VisitorPageDto`, `VisitorInfoDto`, `GetSmartsuppVisitorInfoResponse`, `CloseConversationResponse` (the hand-declared interfaces) are gone — replaced by generated equivalents imported from `../generated/api-client`.
- `getClientAndBaseUrl`/`apiGet`/`apiPost`/`apiDelete` (from the deleted `../smartsuppClient`) are gone.
- `apiFetch` (the local wrapper that threw on `!response.ok`) is gone — the generated client's `process*` methods already do this for the typed-client calls; the escape-hatch calls keep their own manual check.
- `useCloseConversation`'s two-channel handling (resolved `success:false` body vs. thrown exception) matches the design's Decision/Interfaces section — kept even though `BaseApiController.HandleResponse` means the first channel is not reachable today, per this plan's Overview note.

#### Step 2: Update the `MIGRATED_HOOKS` regression guard

In `frontend/src/api/__tests__/authenticated-api-usage.test.ts`, find:

```ts
    const MIGRATED_HOOKS = new Set([
      "useArticles.ts",
      "useExpeditionListArchive.ts",
    ]);
```

Change to:

```ts
    const MIGRATED_HOOKS = new Set([
      "useArticles.ts",
      "useExpeditionListArchive.ts",
      "useSmartsupp.ts",
    ]);
```

This closes the gap the arch-review flagged: `hasLegacyAsAnyFetch`'s carve-out in this same test file currently treats `smartsuppClient` usage as accepted, so nothing was previously flagging `useSmartsupp.ts`'s escape-hatch cast. It will not regress silently again.

#### Step 3: Rewrite `useCloseConversation.test.ts`

Replace `frontend/src/api/hooks/__tests__/useCloseConversation.test.ts` with:

```ts
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
```

#### Step 4: Rewrite `usePresenceHeartbeat.test.ts`

Replace `frontend/src/api/hooks/__tests__/usePresenceHeartbeat.test.ts` with:

```ts
import { renderHook } from "@testing-library/react";
import { usePresenceHeartbeat, otherActiveViewers } from "../useSmartsupp";
import { getAuthenticatedApiClient, getAuthenticatedFetch } from "../../client";

const mockRecordPresence = jest.fn();
const mockDeleteFetch = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getApiBaseUrl: jest.fn(() => "http://localhost:5001"),
  getAuthenticatedFetch: jest.fn(),
  QUERY_KEYS: { smartsupp: ["smartsupp"] },
}));

beforeEach(() => {
  jest.useFakeTimers();
  mockRecordPresence.mockReset();
  mockDeleteFetch.mockReset();
  mockRecordPresence.mockResolvedValue({ success: true });
  mockDeleteFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve({}) });
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    smartsupp_RecordPresence: mockRecordPresence,
  });
  (getAuthenticatedFetch as jest.Mock).mockReturnValue(mockDeleteFetch);
});

afterEach(() => {
  jest.clearAllTimers();
  jest.useRealTimers();
});

describe("usePresenceHeartbeat", () => {
  it("records a heartbeat immediately when a conversation is open", () => {
    renderHook(() => usePresenceHeartbeat("c1"));

    expect(mockRecordPresence).toHaveBeenCalledTimes(1);
    expect(mockRecordPresence).toHaveBeenCalledWith("c1");
  });

  it("does nothing when conversationId is null", () => {
    renderHook(() => usePresenceHeartbeat(null));
    expect(mockRecordPresence).not.toHaveBeenCalled();
  });

  it("keeps beating on the interval", () => {
    renderHook(() => usePresenceHeartbeat("c1"));
    expect(mockRecordPresence).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(20_000);
    expect(mockRecordPresence).toHaveBeenCalledTimes(2);
  });

  it("sends a keepalive DELETE leave on unmount via the escape hatch", () => {
    const { unmount } = renderHook(() => usePresenceHeartbeat("c1"));
    mockDeleteFetch.mockClear();

    unmount();

    expect(mockDeleteFetch).toHaveBeenCalledWith(
      "http://localhost:5001/api/smartsupp/conversations/c1/presence",
      expect.objectContaining({ method: "DELETE", keepalive: true }),
    );
  });
});

describe("otherActiveViewers", () => {
  it("filters out the current user", () => {
    const viewers = otherActiveViewers({
      activeViewers: [
        { agentId: "1", displayName: "Me", source: "Heblo", isCurrentUser: true, enteredAt: new Date() },
        { agentId: "2", displayName: "Petr", source: "Smartsupp", isCurrentUser: false, enteredAt: new Date() },
      ],
    });
    expect(viewers).toHaveLength(1);
    expect(viewers[0].displayName).toBe("Petr");
  });

  it("returns empty when there are no viewers", () => {
    expect(otherActiveViewers({ activeViewers: undefined })).toEqual([]);
  });
});
```

(`enteredAt` moved from `""` to `new Date()` because `ConversationPresenceDto.enteredAt` is now typed `Date | undefined`, matching the generated shape.)

#### Step 5: Rewrite `useSmartsuppVisitorInfo.test.ts`

Replace `frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts` with:

```ts
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useSmartsuppVisitorInfo } from "../useSmartsupp";
import { getAuthenticatedFetch } from "../../client";

const mockFetch = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getApiBaseUrl: jest.fn(() => "http://localhost:5001"),
  getAuthenticatedFetch: jest.fn(),
}));

beforeEach(() => {
  mockFetch.mockReset();
  (getAuthenticatedFetch as jest.Mock).mockReturnValue(mockFetch);
});

function Wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

describe("useSmartsuppVisitorInfo", () => {
  it("is disabled when conversationId is null", () => {
    const { result } = renderHook(() => useSmartsuppVisitorInfo(null), { wrapper: Wrapper });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("returns null when API returns 404", async () => {
    mockFetch.mockResolvedValue({ status: 404, ok: false });

    const { result } = renderHook(() => useSmartsuppVisitorInfo("c1"), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data).toBeNull();
  });

  it("returns visitor info on 200", async () => {
    const payload = {
      success: true,
      visitorInfo: {
        os: "OS X",
        browser: "Chrome",
        browserVersion: "148.0.0.0",
        visitsCount: 321,
        chatsCount: 3,
        pages: [{ url: "https://www.anela.cz/product" }],
      },
    };
    mockFetch.mockResolvedValue({
      status: 200,
      ok: true,
      json: () => Promise.resolve(payload),
    });

    const { result } = renderHook(() => useSmartsuppVisitorInfo("c1"), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data?.visitorInfo?.os).toBe("OS X");
    expect(result.current.data?.visitorInfo?.visitsCount).toBe(321);
    expect(result.current.data?.visitorInfo?.pages).toHaveLength(1);
  });

  it("calls the visitor-info endpoint through the authenticated-fetch escape hatch", async () => {
    mockFetch.mockResolvedValue({ status: 404, ok: false });

    renderHook(() => useSmartsuppVisitorInfo("c1"), { wrapper: Wrapper });

    await waitFor(() => expect(mockFetch).toHaveBeenCalled());
    expect(mockFetch).toHaveBeenCalledWith(
      "http://localhost:5001/api/smartsupp/conversations/c1/visitor-info",
      { method: "GET" },
    );
  });
});
```

#### Step 6: Run the affected tests

```bash
cd frontend
CI=true npx react-scripts test src/api/hooks/__tests__/useCloseConversation.test.ts src/api/hooks/__tests__/usePresenceHeartbeat.test.ts src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts src/api/__tests__/authenticated-api-usage.test.ts --watchAll=false
```

Expect all suites to pass. If `authenticated-api-usage.test.ts`'s "should use getAuthenticatedApiClient() for all API hooks" check fails, re-check that `useSmartsupp.ts` still contains the literal substrings `getAuthenticatedApiClient` and `getAuthenticatedFetch` (it does, via the imports) and that no bare `fetch(` call was introduced outside `getAuthenticatedFetch()(...)`.

#### Step 7: Commit

```bash
git add frontend/src/api/hooks/useSmartsupp.ts \
  frontend/src/api/hooks/__tests__/useCloseConversation.test.ts \
  frontend/src/api/hooks/__tests__/usePresenceHeartbeat.test.ts \
  frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts \
  frontend/src/api/__tests__/authenticated-api-usage.test.ts
git commit -m "Route useSmartsupp.ts core hooks through the generated typed API client"
```

This commit will not pass `npm run build` in isolation (consuming components still assume the old required-field hand-declared types) — that's expected and fixed by the next task, which must land immediately after.

---

### task: fix-smartsupp-consumer-optionality-fallout

Every generated Smartsupp DTO field is optional (`field?: T`), unlike the hand-declared interfaces deleted in the previous task which mixed required and optional fields. This task widens the handful of consuming components that assumed required fields. Every fix below was verified to compile against this repo's actual `tsconfig.json` (`strict: true`) using `npx -p typescript@4.9.5 tsc --noEmit -p tsconfig.json` before being written into this plan — these are not speculative.

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/StatusPill.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/DaySeparator.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationList.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`

#### Step 1: Widen `StatusPill.tsx` to accept an optional status

`conversation.status` is now `string | undefined` everywhere it's read. Rather than adding a `?? ""` fallback at each of `StatusPill`'s three call sites, widen the one component that owns the fallback logic.

In `frontend/src/components/customer-support/smartsupp/StatusPill.tsx`, replace:

```tsx
interface StatusPillProps {
  status: string;
}

interface PillStyle {
  label: string;
  className: string;
}

function resolvePill(status: string): PillStyle {
  switch (status.toLowerCase()) {
```

with:

```tsx
interface StatusPillProps {
  status?: string;
}

interface PillStyle {
  label: string;
  className: string;
}

function resolvePill(status?: string): PillStyle {
  switch ((status ?? "").toLowerCase()) {
```

And replace the `default` branch's `label: status,` with `label: status ?? "",` (it's the only other read of the now-optional parameter in that function).

#### Step 2: Widen `DaySeparator.tsx` to accept `Date | string`

`MessageDto.createdAt` is now `Date | undefined` (was `string`, required). In `frontend/src/components/customer-support/smartsupp/DaySeparator.tsx`, replace:

```tsx
interface DaySeparatorProps {
  date: string;
}

function formatDayLabel(dateStr: string): string {
```

with:

```tsx
interface DaySeparatorProps {
  date: Date | string;
}

function formatDayLabel(dateStr: Date | string): string {
```

No other change needed in this file — `new Date(dateStr)` already accepts both `Date` and `string`.

#### Step 3: Fix `MessageBubble.tsx`

`MessageDto.authorType` is now `string | undefined` and `MessageDto.createdAt` is `Date | undefined`. In `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx`:

Replace:
```tsx
function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });
}
```
with:
```tsx
function formatTime(dateStr: Date | string): string {
  return new Date(dateStr).toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });
}
```

Replace:
```tsx
  const authorType = message.authorType.toLowerCase();
```
with:
```tsx
  const authorType = (message.authorType ?? "").toLowerCase();
```

`formatTime(message.createdAt)` at the call site needs no change — `message.createdAt` being `Date | undefined` and the function parameter being optional-compatible would still fail for `undefined`, so instead fix the call site itself. Replace:
```tsx
          <span>{formatTime(message.createdAt)}</span>
```
with:
```tsx
          <span>{formatTime(message.createdAt ?? new Date(0))}</span>
```

(`createdAt` is a non-nullable persisted field on every real message; the optionality here is purely a TS-strictness artifact of NSwag generating every field as optional, not a real runtime possibility — the epoch fallback is defensive only.)

#### Step 4: Fix `ConversationListItem.tsx`

`ConversationDto.updatedAt` is now `Date | undefined` (was `string`, required); `.lastMessageAt` was already `Date | undefined`-equivalent before. In `frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx`, replace:

```tsx
function formatRelativeTime(dateStr?: string | null): string {
  if (!dateStr) return "";
  const diff = Date.now() - new Date(dateStr).getTime();
```
with:
```tsx
function formatRelativeTime(dateStr?: Date | string | null): string {
  if (!dateStr) return "";
  const diff = Date.now() - new Date(dateStr).getTime();
```

No other change needed in this file — `formatRelativeTime(conversation.lastMessageAt ?? conversation.updatedAt)` already passes an optional value into what is now an optional-accepting parameter, and `StatusPill status={conversation.status}` is fine once Step 1 lands.

#### Step 5: Fix `ConversationList.tsx`

`ConversationDto.lastMessageAt`/`.updatedAt` are both `Date | undefined`; comparing two possibly-undefined values with `<`/`>` is a compile error. In `frontend/src/components/customer-support/smartsupp/ConversationList.tsx`, replace:

```tsx
      {[...conversations]
        .sort((a, b) => {
          const aTime = a.lastMessageAt ?? a.updatedAt;
          const bTime = b.lastMessageAt ?? b.updatedAt;
          return bTime < aTime ? -1 : bTime > aTime ? 1 : 0;
        })
        .map((c) => (
          <ConversationListItem
            key={c.id}
            conversation={c}
            isSelected={c.id === selectedId}
            onClick={() => onSelect(c.id)}
          />
        ))}
```

with:

```tsx
      {[...conversations]
        .sort((a, b) => {
          const aTime = a.lastMessageAt ?? a.updatedAt ?? new Date(0);
          const bTime = b.lastMessageAt ?? b.updatedAt ?? new Date(0);
          return bTime < aTime ? -1 : bTime > aTime ? 1 : 0;
        })
        .map((c) => (
          <ConversationListItem
            key={c.id}
            conversation={c}
            isSelected={c.id === selectedId}
            onClick={() => onSelect(c.id ?? "")}
          />
        ))}
```

(`onSelect` requires a `string`; `c.id` is now `string | undefined`. `c.id` is a real conversation's primary key and will always be present in practice — the `?? ""` is a compile-time formality, matching the same pattern used elsewhere in this task.)

#### Step 6: Fix `ConversationDetail.tsx`

`MessageDto.authorType`/`.createdAt`, `ConversationDto.status`/`.assignedAgentIds` are all now optional. In `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`:

Replace:
```tsx
export function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const authorType = m.authorType.toLowerCase();
```
with:
```tsx
export function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const authorType = (m.authorType ?? "").toLowerCase();
```

Replace:
```tsx
function groupByDay(messages: MessageDto[]): Array<{ day: string; items: MessageDto[] }> {
  const groups: Array<{ day: string; items: MessageDto[] }> = [];
  for (const m of messages) {
    const day = new Date(m.createdAt).toISOString().slice(0, 10);
```
with:
```tsx
function groupByDay(messages: MessageDto[]): Array<{ day: string; items: MessageDto[] }> {
  const groups: Array<{ day: string; items: MessageDto[] }> = [];
  for (const m of messages) {
    const day = new Date(m.createdAt ?? new Date(0)).toISOString().slice(0, 10);
```

Replace:
```tsx
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {liveStatus.toLowerCase() === 'open' && (
```
with:
```tsx
          {(conversation.assignedAgentIds ?? []).map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {(liveStatus ?? "").toLowerCase() === 'open' && (
```

Replace:
```tsx
            <DaySeparator date={g.items[0].createdAt} />
```
with:
```tsx
            <DaySeparator date={g.items[0].createdAt ?? new Date(0)} />
```

`StatusPill status={liveStatus}` needs no change (fine once Task-1-Step-1 widens `StatusPill`, and `liveStatus`'s type is now `string | undefined`, which the widened prop accepts).

#### Step 7: Fix `ContactDetailsPanel.tsx`

`ConversationDto.variables`/`.contactProperties`/`.assignedAgentIds`/`.contactTags`/`.tags`/`.otherConversations` are all now optional. In `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx`:

Replace:
```tsx
  const infoEntries = mergedInfoEntries(conversation.variables, conversation.contactProperties);
```
with:
```tsx
  const infoEntries = mergedInfoEntries(conversation.variables ?? {}, conversation.contactProperties ?? {});
```

Replace:
```tsx
      {conversation.assignedAgentIds.length > 0 && (
        <Section title="Přiřazení operátoři">
          <div className="flex flex-wrap gap-1.5">
            {conversation.assignedAgentIds.map((id) => (
```
with:
```tsx
      {(conversation.assignedAgentIds ?? []).length > 0 && (
        <Section title="Přiřazení operátoři">
          <div className="flex flex-wrap gap-1.5">
            {(conversation.assignedAgentIds ?? []).map((id) => (
```

Replace:
```tsx
      {conversation.contactTags.length > 0 && (
        <Section title="Štítky kontaktu">
          <div className="flex flex-wrap gap-1.5">
            {conversation.contactTags.map((t) => (
```
with:
```tsx
      {(conversation.contactTags ?? []).length > 0 && (
        <Section title="Štítky kontaktu">
          <div className="flex flex-wrap gap-1.5">
            {(conversation.contactTags ?? []).map((t) => (
```

Replace:
```tsx
      {conversation.tags.length > 0 && (
        <Section title="Štítky">
          <div className="flex flex-wrap gap-1.5">
            {conversation.tags.map((t) => (
```
with:
```tsx
      {(conversation.tags ?? []).length > 0 && (
        <Section title="Štítky">
          <div className="flex flex-wrap gap-1.5">
            {(conversation.tags ?? []).map((t) => (
```

Replace:
```tsx
      {conversation.otherConversations.length > 0 && (
        <Section title={`Jiné konverzace (${conversation.otherConversations.length})`}>
          {conversation.otherConversations.map((c) => (
```
with:
```tsx
      {(conversation.otherConversations ?? []).length > 0 && (
        <Section title={`Jiné konverzace (${(conversation.otherConversations ?? []).length})`}>
          {(conversation.otherConversations ?? []).map((c) => (
```

`StatusPill status={conversation.status}` needs no change (fine once `StatusPill` is widened). `conv.id`/`conv.status`/`conv.lastMessageAt` inside `OtherConversationRow` need no change: `conv.status`/`conv.lastMessagePreview` are read only as JSX children (accept `undefined`), `conv.lastMessageAt` is already guarded by `conv.lastMessageAt ? new Date(conv.lastMessageAt)... : "—"`, and `onSelect?.(conv.id)` — `onSelect` is itself optional (`(id: string) => void | undefined`) but `conv.id` being `string | undefined` passed as the required `id: string` argument **does** need a fix. Replace:
```tsx
      onClick={() => onSelect?.(conv.id)}
```
with:
```tsx
      onClick={() => onSelect?.(conv.id ?? "")}
```

#### Step 8: Fix `ShoptetCustomerCard.tsx`

`ShoptetContactInfoDto.recentOrders` is now `ShoptetOrderSnapshotDto[] | undefined` (was required). In `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`, replace:

```tsx
  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  const hasCustomer = customer != null;
  const hasOrders = recentOrders.length > 0;
```
with:
```tsx
  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  const hasCustomer = customer != null;
  const hasOrders = (recentOrders ?? []).length > 0;
```

Replace:
```tsx
            {recentOrders.map((order) => (
```
with:
```tsx
            {(recentOrders ?? []).map((order) => (
```

No other change needed in this file — `customer != null`/`cartUpdatedAt != null` guards already narrow correctly for both `null` and `undefined`, and everything inside those guards was already reading optional fields defensively.

#### Step 9: Verify the full build

```bash
cd frontend
npm run build
```

Expect zero TypeScript errors. If any remain, they will be additional Smartsupp-DTO-optionality fallout in a file not listed above — fix with the same `?? fallback` pattern used throughout this task; do not introduce `as any`/`as unknown as X` casts (forbidden by NFR-1).

```bash
npm run lint
```

Expect no new warnings.

#### Step 10: Run the Smartsupp component test suites

```bash
cd frontend
CI=true npx react-scripts test src/components/customer-support/smartsupp --watchAll=false
```

Expect all existing suites for these components to keep passing unchanged (this task is a pure type-fix pass with no behavior change).

#### Step 11: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/StatusPill.tsx \
  frontend/src/components/customer-support/smartsupp/DaySeparator.tsx \
  frontend/src/components/customer-support/smartsupp/MessageBubble.tsx \
  frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx \
  frontend/src/components/customer-support/smartsupp/ConversationList.tsx \
  frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx \
  frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx \
  frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx
git commit -m "Fix Smartsupp components for generated-DTO field optionality"
```

`npm run build` should now be clean end-to-end for everything touched so far.

---

### task: route-usegeneratedraftreply-hook

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts`

#### Step 1: Rewrite `useGenerateDraftReply.ts`

Replace the entire file with:

```ts
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  GenerateDraftReplyBody,
  type GenerateDraftReplyResponse,
} from "../../../../api/generated/api-client";

export interface DraftReplySource {
  chunkId: string;
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface DraftReplyResult {
  id: string | null;
  answer: string;
  sources: DraftReplySource[];
}

const ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppDraftReplyAiUnavailable]:
    "AI služba je momentálně nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationEmpty]: "Konverzace neobsahuje zprávu zákazníka.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForError(code?: ErrorCodes): string {
  if (code && ERROR_MESSAGES[code]) {
    return ERROR_MESSAGES[code]!;
  }
  return "Nepodařilo se vygenerovat odpověď.";
}

function toDraftReplyResult(data: GenerateDraftReplyResponse): DraftReplyResult {
  return {
    id: data.id ?? null,
    answer: data.answer ?? "",
    sources: (data.sources ?? []).map((s) => ({
      chunkId: s.chunkId ?? "",
      documentId: s.documentId ?? "",
      filename: s.filename ?? "",
      excerpt: s.excerpt ?? "",
      score: s.score ?? 0,
    })),
  };
}

interface UseGenerateDraftReplyResult {
  generate: (topic?: string) => void;
  isLoading: boolean;
  error: string | null;
  result: DraftReplyResult | null;
  reset: () => void;
}

export function useGenerateDraftReply(
  conversationId: string | null,
): UseGenerateDraftReplyResult {
  const mutation = useMutation<DraftReplyResult, Error, string | undefined>({
    mutationFn: async (topic) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      let data: GenerateDraftReplyResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_GenerateDraftReply(
          conversationId,
          new GenerateDraftReplyBody({ topic: topic ?? undefined }),
        );
      } catch {
        // 400/404/503 are all untyped ProducesResponseType on this controller action, so the
        // generated client throws without a usable errorCode here.
        throw new Error(messageForError(undefined));
      }
      if (!data.success) {
        throw new Error(messageForError(data.errorCode));
      }

      return toDraftReplyResult(data);
    },
  });

  return {
    generate: (topic?: string) => mutation.mutate(topic),
    isLoading: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    result: mutation.data ?? null,
    reset: mutation.reset,
  };
}
```

Note: `topic ?? undefined` (not `?? null`) because the generated `GenerateDraftReplyBody.topic` is typed `string | undefined`, not `string | null`; omitting the key from the outgoing JSON has the same effect server-side as sending an explicit `null` did before, since the backend's `Topic` property is a nullable C# string either way.

#### Step 2: Rewrite `useGenerateDraftReply.test.ts`

Replace `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts` with:

```ts
import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useGenerateDraftReply } from "../useGenerateDraftReply";
import { getAuthenticatedApiClient } from "../../../../../api/client";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGenerateDraftReply = jest.fn();

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockGenerateDraftReply.mockReset();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    smartsupp_GenerateDraftReply: mockGenerateDraftReply,
  });
});

describe("useGenerateDraftReply", () => {
  it("returns answer and sources on success", async () => {
    mockGenerateDraftReply.mockResolvedValue({
      success: true,
      answer: "Dobrý den, balíky odesíláme do 24 hodin.",
      sources: [{ documentId: "d1", filename: "doprava.pdf", excerpt: "...", score: 0.9 }],
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Doprava"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(result.current.result!.answer).toMatch(/balíky odesíláme/);
    expect(result.current.result!.sources).toHaveLength(1);
  });

  it("passes the topic through the typed client", async () => {
    mockGenerateDraftReply.mockResolvedValue({ success: true, answer: "x", sources: [] });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Reklamace"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(mockGenerateDraftReply).toHaveBeenCalledWith(
      "c1",
      expect.objectContaining({ topic: "Reklamace" }),
    );
  });

  it("surfaces a Czech message for a known error code on the typed response", async () => {
    mockGenerateDraftReply.mockResolvedValue({
      success: false,
      errorCode: "SmartsuppDraftReplyAiUnavailable",
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/nedostupná/i);
  });

  it("surfaces a generic message when the call throws (untyped 400/404/503)", async () => {
    mockGenerateDraftReply.mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo se/i);
  });
});
```

#### Step 3: Run the test and full type-check

```bash
cd frontend
CI=true npx react-scripts test src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts --watchAll=false
npm run build
```

Both should be clean.

#### Step 4: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts \
  frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts
git commit -m "Route useGenerateDraftReply through the generated typed API client"
```

---

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

### task: route-usesubmitdraftreplyfeedback-hook

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts`

No existing test file covers this hook (confirmed: `Glob **/useSubmitDraftReplyFeedback*.test.ts*` returns no results), and per the spec's Out of Scope section, adding new test coverage where none currently exists is not part of this migration — this task changes only the implementation file.

#### Step 1: Rewrite `useSubmitDraftReplyFeedback.ts`

Replace the entire file with:

```ts
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  SubmitDraftReplyFeedbackRequest,
  type ISubmitDraftReplyFeedbackRequest,
} from "../../../../api/generated/api-client";

export interface SubmitDraftReplyFeedbackResult {
  alreadySubmitted?: true;
}

/**
 * Submit precision/style feedback for a generated Smartsupp draft reply.
 * Returns { alreadySubmitted: true } on the "already submitted"/"log not found" conflict outcomes
 * instead of throwing (both are mapped to HTTP 409 by the backend's ErrorCodes attribute).
 */
export function useSubmitDraftReplyFeedback() {
  return useMutation<SubmitDraftReplyFeedbackResult, Error, ISubmitDraftReplyFeedbackRequest>({
    mutationFn: async (payload) => {
      const request = new SubmitDraftReplyFeedbackRequest(payload);
      try {
        await getAuthenticatedApiClient().smartsupp_SubmitDraftReplyFeedback(request);
        return {};
      } catch (e: unknown) {
        // The generated client's 403/409 branches parse a ProblemDetails-shaped object rather
        // than throwing a SwaggerException, so `.status` is not reliably populated here — only
        // the raw JSON body's own fields (blanket-copied onto the thrown object by
        // ProblemDetails.init()) survive, which is why this branches on errorCode instead of
        // HTTP status. See docs/development/api-client-generation.md.
        const err = e as { errorCode?: string };
        if (
          err.errorCode === ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted ||
          err.errorCode === ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound
        ) {
          return { alreadySubmitted: true };
        }
        throw e;
      }
    },
  });
}
```

Note on the request-variables type: `ISubmitDraftReplyFeedbackRequest` (the generated *interface*, not the class) is used for the mutation's `TVariables`, matching the existing precedent in `frontend/src/api/hooks/useKnowledgeBase.ts`'s `useSubmitFeedbackMutation` (`mutationFn: async (payload: ISubmitFeedbackRequest) => { ... new SubmitFeedbackRequest(payload) ... }`). `DraftReplyFeedback.tsx` (the only caller) already passes a plain object literal (`{logId, precisionScore, styleScore, comment}`); using the interface here keeps that call site working unchanged, whereas typing `TVariables` as the class `SubmitDraftReplyFeedbackRequest` itself would require every caller to also pass class instances (plain object literals don't structurally satisfy NSwag's generated classes — see the note in the `useSendMessage` task).

The 403 (Forbidden — feedback logged by a different user) case is deliberately **not** matched by the errorCode check above and falls through to `throw e`, matching current behavior: the old code's `if (response.status === 409) return {alreadySubmitted:true}; if (!response.ok) throw ...` also only special-cased 409, letting 403 surface as a generic mutation error.

#### Step 2: Manually verify against `DraftReplyFeedback.tsx`

Confirm the call site in `frontend/src/components/customer-support/smartsupp/DraftReplyFeedback.tsx` still type-checks with no changes:

```tsx
submitFeedback.mutate(
  {
    logId,
    precisionScore: data.precisionScore,
    styleScore: data.styleScore,
    comment: data.comment,
  },
  {
    onSuccess: (result) => {
      if (result.alreadySubmitted) setAlreadySubmitted(true);
    },
  },
)
```

No edits needed to this file — it's read-only for this task.

#### Step 3: Type-check

```bash
cd frontend
npm run build
```

Expect zero errors.

#### Step 4: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts
git commit -m "Route useSubmitDraftReplyFeedback through the generated typed API client"
```

---

### task: extract-shared-ragfeedback-mapper

`smartsupp_GetDraftReplyFeedbackList` and `knowledgeBase_GetFeedbackList` return the identical generated classes (`RagFeedbackLogSummary`, `RagFeedbackStatsDto` — both back the same `RagInteractionLogs` table, per `ragFeedbackTypes.ts`'s own header comment). `useKnowledgeBase.ts` already contains a `toLocalFeedbackChunk`/`toLocalFeedbackListResponse` mapper that converts the generator's `Date`/`undefined` shapes into `ragFeedbackTypes.ts`'s `string`/`null` shapes. This task extracts that mapper into a shared module and reuses it from both hooks, avoiding two independently-maintained copies of the same conversion logic.

**Files:**
- Create: `frontend/src/components/feedback/ragFeedbackMapping.ts`
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts`

#### Step 1: Create `ragFeedbackMapping.ts`

Create `frontend/src/components/feedback/ragFeedbackMapping.ts`:

```ts
import type {
  RagFeedbackLogSummary as GeneratedRagFeedbackLogSummary,
  RagFeedbackStatsDto,
} from '../../api/generated/api-client';
import type {
  RagFeedbackChunk,
  RagFeedbackLogSummary,
  RagFeedbackStats,
} from './ragFeedbackTypes';

// smartsupp_GetDraftReplyFeedbackList and knowledgeBase_GetFeedbackList both return this exact
// generated shape (RagFeedbackLogSummary / RagFeedbackStatsDto) — they back the same
// RagInteractionLogs table, per ragFeedbackTypes.ts's header comment. Typed structurally so both
// generated response classes (GetFeedbackListResponse and GetDraftReplyFeedbackListResponse)
// satisfy it without a cross-import between the two feature hook files.
export interface GeneratedFeedbackListShape {
  logs?: GeneratedRagFeedbackLogSummary[];
  totalCount?: number;
  pageNumber?: number;
  pageSize?: number;
  totalPages?: number;
  stats?: RagFeedbackStatsDto;
}

export interface LocalFeedbackListResponse {
  success: boolean;
  logs: RagFeedbackLogSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  stats: RagFeedbackStats;
}

// Keeps the generated RAG DTOs from leaking into ragFeedbackTypes.ts consumers, which assume
// createdAt: string and null (not undefined) for absent optional fields.
export const toLocalFeedbackChunk = (chunk: {
  chunkId?: string;
  documentId?: string;
  filename?: string;
  score?: number;
  content?: string;
}): RagFeedbackChunk => ({
  chunkId: chunk.chunkId ?? '',
  documentId: chunk.documentId ?? '',
  filename: chunk.filename ?? '',
  score: chunk.score ?? 0,
  content: chunk.content ?? '',
});

export const toLocalFeedbackListResponse = (
  generated: GeneratedFeedbackListShape,
): LocalFeedbackListResponse => ({
  success: true,
  logs: (generated.logs ?? []).map((log) => ({
    id: log.id ?? '',
    question: log.question ?? '',
    answer: log.answer ?? '',
    expandedQuery: log.expandedQuery ?? null,
    systemPrompt: log.systemPrompt ?? '',
    retrievedChunks: (log.retrievedChunks ?? []).map(toLocalFeedbackChunk),
    topK: log.topK ?? 0,
    sourceCount: log.sourceCount ?? 0,
    durationMs: log.durationMs ?? 0,
    createdAt: log.createdAt ? log.createdAt.toISOString() : '',
    userId: log.userId ?? null,
    userName: log.userName ?? null,
    conversationId: log.conversationId ?? null,
    topic: log.topic ?? null,
    sentAnswer: log.sentAnswer ?? null,
    wasEdited: log.wasEdited ?? null,
    sentAt: log.sentAt ? log.sentAt.toISOString() : null,
    precisionScore: log.precisionScore ?? null,
    styleScore: log.styleScore ?? null,
    feedbackComment: log.feedbackComment ?? null,
    hasFeedback: log.hasFeedback ?? false,
  })),
  totalCount: generated.totalCount ?? 0,
  pageNumber: generated.pageNumber ?? 0,
  pageSize: generated.pageSize ?? 0,
  totalPages: generated.totalPages ?? 0,
  stats: {
    totalQuestions: generated.stats?.totalQuestions ?? 0,
    totalWithFeedback: generated.stats?.totalWithFeedback ?? 0,
    avgPrecisionScore: generated.stats?.avgPrecisionScore ?? null,
    avgStyleScore: generated.stats?.avgStyleScore ?? null,
  },
});
```

This is a byte-for-byte copy of `useKnowledgeBase.ts`'s existing mapping logic — no behavior change, just relocated and generalized from the KB-specific `GeneratedGetFeedbackListResponse` type to the shared structural `GeneratedFeedbackListShape`.

#### Step 2: Update `useKnowledgeBase.ts` to use the shared mapper

In `frontend/src/api/hooks/useKnowledgeBase.ts`, replace the import block:

```ts
import {
  SearchDocumentsRequest,
  AskQuestionRequest,
  SubmitFeedbackRequest,
  type ISubmitFeedbackRequest,
  type GetDocumentsResponse,
  type GetDocumentContentTypesResponse,
  type SearchDocumentsResponse,
  type AskQuestionResponse,
  type GetChunkDetailResponse,
  type DeleteDocumentResponse,
  type UploadDocumentResponse2,
  type FileParameter,
  type GetFeedbackListResponse as GeneratedGetFeedbackListResponse,
} from '../generated/api-client';
import type {
  RagFeedbackLogSummary,
  RagFeedbackChunk,
  RagFeedbackStats,
} from '../../components/feedback/ragFeedbackTypes';
```

with:

```ts
import {
  SearchDocumentsRequest,
  AskQuestionRequest,
  SubmitFeedbackRequest,
  type ISubmitFeedbackRequest,
  type GetDocumentsResponse,
  type GetDocumentContentTypesResponse,
  type SearchDocumentsResponse,
  type AskQuestionResponse,
  type GetChunkDetailResponse,
  type DeleteDocumentResponse,
  type UploadDocumentResponse2,
  type FileParameter,
} from '../generated/api-client';
import type {
  RagFeedbackLogSummary,
  RagFeedbackStats,
} from '../../components/feedback/ragFeedbackTypes';
import { toLocalFeedbackListResponse } from '../../components/feedback/ragFeedbackMapping';
```

(`RagFeedbackChunk` is no longer imported directly here — it was only used by the now-deleted local `toLocalFeedbackChunk`.)

Then delete the local mapper functions — replace:

```ts
// ---- Feedback-list mapping ----
// Keeps the generated RAG DTOs from leaking into ragFeedbackTypes.ts consumers, which assume
// createdAt: string and null (not undefined) for absent optional fields.

const toLocalFeedbackChunk = (chunk: {
  chunkId?: string;
  documentId?: string;
  filename?: string;
  score?: number;
  content?: string;
}): RagFeedbackChunk => ({
  chunkId: chunk.chunkId ?? '',
  documentId: chunk.documentId ?? '',
  filename: chunk.filename ?? '',
  score: chunk.score ?? 0,
  content: chunk.content ?? '',
});

const toLocalFeedbackListResponse = (
  generated: GeneratedGetFeedbackListResponse,
): GetFeedbackListResponse => ({
  success: true,
  logs: (generated.logs ?? []).map((log) => ({
    id: log.id ?? '',
    question: log.question ?? '',
    answer: log.answer ?? '',
    expandedQuery: log.expandedQuery ?? null,
    systemPrompt: log.systemPrompt ?? '',
    retrievedChunks: (log.retrievedChunks ?? []).map(toLocalFeedbackChunk),
    topK: log.topK ?? 0,
    sourceCount: log.sourceCount ?? 0,
    durationMs: log.durationMs ?? 0,
    createdAt: log.createdAt ? log.createdAt.toISOString() : '',
    userId: log.userId ?? null,
    userName: log.userName ?? null,
    conversationId: log.conversationId ?? null,
    topic: log.topic ?? null,
    sentAnswer: log.sentAnswer ?? null,
    wasEdited: log.wasEdited ?? null,
    sentAt: log.sentAt ? log.sentAt.toISOString() : null,
    precisionScore: log.precisionScore ?? null,
    styleScore: log.styleScore ?? null,
    feedbackComment: log.feedbackComment ?? null,
    hasFeedback: log.hasFeedback ?? false,
  })),
  totalCount: generated.totalCount ?? 0,
  pageNumber: generated.pageNumber ?? 0,
  pageSize: generated.pageSize ?? 0,
  totalPages: generated.totalPages ?? 0,
  stats: {
    totalQuestions: generated.stats?.totalQuestions ?? 0,
    totalWithFeedback: generated.stats?.totalWithFeedback ?? 0,
    avgPrecisionScore: generated.stats?.avgPrecisionScore ?? null,
    avgStyleScore: generated.stats?.avgStyleScore ?? null,
  },
});
```

with nothing (delete the block entirely — the two functions now come from the import).

Everything else in `useKnowledgeBase.ts` (the `GetFeedbackListResponse`/`FeedbackLogSummary`/`FeedbackStatsDto` local type aliases, `useKnowledgeBaseFeedbackListQuery`'s body calling `toLocalFeedbackListResponse(generated)`) is unchanged — `toLocalFeedbackListResponse`'s return type (`LocalFeedbackListResponse` from the new module) is structurally identical to `useKnowledgeBase.ts`'s own `GetFeedbackListResponse` interface, so it satisfies the existing `queryFn: async (): Promise<GetFeedbackListResponse> => {...}` return-type annotation with no cast.

#### Step 3: Rewrite `useSmartsuppDraftReplyFeedbackListQuery.ts`

Replace `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts` with:

```ts
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  toLocalFeedbackListResponse,
  type LocalFeedbackListResponse,
} from "../../../feedback/ragFeedbackMapping";

export interface DraftReplyFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export type DraftReplyFeedbackListResponse = LocalFeedbackListResponse;

const QUERY_KEY = ["smartsupp", "draft-reply", "feedback-list"] as const;

export function useSmartsuppDraftReplyFeedbackListQuery(params: DraftReplyFeedbackListParams = {}) {
  return useQuery<DraftReplyFeedbackListResponse>({
    queryKey: [...QUERY_KEY, params],
    queryFn: async () => {
      const generated = await getAuthenticatedApiClient().smartsupp_GetDraftReplyFeedbackList(
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDescending,
        params.hasFeedback,
        params.userId,
      );
      return toLocalFeedbackListResponse(generated);
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
}
```

No manual `URLSearchParams` construction, no manual `!response.ok` check (the generated client throws directly on any non-2xx, which is the only status this endpoint's controller annotates besides 200, so there's nothing else to special-case), and the returned shape is unchanged from the caller's point of view (`frontend/src/components/feedback/adapters/useSmartsuppFeedbackAdapter.ts` reads `query.data?.logs`/`.stats`/`.totalCount`/`.totalPages`/`.pageNumber`, all of which are still present with the same field types as before) — that adapter file needs no edits.

#### Step 4: Run the affected tests

```bash
cd frontend
CI=true npx react-scripts test src/api/hooks/__tests__/useKnowledgeBase.test.ts --watchAll=false
npm run build
```

`useKnowledgeBase.test.ts` should keep passing unchanged (it only exercises `useKnowledgeBaseFeedbackListQuery`'s public behavior, not the private mapper functions, confirmed by inspection before this task started). `npm run build` should be clean.

#### Step 5: Commit

```bash
git add frontend/src/components/feedback/ragFeedbackMapping.ts \
  frontend/src/api/hooks/useKnowledgeBase.ts \
  frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts
git commit -m "Extract shared RAG feedback-list mapper and route useSmartsuppDraftReplyFeedbackListQuery through it"
```

---

### task: retire-smartsuppclient

**Files:**
- Delete: `frontend/src/api/smartsuppClient.ts`

By this point, FR-1 through FR-6 have removed every caller of `smartsuppClient.ts`. This task deletes it and runs the full verification pass.

#### Step 1: Confirm no remaining references

```bash
cd frontend
grep -rn "smartsuppClient" src --include="*.ts" --include="*.tsx"
grep -rn "asInternal" src --include="*.ts" --include="*.tsx"
```

Expect both to return nothing. If anything shows up, it's a file this plan's earlier tasks missed — go back and finish routing it through the typed client or the `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch before continuing.

#### Step 2: Delete the file

```bash
cd frontend
rm src/api/smartsuppClient.ts
```

#### Step 3: Full frontend verification

```bash
cd frontend
npm run build
npm run lint
CI=true npx react-scripts test src/api/hooks/__tests__ src/components/customer-support/smartsupp --watchAll=false
```

All three must be clean/green. `npm run build` succeeding here is the confirmation that deleting `smartsuppClient.ts` broke nothing (no dangling import survived).

#### Step 4: NFR-3 compile-time spot-check

This confirms the actual motivation for the whole migration: a backend DTO field rename now surfaces as a frontend compile error instead of a silent runtime `undefined`.

```bash
cd backend
grep -n "public bool Success" src/Anela.Heblo.Application/Shared/BaseResponse.cs
```

Temporarily rename the `Success` property on `BaseResponse` (e.g. to `SuccessX`) — every Smartsupp response DTO extends this class:

```bash
cd backend
sed -i 's/public bool Success/public bool SuccessX/' src/Anela.Heblo.Application/Shared/BaseResponse.cs
grep -rln "\.Success\b" src/Anela.Heblo.API/Controllers/BaseApiController.cs src/Anela.Heblo.Application/Shared/BaseResponse.cs
```

(Expect this to also require touching `BaseApiController.HandleResponse`'s `response.Success` read and `BaseResponse`'s own constructor/property references — fix those up locally too, just enough to get a clean backend build, since the point of this check is only to observe the generated client regenerate and the frontend break, not to ship a real rename.)

```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Regenerate the TypeScript client per `docs/development/api-client-generation.md`'s documented command (find and run the project's NSwag regeneration script/command — check `frontend/package.json` for a `generate-api` or similar script, or `backend/src/Anela.Heblo.API/nswag-templates/` for the generation config referenced there).

```bash
cd frontend
npm run build
```

Expect `npm run build` to now fail with TypeScript compile errors in one or more of the five migrated hook files (any place reading `.success` on a Smartsupp response — `useSmartsupp.ts`'s `useCloseConversation`, `useGenerateDraftReply.ts`, `useSendMessage.ts` all read `data.success`). This is the desired outcome — it demonstrates NFR-3 holds.

Then revert everything from this step:

```bash
cd backend
git checkout -- src/Anela.Heblo.Application/Shared/BaseResponse.cs src/Anela.Heblo.API/Controllers/BaseApiController.cs
cd ../frontend
git checkout -- src/api/generated/api-client.ts
npm run build
```

Confirm the final `npm run build` is clean again (back to the state at the end of Step 3).

#### Step 5: Commit

```bash
cd frontend
git add -A src/api/smartsuppClient.ts
git commit -m "Delete smartsuppClient.ts now that every Smartsupp hook uses the generated typed client"
```

(`git add -A` here specifically to stage the deletion; if any other unrelated files show as modified from the spot-check in Step 4, `git status` first and stage only the deletion.)

#### Step 6: Final full-repo verification

```bash
cd frontend
npm run build
npm run lint
CI=true npx react-scripts test --watchAll=false
```

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
```

All must pass — this is the final task in the plan, and CLAUDE.md's "Validation before completion" applies to the whole feature at this point (BE build/format, FE build/lint, all touched tests). No backend source was actually changed by this feature (Step 4's edits were reverted), so `dotnet build`/`dotnet format` should already be clean from the base branch, but running them here confirms the spot-check's revert was complete and nothing was left half-changed.
