import { renderHook } from "@testing-library/react";
import { usePresenceHeartbeat, otherActiveViewers } from "../useSmartsupp";

const mockFetch = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: () => ({
    baseUrl: "http://localhost:5001",
    http: { fetch: mockFetch },
  }),
  QUERY_KEYS: { smartsupp: ["smartsupp"] },
}));

beforeEach(() => {
  jest.useFakeTimers();
  mockFetch.mockReset();
  mockFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve({}) });
});

afterEach(() => {
  jest.clearAllTimers();
  jest.useRealTimers();
});

describe("usePresenceHeartbeat", () => {
  it("posts a heartbeat immediately when a conversation is open", () => {
    renderHook(() => usePresenceHeartbeat("c1"));

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, init] = mockFetch.mock.calls[0];
    expect(url).toBe("http://localhost:5001/api/smartsupp/conversations/c1/presence");
    expect(init.method).toBe("POST");
  });

  it("does nothing when conversationId is null", () => {
    renderHook(() => usePresenceHeartbeat(null));
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("keeps beating on the interval", () => {
    renderHook(() => usePresenceHeartbeat("c1"));
    expect(mockFetch).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(20_000);
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it("sends a DELETE leave on unmount", () => {
    const { unmount } = renderHook(() => usePresenceHeartbeat("c1"));
    mockFetch.mockClear();

    unmount();

    const deleteCall = mockFetch.mock.calls.find(([, init]) => init.method === "DELETE");
    expect(deleteCall).toBeDefined();
    expect(deleteCall?.[1].keepalive).toBe(true);
  });
});

describe("otherActiveViewers", () => {
  it("filters out the current user", () => {
    const viewers = otherActiveViewers({
      activeViewers: [
        { agentId: "1", displayName: "Me", source: "Heblo", isCurrentUser: true, enteredAt: "" },
        { agentId: "2", displayName: "Petr", source: "Smartsupp", isCurrentUser: false, enteredAt: "" },
      ],
    });
    expect(viewers).toHaveLength(1);
    expect(viewers[0].displayName).toBe("Petr");
  });

  it("returns empty when there are no viewers", () => {
    expect(otherActiveViewers({ activeViewers: undefined })).toEqual([]);
  });
});
