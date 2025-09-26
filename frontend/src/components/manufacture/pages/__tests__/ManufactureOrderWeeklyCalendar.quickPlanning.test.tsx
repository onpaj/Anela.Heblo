// Mock the ManufactureOrderState enum first
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ManufactureOrderWeeklyCalendar from "../ManufactureOrderWeeklyCalendar";

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

// Mock the API hook
const mockUseManufactureOrderCalendarQuery = jest.fn();
jest.mock("../../../../api/hooks/useManufactureOrders", () => ({
  useManufactureOrderCalendarQuery: () => mockUseManufactureOrderCalendarQuery(),
  ManufactureOrderState: {
    Draft: "Draft",
    Planned: "Planned", 
    SemiProductManufactured: "SemiProductManufactured",
    Completed: "Completed",
    Cancelled: "Cancelled",
  },
}));

// Mock the planning list context
const mockUsePlanningList = jest.fn();
jest.mock("../../../../contexts/PlanningListContext", () => ({
  PlanningListProvider: ({ children }: any) => children,
  usePlanningList: () => mockUsePlanningList(),
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

// Mock calendar data
const mockCalendarData = {
  events: [
    {
      id: 1,
      title: "Test Order",
      date: new Date("2024-01-15"),
      state: "Planned",
      productCount: 2,
    },
  ],
};

describe("ManufactureOrderWeeklyCalendar - Quick Planning", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockNavigate.mockClear();
    mockUsePlanningList.mockClear();
  });

  describe("Quick Planning Buttons", () => {
    beforeEach(() => {
      mockUseManufactureOrderCalendarQuery.mockReturnValue({
        data: mockCalendarData,
        isLoading: false,
        error: null,
        refetch: jest.fn(),
      });
    });

    it("should not show quick planning buttons when no items in planning list", () => {
      // Mock empty planning list
      mockUsePlanningList.mockReturnValue({
        hasItems: false,
        items: [],
        removeItem: jest.fn(),
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar />
        </Wrapper>
      );

      // Quick planning buttons should not be visible
      const plusButtons = screen.queryAllByTitle("Rychlé plánování ze seznamu");
      expect(plusButtons).toHaveLength(0);
    });

    it("should show quick planning buttons when items exist in planning list", () => {
      // Mock planning list context with items
      mockUsePlanningList.mockReturnValue({
        hasItems: true,
        items: [
          {
            productCode: "TEST01",
            productName: "Test Product 1",
            addedAt: new Date(),
          },
        ],
        removeItem: jest.fn(),
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar />
        </Wrapper>
      );

      // Quick planning buttons should be visible (one for each day of the week)
      const plusButtons = screen.getAllByTitle("Rychlé plánování ze seznamu");
      expect(plusButtons.length).toBeGreaterThan(0);
      
      // Check button styling
      const firstButton = plusButtons[0];
      expect(firstButton).toHaveClass("bg-emerald-500");
      expect(firstButton).toHaveClass("hover:bg-emerald-600");
    });

    it("should open quick planning modal when plus button is clicked", async () => {
      mockUsePlanningList.mockReturnValue({
        hasItems: true,
        items: [
          {
            productCode: "TEST01",
            productName: "Test Product 1",
            addedAt: new Date(),
          },
        ],
        removeItem: jest.fn(),
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar />
        </Wrapper>
      );

      // Click on quick planning button
      const plusButton = screen.getAllByTitle("Rychlé plánování ze seznamu")[0];
      fireEvent.click(plusButton);

      // Modal should be visible
      await waitFor(() => {
        expect(screen.getByText("Rychlé plánování")).toBeInTheDocument();
      });
    });

    it("should display planning list items in modal", async () => {
      mockUsePlanningList.mockReturnValue({
        hasItems: true,
        items: [
          {
            productCode: "TEST01",
            productName: "Test Product 1",
            addedAt: new Date(),
          },
          {
            productCode: "TEST02",
            productName: "Test Product 2",
            addedAt: new Date(),
          },
        ],
        removeItem: jest.fn(),
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar />
        </Wrapper>
      );

      // Open modal
      const plusButton = screen.getAllByTitle("Rychlé plánování ze seznamu")[0];
      fireEvent.click(plusButton);

      // Check that items are displayed
      await waitFor(() => {
        expect(screen.getByText("Test Product 1")).toBeInTheDocument();
        expect(screen.getByText("TEST01")).toBeInTheDocument();
        expect(screen.getByText("Test Product 2")).toBeInTheDocument();
        expect(screen.getByText("TEST02")).toBeInTheDocument();
      });
    });

    it("should navigate to batch planning when item is clicked", async () => {
      const mockRemoveItem = jest.fn();
      mockUsePlanningList.mockReturnValue({
        hasItems: true,
        items: [
          {
            productCode: "PROD12345",
            productName: "Test Product 1",
            addedAt: new Date(),
          },
        ],
        removeItem: mockRemoveItem,
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar />
        </Wrapper>
      );

      // Open modal
      const plusButton = screen.getAllByTitle("Rychlé plánování ze seznamu")[0];
      fireEvent.click(plusButton);

      // Click on item
      await waitFor(() => {
        const productButton = screen.getByText("Test Product 1");
        fireEvent.click(productButton);
      });

      // Should navigate with correct parameters
      expect(mockNavigate).toHaveBeenCalledWith(
        expect.stringContaining("/manufacturing/batch-planning?")
      );
      
      // Should remove item from planning list
      expect(mockRemoveItem).toHaveBeenCalledWith("PROD12345");
    });

    it("should include date parameter in navigation URL", async () => {
      const mockRemoveItem = jest.fn();
      const testDate = new Date("2024-01-15");
      
      mockUsePlanningList.mockReturnValue({
        hasItems: true,
        items: [
          {
            productCode: "PROD12345",
            productName: "Test Product 1",
            addedAt: new Date(),
          },
        ],
        removeItem: mockRemoveItem,
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar initialDate={testDate} />
        </Wrapper>
      );

      // Open modal and click item
      const plusButton = screen.getAllByTitle("Rychlé plánování ze seznamu")[0];
      fireEvent.click(plusButton);

      await waitFor(() => {
        const productButton = screen.getByText("Test Product 1");
        fireEvent.click(productButton);
      });

      // Should include productCode and productName parameters
      const navigationCall = mockNavigate.mock.calls[0][0];
      expect(navigationCall).toContain("productCode=PROD12345");
      expect(navigationCall).toContain("productName=PROD12345"); // Full productCode is now sent
    });

    it("should close modal when cancel is clicked", async () => {
      mockUsePlanningList.mockReturnValue({
        hasItems: true,
        items: [
          {
            productCode: "TEST01",
            productName: "Test Product 1",
            addedAt: new Date(),
          },
        ],
        removeItem: jest.fn(),
        addItem: jest.fn(),
        clearList: jest.fn(),
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar />
        </Wrapper>
      );

      // Open modal
      const plusButton = screen.getAllByTitle("Rychlé plánování ze seznamu")[0];
      fireEvent.click(plusButton);

      // Modal should be visible
      await waitFor(() => {
        expect(screen.getByText("Rychlé plánování")).toBeInTheDocument();
      });

      // Click cancel
      const cancelButton = screen.getByText("Zrušit");
      fireEvent.click(cancelButton);

      // Modal should be closed
      await waitFor(() => {
        expect(screen.queryByText("Rychlé plánování")).not.toBeInTheDocument();
      });
    });
  });

  describe("Calendar Refresh Functionality", () => {
    it("should expose refetch function through onRefreshAvailable callback", () => {
      const mockRefetch = jest.fn();
      const mockOnRefreshAvailable = jest.fn();
      
      // Mock empty planning list for this test
      mockUsePlanningList.mockReturnValue({
        hasItems: false,
        items: [],
        removeItem: jest.fn(),
        addItem: jest.fn(),
        clearList: jest.fn(),
      });
      
      mockUseManufactureOrderCalendarQuery.mockReturnValue({
        data: mockCalendarData,
        isLoading: false,
        error: null,
        refetch: mockRefetch,
      });

      const Wrapper = createWrapper();
      render(
        <Wrapper>
          <ManufactureOrderWeeklyCalendar onRefreshAvailable={mockOnRefreshAvailable} />
        </Wrapper>
      );

      // Should call onRefreshAvailable with refetch function
      expect(mockOnRefreshAvailable).toHaveBeenCalledWith(mockRefetch);
    });
  });
});