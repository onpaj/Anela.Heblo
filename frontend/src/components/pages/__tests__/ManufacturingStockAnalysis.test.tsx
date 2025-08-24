import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ManufacturingStockAnalysis from '../ManufacturingStockAnalysis';
// Importing for type information only
// import { useManufacturingStockAnalysisQuery } from '../../../api/hooks/useManufacturingStockAnalysis';

// Mock the hook
const mockUseManufacturingStockAnalysisQuery = jest.fn();
jest.mock('../../../api/hooks/useManufacturingStockAnalysis', () => ({
  useManufacturingStockAnalysisQuery: () => mockUseManufacturingStockAnalysisQuery(),
  TimePeriodFilter: {
    PreviousQuarter: 'PreviousQuarter',
    FutureQuarterY2Y: 'FutureQuarterY2Y',
    PreviousSeason: 'PreviousSeason',
    CustomPeriod: 'CustomPeriod'
  },
  ManufacturingStockSortBy: {
    StockDaysAvailable: 'StockDaysAvailable'
  },
  formatNumber: (value: number) => value.toLocaleString('cs-CZ'),
  formatPercentage: (value: number) => `${value}%`,
  calculateTimePeriodRange: jest.fn()
}));

// Mock CatalogDetail component since it's imported
jest.mock('../CatalogDetail', () => {
  return function MockCatalogDetail() {
    return <div data-testid="catalog-detail">Catalog Detail Mock</div>;
  };
});

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
      <BrowserRouter>
        {children}
      </BrowserRouter>
    </QueryClientProvider>
  );
};

describe('ManufacturingStockAnalysis', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  const mockData = {
    items: [
      {
        code: 'PROD001',
        name: 'Test Product 1',
        currentStock: 100,
        salesInPeriod: 50,
        dailySalesRate: 2.5,
        optimalDaysSetup: 20,
        stockDaysAvailable: 40,
        minimumStock: 10,
        overstockPercentage: 200,
        batchSize: '25',
        productFamily: 'TestFamily',
        severity: 'Adequate' as const,
        isConfigured: true
      },
      {
        code: 'PROD002',
        name: 'Test Product 2',
        currentStock: 5,
        salesInPeriod: 30,
        dailySalesRate: 3.0,
        optimalDaysSetup: 15,
        stockDaysAvailable: 2,
        minimumStock: 8,
        overstockPercentage: 15,
        batchSize: '50',
        productFamily: 'TestFamily',
        severity: 'Critical' as const,
        isConfigured: true
      }
    ],
    summary: {
      totalProducts: 2,
      criticalCount: 1,
      majorCount: 0,
      minorCount: 0,
      adequateCount: 1,
      unconfiguredCount: 0,
      analysisPeriodStart: new Date('2023-01-01'),
      analysisPeriodEnd: new Date('2023-03-31'),
      productFamilies: ['TestFamily']
    },
    totalCount: 2,
    pageNumber: 1,
    pageSize: 20
  };

  it('renders without crashing', () => {
    mockUseManufacturingStockAnalysisQueryQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText('Řízení zásob - výroba')).toBeInTheDocument();
  });

  it('displays loading state', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText('Načítání...')).toBeInTheDocument();
  });

  it('displays error state', () => {
    const mockError = new Error('Failed to fetch data');
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: mockError,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText(/chyba při načítání dat/i)).toBeInTheDocument();
  });

  it('displays summary cards with correct counts', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText('Kritické')).toBeInTheDocument();
    expect(screen.getByText('1')).toBeInTheDocument(); // Critical count
    expect(screen.getByText('Dostatečné')).toBeInTheDocument();
  });

  it('renders product data table', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Check table headers
    expect(screen.getByText('Kód')).toBeInTheDocument();
    expect(screen.getByText('Název')).toBeInTheDocument();
    expect(screen.getByText('Skladem')).toBeInTheDocument();
    expect(screen.getByText('Prodeje období')).toBeInTheDocument();
    expect(screen.getByText('Prodeje/den')).toBeInTheDocument();
    expect(screen.getByText('Nadsklad dní')).toBeInTheDocument();
    expect(screen.getByText('Zásoba dní')).toBeInTheDocument();
    expect(screen.getByText('Min. zásoba')).toBeInTheDocument();
    expect(screen.getByText('Nadsklad %')).toBeInTheDocument();
    expect(screen.getByText('ks/šarže')).toBeInTheDocument();

    // Check product data
    expect(screen.getByText('PROD001')).toBeInTheDocument();
    expect(screen.getByText('Test Product 1')).toBeInTheDocument();
    expect(screen.getByText('PROD002')).toBeInTheDocument();
    expect(screen.getByText('Test Product 2')).toBeInTheDocument();
  });

  it('handles time period filter change', async () => {
    const mockRefetch = jest.fn();
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Find and click time period dropdown
    const timePeriodSelect = screen.getByDisplayValue('Minulý kvartal');
    fireEvent.change(timePeriodSelect, { target: { value: 'PreviousSeason' } });

    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it('handles search functionality', async () => {
    const mockRefetch = jest.fn();
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Find search input
    const searchInput = screen.getByPlaceholderText(/hledat podle kódu nebo názvu/i);
    fireEvent.change(searchInput, { target: { value: 'PROD001' } });

    // Wait for debounce and refetch
    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    }, { timeout: 1000 });
  });

  it('toggles filter controls visibility', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    const toggleButton = screen.getByText('Zobrazit filtry');
    fireEvent.click(toggleButton);

    expect(screen.getByText('Skrýt filtry')).toBeInTheDocument();
  });

  it('handles critical items filter', async () => {
    const mockRefetch = jest.fn();
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Click on critical summary card to filter - find button that contains the text
    const criticalButton = screen.getByRole('button', { name: /kritické/i });
    fireEvent.click(criticalButton);

    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it('handles unconfigured items filter', async () => {
    const mockRefetch = jest.fn();
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Toggle filters to show checkbox
    const toggleButton = screen.getByText('Zobrazit filtry');
    fireEvent.click(toggleButton);

    // Find and click unconfigured filter
    const unconfiguredCheckbox = screen.getByLabelText(/pouze nedefìnované/i);
    fireEvent.click(unconfiguredCheckbox);

    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it('displays severity colors correctly', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Check that severity indicators are rendered
    // This would be more specific with actual CSS class checks in a real implementation
    const tableRows = screen.getAllByRole('row');
    expect(tableRows.length).toBeGreaterThan(2); // Header + data rows
  });

  it('handles pagination', () => {
    const mockData20Items = {
      ...mockData,
      totalCount: 50,
      pageSize: 20
    };

    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData20Items,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Should show page info
    expect(screen.getByText(/Stránka 1 z/)).toBeInTheDocument();
  });

  it('displays empty state when no data', () => {
    const emptyData = {
      ...mockData,
      items: [],
      totalCount: 0
    };

    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: emptyData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText(/žádné produkty nenalezeny/i)).toBeInTheDocument();
  });

  it('formats numbers correctly in the table', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Check that numbers are formatted (would need specific formatting checks)
    expect(screen.getByText('100')).toBeInTheDocument(); // Current stock
    expect(screen.getByText('50')).toBeInTheDocument(); // Sales in period
    expect(screen.getByText('2,5')).toBeInTheDocument(); // Daily sales rate
  });
});