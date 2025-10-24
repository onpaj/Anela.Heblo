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

describe("ManufactureOrderDetail - Auto-calculation Logic", () => {
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

  describe("On Form Open (Initial Load)", () => {
    it("should NOT auto-calculate lot number and expiration when order loads in Draft state", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Draft,
        plannedDate: new Date("2024-01-15"),
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 12,
          lotNumber: "03202401", // Existing lot number from DB
          expirationDate: new Date("2025-02-28"), // Existing expiration from DB
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Check that lot number and expiration remain unchanged from DB
      const lotNumberInput = screen.getByPlaceholderText("38202412") as HTMLInputElement;
      const expirationInput = screen.getByDisplayValue("2025-02") as HTMLInputElement;

      expect(lotNumberInput.value).toBe("03202401"); // Original from DB
      expect(expirationInput.value).toBe("2025-02"); // Original from DB
    });

    it("should NOT auto-calculate lot number and expiration when order loads in Planned state", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Planned,
        plannedDate: new Date("2024-01-15"),
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 12,
          lotNumber: "03202401",
          expirationDate: new Date("2025-02-28"),
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      const lotNumberInput = screen.getByPlaceholderText("38202412") as HTMLInputElement;
      const expirationInput = screen.getByDisplayValue("2025-02") as HTMLInputElement;

      expect(lotNumberInput.value).toBe("03202401");
      expect(expirationInput.value).toBe("2025-02");
    });
  });

  describe("On Production Date Change in Draft State", () => {
    it("should auto-calculate lot number and expiration when production date changes in Draft state", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Draft,
        plannedDate: new Date("2024-01-15"), // Week 3, 2024-01
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 12,
          lotNumber: "03202401",
          expirationDate: new Date("2025-02-28"),
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Change production date to 2024-09-16 (Week 38, September 2024)
      const dateInput = screen.getByDisplayValue("2024-01-15") as HTMLInputElement;
      fireEvent.change(dateInput, { target: { value: "2024-09-16" } });

      await waitFor(() => {
        const lotNumberInput = screen.getByPlaceholderText("38202412") as HTMLInputElement;
        const expirationInput = screen.getByDisplayValue(/2025-10/) as HTMLInputElement;

        // Expected lot number: Week 38, 2024, September (09)
        expect(lotNumberInput.value).toBe("38202409");

        // Expected expiration: 2024-09-16 + 12 months + 1 = 2025-10-31 (last day of October)
        expect(expirationInput.value).toBe("2025-10");
      });
    });

    it("should calculate correct lot number format: {week}{year}{month}", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Draft,
        plannedDate: new Date("2024-12-25"), // Week 52, December 2024
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 6,
          lotNumber: "",
          expirationDate: null,
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Change to 2024-03-05 (Week 10, March 2024)
      const dateInput = screen.getByDisplayValue("2024-12-25") as HTMLInputElement;
      fireEvent.change(dateInput, { target: { value: "2024-03-05" } });

      await waitFor(() => {
        const lotNumberInput = screen.getByPlaceholderText("38202412") as HTMLInputElement;
        // Expected: Week 10, 2024, March (03) = 10202403
        expect(lotNumberInput.value).toBe("10202403");
      });
    });

    it("should calculate expiration as last day of month (production date + expirationMonths + 1)", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Draft,
        plannedDate: new Date("2024-01-15"),
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 6, // 6 months shelf life
          lotNumber: "",
          expirationDate: null,
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Change to 2024-01-31 (last day of January)
      const dateInput = screen.getByDisplayValue("2024-01-15") as HTMLInputElement;
      fireEvent.change(dateInput, { target: { value: "2024-01-31" } });

      await waitFor(() => {
        const expirationInput = screen.getByDisplayValue(/2024-08/) as HTMLInputElement;
        // Production: 2024-01-31
        // + 6 months = 2024-07-31
        // + 1 month = 2024-08-31 (last day of August)
        expect(expirationInput.value).toBe("2024-08");
      });
    });

    it("should handle leap year February correctly", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Draft,
        plannedDate: new Date("2024-01-15"),
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 1, // 1 month shelf life
          lotNumber: "",
          expirationDate: null,
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Change to 2024-01-15 (user actively changes date even if it's the same value)
      const dateInput = screen.getByDisplayValue("2024-01-15") as HTMLInputElement;
      fireEvent.change(dateInput, { target: { value: "2024-01-10" } }); // Change to different date first
      fireEvent.change(dateInput, { target: { value: "2024-01-15" } }); // Then back to trigger calculation

      await waitFor(() => {
        const expirationInput = screen.getByDisplayValue(/2024-03/) as HTMLInputElement;
        // Production: 2024-01-15
        // + 1 month (expirationMonths) = 2024-02-15
        // + 1 month (additional) = 2024-03-15
        // Last day of March = 2024-03-31
        expect(expirationInput.value).toBe("2024-03");
      });
    });
  });

  describe("On Production Date Change in Planned State", () => {
    it("should NOT auto-calculate lot number and expiration when production date changes in Planned state", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Planned,
        plannedDate: new Date("2024-01-15"),
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 12,
          lotNumber: "03202401", // Should remain unchanged
          expirationDate: new Date("2025-02-28"), // Should remain unchanged
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Change production date
      const dateInput = screen.getByDisplayValue("2024-01-15") as HTMLInputElement;
      fireEvent.change(dateInput, { target: { value: "2024-09-16" } });

      // Lot number and expiration should NOT change
      await waitFor(() => {
        const lotNumberInput = screen.getByPlaceholderText("38202412") as HTMLInputElement;
        const expirationInput = screen.getByDisplayValue("2025-02") as HTMLInputElement;

        expect(lotNumberInput.value).toBe("03202401"); // Still original
        expect(expirationInput.value).toBe("2025-02"); // Still original
      });
    });

    it("should allow manual editing of lot number and expiration in Planned state", async () => {
      const mockOrder = {
        id: 1,
        orderNumber: "VZ-2024-001",
        state: ManufactureOrderState.Planned,
        plannedDate: new Date("2024-01-15"),
        responsiblePerson: "Test User",
        semiProduct: {
          productCode: "SP-001",
          productName: "Semi Product",
          plannedQuantity: 100,
          expirationMonths: 12,
          lotNumber: "03202401",
          expirationDate: new Date("2025-02-28"),
        },
        products: [],
        notes: [],
        manualActionRequired: false,
      };

      mockUseManufactureOrderDetailQuery.mockReturnValue({
        data: { order: mockOrder },
        isLoading: false,
        error: null,
      });

      render(<ManufactureOrderDetail orderId={1} isOpen={true} />, {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(screen.getByText(/Výrobní zakázka VZ-2024-001/i)).toBeInTheDocument();
      });

      // Manually edit lot number
      const lotNumberInput = screen.getByPlaceholderText("38202412") as HTMLInputElement;
      fireEvent.change(lotNumberInput, { target: { value: "99999999" } });

      expect(lotNumberInput.value).toBe("99999999");

      // Manually edit expiration
      const expirationInput = screen.getByDisplayValue("2025-02") as HTMLInputElement;
      fireEvent.change(expirationInput, { target: { value: "2026-12" } });

      expect(expirationInput.value).toBe("2026-12");
    });
  });
});
