import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CatalogDetail from '../CatalogDetail';
import { CatalogItemDto, ProductType } from '../../../api/hooks/useCatalog';

// Mock the chart components to avoid canvas issues in tests
jest.mock('react-chartjs-2', () => ({
  Line: ({ data }: any) => (
    <div data-testid="chart">
      Chart with {data.datasets[0].data.length} data points
    </div>
  ),
}));

jest.mock('chart.js', () => ({
  Chart: {
    register: jest.fn(),
  },
  CategoryScale: {},
  LinearScale: {},
  BarElement: {},
  Title: {},
  Tooltip: {},
  Legend: {},
  LineElement: {},
  PointElement: {},
}));

// Mock the API hook
jest.mock('../../../api/hooks/useCatalog', () => ({
  ...jest.requireActual('../../../api/hooks/useCatalog'),
  useCatalogDetail: jest.fn(),
}));

import { useCatalogDetail } from '../../../api/hooks/useCatalog';

const mockUseCatalogDetail = useCatalogDetail as jest.MockedFunction<typeof useCatalogDetail>;

const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

const mockCatalogItem: CatalogItemDto = {
  productCode: 'TEST001',
  productName: 'Test Product',
  type: ProductType.Product,
  stock: {
    available: 100.5,
    eshop: 50.25,
    erp: 40.75,
    transport: 5.5,
    reserve: 4.0,
  },
  properties: {
    optimalStockDaysSetup: 30,
    stockMinSetup: 10,
    batchSize: 50,
    seasonMonths: [1, 2, 3],
  },
  location: 'A1-01',
  minimalOrderQuantity: '10',
  minimalManufactureQuantity: 20,
};

const mockSalesData = [
  { year: 2024, month: 7, amountTotal: 15, amountB2B: 10, amountB2C: 5, sumTotal: 1500, sumB2B: 1000, sumB2C: 500 },
  { year: 2024, month: 6, amountTotal: 20, amountB2B: 12, amountB2C: 8, sumTotal: 2000, sumB2B: 1200, sumB2C: 800 },
  { year: 2024, month: 5, amountTotal: 25, amountB2B: 15, amountB2C: 10, sumTotal: 2500, sumB2B: 1500, sumB2C: 1000 },
];

const mockConsumedData = [
  { year: 2024, month: 7, amount: 8, productName: 'Test Material' },
  { year: 2024, month: 6, amount: 12, productName: 'Test Material' },
  { year: 2024, month: 5, amount: 15, productName: 'Test Material' },
];

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = createQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      {component}
    </QueryClientProvider>
  );
};

describe('CatalogDetail', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should display product information correctly', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: mockCatalogItem,
        historicalData: {
          salesHistory: mockSalesData,
          purchaseHistory: [],
          consumedHistory: [],
        },
      },
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={mockCatalogItem}
        isOpen={true}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('Test Product')).toBeInTheDocument();
    expect(screen.getByText('Kód: TEST001')).toBeInTheDocument();
    expect(screen.getByText('100.5')).toBeInTheDocument(); // Available stock rounded
    expect(screen.getByText('A1-01')).toBeInTheDocument();
  });

  it('should display sales chart for products', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: mockCatalogItem,
        historicalData: {
          salesHistory: mockSalesData,
          purchaseHistory: [],
          consumedHistory: [],
        },
      },
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={mockCatalogItem}
        isOpen={true}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('Prodeje za posledních 13 měsíců')).toBeInTheDocument();
    expect(screen.getByTestId('chart')).toBeInTheDocument();
  });

  it('should display consumption chart for materials', () => {
    const materialItem = { ...mockCatalogItem, type: ProductType.Material };
    
    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: materialItem,
        historicalData: {
          salesHistory: [],
          purchaseHistory: [],
          consumedHistory: mockConsumedData,
        },
      },
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={materialItem}
        isOpen={true}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('Spotřeba za posledních 13 měsíců')).toBeInTheDocument();
    expect(screen.getByTestId('chart')).toBeInTheDocument();
  });

  it('should show no data message when historical data is empty', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: mockCatalogItem,
        historicalData: {
          salesHistory: [],
          purchaseHistory: [],
          consumedHistory: [],
        },
      },
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={mockCatalogItem}
        isOpen={true}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('Žádná data pro zobrazení grafu')).toBeInTheDocument();
  });

  it('should show loading state', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={mockCatalogItem}
        isOpen={true}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('Načítání detailů produktu...')).toBeInTheDocument();
  });

  it('should show error state', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Network error'),
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={mockCatalogItem}
        isOpen={true}
        onClose={() => {}}
      />
    );

    expect(screen.getByText(/Chyba při načítání detailů/)).toBeInTheDocument();
  });

  it('should round stock values to 2 decimal places', () => {
    const itemWithPreciseStock = {
      ...mockCatalogItem,
      stock: {
        ...mockCatalogItem.stock,
        available: 123.456789,
        eshop: 50.999,
        erp: 40.001,
        transport: 5.555,
        reserve: 4.444,
      },
    };

    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: itemWithPreciseStock,
        historicalData: {
          salesHistory: [],
          purchaseHistory: [],
          consumedHistory: [],
        },
      },
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(
      <CatalogDetail
        item={itemWithPreciseStock}
        isOpen={true}
        onClose={() => {}}
      />
    );

    // Check that values are rounded to 2 decimal places
    expect(screen.getByText('123.46')).toBeInTheDocument(); // available stock
    expect(screen.getByText('51')).toBeInTheDocument(); // eshop stock (50.999 -> 51)
    expect(screen.getByText('40')).toBeInTheDocument(); // erp stock (40.001 -> 40)
  });

  it('should not render when modal is closed', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: mockCatalogItem,
        historicalData: {
          salesHistory: [],
          purchaseHistory: [],
          consumedHistory: [],
        },
      },
      isLoading: false,
      error: null,
    } as any);

    const { container } = renderWithQueryClient(
      <CatalogDetail
        item={mockCatalogItem}
        isOpen={false}
        onClose={() => {}}
      />
    );

    expect(container.firstChild).toBeNull();
  });
});