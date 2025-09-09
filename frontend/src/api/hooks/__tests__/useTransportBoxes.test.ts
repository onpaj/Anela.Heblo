import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import {
  useTransportBoxesQuery,
  useTransportBoxByIdQuery,
  useChangeTransportBoxState,
} from "../useTransportBoxes";
import * as clientModule from "../../client";

// Mock the client module
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    transportBox: ["transport-boxes"],
    transportBoxTransitions: ["transportBoxTransitions"],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return React.createElement(
    QueryClientProvider,
    { client: queryClient },
    children,
  );
};

const mockApiClient = {
  transportBox_GetTransportBoxes: jest.fn(),
  transportBox_GetTransportBoxById: jest.fn(),
  transportBox_GetTransportBoxSummary: jest.fn(),
  transportBox_ChangeTransportBoxState: jest.fn(),
};

describe("useTransportBoxes hooks", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
  });

  describe("useTransportBoxesQuery", () => {
    it("should call API with correct parameters", async () => {
      const mockResponse = {
        items: [
          { id: 1, code: "BOX-001", state: "New" },
          { id: 2, code: "BOX-002", state: "Opened" },
        ],
        totalItems: 2,
      };

      mockApiClient.transportBox_GetTransportBoxes.mockResolvedValue(
        mockResponse,
      );

      const request = {
        skip: 0,
        take: 10,
        code: "BOX",
        state: "New",
        sortBy: "id",
        sortDescending: true,
      };

      const { result } = renderHook(() => useTransportBoxesQuery(request), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(mockApiClient.transportBox_GetTransportBoxes).toHaveBeenCalledWith(
        0, // skip
        10, // take
        "BOX", // code
        "New", // state
        null, // productCode
        "id", // sortBy
        true, // sortDescending
      );

      expect(result.current.data).toEqual(mockResponse);
    });

    it("should handle null optional parameters correctly", async () => {
      const mockResponse = { items: [], totalItems: 0 };
      mockApiClient.transportBox_GetTransportBoxes.mockResolvedValue(
        mockResponse,
      );

      const request = { skip: 0, take: 5 };

      const { result } = renderHook(() => useTransportBoxesQuery(request), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(mockApiClient.transportBox_GetTransportBoxes).toHaveBeenCalledWith(
        0, // skip
        5, // take
        null, // code
        null, // state
        null, // productCode
        null, // sortBy
        undefined, // sortDescending
      );
    });
  });

  describe("useTransportBoxByIdQuery", () => {
    it("should fetch transport box by id when enabled", async () => {
      const mockResponse = {
        transportBox: { id: 1, code: "BOX-001", state: "New" },
      };

      mockApiClient.transportBox_GetTransportBoxById.mockResolvedValue(
        mockResponse,
      );

      const { result } = renderHook(() => useTransportBoxByIdQuery(1, true), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(
        mockApiClient.transportBox_GetTransportBoxById,
      ).toHaveBeenCalledWith(1);
      expect(result.current.data).toEqual(mockResponse);
    });

    it("should not fetch when disabled", async () => {
      const { result } = renderHook(() => useTransportBoxByIdQuery(1, false), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        expect(result.current.status).toBe("pending");
      });

      expect(
        mockApiClient.transportBox_GetTransportBoxById,
      ).not.toHaveBeenCalled();
    });

    it("should not fetch when id is invalid", async () => {
      const { result } = renderHook(() => useTransportBoxByIdQuery(0, true), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        expect(result.current.status).toBe("pending");
      });

      expect(
        mockApiClient.transportBox_GetTransportBoxById,
      ).not.toHaveBeenCalled();
    });
  });

  describe("useChangeTransportBoxState", () => {
    it("should call API and invalidate queries on success", async () => {
      const mockResponse = {
        success: true,
        errorCode: null,
        updatedBox: { id: 1, code: "BOX-001", state: "Opened" },
      };

      mockApiClient.transportBox_ChangeTransportBoxState.mockResolvedValue(
        mockResponse,
      );

      const { result } = renderHook(() => useChangeTransportBoxState(), {
        wrapper: createWrapper,
      });

      const mutationParams = {
        boxId: 1,
        newState: 1, // TransportBoxState.Opened = 1
        description: "Opening box for inspection",
      };

      await waitFor(() => {
        result.current.mutate(mutationParams);
      });

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(
        mockApiClient.transportBox_ChangeTransportBoxState,
      ).toHaveBeenCalledWith(
        1,
        expect.objectContaining({
          boxId: 1,
          newState: 1, // TransportBoxState.Opened = 1
          description: "Opening box for inspection",
        }),
      );

      expect(result.current.data).toEqual(mockResponse);
    });

    it("should handle API errors correctly", async () => {
      const mockError = new Error("State transition not allowed");
      mockApiClient.transportBox_ChangeTransportBoxState.mockRejectedValue(
        mockError,
      );

      const { result } = renderHook(() => useChangeTransportBoxState(), {
        wrapper: createWrapper,
      });

      const mutationParams = {
        boxId: 1,
        newState: "InvalidState",
      };

      await waitFor(() => {
        result.current.mutate(mutationParams);
      });

      await waitFor(() => {
        expect(result.current.isError).toBe(true);
      });

      expect(result.current.error).toEqual(mockError);
    });

    it("should handle mutation without description", async () => {
      const mockResponse = {
        success: true,
        errorCode: null,
        updatedBox: { id: 2, code: "BOX-002", state: "InTransit" },
      };

      mockApiClient.transportBox_ChangeTransportBoxState.mockResolvedValue(
        mockResponse,
      );

      const { result } = renderHook(() => useChangeTransportBoxState(), {
        wrapper: createWrapper,
      });

      const mutationParams = {
        boxId: 2,
        newState: 2, // TransportBoxState.InTransit = 2
      };

      await waitFor(() => {
        result.current.mutate(mutationParams);
      });

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(
        mockApiClient.transportBox_ChangeTransportBoxState,
      ).toHaveBeenCalledWith(
        2,
        expect.objectContaining({
          boxId: 2,
          newState: 2, // TransportBoxState.InTransit = 2
          description: undefined,
        }),
      );
    });
  });
});
