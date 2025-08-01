import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CatalogList from '../CatalogList';
import { ProductType, CatalogItemDto } from '../../../api/hooks/useCatalog';

import { useCatalogQuery } from '../../../api/hooks/useCatalog';

// Mock the API hook
jest.mock('../../../api/hooks/useCatalog', () => ({
  ...jest.requireActual('../../../api/hooks/useCatalog'),
  useCatalogQuery: jest.fn(),
}));

// Mock CatalogDetail component to avoid complex modal testing
jest.mock('../CatalogDetail', () => {
  return function MockCatalogDetail({ item, isOpen, onClose }: any) {
    if (!isOpen || !item) return null;
    return (
      <div data-testid="catalog-detail-modal">
        <div>Detail for: {item.productName}</div>
        <button onClick={onClose} data-testid="close-detail">Close</button>
      </div>
    );
  };
});

const mockUseCatalogQuery = useCatalogQuery as jest.MockedFunction<typeof useCatalogQuery>;

const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

const mockCatalogItems: CatalogItemDto[] = [
  {
    productCode: 'TEST001',
    productName: 'Test Product 1',
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
  },
  {
    productCode: 'TEST002',
    productName: 'Test Material 1',
    type: ProductType.Material,
    stock: {
      available: 75.25,
      eshop: 30.0,
      erp: 45.25,
      transport: 0,
      reserve: 0,
    },
    properties: {
      optimalStockDaysSetup: 15,
      stockMinSetup: 5,
      batchSize: 25,
      seasonMonths: [],
    },
    location: 'B2-03',
    minimalOrderQuantity: '5',
    minimalManufactureQuantity: 10,
  },
];

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = createQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      {component}
    </QueryClientProvider>
  );
};

describe('CatalogList', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should display loading state', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    expect(screen.getByText('Načítání katalogu...')).toBeInTheDocument();
  });

  it('should display error state', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Network error'),
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    expect(screen.getByText(/Chyba při načítání katalogu/)).toBeInTheDocument();
  });

  it('should display catalog items correctly', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    expect(screen.getByText('Seznam produktů')).toBeInTheDocument();
    expect(screen.getByText('TEST001')).toBeInTheDocument();
    expect(screen.getByText('Test Product 1')).toBeInTheDocument();
    expect(screen.getByText('TEST002')).toBeInTheDocument();
    expect(screen.getByText('Test Material 1')).toBeInTheDocument();
  });

  it('should handle product name filter input', async () => {
    const mockRefetch = jest.fn();
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    } as any);

    renderWithQueryClient(<CatalogList />);

    const productNameInput = screen.getByPlaceholderText('Název produktu...');
    fireEvent.change(productNameInput, { target: { value: 'Test Product' } });

    expect(productNameInput).toHaveValue('Test Product');

    // Test Enter key press
    fireEvent.keyDown(productNameInput, { key: 'Enter' });
    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it('should handle product code filter input', async () => {
    const mockRefetch = jest.fn();
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    } as any);

    renderWithQueryClient(<CatalogList />);

    const productCodeInput = screen.getByPlaceholderText('Kód produktu...');
    fireEvent.change(productCodeInput, { target: { value: 'TEST001' } });

    expect(productCodeInput).toHaveValue('TEST001');

    // Test filter button click
    const filterButton = screen.getByText('Filtrovat');
    fireEvent.click(filterButton);
    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it('should handle product type filter', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    const typeSelect = screen.getByDisplayValue('Všechny typy');
    fireEvent.change(typeSelect, { target: { value: ProductType.Material.toString() } });

    expect(typeSelect).toHaveValue(ProductType.Material.toString());
  });

  it('should clear all filters', async () => {
    const mockRefetch = jest.fn();
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    } as any);

    renderWithQueryClient(<CatalogList />);

    // Set some filter values
    const productNameInput = screen.getByPlaceholderText('Název produktu...');
    const productCodeInput = screen.getByPlaceholderText('Kód produktu...');
    const typeSelect = screen.getByDisplayValue('Všechny typy');

    fireEvent.change(productNameInput, { target: { value: 'Test' } });
    fireEvent.change(productCodeInput, { target: { value: 'TEST001' } });
    fireEvent.change(typeSelect, { target: { value: ProductType.Material.toString() } });

    // Clear filters
    const clearButton = screen.getByText('Vymazat');
    fireEvent.click(clearButton);

    await waitFor(() => {
      expect(productNameInput).toHaveValue('');
    });
    expect(productCodeInput).toHaveValue('');
    expect(typeSelect).toHaveValue('');
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('should handle sorting', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    const productCodeHeader = screen.getByText('Kód produktu');
    fireEvent.click(productCodeHeader);

    // Check that the header is clickable and contains sorting elements
    const headerCell = screen.getByRole('columnheader', { name: /Kód produktu/ });
    expect(headerCell).toHaveClass('cursor-pointer');
  });

  it('should handle pagination', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 50, // More items to enable pagination
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    // Check pagination info
    expect(screen.getByText(/Zobrazeno/)).toBeInTheDocument();
    expect(screen.getByText(/výsledků/)).toBeInTheDocument();

    // Check page size selector
    const pageSizeSelect = screen.getByDisplayValue('20');
    expect(pageSizeSelect).toBeInTheDocument();

    fireEvent.change(pageSizeSelect, { target: { value: '50' } });
    expect(pageSizeSelect).toHaveValue('50');
  });

  it('should open detail modal when item is clicked', async () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    const productRow = screen.getByText('Test Product 1');
    expect(productRow).toBeInTheDocument();

    fireEvent.click(productRow);

    await waitFor(() => {
      expect(screen.getByTestId('catalog-detail-modal')).toBeInTheDocument();
    });
    expect(screen.getByText('Detail for: Test Product 1')).toBeInTheDocument();
  });

  it('should close detail modal', async () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    // Open modal
    const productRow = screen.getByText('Test Product 1');
    fireEvent.click(productRow);

    await waitFor(() => {
      expect(screen.getByTestId('catalog-detail-modal')).toBeInTheDocument();
    });

    // Close modal
    const closeButton = screen.getByTestId('close-detail');
    fireEvent.click(closeButton);

    await waitFor(() => {
      expect(screen.queryByTestId('catalog-detail-modal')).not.toBeInTheDocument();
    });
  });

  it('should display empty state when no items found', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: [],
        totalCount: 0,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    expect(screen.getByText('Žádné produkty nebyly nalezeny.')).toBeInTheDocument();
  });

  it('should round stock values correctly', () => {
    const itemWithPreciseStock = {
      ...mockCatalogItems[0],
      stock: {
        ...mockCatalogItems[0].stock,
        available: 123.456789,
      },
    };

    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: [itemWithPreciseStock],
        totalCount: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    expect(screen.getByText('123.46')).toBeInTheDocument();
  });

  it('should display product type labels correctly', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    // Check that product type labels appear in the table
    const productBadges = screen.getAllByText('Produkt');
    const materialBadges = screen.getAllByText('Materiál');
    
    expect(productBadges.length).toBeGreaterThan(0);
    expect(materialBadges.length).toBeGreaterThan(0);
  });

  it('should show filter status in pagination info', () => {
    mockUseCatalogQuery.mockReturnValue({
      data: {
        items: mockCatalogItems,
        totalCount: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    renderWithQueryClient(<CatalogList />);

    // Set a filter to trigger the filter status
    const productNameInput = screen.getByPlaceholderText('Název produktu...');
    fireEvent.change(productNameInput, { target: { value: 'Test' } });
    
    const filterButton = screen.getByText('Filtrovat');
    fireEvent.click(filterButton);

    // The pagination info should show filter status
    expect(screen.getByText(/s aplikovanými filtry/)).toBeInTheDocument();
  });
});