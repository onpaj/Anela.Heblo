import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import GiftPackageManufacturing from "../index";
import {
  useCreateGiftPackageManufacture,
  useEnqueueGiftPackageManufacture,
} from "../../../../api/hooks/useGiftPackageManufacturing";
import { useStockUpOperationsSummary } from "../../../../api/hooks/useStockUpOperations";

// Mock the manufacturing hooks
jest.mock("../../../../api/hooks/useGiftPackageManufacturing", () => ({
  useCreateGiftPackageManufacture: jest.fn(),
  useEnqueueGiftPackageManufacture: jest.fn(),
}));

// Mock the StockUp operations hook
jest.mock("../../../../api/hooks/useStockUpOperations", () => ({
  useStockUpOperationsSummary: jest.fn(),
}));

// Mock sub-components
jest.mock("../GiftPackageManufacturingList", () => {
  return function MockGiftPackageManufacturingList() {
    return <div data-testid="gift-package-manufacturing-list" />;
  };
});

jest.mock("../GiftPackageManufacturingDetail", () => {
  return function MockGiftPackageManufacturingDetail() {
    return <div data-testid="gift-package-manufacturing-detail" />;
  };
});

jest.mock("../../CatalogDetail", () => {
  return function MockCatalogDetail() {
    return <div data-testid="catalog-detail" />;
  };
});

jest.mock("../../../common/StockUpOperationStatusIndicator", () => {
  return function MockStockUpOperationStatusIndicator() {
    return <div data-testid="stock-up-operation-status-indicator" />;
  };
});

// Mock the API client
jest.mock("../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    catalog: ["catalog"],
    giftPackageManufacturing: ["gift-package-manufacturing"],
    stockUpOperations: ["stock-up-operations"],
  },
}));

// Mock the generated API client
jest.mock("../../../../api/generated/api-client", () => ({
  CreateGiftPackageManufactureRequest: jest
    .fn()
    .mockImplementation((data) => data),
  EnqueueGiftPackageManufactureRequest: jest
    .fn()
    .mockImplementation((data) => data),
  StockUpSourceType: {
    TransportBox: "TransportBox",
    GiftPackageManufacture: "GiftPackageManufacture",
  },
}));

// Mock the telemetry hook
jest.mock("../../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

// Mock PermissionsContext with dynamic control
let mockHasPermission: (perm: string) => boolean = () => true;
let mockPermsLoading = false;

jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: mockPermsLoading,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));

const mockUseCreateGiftPackageManufacture = useCreateGiftPackageManufacture as jest.Mock;
const mockUseEnqueueGiftPackageManufacture = useEnqueueGiftPackageManufacture as jest.Mock;
const mockUseStockUpOperationsSummary = useStockUpOperationsSummary as jest.Mock;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe("GiftPackageManufacturing — StockUpOperations summary permission gate", () => {
  beforeEach(() => {
    jest.clearAllMocks();

    // Reset permission controls
    mockPermsLoading = false;
    mockHasPermission = () => true;

    // Default mocks for mutations
    mockUseCreateGiftPackageManufacture.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
      error: null,
    });

    mockUseEnqueueGiftPackageManufacture.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
      error: null,
    });

    mockUseStockUpOperationsSummary.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    });
  });

  it("does NOT call stockUpOperations_GetSummary when user lacks warehouse.stock_up.read", async () => {
    // User does not have the required permission
    mockHasPermission = (perm: string) => {
      if (perm === "warehouse.stock_up.read") {
        return false;
      }
      return true;
    };

    render(<GiftPackageManufacturing />, { wrapper: createWrapper });

    // Wait a bit to ensure no API call is made
    await new Promise((r) => setTimeout(r, 100));

    // Assert the hook was called with enabled: false
    expect(mockUseStockUpOperationsSummary).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        enabled: false,
      }),
    );
  });

  it("calls stockUpOperations_GetSummary when user holds warehouse.stock_up.read", async () => {
    // User has the required permission
    mockHasPermission = (perm: string) => {
      if (perm === "warehouse.stock_up.read") {
        return true;
      }
      return true;
    };

    // Mock return value with some data
    mockUseStockUpOperationsSummary.mockReturnValue({
      data: {
        totalInQueue: 3,
        failedCount: 1,
      },
      isLoading: false,
      error: null,
    });

    render(<GiftPackageManufacturing />, { wrapper: createWrapper });

    // Assert the hook was called with enabled: true
    await waitFor(() => {
      expect(mockUseStockUpOperationsSummary).toHaveBeenCalledWith(
        expect.anything(),
        expect.objectContaining({
          enabled: true,
        }),
      );
    });
  });

  it("does NOT call stockUpOperations_GetSummary while permissions are loading", async () => {
    // Permissions are still loading
    mockPermsLoading = true;
    mockHasPermission = () => true; // Will be ignored since isLoading is true

    render(<GiftPackageManufacturing />, { wrapper: createWrapper });

    // Wait a bit to ensure no API call is made with enabled: true
    await new Promise((r) => setTimeout(r, 100));

    // Assert the hook was called with enabled: false (because permissions are loading)
    expect(mockUseStockUpOperationsSummary).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        enabled: false,
      }),
    );
  });
});
