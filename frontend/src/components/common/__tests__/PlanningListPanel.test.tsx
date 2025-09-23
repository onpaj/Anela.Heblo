import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";

// Mock the PlanningListContext
const mockUsePlanningList = jest.fn();
const mockPlanningListProvider = ({ children }: { children: React.ReactNode }) => <div>{children}</div>;

jest.mock("../../../contexts/PlanningListContext", () => ({
  PlanningListProvider: mockPlanningListProvider,
  usePlanningList: mockUsePlanningList,
}));

// Import after mocking
const PlanningListPanel = require("../PlanningListPanel").default;

describe("PlanningListPanel", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("should not render when there are no items", () => {
    mockUsePlanningList.mockReturnValue({
      items: [],
      removeItem: jest.fn(),
      hasItems: false,
    });

    render(<PlanningListPanel isVisible={false} />);
    
    // Panel should not be visible when no items
    expect(screen.queryByText("Seznam k plánování")).not.toBeInTheDocument();
  });

  it("should render panel structure when items exist", () => {
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: jest.fn(),
      hasItems: true,
    });

    render(<PlanningListPanel isVisible={true} />);
    
    // Should show the header
    expect(screen.getByText("Seznam k plánování")).toBeInTheDocument();
    expect(screen.getByText("(1/20)")).toBeInTheDocument();
  });

  it("should show empty state when items array is empty but hasItems is false", () => {
    mockUsePlanningList.mockReturnValue({
      items: [],
      removeItem: jest.fn(),
      hasItems: false,
    });

    render(<PlanningListPanel isVisible={true} />);
    
    // Should not render anything when hasItems is false
    expect(screen.queryByText("Seznam k plánování")).not.toBeInTheDocument();
  });

  it("should call onItemClick when item is clicked", () => {
    const mockOnItemClick = jest.fn();
    const mockRemoveItem = jest.fn();
    
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: mockRemoveItem,
      hasItems: true,
    });

    render(<PlanningListPanel isVisible={true} onItemClick={mockOnItemClick} />);
    
    // Click on the product
    const productButton = screen.getByText("Test Product 1");
    fireEvent.click(productButton);
    
    expect(mockOnItemClick).toHaveBeenCalledWith({
      productCode: "TEST01",
      productName: "Test Product 1",
      addedAt: expect.any(Date),
    });
  });

  it("should call removeItem when remove button is clicked", () => {
    const mockRemoveItem = jest.fn();
    
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: mockRemoveItem,
      hasItems: true,
    });

    render(<PlanningListPanel isVisible={true} />);
    
    // Find and click the remove button (X)
    const removeButton = screen.getByTitle("Odebrat ze seznamu");
    fireEvent.click(removeButton);
    
    expect(mockRemoveItem).toHaveBeenCalledWith("TEST01");
  });

  it("should have correct positioning and styling", () => {
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: jest.fn(),
      hasItems: true,
    });

    const { container } = render(<PlanningListPanel isVisible={false} />);
    
    // Check that panel has correct positioning classes
    const panelContainer = container.querySelector('[class*="fixed"][class*="right-0"]');
    expect(panelContainer).toBeInTheDocument();
    
    // Check that panel has required styling (width is inline style)
    const panelWithStyle = container.querySelector('[style*="width"]');
    expect(panelWithStyle).toBeInTheDocument();
  });

  it("should show hover trigger strip when panel is closed", () => {
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: jest.fn(),
      hasItems: true,
    });

    const { container } = render(<PlanningListPanel isVisible={false} />);
    
    // Check that hover trigger strip is visible
    const hoverStrip = container.querySelector('[class*="w-2"][class*="bg-indigo-500"][class*="opacity-70"]');
    expect(hoverStrip).toBeInTheDocument();
    expect(hoverStrip).toHaveAttribute('title', 'Seznam k plánování');
  });

  it("should handle panel positioning with partial visibility", () => {
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: jest.fn(),
      hasItems: true,
    });

    const { container } = render(<PlanningListPanel isVisible={false} />);
    
    // Check that panel uses correct translate class when closed (10px visible)
    const panelContent = container.querySelector('[class*="translate-x-[calc(100%-10px)]"]');
    expect(panelContent).toBeInTheDocument();
  });

  it("should display multiple items correctly", () => {
    mockUsePlanningList.mockReturnValue({
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
        {
          productCode: "TEST03",
          productName: "Test Product 3",
          addedAt: new Date(),
        },
      ],
      removeItem: jest.fn(),
      hasItems: true,
    });

    render(<PlanningListPanel isVisible={true} />);
    
    // Should show correct item count
    expect(screen.getByText("(3/20)")).toBeInTheDocument();
    
    // Should display all items
    expect(screen.getByText("Test Product 1")).toBeInTheDocument();
    expect(screen.getByText("Test Product 2")).toBeInTheDocument();
    expect(screen.getByText("Test Product 3")).toBeInTheDocument();
    expect(screen.getByText("TEST01")).toBeInTheDocument();
    expect(screen.getByText("TEST02")).toBeInTheDocument();
    expect(screen.getByText("TEST03")).toBeInTheDocument();
  });

  it("should show instructions at the bottom of panel", () => {
    mockUsePlanningList.mockReturnValue({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date(),
        },
      ],
      removeItem: jest.fn(),
      hasItems: true,
    });

    render(<PlanningListPanel isVisible={true} />);
    
    // Should show instruction text
    expect(screen.getByText("Klikněte na produkt pro rychlé plánování")).toBeInTheDocument();
  });

  // Restore mocks after tests
  afterEach(() => {
    jest.restoreAllMocks();
  });
});