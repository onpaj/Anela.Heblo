import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useSmartsuppVisitorInfo } from "../useSmartsupp";
import { getApiBaseUrl, getAuthenticatedFetch } from "../../client";

const mockFetch = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getApiBaseUrl: jest.fn(),
  getAuthenticatedFetch: jest.fn(),
}));

beforeEach(() => {
  mockFetch.mockReset();
  (getApiBaseUrl as jest.Mock).mockReturnValue("http://localhost:5001");
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
