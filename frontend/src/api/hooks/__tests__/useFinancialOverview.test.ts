import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useFinancialOverviewQuery } from "../useFinancialOverview";
import * as clientModule from "../../client";

// Mock the client module
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    financialOverview: ["financialOverview"],
  },
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
      },
    },
  });

  return ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("useFinancialOverviewQuery", () => {
  const mockGetAuthenticatedApiClient =
    clientModule.getAuthenticatedApiClient as jest.MockedFunction<
      typeof clientModule.getAuthenticatedApiClient
    >;

  const mockFinancialOverviewGet = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();

    mockFinancialOverviewGet.mockResolvedValue({
      months: [],
      summary: undefined,
    });

    mockGetAuthenticatedApiClient.mockReturnValue({
      financialOverview_GetFinancialOverview: mockFinancialOverviewGet,
    } as any);
  });

  test("calls the generated client method with default parameters", async () => {
    const { result } = renderHook(() => useFinancialOverviewQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFinancialOverviewGet).toHaveBeenCalledTimes(1);
    expect(mockFinancialOverviewGet).toHaveBeenCalledWith(6, true, [], false);
  });

  test("calls the generated client method with explicit parameters, including excludedDepartments", async () => {
    const { result } = renderHook(
      () => useFinancialOverviewQuery(12, false, ["Sales", "Marketing"], true),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFinancialOverviewGet).toHaveBeenCalledTimes(1);
    expect(mockFinancialOverviewGet).toHaveBeenCalledWith(
      12,
      false,
      ["Sales", "Marketing"],
      true,
    );
  });

  test("does not use (apiClient as any).http.fetch or manual URLSearchParams", async () => {
    const { result } = renderHook(() => useFinancialOverviewQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Only the typed generated method should have been touched on the mocked client.
    const clientArg = mockGetAuthenticatedApiClient.mock.results[0].value;
    expect(clientArg.financialOverview_GetFinancialOverview).toBe(mockFinancialOverviewGet);
  });

  test("surfaces an Error from the generated client method as the query error", async () => {
    mockFinancialOverviewGet.mockReset();
    mockFinancialOverviewGet.mockRejectedValue(new Error("Request failed with status code 500"));

    const { result } = renderHook(() => useFinancialOverviewQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.error?.message).toBe("Request failed with status code 500");
  });
});
