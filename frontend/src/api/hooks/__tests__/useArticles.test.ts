import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import { useArticleFeedbackListQuery } from "../useArticles";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<
    typeof getAuthenticatedApiClient
  >;

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

const emptyClientResponse = {
  items: [],
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
  let mockFeedbackList: jest.Mock;

  beforeEach(() => {
    mockFeedbackList = jest.fn().mockResolvedValue(emptyClientResponse);
    mockGetAuthenticatedApiClient.mockReturnValue({
      articles_FeedbackList: mockFeedbackList,
    } as any);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it("passes sortDescending=true to the API client (not descending)", async () => {
    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: true }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      null,      // hasFeedback
      null,      // requestedBy
      undefined, // sortBy
      true,      // sortDescending
      undefined, // page
      undefined, // pageSize
    );
  });

  it("passes sortDescending=false when toggled off", async () => {
    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: false }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      null,
      null,
      undefined,
      false,
      undefined,
      undefined,
    );
  });

  it("passes sortDescending=undefined when not specified (backend default applies)", async () => {
    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortBy: "CreatedAt" }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      null,
      null,
      "CreatedAt",
      undefined,
      undefined,
      undefined,
    );
  });

  it("passes all filter params including sortDescending to the API client", async () => {
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

    expect(mockFeedbackList).toHaveBeenCalledWith(
      true,
      "user@anela.cz",
      "CreatedAt",
      false,
      2,
      10,
    );
  });
});
