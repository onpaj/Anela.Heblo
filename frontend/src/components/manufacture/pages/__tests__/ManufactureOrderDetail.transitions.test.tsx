import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ManufactureOrderDetail from "../ManufactureOrderDetail";
import { ManufactureOrderState } from "../../../../api/generated/api-client";
import { ThemeProvider } from "../../../../contexts/ThemeContext";

// Mock i18next
jest.mock("react-i18next", () => ({
  // i18n.ts registers this plugin at import time (pulled in via the modals' error handling).
  initReactI18next: { type: "3rdParty", init: jest.fn() },
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: {
      changeLanguage: jest.fn(),
    },
  }),
}));

jest.mock("../../../../contexts/ThemeContext", () => ({
  useTheme: () => ({ theme: "light", toggle: jest.fn() }),
  ThemeProvider: ({ children }: any) => children,
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
      <ThemeProvider>
        <BrowserRouter>{children}</BrowserRouter>
      </ThemeProvider>
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

describe("ManufactureOrderDetail - available transitions", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    const mutation = { mutateAsync: jest.fn(), isPending: false };
    mockUseUpdateManufactureOrder.mockReturnValue(mutation);
    mockUseUpdateManufactureOrderStatus.mockReturnValue(mutation);
    mockUseConfirmSemiProductManufacture.mockReturnValue(mutation);
    mockUseConfirmProductCompletion.mockReturnValue(mutation);
    mockUseDuplicateManufactureOrder.mockReturnValue(mutation);
  });

  const renderWith = async (availableTransitions: ManufactureOrderState[]) => {
    mockUseManufactureOrderDetailQuery.mockReturnValue({
      data: { order: { ...mockOrder, state: ManufactureOrderState.Planned }, availableTransitions },
      isLoading: false,
      error: null,
    });
    render(<ManufactureOrderDetail orderId={42} isOpen={true} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText(/Výrobní zakázka VZ-2024-042/i)).toBeInTheDocument();
    });
  };

  it("renders the transition buttons when the backend includes their targets", async () => {
    await renderWith([ManufactureOrderState.Draft, ManufactureOrderState.SemiProductManufactured]);
    expect(screen.getByTitle(/^Zpět na:/)).toBeInTheDocument();
    expect(screen.getByTitle(/^Pokračovat na:/)).toBeInTheDocument();
  });

  it("does not render a transition button when availableTransitions omits its target", async () => {
    await renderWith([ManufactureOrderState.Draft]);
    expect(screen.getByTitle(/^Zpět na:/)).toBeInTheDocument();
    expect(screen.queryByTitle(/^Pokračovat na:/)).not.toBeInTheDocument();
  });
});
