import React from "react";
import { render, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TransportBoxList from "../TransportBoxList";
import {
  useTransportBoxesQuery,
  useTransportBoxSummaryQuery,
} from "../../../api/hooks/useTransportBoxes";
import { useStockUpOperationsSummary } from "../../../api/hooks/useStockUpOperations";
import { TestRouterWrapper } from "../../../test-utils/router-wrapper";

// Mock the hooks
jest.mock("../../../api/hooks/useTransportBoxes", () => ({
  useTransportBoxesQuery: jest.fn(),
  useTransportBoxSummaryQuery: jest.fn(),
}));

// Mock the StockUp operations hook
jest.mock("../../../api/hooks/useStockUpOperations", () => ({
  useStockUpOperationsSummary: jest.fn(),
}));

// Mock the CatalogAutocomplete component
jest.mock("../../common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: jest.fn(({ onSelect, placeholder }) => (
    <input
      placeholder={placeholder}
      data-testid="catalog-autocomplete"
      onChange={(e) => onSelect && onSelect(null)}
    />
  )),
}));

// Mock the TransportBoxDetail component
jest.mock("../TransportBoxDetail", () => {
  return function MockTransportBoxDetail({ isOpen, onClose, boxId }: any) {
    return isOpen ? (
      <div data-testid="transport-box-detail-modal">
        <div>Transport Box Detail Modal - Box ID: {boxId}</div>
        <button onClick={onClose}>Close Modal</button>
      </div>
    ) : null;
  };
});

// Mock the StockUpOperationStatusIndicator component
jest.mock("../../common/StockUpOperationStatusIndicator", () => {
  return function MockStockUpOperationStatusIndicator() {
    return null;
  };
});

// Mock the API client
jest.mock("../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    catalog: ["catalog"],
    transportBox: ["transport-boxes"],
    transportBoxTransitions: ["transportBoxTransitions"],
    stockUpOperations: ["stock-up-operations"],
  },
}));

// Mock the generated API client
jest.mock("../../../api/generated/api-client", () => ({
  CreateNewTransportBoxRequest: jest.fn().mockImplementation((data) => data),
  ProductType: {
    Material: "Material",
    Product: "Product",
    SemiProduct: "SemiProduct",
    Goods: "Goods",
    Set: "Set",
    UNDEFINED: "UNDEFINED",
  },
  StockUpSourceType: {
    TransportBox: "TransportBox",
    GiftPackageManufacture: "GiftPackageManufacture",
  },
}));

// Mock the telemetry hook
jest.mock("../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

// Mock PermissionsContext with dynamic control
let mockHasPermission: (perm: string) => boolean = () => true;
let mockPermsLoading = false;

jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: mockPermsLoading,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));

const mockUseTransportBoxesQuery = useTransportBoxesQuery as jest.Mock;
const mockUseTransportBoxSummaryQuery =
  useTransportBoxSummaryQuery as jest.Mock;
const mockUseStockUpOperationsSummary = useStockUpOperationsSummary as jest.Mock;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return (
    <TestRouterWrapper>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </TestRouterWrapper>
  );
};

const mockTransportBoxes = [
  {
    id: 1,
    code: "BOX-001",
    state: "New",
    description: "Test box 1",
    createdAt: "2024-01-01T10:00:00Z",
    updatedAt: "2024-01-01T10:00:00Z",
    itemsCount: 5,
    location: null,
  },
];

const mockSummaryData = {
  totalBoxes: 1,
  activeBoxes: 1,
  statesCounts: {
    New: 1,
    Opened: 0,
    InTransit: 0,
    Received: 0,
    Stocked: 0,
    Reserve: 0,
    Closed: 0,
    Error: 0,
  },
};

describe("TransportBoxList — StockUpOperations summary permission gate", () => {
  const mockRefetch = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();

    // Reset permission controls
    mockPermsLoading = false;
    mockHasPermission = () => true;

    // Default mocks
    mockUseTransportBoxesQuery.mockReturnValue({
      data: {
        items: mockTransportBoxes,
        totalCount: 1,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });

    mockUseTransportBoxSummaryQuery.mockReturnValue({
      data: mockSummaryData,
      isLoading: false,
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

    render(<TransportBoxList />, { wrapper: createWrapper });

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
        totalInQueue: 5,
        failedCount: 2,
      },
      isLoading: false,
      error: null,
    });

    render(<TransportBoxList />, { wrapper: createWrapper });

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

    render(<TransportBoxList />, { wrapper: createWrapper });

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
