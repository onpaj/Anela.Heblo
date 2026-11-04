import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
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

describe("ManufactureOrderDetail - lot number and expiration", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockNavigate.mockClear();

    // Default mock setups
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
    mockUseDuplicateManufactureOrder.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    });
  });

  // Lot number and expiration are computed by the backend from the planned date. The form only
  // shows the server's values (and lets the user override them by hand).
  const makeOrder = (state: ManufactureOrderState, overrides: Record<string, unknown> = {}) => ({
    id: 1,
    orderNumber: "VZ-2024-001",
    state,
    plannedDate: new Date("2024-01-15"),
    responsiblePerson: "Test User",
    semiProduct: {
      productCode: "SP-001",
      productName: "Semi Product",
      plannedQuantity: 100,
      expirationMonths: 12,
      lotNumber: "0324",
      expirationDate: new Date("2025-02-28"),
    },
    products: [],
    notes: [],
    manualActionRequired: false,
    ...overrides,
  });

  const renderWithOrder = (order: ReturnType<typeof makeOrder>) => {
    mockUseManufactureOrderDetailQuery.mockReturnValue({
      data: { order },
      isLoading: false,
      error: null,
    });
    return render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
      wrapper: createWrapper(),
    });
  };

  const waitForForm = () =>
    waitFor(() => {
      expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
    });

  const lotInput = () => screen.getByPlaceholderText("2926") as HTMLInputElement;

  it.each([ManufactureOrderState.Draft, ManufactureOrderState.Planned])(
    "shows the server's lot and expiration on load (%s)",
    async (state) => {
      renderWithOrder(makeOrder(state));
      await waitForForm();

      expect(lotInput().value).toBe("0324");
      expect(screen.getByDisplayValue("2025-02")).toBeInTheDocument();
    }
  );

  it.each([ManufactureOrderState.Draft, ManufactureOrderState.Planned])(
    "does not compute lot or expiration in the browser when the planned date changes (%s)",
    async (state) => {
      renderWithOrder(makeOrder(state));
      await waitForForm();

      fireEvent.change(screen.getByDisplayValue("2024-01-15"), { target: { value: "2024-09-16" } });

      await waitFor(() => {
        expect(screen.getByDisplayValue("2024-09-16")).toBeInTheDocument();
      });
      expect(lotInput().value).toBe("0324");
      expect(screen.getByDisplayValue("2025-02")).toBeInTheDocument();
    }
  );

  it("sends the new planned date with the stored lot and expiration so the backend recomputes them", async () => {
    const mutateAsync = jest.fn().mockResolvedValue({});
    mockUseUpdateManufactureOrder.mockReturnValue({ mutateAsync, isPending: false });
    renderWithOrder(makeOrder(ManufactureOrderState.Planned));
    await waitForForm();

    fireEvent.change(screen.getByDisplayValue("2024-01-15"), { target: { value: "2024-09-16" } });
    fireEvent.click(screen.getByRole("button", { name: /^Save$/ }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    const request = mutateAsync.mock.calls[0][0];
    expect(request.plannedDate.toISOString().slice(0, 10)).toBe("2024-09-16");
    expect(request.semiProduct.lotNumber).toBe("0324");
  });

  it("shows the recomputed lot and expiration once the order is refetched after save", async () => {
    const { rerender } = renderWithOrder(makeOrder(ManufactureOrderState.Planned));
    await waitForForm();

    // Simulate the detail query refetch after the update mutation invalidates it.
    mockUseManufactureOrderDetailQuery.mockReturnValue({
      data: {
        order: makeOrder(ManufactureOrderState.Planned, {
          plannedDate: new Date("2024-09-16"),
          semiProduct: {
            productCode: "SP-001",
            productName: "Semi Product",
            plannedQuantity: 100,
            expirationMonths: 12,
            lotNumber: "3824",
            expirationDate: new Date("2025-10-31"),
          },
        }),
      },
      isLoading: false,
      error: null,
    });
    rerender(<ManufactureOrderDetail orderId={1} isOpen={true} />);

    await waitFor(() => {
      expect(lotInput().value).toBe("3824");
    });
    expect(screen.getByDisplayValue("2025-10")).toBeInTheDocument();
  });

  it("still allows manual editing of lot number and expiration", async () => {
    renderWithOrder(makeOrder(ManufactureOrderState.Planned));
    await waitForForm();

    fireEvent.change(lotInput(), { target: { value: "9999" } });
    expect(lotInput().value).toBe("9999");

    const expirationInput = screen.getByDisplayValue("2025-02") as HTMLInputElement;
    fireEvent.change(expirationInput, { target: { value: "2026-12" } });
    expect(expirationInput.value).toBe("2026-12");
  });
});
