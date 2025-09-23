import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { PlanningListProvider } from "../../../contexts/PlanningListContext";
import PlanningListPanel from "../PlanningListPanel";

// Mock component to control the planning list context
const TestWrapper: React.FC<{ 
  children: React.ReactNode;
  initialItems?: Array<{ code: string; name: string }>;
}> = ({ children, initialItems = [] }) => {
  return (
    <PlanningListProvider>
      <div data-testid="test-wrapper">
        {children}
        {/* Add items to context for testing */}
        <TestController initialItems={initialItems} />
      </div>
    </PlanningListProvider>
  );
};

// Helper component to manipulate context state for testing
const TestController: React.FC<{ 
  initialItems: Array<{ code: string; name: string }>;
}> = ({ initialItems }) => {
  const [initialized, setInitialized] = React.useState(false);
  
  React.useEffect(() => {
    if (!initialized && initialItems.length > 0) {
      // We would need to expose addItem from context here, but for now
      // we'll test the component behavior when items exist vs don't exist
      setInitialized(true);
    }
  }, [initialized, initialItems]);

  return null;
};

describe("PlanningListPanel", () => {
  it("should not render when there are no items", () => {
    render(
      <TestWrapper>
        <PlanningListPanel isVisible={false} />
      </TestWrapper>
    );
    
    // Panel should not be visible when no items
    expect(screen.queryByText("Seznam k plánování")).not.toBeInTheDocument();
  });

  it("should render panel structure when items exist", () => {
    // For this test, we'll mock the usePlanningList hook to return test data
    const mockUsePlanningList = jest.fn(() => ({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1", 
          addedAt: new Date()
        }
      ],
      removeItem: jest.fn(),
      hasItems: true,
    }));

    // Mock the hook
    jest.mock("../../../contexts/PlanningListContext", () => ({
      usePlanningList: mockUsePlanningList,
    }));

    render(
      <TestWrapper>
        <PlanningListPanel isVisible={true} />
      </TestWrapper>
    );
    
    // Should show the header
    expect(screen.getByText("Seznam k plánování")).toBeInTheDocument();
    expect(screen.getByText("(1/20)")).toBeInTheDocument();
  });

  it("should show empty state when items array is empty but hasItems is false", () => {
    const mockUsePlanningList = jest.fn(() => ({
      items: [],
      removeItem: jest.fn(),
      hasItems: false,
    }));

    jest.mock("../../../contexts/PlanningListContext", () => ({
      usePlanningList: mockUsePlanningList,
    }));

    render(
      <TestWrapper>
        <PlanningListPanel isVisible={true} />
      </TestWrapper>
    );
    
    // Should not render anything when hasItems is false
    expect(screen.queryByText("Seznam k plánování")).not.toBeInTheDocument();
  });

  it("should call onItemClick when item is clicked", () => {
    const mockOnItemClick = jest.fn();
    const mockRemoveItem = jest.fn();
    
    const mockUsePlanningList = jest.fn(() => ({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date()
        }
      ],
      removeItem: mockRemoveItem,
      hasItems: true,
    }));

    jest.mock("../../../contexts/PlanningListContext", () => ({
      usePlanningList: mockUsePlanningList,
    }));

    render(
      <TestWrapper>
        <PlanningListPanel isVisible={true} onItemClick={mockOnItemClick} />
      </TestWrapper>
    );
    
    // Click on the product
    const productButton = screen.getByText("Test Product 1");
    fireEvent.click(productButton);
    
    expect(mockOnItemClick).toHaveBeenCalledWith({
      productCode: "TEST01",
      productName: "Test Product 1"
    });
  });

  it("should call removeItem when remove button is clicked", () => {
    const mockRemoveItem = jest.fn();
    
    const mockUsePlanningList = jest.fn(() => ({
      items: [
        {
          productCode: "TEST01",
          productName: "Test Product 1",
          addedAt: new Date()
        }
      ],
      removeItem: mockRemoveItem,
      hasItems: true,
    }));

    jest.mock("../../../contexts/PlanningListContext", () => ({
      usePlanningList: mockUsePlanningList,
    }));

    render(
      <TestWrapper>
        <PlanningListPanel isVisible={true} />
      </TestWrapper>
    );
    
    // Find and click the remove button (X)
    const removeButton = screen.getByTitle("Odebrat ze seznamu");
    fireEvent.click(removeButton);
    
    expect(mockRemoveItem).toHaveBeenCalledWith("TEST01");
  });

  // Restore mocks after tests
  afterEach(() => {
    jest.restoreAllMocks();
  });
});