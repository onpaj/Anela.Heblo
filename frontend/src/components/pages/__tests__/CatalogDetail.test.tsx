import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import CatalogDetail from '../CatalogDetail';
import { CatalogItemDto, ProductType } from '../../../api/hooks/useCatalog';

import { useCatalogDetail } from '../../../api/hooks/useCatalog';
import { useJournalEntriesByProduct } from '../../../api/hooks/useJournal';

// Mock the Journal hooks
jest.mock('../../../api/hooks/useJournal', () => ({
  useJournalEntriesByProduct: jest.fn(),
  useJournalEntries: jest.fn(),
  useJournalEntry: jest.fn(),
  useSearchJournalEntries: jest.fn(),
  useCreateJournalEntry: jest.fn(),
  useUpdateJournalEntry: jest.fn(),
  useDeleteJournalEntry: jest.fn(),
  useJournalTags: jest.fn(),
  useCreateJournalTag: jest.fn(),
}));

// Mock the chart components to avoid canvas issues in tests
jest.mock('react-chartjs-2', () => ({
  Line: ({ data, options }: any) => (
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

const mockUseCatalogDetail = useCatalogDetail as jest.MockedFunction<typeof useCatalogDetail>;
const mockUseJournalEntriesByProduct = useJournalEntriesByProduct as jest.MockedFunction<typeof useJournalEntriesByProduct>;

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
  manufactureDifficulty: 2.5,
};

// Create mock data for current year/month to ensure it falls within the 13-month window
const currentDate = new Date();
const currentYear = currentDate.getFullYear();
const currentMonth = currentDate.getMonth() + 1; // Convert to 1-based

const mockSalesData = [
  { year: currentYear, month: currentMonth, amountTotal: 15, amountB2B: 10, amountB2C: 5, sumTotal: 1500, sumB2B: 1000, sumB2C: 500 },
  { year: currentYear, month: currentMonth - 1 > 0 ? currentMonth - 1 : 12, amountTotal: 20, amountB2B: 12, amountB2C: 8, sumTotal: 2000, sumB2B: 1200, sumB2C: 800 },
  { year: currentYear, month: currentMonth - 2 > 0 ? currentMonth - 2 : 12, amountTotal: 25, amountB2B: 15, amountB2C: 10, sumTotal: 2500, sumB2B: 1500, sumB2C: 1000 },
];

const mockConsumedData = [
  { year: currentYear, month: currentMonth, amount: 8, productName: 'Test Material' },
  { year: currentYear, month: currentMonth - 1 > 0 ? currentMonth - 1 : 12, amount: 12, productName: 'Test Material' },
  { year: currentYear, month: currentMonth - 2 > 0 ? currentMonth - 2 : 12, amount: 15, productName: 'Test Material' },
];

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = createQueryClient();
  return render(
    <BrowserRouter>
      <QueryClientProvider client={queryClient}>
        {component}
      </QueryClientProvider>
    </BrowserRouter>
  );
};

describe('CatalogDetail', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    
    // Setup default mock for Journal hook
    mockUseJournalEntriesByProduct.mockReturnValue({
      data: { entries: [], totalCount: 0, pageNumber: 1, pageSize: 100, totalPages: 0, hasNextPage: false, hasPreviousPage: false },
      isLoading: false,
      error: null,
    } as any);
  });

  it('should display product information correctly', () => {
    mockUseCatalogDetail.mockReturnValue({
      data: {
        item: mockCatalogItem,
        historicalData: {
          salesHistory: mockSalesData,
          purchaseHistory: [],
          consumedHistory: [],
          manufactureHistory: [],
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
          manufactureHistory: [],
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

    expect(screen.getByText('Prodeje')).toBeInTheDocument();
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
          manufactureHistory: [],
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

    expect(screen.getByText('Spotřeba')).toBeInTheDocument();
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
          manufactureHistory: [],
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
          manufactureHistory: [],
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
          manufactureHistory: [],
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

    expect(container).toBeEmptyDOMElement();
  });

  describe('ManufactureDifficulty', () => {
    it('should display manufacture difficulty for products with difficulty > 0', () => {
      const productWithDifficulty = {
        ...mockCatalogItem,
        type: ProductType.Product,
        manufactureDifficulty: 3.75,
      };

      mockUseCatalogDetail.mockReturnValue({
        data: {
          item: productWithDifficulty,
          historicalData: {
            salesHistory: [],
            purchaseHistory: [],
            consumedHistory: [],
            manufactureHistory: [],
          },
        },
        isLoading: false,
        error: null,
      } as any);

      renderWithQueryClient(
        <CatalogDetail
          item={productWithDifficulty}
          isOpen={true}
          onClose={() => {}}
        />
      );

      expect(screen.getByText('Náročnost výroby')).toBeInTheDocument();
      expect(screen.getByText('3.75')).toBeInTheDocument();
    });

    it('should display manufacture difficulty for semi-products with difficulty > 0', () => {
      const semiProductWithDifficulty = {
        ...mockCatalogItem,
        type: ProductType.SemiProduct,
        manufactureDifficulty: 1.25,
      };

      mockUseCatalogDetail.mockReturnValue({
        data: {
          item: semiProductWithDifficulty,
          historicalData: {
            salesHistory: [],
            purchaseHistory: [],
            consumedHistory: [],
            manufactureHistory: [],
          },
        },
        isLoading: false,
        error: null,
      } as any);

      renderWithQueryClient(
        <CatalogDetail
          item={semiProductWithDifficulty}
          isOpen={true}
          onClose={() => {}}
        />
      );

      expect(screen.getByText('Náročnost výroby')).toBeInTheDocument();
      expect(screen.getByText('1.25')).toBeInTheDocument();
    });

    it('should display dash when manufacture difficulty is 0', () => {
      const productWithZeroDifficulty = {
        ...mockCatalogItem,
        type: ProductType.Product,
        manufactureDifficulty: 0,
      };

      mockUseCatalogDetail.mockReturnValue({
        data: {
          item: productWithZeroDifficulty,
          historicalData: {
            salesHistory: [],
            purchaseHistory: [],
            consumedHistory: [],
            manufactureHistory: [],
          },
        },
        isLoading: false,
        error: null,
      } as any);

      renderWithQueryClient(
        <CatalogDetail
          item={productWithZeroDifficulty}
          isOpen={true}
          onClose={() => {}}
        />
      );

      expect(screen.getByText('Náročnost výroby')).toBeInTheDocument();
      expect(screen.getByText('Nenastaveno')).toBeInTheDocument();
    });

    it('should display dash when manufacture difficulty is undefined', () => {
      const productWithoutDifficulty = {
        ...mockCatalogItem,
        type: ProductType.Material,
        manufactureDifficulty: undefined,
      };

      mockUseCatalogDetail.mockReturnValue({
        data: {
          item: productWithoutDifficulty,
          historicalData: {
            salesHistory: [],
            purchaseHistory: [],
            consumedHistory: [],
            manufactureHistory: [],
          },
        },
        isLoading: false,
        error: null,
      } as any);

      renderWithQueryClient(
        <CatalogDetail
          item={productWithoutDifficulty}
          isOpen={true}
          onClose={() => {}}
        />
      );

      expect(screen.getByText('Náročnost výroby')).toBeInTheDocument();
      expect(screen.getByText('Nenastaveno')).toBeInTheDocument();
    });

    it('should round manufacture difficulty to 2 decimal places', () => {
      const productWithPreciseDifficulty = {
        ...mockCatalogItem,
        type: ProductType.Product,
        manufactureDifficulty: 2.66666666,
      };

      mockUseCatalogDetail.mockReturnValue({
        data: {
          item: productWithPreciseDifficulty,
          historicalData: {
            salesHistory: [],
            purchaseHistory: [],
            consumedHistory: [],
            manufactureHistory: [],
          },
        },
        isLoading: false,
        error: null,
      } as any);

      renderWithQueryClient(
        <CatalogDetail
          item={productWithPreciseDifficulty}
          isOpen={true}
          onClose={() => {}}
        />
      );

      expect(screen.getByText('Náročnost výroby')).toBeInTheDocument();
      expect(screen.getByText('2.67')).toBeInTheDocument();
    });
  });
});