import React from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  useManufactureOutputQuery,
  formatMonthDisplay,
  getMonthShortName,
} from "../useManufactureOutput";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<
    typeof getAuthenticatedApiClient
  >;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe("useManufactureOutputQuery", () => {
  const mockApiClient = {
    manufactureOutput_GetManufactureOutput: jest.fn(),
  };

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("calls the generated typed client method with monthsBack, not raw fetch", async () => {
    const mockResponse = {
      months: [
        {
          month: "2024-01",
          totalOutput: 10,
          products: [],
          productionDetails: [],
        },
      ],
    };
    mockApiClient.manufactureOutput_GetManufactureOutput.mockResolvedValue(
      mockResponse,
    );
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    const wrapper = createWrapper();
    const { result } = renderHook(() => useManufactureOutputQuery(13), {
      wrapper,
    });

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    expect(result.current.data).toEqual(mockResponse);
    expect(
      mockApiClient.manufactureOutput_GetManufactureOutput,
    ).toHaveBeenCalledWith(13);
    expect((mockApiClient as any).http).toBeUndefined();
  });

  it("defaults monthsBack to 13 when not provided", async () => {
    mockApiClient.manufactureOutput_GetManufactureOutput.mockResolvedValue({
      months: [],
    });
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    const wrapper = createWrapper();
    const { result } = renderHook(() => useManufactureOutputQuery(), {
      wrapper,
    });

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    expect(
      mockApiClient.manufactureOutput_GetManufactureOutput,
    ).toHaveBeenCalledWith(13);
  });

  it("surfaces errors thrown by the generated client", async () => {
    mockApiClient.manufactureOutput_GetManufactureOutput.mockRejectedValue(
      new Error("boom"),
    );
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    const wrapper = createWrapper();
    const { result } = renderHook(() => useManufactureOutputQuery(13), {
      wrapper,
    });

    await waitFor(
      () => {
        expect(result.current.isError).toBe(true);
      },
      { timeout: 5000 },
    );

    expect(result.current.error).toBeTruthy();
  });
});

describe("formatMonthDisplay", () => {
  it("formats a YYYY-MM string into a Czech month name and year", () => {
    expect(formatMonthDisplay("2024-01")).toBe("Leden 2024");
    expect(formatMonthDisplay("2024-12")).toBe("Prosinec 2024");
  });
});

describe("getMonthShortName", () => {
  it("returns the short Czech month name", () => {
    expect(getMonthShortName("2024-01")).toBe("Led");
    expect(getMonthShortName("2024-12")).toBe("Pro");
  });
});
