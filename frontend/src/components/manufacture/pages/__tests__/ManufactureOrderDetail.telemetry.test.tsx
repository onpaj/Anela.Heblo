import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ManufactureOrderDetail from "../ManufactureOrderDetail";
import { ManufactureOrderState } from "../../../../api/generated/api-client";

// Mock i18next
jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: {
      changeLanguage: jest.fn(),
    },
  }),
}));

// Mock PermissionsContext (ManufactureOrderDetail renders ResponsiblePersonCombobox,
// which calls usePermissionsContext and would otherwise throw outside a PermissionsProvider)
jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    hasPermission: () => true,
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
  }),
}));

// Mock the API client
jest.mock("../../../../api/generated/api-client", () => ({
  ...jest.requireActual("../../../../api/generated/api-client"),
  ManufactureOrderState: {
    Draft: "Draft",
    Planned: "Planned",
    SemiProductManufactured: "SemiProductManufactured",
    Completed: "Completed",
    Cancelled: "Cancelled",
  },
}));

// Mock useTelemetry
const mockTrackEvent = jest.fn();
jest.mock("../../../../telemetry/useTelemetry", () => ({
  useTelemetry: () => ({
    trackEvent: mockTrackEvent,
    trackException: jest.fn(),
    trackMetric: jest.fn(),
  }),
}));

// Mock the API hooks
const mockUseManufactureOrderDetailQuery = jest.fn();
const mockUseUpdateManufactureOrder = jest.fn();
const mockUseUpdateManufactureOrderStatus = jest.fn();
const mockUseConfirmSemiProductManufacture = jest.fn();
const mockUseConfirmProductCompletion = jest.fn();
const mockUseDuplicateManufactureOrder = jest.fn();

jest.mock("../../../../api/hooks/useManufactureOrders", () => ({
  useManufactureOrderDetailQuery: (id: number) => mockUseManufactureOrderDetailQuery(id),
  useUpdateManufactureOrder: () => mockUseUpdateManufactureOrder(),
  useUpdateManufactureOrderStatus: () => mockUseUpdateManufactureOrderStatus(),
  useConfirmSemiProductManufacture: () => mockUseConfirmSemiProductManufacture(),
  useConfirmProductCompletion: () => mockUseConfirmProductCompletion(),
  useDuplicateManufactureOrder: () => mockUseDuplicateManufactureOrder(),
  useOpenManufactureProtocol: () => ({ openProtocol: jest.fn(), isLoading: false, error: null }),
}));

// Mock react-router-dom navigate
const mockNavigate = jest.fn();
jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

const mockOrder = {
  id: 42,
  orderNumber: "VZ-2024-042",
  state: ManufactureOrderState.Draft,
  plannedDate: new Date("2024-06-15"),
  responsiblePerson: "Test User",
  productCode: "PROD-001",
  semiProduct: {
    productCode: "SP-001",
    productName: "Semi Product",
    plannedQuantity: 100,
    expirationMonths: 12,
    lotNumber: "22202406",
    expirationDate: new Date("2025-07-31"),
  },
  products: [],
  notes: [],
  manualActionRequired: false,
};

describe("ManufactureOrderDetail - Telemetry", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockNavigate.mockClear();

    mockUseUpdateManufactureOrder.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    });
    mockUseUpdateManufactureOrderStatus.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    });
    mockUseConfirmSemiProductManufacture.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    });
    mockUseConfirmProductCompletion.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    });
  });

  it("tracks ManufactureOrderCreated with productCode when duplication succeeds", async () => {
    const mockDuplicateMutateAsync = jest.fn().mockResolvedValue({ id: 99 });
    mockUseDuplicateManufactureOrder.mockReturnValue({
      mutateAsync: mockDuplicateMutateAsync,
      isPending: false,
    });

    mockUseManufactureOrderDetailQuery.mockReturnValue({
      data: { order: mockOrder },
      isLoading: false,
      error: null,
    });

    render(<ManufactureOrderDetail orderId={42} isOpen={true} />, {
      wrapper: createWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText(/Výrobní zakázka VZ-2024-042/i)).toBeInTheDocument();
    });

    const duplicateButton = screen.getByTitle("Duplikovat zakázku");
    fireEvent.click(duplicateButton);

    await waitFor(() => {
      expect(mockDuplicateMutateAsync).toHaveBeenCalledWith(42);
      expect(mockTrackEvent).toHaveBeenCalledWith("ManufactureOrderCreated", {
        productCode: "SP-001",
      });
    });
  });

  it("does not track ManufactureOrderCreated when duplication fails", async () => {
    const mockDuplicateMutateAsync = jest.fn().mockRejectedValue(new Error("Duplication failed"));
    mockUseDuplicateManufactureOrder.mockReturnValue({
      mutateAsync: mockDuplicateMutateAsync,
      isPending: false,
    });

    mockUseManufactureOrderDetailQuery.mockReturnValue({
      data: { order: mockOrder },
      isLoading: false,
      error: null,
    });

    render(<ManufactureOrderDetail orderId={42} isOpen={true} />, {
      wrapper: createWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText(/Výrobní zakázka VZ-2024-042/i)).toBeInTheDocument();
    });

    const duplicateButton = screen.getByTitle("Duplikovat zakázku");
    fireEvent.click(duplicateButton);

    await waitFor(() => {
      expect(mockDuplicateMutateAsync).toHaveBeenCalledWith(42);
    });

    expect(mockTrackEvent).not.toHaveBeenCalledWith('ManufactureOrderCreated', expect.anything());
  });

  it("uses empty string as productCode fallback when order has no productCode", async () => {
    const orderWithoutProductCode = { ...mockOrder, semiProduct: { ...mockOrder.semiProduct, productCode: undefined } };
    const mockDuplicateMutateAsync = jest.fn().mockResolvedValue({ id: 100 });
    mockUseDuplicateManufactureOrder.mockReturnValue({
      mutateAsync: mockDuplicateMutateAsync,
      isPending: false,
    });

    mockUseManufactureOrderDetailQuery.mockReturnValue({
      data: { order: orderWithoutProductCode },
      isLoading: false,
      error: null,
    });

    render(<ManufactureOrderDetail orderId={42} isOpen={true} />, {
      wrapper: createWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText(/Výrobní zakázka VZ-2024-042/i)).toBeInTheDocument();
    });

    const duplicateButton = screen.getByTitle("Duplikovat zakázku");
    fireEvent.click(duplicateButton);

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith("ManufactureOrderCreated", {
        productCode: "",
      });
    });
  });
});
