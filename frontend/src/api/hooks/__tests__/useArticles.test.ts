import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import { useArticleFeedbackListQuery } from "../useArticles";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
}

const emptyFeedbackListResponse = {
  articles: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  totalPages: 0,
  stats: {
    totalArticles: 0,
    totalWithFeedback: 0,
    avgPrecisionScore: null,
    avgStyleScore: null,
  },
};

describe("useArticleFeedbackListQuery", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      baseUrl: "http://localhost:5001",
      http: { fetch: mockFetch },
    } as any);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it("appends sortDescending=true to the query string (not descending=...)", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(emptyFeedbackListResponse),
    });

    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: true }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockFetch.mock.calls[0][0];
    expect(calledUrl).toContain("sortDescending=true");
    expect(calledUrl).not.toContain("descending=true");
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });

  it("appends sortDescending=false when toggled off", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(emptyFeedbackListResponse),
    });

    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: false }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockFetch.mock.calls[0][0];
    expect(calledUrl).toContain("sortDescending=false");
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });

  it("omits sortDescending from the URL when undefined (backend default applies)", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(emptyFeedbackListResponse),
    });

    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortBy: "CreatedAt" }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockFetch.mock.calls[0][0];
    expect(calledUrl).not.toMatch(/[?&]sortDescending=/);
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });

  it("builds URL with all filter params including sortDescending", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(emptyFeedbackListResponse),
    });

    const { result } = renderHook(
      () =>
        useArticleFeedbackListQuery({
          hasFeedback: true,
          requestedBy: "user@anela.cz",
          sortBy: "CreatedAt",
          sortDescending: false,
          page: 2,
          pageSize: 10,
        }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockFetch.mock.calls[0][0];
    expect(calledUrl).toContain("hasFeedback=true");
    expect(calledUrl).toContain("requestedBy=user%40anela.cz");
    expect(calledUrl).toContain("sortBy=CreatedAt");
    expect(calledUrl).toContain("sortDescending=false");
    expect(calledUrl).toContain("page=2");
    expect(calledUrl).toContain("pageSize=10");
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });
});
