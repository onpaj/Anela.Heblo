import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { InventoryModal } from '../InventoryModal';

// Mock the API hooks
jest.mock('../../api/hooks/useStockTaking', () => ({
  useSubmitStockTaking: () => ({
    mutate: jest.fn(),
    isPending: false,
    isError: false,
    error: null,
  }),
}));

jest.mock('../../api/hooks/useStockTaking', () => ({
  useStockTakingHistory: (request: any) => ({
    data: request.productCode === 'TEST-PRODUCT' ? {
      items: [
        {
          date: '2024-01-15T10:30:00Z',
          previousAmount: 10,
          newAmount: 15,
          userId: 'user1',
          reason: 'Manual adjustment'
        },
        {
          date: '2024-01-10T14:20:00Z',
          previousAmount: 5,
          newAmount: 10,
          userId: 'user2',
          reason: 'Inventory count'
        }
      ],
      totalCount: 2,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1
    } : null,
    isLoading: false,
    isError: false,
    error: null,
  }),
}));

const mockItem = {
  productCode: 'TEST-PRODUCT',
  productName: 'Test Product',
  type: 1 as const,
  location: 'A1-B2',
  stock: {
    available: 25,
    transport: 5,
    reserve: 3,
    erp: 28,
    eshop: 22
  },
  lastStockTaking: '2024-01-15T10:30:00Z'
};

const mockOnClose = jest.fn();

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
      {children}
    </QueryClientProvider>
  );
};

describe('InventoryModal', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders with default "Inventura" tab active', () => {
    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Check that Inventura tab is active
    const inventuraTab = screen.getByRole('tab', { name: /inventura/i });
    const logTab = screen.getByRole('tab', { name: /log/i });
    
    expect(inventuraTab).toHaveAttribute('aria-selected', 'true');
    expect(logTab).toHaveAttribute('aria-selected', 'false');
    
    // Check that inventory content is visible
    expect(screen.getByText('Test Product')).toBeInTheDocument();
    expect(screen.getByText('TEST-PRODUCT')).toBeInTheDocument();
    expect(screen.getByText('A1-B2')).toBeInTheDocument();
    expect(screen.getByDisplayValue('25')).toBeInTheDocument(); // Available stock input
  });

  it('switches to Log tab when clicked', async () => {
    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);

    await waitFor(() => {
      expect(logTab).toHaveAttribute('aria-selected', 'true');
    });

    const inventuraTab = screen.getByRole('tab', { name: /inventura/i });
    expect(inventuraTab).toHaveAttribute('aria-selected', 'false');
  });

  it('displays stock taking history in Log tab', async () => {
    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Switch to Log tab
    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);

    await waitFor(() => {
      // Check that history table is displayed
      expect(screen.getByText('Datum')).toBeInTheDocument();
      expect(screen.getByText('Předchozí množství')).toBeInTheDocument();
      expect(screen.getByText('Nové množství')).toBeInTheDocument();
      expect(screen.getByText('Uživatel')).toBeInTheDocument();
      expect(screen.getByText('Důvod')).toBeInTheDocument();
      
      // Check history data
      expect(screen.getByText('15')).toBeInTheDocument(); // newAmount from first record
      expect(screen.getByText('10')).toBeInTheDocument(); // previousAmount from first record
      expect(screen.getByText('user1')).toBeInTheDocument();
      expect(screen.getByText('Manual adjustment')).toBeInTheDocument();
      
      expect(screen.getByText('5')).toBeInTheDocument(); // previousAmount from second record
      expect(screen.getByText('user2')).toBeInTheDocument();
      expect(screen.getByText('Inventory count')).toBeInTheDocument();
    });
  });

  it('hides inventory content when Log tab is active', async () => {
    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Initially, inventory content should be visible
    expect(screen.getByDisplayValue('25')).toBeInTheDocument();
    
    // Switch to Log tab
    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);

    await waitFor(() => {
      // Inventory input should no longer be visible
      expect(screen.queryByDisplayValue('25')).not.toBeInTheDocument();
    });
  });

  it('switches back to Inventura tab correctly', async () => {
    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    const inventuraTab = screen.getByRole('tab', { name: /inventura/i });
    const logTab = screen.getByRole('tab', { name: /log/i });
    
    // Switch to Log tab first
    fireEvent.click(logTab);
    await waitFor(() => {
      expect(logTab).toHaveAttribute('aria-selected', 'true');
    });

    // Switch back to Inventura tab
    fireEvent.click(inventuraTab);
    
    await waitFor(() => {
      expect(inventuraTab).toHaveAttribute('aria-selected', 'true');
      expect(logTab).toHaveAttribute('aria-selected', 'false');
      
      // Verify inventory content is visible again
      expect(screen.getByDisplayValue('25')).toBeInTheDocument();
    });
  });

  it('maintains tab state when modal is reopened', async () => {
    const { rerender } = render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Switch to Log tab
    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);
    
    await waitFor(() => {
      expect(logTab).toHaveAttribute('aria-selected', 'true');
    });

    // Close modal
    rerender(
      <InventoryModal 
        item={mockItem} 
        isOpen={false} 
        onClose={mockOnClose} 
      />
    );

    // Reopen modal - should remember tab state (or reset to default, depending on implementation)
    rerender(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />
    );

    // Check if it resets to Inventura tab (expected behavior)
    const inventuraTabReopened = screen.getByRole('tab', { name: /inventura/i });
    expect(inventuraTabReopened).toHaveAttribute('aria-selected', 'true');
  });

  it('displays loading state in Log tab when history is loading', async () => {
    // Mock loading state
    jest.doMock('../../api/hooks/useStockTaking', () => ({
      useStockTakingHistory: () => ({
        data: null,
        isLoading: true,
        isError: false,
        error: null,
      }),
    }));

    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Switch to Log tab
    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);

    await waitFor(() => {
      expect(screen.getByText(/načítá se/i)).toBeInTheDocument();
    });
  });

  it('displays error state in Log tab when history fails to load', async () => {
    // Mock error state
    jest.doMock('../../api/hooks/useStockTaking', () => ({
      useStockTakingHistory: () => ({
        data: null,
        isLoading: false,
        isError: true,
        error: new Error('Failed to load history'),
      }),
    }));

    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Switch to Log tab
    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);

    await waitFor(() => {
      expect(screen.getByText(/chyba při načítání historie/i)).toBeInTheDocument();
    });
  });

  it('displays empty state when no history is available', async () => {
    // Mock empty history
    jest.doMock('../../api/hooks/useStockTaking', () => ({
      useStockTakingHistory: () => ({
        data: {
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 0
        },
        isLoading: false,
        isError: false,
        error: null,
      }),
    }));

    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    // Switch to Log tab
    const logTab = screen.getByRole('tab', { name: /log/i });
    fireEvent.click(logTab);

    await waitFor(() => {
      expect(screen.getByText(/žádná historie inventur/i)).toBeInTheDocument();
    });
  });

  it('closes modal when close button is clicked', () => {
    render(
      <InventoryModal 
        item={mockItem} 
        isOpen={true} 
        onClose={mockOnClose} 
      />,
      { wrapper: createWrapper() }
    );

    const closeButton = screen.getByRole('button', { name: /zavřít|close/i });
    fireEvent.click(closeButton);

    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });
});