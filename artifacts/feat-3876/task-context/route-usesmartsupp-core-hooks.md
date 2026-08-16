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
