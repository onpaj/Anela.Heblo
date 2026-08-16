import { renderHook } from "@testing-library/react";
import { usePresenceHeartbeat, otherActiveViewers } from "../useSmartsupp";
import { getAuthenticatedApiClient, getApiBaseUrl, getAuthenticatedFetch } from "../../client";

const mockRecordPresence = jest.fn();
const mockDeleteFetch = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getApiBaseUrl: jest.fn(),
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
  (getApiBaseUrl as jest.Mock).mockReturnValue("http://localhost:5001");
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
