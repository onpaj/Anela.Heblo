import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { ToastProvider } from '../../../contexts/ToastContext';
import InventoryList from '../InventoryList';
// Mock the useInventory hook
jest.mock('../../../api/hooks/useInventory', () => ({
  useInventoryQuery: jest.fn(),
}));

// Mock the stock taking hooks (used by InventoryModal)
jest.mock('../../../api/hooks/useStockTaking', () => ({
  useSubmitStockTaking: () => ({
    mutate: jest.fn(),
    mutateAsync: jest.fn(),
    isPending: false,
    isError: false,
    error: null,
    isSuccess: false,
    reset: jest.fn(),
  }),
  useEnqueueStockTaking: () => ({
    mutate: jest.fn(),
    mutateAsync: jest.fn(),
    isPending: false,
    isError: false,
    error: null,
    isSuccess: false,
    reset: jest.fn(),
  }),
  useStockTakingHistory: () => ({
    data: null,
    isLoading: false,
    isError: false,
    error: null,
  }),
}));

// Mock the useCatalog hook (used by InventoryModal) and ProductType enum
jest.mock('../../../api/hooks/useCatalog', () => ({
  useCatalogDetail: () => ({
    data: null,
    isLoading: false,
    isError: false,
    error: null,
  }),
  ProductType: {
    UNDEFINED: "UNDEFINED",
    Goods: "Goods",
    Material: "Material",
    SemiProduct: "SemiProduct",
    Product: "Product",
    Set: "Set",
  },
}));

const { useInventoryQuery } = require('../../../api/hooks/useInventory');
const mockUseInventoryQuery = useInventoryQuery as jest.MockedFunction<typeof useInventoryQuery>;

const mockInventoryData = {
  data: {
    items: [
      {
        productCode: 'PRODUCT-1',
        productName: 'Test Product 1',
        type: 'Product',
        location: 'A1-B2',
        stock: {
          available: 25,
          transport: 5,
          reserve: 3,
          erp: 28,
          eshop: 22
        },
        lastStockTaking: '2024-01-10T10:30:00Z' // 5 days ago if today is 2024-01-15
      },
      {
        productCode: 'PRODUCT-2',
        productName: 'Test Product 2',
        type: 'Product',
        location: 'B2-C3',
        stock: {
          available: 15,
          transport: 2,
          reserve: 1,
          erp: 18,
          eshop: 14
        },
        lastStockTaking: null // No stock taking
      },
      {
        productCode: 'PRODUCT-3',
        productName: 'Test Product 3',
        type: 'Product',
        location: 'C3-D4',
        stock: {
          available: 8,
          transport: 1,
          reserve: 0,
          erp: 9,
          eshop: 8
        },
        lastStockTaking: '2024-01-14T10:00:00Z' // 1 day ago (26 hours) if today is 2024-01-15T12:00:00Z
      }
    ],
    totalCount: 3,
    pageNumber: 1,
    pageSize: 20,
    totalPages: 1
  },
  isLoading: false,
  isError: false,
  error: null,
  refetch: jest.fn()
};

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
      <ToastProvider>
        <MemoryRouter>
          {children}
        </MemoryRouter>
      </ToastProvider>
    </QueryClientProvider>
  );
};

// Mock current date for consistent testing
const mockCurrentDate = new Date('2024-01-15T12:00:00Z');
jest.useFakeTimers();
jest.setSystemTime(mockCurrentDate);

describe('InventoryList - LastInventoryDays Column', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseInventoryQuery.mockReturnValue(mockInventoryData);
  });

  afterAll(() => {
    jest.useRealTimers();
  });

  it('renders LastInventoryDays column header', () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    expect(screen.getByText('Posl. Inventura')).toBeInTheDocument();
  });

  it('displays days since last inventory for items with stock taking', () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    // PRODUCT-1: lastStockTaking = '2024-01-10T10:30:00Z', current = '2024-01-15T12:00:00Z' = 5 days
    expect(screen.getByText('5 d')).toBeInTheDocument();
    
    // PRODUCT-3: lastStockTaking = '2024-01-14T15:45:00Z', current = '2024-01-15T12:00:00Z' = 1 day
    expect(screen.getByText('1 d')).toBeInTheDocument();
  });

  it('displays dash for items without stock taking', () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    // PRODUCT-2 has no lastStockTaking, should show dash
    const dashElements = screen.getAllByText('-');
    expect(dashElements.length).toBeGreaterThan(0);
  });

  it('shows tooltip with formatted date when hovering over days', async () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    const fiveDaysElement = screen.getByText('5 d');
    
    // Check the title attribute for tooltip (Czech format with timezone conversion)
    expect(fiveDaysElement).toHaveAttribute('title', expect.stringContaining('Poslední inventura:'));
    expect(fiveDaysElement).toHaveAttribute('title', expect.stringContaining('10. 01. 2024'));
    
    const oneDayElement = screen.getByText('1 d');
    expect(oneDayElement).toHaveAttribute('title', expect.stringContaining('Poslední inventura:'));
    expect(oneDayElement).toHaveAttribute('title', expect.stringContaining('14. 01. 2024'));
  });

  it('uses Czech locale for date formatting in tooltip', () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    const fiveDaysElement = screen.getByText('5 d');
    const titleText = fiveDaysElement.getAttribute('title');
    
    // Check Czech date format (DD. MM. YYYY HH:MM:SS - with spaces around dots)
    expect(titleText).toMatch(/Poslední inventura: \d{1,2}\. \d{1,2}\. \d{4} \d{1,2}:\d{2}:\d{2}$/);
    expect(titleText).toContain('Poslední inventura:');
  });

  it('defaults to sorting by lastInventoryDays descending', () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    // Verify the hook was called with default sorting parameters
    expect(mockUseInventoryQuery).toHaveBeenCalledWith(
      '', // productNameFilter
      '', // productCodeFilter  
      '', // productTypeFilter
      1,  // pageNumber
      20, // pageSize
      'lastInventoryDays', // sortBy
      true // sortDescending
    );
  });

  it('allows clicking column header to change sorting', async () => {
    const { rerender } = render(<InventoryList />, { wrapper: createWrapper() });

    const columnHeader = screen.getByText('Posl. Inventura');
    
    // Click to sort ascending
    fireEvent.click(columnHeader);
    
    // Re-render to trigger the hook call with new parameters
    rerender(<InventoryList />);

    await waitFor(() => {
      // Should now be called with ascending sort
      expect(mockUseInventoryQuery).toHaveBeenCalledWith(
        '', // productNameFilter
        '', // productCodeFilter  
        '', // productTypeFilter
        1,  // pageNumber
        20, // pageSize
        'lastInventoryDays', // sortBy
        false // sortDescending (now ascending)
      );
    });
  });

  it('handles zero days correctly (same day stock taking)', () => {
    const sameDayData = {
      ...mockInventoryData,
      data: {
        ...mockInventoryData.data,
        items: [
          {
            ...mockInventoryData.data.items[0],
            lastStockTaking: '2024-01-15T10:00:00Z' // Same day as current time
          }
        ]
      }
    };

    mockUseInventoryQuery.mockReturnValue(sameDayData);
    
    render(<InventoryList />, { wrapper: createWrapper() });

    expect(screen.getByText('0 d')).toBeInTheDocument();
  });

  it('calculates days correctly across different time zones', () => {
    // Test with different times within the same UTC day
    const timeZoneData = {
      ...mockInventoryData,
      data: {
        ...mockInventoryData.data,
        items: [
          {
            ...mockInventoryData.data.items[0],
            lastStockTaking: '2024-01-14T11:59:59Z' // Earlier on previous day to ensure full day
          }
        ]
      }
    };

    mockUseInventoryQuery.mockReturnValue(timeZoneData);
    
    render(<InventoryList />, { wrapper: createWrapper() });

    // Should still be 1 day difference
    expect(screen.getByText('1 d')).toBeInTheDocument();
  });

  it('displays loading state correctly', () => {
    mockUseInventoryQuery.mockReturnValue({
      ...mockInventoryData,
      isLoading: true,
      data: null
    });

    render(<InventoryList />, { wrapper: createWrapper() });

    expect(screen.getByText(/načítání zásob/i)).toBeInTheDocument();
  });

  it('displays error state correctly', () => {
    mockUseInventoryQuery.mockReturnValue({
      ...mockInventoryData,
      isLoading: false,
      isError: true,
      error: new Error('Failed to load inventory'),
      data: null
    });

    render(<InventoryList />, { wrapper: createWrapper() });

    expect(screen.getByText(/chyba při načítání/i)).toBeInTheDocument();
  });

  it('sorts by other columns while preserving LastInventoryDays functionality', async () => {
    render(<InventoryList />, { wrapper: createWrapper() });

    // Click on Product Code column to sort by it
    const productCodeHeader = screen.getByText('Kód produktu');
    fireEvent.click(productCodeHeader);

    await waitFor(() => {
      expect(mockUseInventoryQuery).toHaveBeenCalledWith(
        '', // productNameFilter
        '', // productCodeFilter  
        '', // productTypeFilter
        1,  // pageNumber
        20, // pageSize
        'productCode', // sortBy (changed)
        false // sortDescending
      );
    });

    // The LastInventoryDays column should still display correctly
    expect(screen.getByText('5 d')).toBeInTheDocument();
    expect(screen.getByText('1 d')).toBeInTheDocument();
  });

  it('preserves tooltip functionality when data is sorted by different columns', () => {
    // Mock data sorted by product code instead of lastInventoryDays
    const sortedData = {
      ...mockInventoryData,
      data: {
        ...mockInventoryData.data,
        items: [
          mockInventoryData.data.items[1], // PRODUCT-2 (no stock taking)
          mockInventoryData.data.items[0], // PRODUCT-1 (5 days)
          mockInventoryData.data.items[2]  // PRODUCT-3 (1 day)
        ]
      }
    };

    mockUseInventoryQuery.mockReturnValue(sortedData);
    
    render(<InventoryList />, { wrapper: createWrapper() });

    // Tooltips should still work correctly regardless of sort order
    const fiveDaysElement = screen.getByText('5 d');
    expect(fiveDaysElement).toHaveAttribute('title', expect.stringContaining('10. 01. 2024'));
    
    const oneDayElement = screen.getByText('1 d');
    expect(oneDayElement).toHaveAttribute('title', expect.stringContaining('14. 01. 2024'));
  });
});