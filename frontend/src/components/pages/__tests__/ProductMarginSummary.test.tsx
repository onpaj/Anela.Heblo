import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import ProductMarginSummary from '../ProductMarginSummary';
import * as useProductMarginSummaryHook from '../../../api/hooks/useProductMarginSummary';

// Mock the hook
jest.mock('../../../api/hooks/useProductMarginSummary');

const mockUseProductMarginSummary = useProductMarginSummaryHook.useProductMarginSummaryQuery as jest.MockedFunction<
  typeof useProductMarginSummaryHook.useProductMarginSummaryQuery
>;

// Mock Chart component to avoid canvas issues in tests
jest.mock('react-chartjs-2', () => ({
  Chart: ({ data, options }: any) => <div data-testid="chart" data-chart-data={JSON.stringify(data)} data-chart-options={JSON.stringify(options)} />
}));

const mockData = {
  monthlyData: [
    {
      year: 2024,
      month: 3,
      monthDisplay: 'Bře 2024',
      productSegments: [
        {
          productCode: 'PROD001',
          productName: 'Product 1',
          marginContribution: 1500,
          percentage: 60,
          colorCode: '#2563EB',
          marginPerPiece: 100,
          unitsSold: 15,
          sellingPriceWithoutVat: 150,
          materialCosts: 30,
          laborCosts: 20,
          isOther: false
        },
        {
          productCode: 'OTHER',
          productName: 'Ostatní produkty',
          marginContribution: 1000,
          percentage: 40,
          colorCode: '#9CA3AF',
          marginPerPiece: 0,
          unitsSold: 0,
          sellingPriceWithoutVat: 0,
          materialCosts: 0,
          laborCosts: 0,
          isOther: true
        }
      ],
      totalMonthMargin: 2500
    }
  ],
  topProducts: [
    {
      productCode: 'PROD001',
      productName: 'Product 1',
      totalMargin: 1500,
      colorCode: '#2563EB',
      rank: 1
    }
  ],
  totalMargin: 1500,
  timeWindow: 'current-year',
  fromDate: '2024-01-01T00:00:00',
  toDate: '2024-12-31T23:59:59'
};

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {children}
      </BrowserRouter>
    </QueryClientProvider>
  );
};

describe('ProductMarginSummary', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders loading state', () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    expect(screen.getByText('Načítám data o marži produktů...')).toBeInTheDocument();
  });

  it('renders error state', () => {
    const error = new Error('API Error');
    mockUseProductMarginSummary.mockReturnValue({
      data: undefined,
      isLoading: false,
      error
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    expect(screen.getByText('Chyba při načítání dat o marži')).toBeInTheDocument();
    expect(screen.getByText('API Error')).toBeInTheDocument();
  });

  it('renders empty state when no data', () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: { monthlyData: [], topProducts: [], totalMargin: 0 },
      isLoading: false,
      error: null
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    expect(screen.getByText('Žádná data o marži')).toBeInTheDocument();
    expect(screen.getByText('Pro vybrané období nejsou k dispozici žádná data o marži produktů.')).toBeInTheDocument();
  });

  it('renders chart with data', async () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    expect(screen.getByText('Přehled marží produktů')).toBeInTheDocument();
    expect(screen.getByTestId('chart')).toBeInTheDocument();
    expect(screen.getByText('Celková marže: 1 500 Kč')).toBeInTheDocument();
  });

  it('changes time window when dropdown is selected', async () => {
    const user = userEvent.setup();
    
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    const dropdown = screen.getByLabelText('Časové období:');
    await user.selectOptions(dropdown, 'last-6-months');
    
    expect(dropdown).toHaveValue('last-6-months');
  });

  it('displays summary information correctly', () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    expect(screen.getByText(/Celková marže:.*1 500 Kč/)).toBeInTheDocument();
    expect(screen.getByText(/Období:.*1\. 1\. 2024.*31\. 12\. 2024/)).toBeInTheDocument();
  });

  it('has proper page structure following layout standards', () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });
    
    // Check main heading is present
    expect(screen.getByRole('heading', { name: 'Přehled marží produktů' })).toBeInTheDocument();
    
    // Check time window selector is present
    expect(screen.getByLabelText('Časové období:')).toBeInTheDocument();
    
    // Check chart is rendered
    expect(screen.getByTestId('chart')).toBeInTheDocument();
  });
});