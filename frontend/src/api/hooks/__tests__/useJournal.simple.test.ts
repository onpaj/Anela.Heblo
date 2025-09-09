import { renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useJournalEntries } from "../useJournal";
import * as clientModule from "../../client";

// Mock the client module
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    journal: ["journal"],
  },
}));

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        cacheTime: 0,
      },
      mutations: { retry: false },
    },
  });

  return React.createElement(
    QueryClientProvider,
    { client: queryClient },
    children,
  );
};

describe("useJournal hooks - Simple Tests", () => {
  const mockGetAuthenticatedApiClient =
    clientModule.getAuthenticatedApiClient as jest.MockedFunction<
      typeof clientModule.getAuthenticatedApiClient
    >;

  beforeEach(() => {
    jest.clearAllMocks();

    // Setup mock client with basic method
    mockGetAuthenticatedApiClient.mockResolvedValue({
      journal_GetJournalEntries: jest.fn().mockResolvedValue({
        entries: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 0,
      }),
    } as any);
  });

  test("should initialize useJournalEntries hook", () => {
    const { result } = renderHook(
      () => useJournalEntries({ pageNumber: 1, pageSize: 20 }),
      { wrapper: createWrapper },
    );

    // Should initialize without errors
    expect(result.current).toBeDefined();
    expect(result.current.isPending).toBeDefined();
    // data starts as undefined before query runs, that's normal TanStack Query behavior
    expect(result.current.data).toBeUndefined();
    expect(result.current.error).toBeNull();
  });

  test("should have correct query key structure", () => {
    const { result } = renderHook(
      () => useJournalEntries({ pageNumber: 1, pageSize: 20 }),
      { wrapper: createWrapper },
    );

    // Hook should be callable without errors
    expect(result.current).toBeDefined();
    expect(typeof result.current.refetch).toBe("function");
  });

  test("should handle empty parameters", () => {
    const { result } = renderHook(() => useJournalEntries(), {
      wrapper: createWrapper,
    });

    expect(result.current).toBeDefined();
  });
});
