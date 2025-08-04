import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import PurchaseStockAnalysis from '../PurchaseStockAnalysis';
import { usePurchaseStockAnalysisQuery } from '../../../api/hooks/usePurchaseStockAnalysis';

// Mock the API hook
jest.mock('../../../api/hooks/usePurchaseStockAnalysis');

const mockUsePurchaseStockAnalysisQuery = usePurchaseStockAnalysisQuery as jest.MockedFunction<typeof usePurchaseStockAnalysisQuery>;

// Test wrapper with providers
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {children}
      </BrowserRouter>
    </QueryClientProvider>
  );
};

// Mock data
const mockSummary = {
  totalProducts: 150,
  criticalCount: 5,
  lowStockCount: 12,
  optimalCount: 100,
  overstockedCount: 20,
  notConfiguredCount: 13,
  totalInventoryValue: 250000,
  analysisPeriodStart: '2023-01-01T00:00:00Z',
  analysisPeriodEnd: '2024-01-01T00:00:00Z'
};

const mockItems = [
  {
    productCode: 'MAT001',
    productName: 'Test Material 1',
    productType: 'Material',
    availableStock: 10,
    minStockLevel: 50,
    optimalStockLevel: 100,
    consumptionInPeriod: 200,
    dailyConsumption: 0.55,
    daysUntilStockout: 18,
    stockEfficiencyPercentage: 10,
    severity: 'Critical' as any,
    minimalOrderQuantity: '100',
    lastPurchase: {
      date: '2023-12-01T00:00:00Z',
      supplierName: 'Test Supplier',
      amount: 100,
      unitPrice: 50,
      totalPrice: 5000
    },
    suppliers: ['Test Supplier'],
    recommendedOrderQuantity: 150,
    isConfigured: true
  },
  {
    productCode: 'GOD001',
    productName: 'Test Goods 1',
    productType: 'Goods',
    availableStock: 75,
    minStockLevel: 20,
    optimalStockLevel: 50,
    consumptionInPeriod: 100,
    dailyConsumption: 0.27,
    daysUntilStockout: 278,
    stockEfficiencyPercentage: 150,
    severity: 'Overstocked' as any,
    minimalOrderQuantity: '25',
    lastPurchase: null,
    suppliers: ['Another Supplier'],
    recommendedOrderQuantity: null,
    isConfigured: true
  }
];

const mockResponse = {
  items: mockItems,
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
  summary: mockSummary
};

describe('PurchaseStockAnalysis', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders loading state correctly', () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    expect(screen.getByText('Načítání dat...')).toBeInTheDocument();
  });

  it('renders error state correctly', () => {
    const mockRefetch = jest.fn();
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('API Error'),
      isRefetching: false,
      refetch: mockRefetch
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    expect(screen.getByText('Chyba při načítání dat')).toBeInTheDocument();
    expect(screen.getByText('API Error')).toBeInTheDocument();
    
    const retryButton = screen.getByText('Zkusit znovu');
    fireEvent.click(retryButton);
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('renders data correctly', () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    // Check header
    expect(screen.getByText('Analýza skladových zásob')).toBeInTheDocument();

    // Check summary cards
    expect(screen.getByText('150')).toBeInTheDocument(); // Total products
    expect(screen.getByText('5')).toBeInTheDocument(); // Critical count
    expect(screen.getByText('12')).toBeInTheDocument(); // Low stock count

    // Check table data
    expect(screen.getByText('MAT001')).toBeInTheDocument();
    expect(screen.getByText('Test Material 1')).toBeInTheDocument();
    expect(screen.getByText('GOD001')).toBeInTheDocument();
    expect(screen.getByText('Test Goods 1')).toBeInTheDocument();

    // Check severity badges
    expect(screen.getByText('Kritický')).toBeInTheDocument();
    expect(screen.getByText('Přeskladněno')).toBeInTheDocument();
  });

  it('handles search filter correctly', async () => {
    const mockRefetch = jest.fn();
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: mockRefetch
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    const searchInput = screen.getByPlaceholderText('Kód, název, dodavatel...');
    fireEvent.change(searchInput, { target: { value: 'MAT001' } });

    await waitFor(() => {
      expect(searchInput).toHaveValue('MAT001');
    });
  });

  it('handles stock status filter correctly', async () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    const statusSelect = screen.getByDisplayValue('Všechny');
    fireEvent.change(statusSelect, { target: { value: 'Critical' } });

    await waitFor(() => {
      expect(statusSelect).toHaveValue('Critical');
    });
  });

  it('handles date filters correctly', async () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    const fromDateInput = screen.getByLabelText('Od data');
    fireEvent.change(fromDateInput, { target: { value: '2023-06-01' } });

    await waitFor(() => {
      expect(fromDateInput).toHaveValue('2023-06-01');
    });
  });

  it('handles only configured checkbox correctly', async () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    const onlyConfiguredCheckbox = screen.getByLabelText('Pouze konfigurované produkty');
    fireEvent.click(onlyConfiguredCheckbox);

    await waitFor(() => {
      expect(onlyConfiguredCheckbox).toBeChecked();
    });
  });

  it('handles refresh button correctly', () => {
    const mockRefetch = jest.fn();
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: mockRefetch
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    const refreshButton = screen.getByText('Obnovit');
    fireEvent.click(refreshButton);
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('renders empty state when no data', () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: { ...mockResponse, items: [], totalCount: 0 },
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    expect(screen.getByText('Žádné výsledky')).toBeInTheDocument();
    expect(screen.getByText('Zkuste upravit filtry nebo vyhledávací kritéria.')).toBeInTheDocument();
  });

  it('handles sorting correctly', async () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    // Click on Product column header to sort
    const productHeader = screen.getByText('Produkt');
    fireEvent.click(productHeader);

    // Test sorting instructions text
    expect(screen.getByText('Klikněte na záhlaví sloupce pro řazení')).toBeInTheDocument();
  });

  it('displays formatted numbers correctly', () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    // Check for formatted efficiency percentage
    expect(screen.getByText('10,0%')).toBeInTheDocument();
    expect(screen.getByText('150,0%')).toBeInTheDocument();

    // Check for formatted daily consumption
    expect(screen.getByText('0,55/den')).toBeInTheDocument();
    expect(screen.getByText('0,27/den')).toBeInTheDocument();
  });

  it('displays last purchase information correctly', () => {
    mockUsePurchaseStockAnalysisQuery.mockReturnValue({
      data: mockResponse,
      isLoading: false,
      error: null,
      isRefetching: false,
      refetch: jest.fn()
    } as any);

    render(
      <TestWrapper>
        <PurchaseStockAnalysis />
      </TestWrapper>
    );

    // Check for last purchase data
    expect(screen.getByText('Test Supplier')).toBeInTheDocument();
    expect(screen.getByText('50,00 Kč')).toBeInTheDocument();
    
    // Check for "no purchase" case
    expect(screen.getByText('Žádný')).toBeInTheDocument();
  });
});