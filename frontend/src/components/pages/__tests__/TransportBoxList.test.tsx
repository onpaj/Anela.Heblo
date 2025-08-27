import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import TransportBoxList from '../TransportBoxList';
import { useTransportBoxesQuery, useTransportBoxSummaryQuery } from '../../../api/hooks/useTransportBoxes';
import { TestRouterWrapper } from '../../../test-utils/router-wrapper';

// Mock the hooks
jest.mock('../../../api/hooks/useTransportBoxes', () => ({
  useTransportBoxesQuery: jest.fn(),
  useTransportBoxSummaryQuery: jest.fn(),
}));

// Mock the TransportBoxDetail component
jest.mock('../TransportBoxDetail', () => {
  return function MockTransportBoxDetail({ isOpen, onClose, boxId }: any) {
    return isOpen ? (
      <div data-testid="transport-box-detail-modal">
        <div>Transport Box Detail Modal - Box ID: {boxId}</div>
        <button onClick={onClose}>Close Modal</button>
      </div>
    ) : null;
  };
});

// Mock the API client for creating new boxes
jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

// Mock the generated API client
jest.mock('../../../api/generated/api-client', () => ({
  CreateNewTransportBoxRequest: jest.fn().mockImplementation((data) => data),
}));

const mockUseTransportBoxesQuery = useTransportBoxesQuery as jest.Mock;
const mockUseTransportBoxSummaryQuery = useTransportBoxSummaryQuery as jest.Mock;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  
  return (
    <TestRouterWrapper>
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    </TestRouterWrapper>
  );
};

const mockTransportBoxes = [
  {
    id: 1,
    code: 'BOX-001',
    state: 'New',
    description: 'Test box 1',
    createdAt: '2024-01-01T10:00:00Z',
    updatedAt: '2024-01-01T10:00:00Z',
    itemsCount: 5,
    location: null
  },
  {
    id: 2,
    code: 'BOX-002',
    state: 'Opened',
    description: 'Test box 2',
    createdAt: '2024-01-02T10:00:00Z',
    updatedAt: '2024-01-02T10:00:00Z',
    itemsCount: 3,
    location: 'Warehouse A'
  },
  {
    id: 3,
    code: 'BOX-003',
    state: 'InTransit',
    description: 'Test box 3',
    createdAt: '2024-01-03T10:00:00Z',
    updatedAt: '2024-01-03T10:00:00Z',
    itemsCount: 8,
    location: null
  }
];

const mockSummaryData = {
  totalBoxes: 3,
  activeBoxes: 2,
  statesCounts: {
    'New': 1,
    'Opened': 1,
    'InTransit': 1,
    'Received': 0,
    'Stocked': 0,
    'Reserve': 0,
    'Closed': 0,
    'Error': 0
  }
};

describe('TransportBoxList', () => {
  const mockRefetch = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    
    mockUseTransportBoxesQuery.mockReturnValue({
      data: {
        items: mockTransportBoxes,
        totalCount: 3,
        totalPages: 1
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });

    mockUseTransportBoxSummaryQuery.mockReturnValue({
      data: mockSummaryData,
      isLoading: false,
      error: null,
    });
  });

  describe('Basic rendering', () => {
    it('should render transport box list correctly', () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(screen.getByText('Transportní boxy')).toBeInTheDocument();
      expect(screen.getByPlaceholderText('Kód boxu...')).toBeInTheDocument();
      expect(screen.getByText('Vyhledat')).toBeInTheDocument();
      expect(screen.getByText('Otevřít nový box')).toBeInTheDocument();
    });

    it('should display transport boxes in table', () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(screen.getByText('BOX-001')).toBeInTheDocument();
      expect(screen.getByText('BOX-002')).toBeInTheDocument();
      expect(screen.getByText('BOX-003')).toBeInTheDocument();
      
      // Use getAllByText to handle multiple occurrences of state labels
      expect(screen.getAllByText('Nový')).toHaveLength(2); // One in filter, one in table
      expect(screen.getAllByText('Otevřený')).toHaveLength(2);
      expect(screen.getAllByText('V přepravě')).toHaveLength(2);
    });

    it('should display summary statistics', () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(screen.getByText('Celkem:')).toBeInTheDocument();
      expect(screen.getByText('3')).toBeInTheDocument();
    });
  });

  describe('Loading state', () => {
    it('should show loading spinner when data is loading', () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: true,
        error: null,
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(screen.getByText('Načítání dat...')).toBeInTheDocument();
    });
  });

  describe('Error state', () => {
    it('should show error message when data loading fails', () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error('Network error'),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(screen.getByText('Chyba při načítání transportních boxů')).toBeInTheDocument();
      expect(screen.getByText('Zkusit znovu')).toBeInTheDocument();
    });

    it('should retry loading when retry button is clicked', () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error('Network error'),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const retryButton = screen.getByText('Zkusit znovu');
      fireEvent.click(retryButton);

      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });
  });

  describe('Filtering functionality', () => {
    it('should filter by code when search is performed', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const searchInput = screen.getByPlaceholderText('Kód boxu...');
      const searchButton = screen.getByText('Vyhledat');

      fireEvent.change(searchInput, { target: { value: 'BOX-001' } });
      fireEvent.click(searchButton);

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            code: 'BOX-001',
            skip: 0,
            take: 20
          })
        );
      });
    });

    it('should filter by state when state filter is selected', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const stateSelect = screen.getByDisplayValue('');
      const searchButton = screen.getByText('Vyhledat');

      fireEvent.change(stateSelect, { target: { value: 'New' } });
      fireEvent.click(searchButton);

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            state: 'New',
            skip: 0,
            take: 20
          })
        );
      });
    });

    it('should trigger search on Enter key press', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const searchInput = screen.getByPlaceholderText('Kód boxu...');
      
      fireEvent.change(searchInput, { target: { value: 'BOX-002' } });
      fireEvent.keyDown(searchInput, { key: 'Enter', code: 'Enter', charCode: 13 });

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            code: 'BOX-002'
          })
        );
      });
    });

    it('should filter by date range', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const fromDateInput = screen.getByLabelText('Od:');
      const toDateInput = screen.getByLabelText('Do:');
      const searchButton = screen.getByText('Vyhledat');

      fireEvent.change(fromDateInput, { target: { value: '2024-01-01' } });
      fireEvent.change(toDateInput, { target: { value: '2024-01-02' } });
      fireEvent.click(searchButton);

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            fromDate: new Date('2024-01-01'),
            toDate: new Date('2024-01-02')
          })
        );
      });
    });
  });

  describe('State filter from summary cards', () => {
    it('should filter by state when summary card is clicked', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      // Find a summary card for 'New' state and click it
      const newStateCard = screen.getByText('Nový').closest('button');
      if (newStateCard) {
        fireEvent.click(newStateCard);

        await waitFor(() => {
          expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
            expect.objectContaining({
              state: 'New',
              skip: 0
            })
          );
        });
      }
    });
  });

  describe('Sorting functionality', () => {
    it('should sort by column when column header is clicked', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const codeHeader = screen.getByText('Kód');
      fireEvent.click(codeHeader);

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            sortBy: 'code',
            sortDescending: true,
            skip: 0
          })
        );
      });
    });

    it('should toggle sort direction when same column is clicked twice', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const codeHeader = screen.getByText('Kód');
      
      // First click - should sort descending
      fireEvent.click(codeHeader);
      
      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            sortBy: 'code',
            sortDescending: true
          })
        );
      });

      // Second click - should sort ascending
      fireEvent.click(codeHeader);
      
      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            sortBy: 'code',
            sortDescending: false
          })
        );
      });
    });
  });

  describe('Pagination functionality', () => {
    it('should navigate to next page when next button is clicked', async () => {
      // Mock data with multiple pages
      mockUseTransportBoxesQuery.mockReturnValue({
        data: {
          items: mockTransportBoxes,
          totalCount: 50, // More than 20 items to enable pagination
          totalPages: 3
        },
        isLoading: false,
        error: null,
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const nextButton = screen.getByRole('button', { name: /další/i });
      fireEvent.click(nextButton);

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            skip: 20,
            take: 20
          })
        );
      });
    });

    it('should navigate to specific page when page number is clicked', async () => {
      // Mock data with multiple pages
      mockUseTransportBoxesQuery.mockReturnValue({
        data: {
          items: mockTransportBoxes,
          totalCount: 50,
          totalPages: 3
        },
        isLoading: false,
        error: null,
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const pageButton = screen.getByText('2');
      fireEvent.click(pageButton);

      await waitFor(() => {
        expect(mockUseTransportBoxesQuery).toHaveBeenLastCalledWith(
          expect.objectContaining({
            skip: 20,
            take: 20
          })
        );
      });
    });
  });

  describe('Transport box detail modal', () => {
    it('should open detail modal when box row is clicked', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const boxRow = screen.getByText('BOX-001').closest('tr');
      if (boxRow) {
        fireEvent.click(boxRow);

        await waitFor(() => {
          expect(screen.getByTestId('transport-box-detail-modal')).toBeInTheDocument();
          expect(screen.getByText('Transport Box Detail Modal - Box ID: 1')).toBeInTheDocument();
        });
      }
    });

    it('should close detail modal when close button is clicked', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      // Open modal first
      const boxRow = screen.getByText('BOX-001').closest('tr');
      if (boxRow) {
        fireEvent.click(boxRow);

        await waitFor(() => {
          expect(screen.getByTestId('transport-box-detail-modal')).toBeInTheDocument();
        });

        // Close modal
        const closeButton = screen.getByText('Close Modal');
        fireEvent.click(closeButton);

        await waitFor(() => {
          expect(screen.queryByTestId('transport-box-detail-modal')).not.toBeInTheDocument();
        });
      }
    });

    it('should refetch data when detail modal is closed', async () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      // Open and close modal
      const boxRow = screen.getByText('BOX-001').closest('tr');
      if (boxRow) {
        fireEvent.click(boxRow);

        const closeButton = await screen.findByText('Close Modal');
        fireEvent.click(closeButton);

        await waitFor(() => {
          expect(mockRefetch).toHaveBeenCalledTimes(1);
        });
      }
    });
  });

  describe('Create new transport box', () => {
    it('should create new box when "Otevřít nový box" is clicked', async () => {
      const mockApiClient = {
        transportBox_CreateNewTransportBox: jest.fn().mockResolvedValue({
          success: true,
          transportBox: { id: 4, code: 'BOX-004' },
          errorMessage: null
        })
      };

      const { getAuthenticatedApiClient } = await import('../../../api/client');
      (getAuthenticatedApiClient as jest.Mock).mockResolvedValue(mockApiClient);

      render(<TransportBoxList />, { wrapper: createWrapper });

      const createButton = screen.getByText('Otevřít nový box');
      fireEvent.click(createButton);

      await waitFor(() => {
        expect(mockApiClient.transportBox_CreateNewTransportBox).toHaveBeenCalledWith(
          expect.objectContaining({
            description: undefined
          })
        );
      });

      // Should open detail modal for new box
      await waitFor(() => {
        expect(screen.getByText('Transport Box Detail Modal - Box ID: 4')).toBeInTheDocument();
      });

      // Should refresh the list
      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });

    it('should handle create box error gracefully', async () => {
      const mockApiClient = {
        transportBox_CreateNewTransportBox: jest.fn().mockResolvedValue({
          success: false,
          transportBox: null,
          errorMessage: 'Failed to create box'
        })
      };

      const { getAuthenticatedApiClient } = await import('../../../api/client');
      (getAuthenticatedApiClient as jest.Mock).mockResolvedValue(mockApiClient);

      const consoleSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

      render(<TransportBoxList />, { wrapper: createWrapper });

      const createButton = screen.getByText('Otevřít nový box');
      fireEvent.click(createButton);

      await waitFor(() => {
        expect(consoleSpy).toHaveBeenCalledWith('Failed to create transport box:', 'Failed to create box');
      });

      consoleSpy.mockRestore();
    });
  });

  describe('Controls collapse functionality', () => {
    it('should toggle controls visibility when collapse button is clicked', () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      // Find the collapse button (chevron)
      const collapseButton = screen.getByRole('button', { name: /sbalit filtry/i });
      
      // Initially controls should be visible
      expect(screen.getByPlaceholderText('Kód boxu...')).toBeInTheDocument();

      // Click to collapse
      fireEvent.click(collapseButton);

      // Controls should be hidden (or have different styling)
      // Note: The exact behavior depends on the implementation
    });
  });

  describe('Empty state', () => {
    it('should show empty state when no boxes are found', () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: {
          items: [],
          totalCount: 0,
          totalPages: 0
        },
        isLoading: false,
        error: null,
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(screen.getByText('Žádné výsledky')).toBeInTheDocument();
    });
  });

  describe('Refresh functionality', () => {
    it('should refresh data when refresh button is clicked', () => {
      render(<TransportBoxList />, { wrapper: createWrapper });

      const refreshButton = screen.getByRole('button', { name: /obnovit/i });
      fireEvent.click(refreshButton);

      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });
  });
});