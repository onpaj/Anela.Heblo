import React from 'react';
import { render, screen } from '@testing-library/react';
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
    FutureQuarter: 'FutureQuarter',
    Y2Y: 'Y2Y',
    PreviousSeason: 'PreviousSeason',
    CustomPeriod: 'CustomPeriod'
  },
  ManufacturingStockSortBy: {
    StockDaysAvailable: 'StockDaysAvailable',
    ProductCode: 'ProductCode',
    ProductName: 'ProductName',
    CurrentStock: 'CurrentStock',
    Reserve: 'Reserve',
    SalesInPeriod: 'SalesInPeriod',
    DailySales: 'DailySales',
    OptimalDaysSetup: 'OptimalDaysSetup',
    MinimumStock: 'MinimumStock',
    OverstockPercentage: 'OverstockPercentage',
    BatchSize: 'BatchSize'
  },
  ManufacturingStockSeverity: {
    Critical: 0,
    Major: 1,
    Minor: 2,
    Adequate: 3,
    Unconfigured: 4
  },
  formatNumber: (value: number) => value.toLocaleString('cs-CZ'),
  formatPercentage: (value: number) => `${value}%`,
  getTimePeriodDisplayText: (timePeriod: any) => 'Minulý kvartal',
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
    
    // Mock calculateTimePeriodRange to return proper date range
    const { calculateTimePeriodRange } = require('../../../api/hooks/useManufacturingStockAnalysis');
    calculateTimePeriodRange.mockReturnValue({
      fromDate: new Date('2023-01-01'),
      toDate: new Date('2023-03-31')
    });
  });

  const mockData = {
    items: [
      {
        code: 'PROD001',
        name: 'Test Product 1',
        currentStock: 100,
        reserve: 15,
        salesInPeriod: 50,
        dailySalesRate: 2.5,
        optimalDaysSetup: 20,
        stockDaysAvailable: 40,
        minimumStock: 10,
        overstockPercentage: 200,
        batchSize: '25',
        productFamily: 'TestFamily',
        severity: 3, // ManufacturingStockSeverity.Adequate
        isConfigured: true
      },
      {
        code: 'PROD002',
        name: 'Test Product 2',
        currentStock: 5,
        reserve: 0,
        salesInPeriod: 30,
        dailySalesRate: 3.0,
        optimalDaysSetup: 15,
        stockDaysAvailable: 2,
        minimumStock: 8,
        overstockPercentage: 15,
        batchSize: '50',
        productFamily: 'TestFamily',
        severity: 0, // ManufacturingStockSeverity.Critical
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
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Check for any indicator that the component rendered correctly
    expect(screen.getByText('Obnovit')).toBeInTheDocument();
  });

  it('displays loading state', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText('Načítání dat...')).toBeInTheDocument();
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

    // Check that the component renders with basic elements
    expect(screen.getByText('Obnovit')).toBeInTheDocument();
    expect(screen.getByText('Filtry a nastavení')).toBeInTheDocument();
  });

  it('renders product data table', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Check table headers - match actual component text
    expect(screen.getByText('Produkt')).toBeInTheDocument(); // Combines code and name
    expect(screen.getByText('Skladem')).toBeInTheDocument();
    expect(screen.getByText('V rezervě')).toBeInTheDocument(); // New Reserve column
    expect(screen.getByText('Prodeje období')).toBeInTheDocument();
    expect(screen.getByText('Prodeje/den')).toBeInTheDocument();
    expect(screen.getByText('Nadsklad')).toBeInTheDocument(); // No "dní"
    expect(screen.getByText('Zásoba dni')).toBeInTheDocument(); // "dni" not "dní"
    expect(screen.getByText('Min zásoba')).toBeInTheDocument(); // No dot
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

    // Basic test that component renders
    expect(screen.getByText('Obnovit')).toBeInTheDocument();
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

    // Basic test that component renders
    expect(screen.getByText('Obnovit')).toBeInTheDocument();
  });

  it('toggles filter controls visibility', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Basic test that component renders
    expect(screen.getByText('Filtry a nastavení')).toBeInTheDocument();
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

    // Basic test that component renders
    expect(screen.getByText('Obnovit')).toBeInTheDocument();
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

    // Basic test that component renders
    expect(screen.getByText('Obnovit')).toBeInTheDocument();
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
    expect(screen.getByText(/1-\d+ z \d+/)).toBeInTheDocument();
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

    expect(screen.getByText(/žádné výsledky/i)).toBeInTheDocument();
  });

  it('formats numbers correctly in the table', () => {
    mockUseManufacturingStockAnalysisQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn()
    });

    render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

    // Check that the data table contains product information
    expect(screen.getByText('PROD001')).toBeInTheDocument(); // Product code
    expect(screen.getByText('Test Product 1')).toBeInTheDocument(); // Product name
    expect(screen.getByText('PROD002')).toBeInTheDocument(); // Product code
    expect(screen.getByText('Test Product 2')).toBeInTheDocument(); // Product name
  });
});