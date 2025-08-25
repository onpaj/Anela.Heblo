import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import Dashboard from '../Dashboard';
import { useRecentAuditLogs, useRecentAuditSummary } from '../../../api/hooks/useAudit';
import { useManualCatalogRefresh } from '../../../api/hooks/useManualCatalogRefresh';

// Mock the API hooks
jest.mock('../../../api/hooks/useAudit', () => ({
  useRecentAuditLogs: jest.fn(),
  useRecentAuditSummary: jest.fn(),
}));

// Mock the manual refresh hook
jest.mock('../../../api/hooks/useManualCatalogRefresh', () => ({
  useManualCatalogRefresh: jest.fn(),
  refreshOperations: [
    {
      key: 'transport',
      name: 'Transport Data',
      methodName: 'catalog_RefreshTransportData',
      description: 'Obnovit data přepravy a balení'
    },
    {
      key: 'manufacture-difficulty',
      name: 'Manufacture Difficulty',
      methodName: 'catalog_RefreshManufactureDifficultyData',
      description: 'Obnovit náročnost výroby'
    }
  ]
}));

const mockUseRecentAuditLogs = useRecentAuditLogs as jest.MockedFunction<typeof useRecentAuditLogs>;
const mockUseRecentAuditSummary = useRecentAuditSummary as jest.MockedFunction<typeof useRecentAuditSummary>;
const mockUseManualCatalogRefresh = useManualCatalogRefresh as jest.MockedFunction<typeof useManualCatalogRefresh>;

const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = createQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      {component}
    </QueryClientProvider>
  );
};

const mockAuditSummary = {
  summary: [
    {
      dataType: 'ProductStock',
      source: 'ERP',
      totalRequests: 10,
      successfulRequests: 9,
      failedRequests: 1,
      totalRecords: 1500,
      averageDuration: 250.5,
      lastSuccessfulLoad: '2024-08-13T10:30:00Z'
    }
  ]
};

const mockAuditLogs = {
  logs: [
    {
      id: '1',
      timestamp: '2024-08-13T10:30:00Z',
      success: true,
      dataType: 'ProductStock',
      source: 'ERP',
      recordCount: 150,
      duration: '250.5',
      errorMessage: null
    }
  ]
};

describe('Dashboard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('Manual Refresh Tab', () => {
    beforeEach(() => {
      mockUseRecentAuditLogs.mockReturnValue({
        data: mockAuditLogs,
        isLoading: false,
        error: null,
      } as any);

      mockUseRecentAuditSummary.mockReturnValue({
        data: mockAuditSummary,
        isLoading: false,
        error: null,
      } as any);
    });

    it('should display manual refresh tab and manufacture difficulty refresh button', () => {
      const mockMutateAsync = jest.fn().mockResolvedValue({});
      mockUseManualCatalogRefresh.mockReturnValue({
        mutateAsync: mockMutateAsync,
        isPending: false,
        isError: false,
        isSuccess: false,
        error: null,
      } as any);

      renderWithQueryClient(<Dashboard />);

      // Click on manual refresh tab
      const manualRefreshTab = screen.getByText('Manuální načítání');
      fireEvent.click(manualRefreshTab);

      // Check that manufacture difficulty refresh button exists
      expect(screen.getByText('Manufacture Difficulty')).toBeInTheDocument();
      expect(screen.getByText('Obnovit náročnost výroby')).toBeInTheDocument();
      
      // Check that refresh button is present
      const refreshButtons = screen.getAllByText('Načíst');
      expect(refreshButtons.length).toBeGreaterThan(0);
    });

    it('should call refresh function when manufacture difficulty button is clicked', async () => {
      const mockMutateAsync = jest.fn().mockResolvedValue({});
      mockUseManualCatalogRefresh.mockReturnValue({
        mutateAsync: mockMutateAsync,
        isPending: false,
        isError: false,
        isSuccess: false,
        error: null,
      } as any);

      renderWithQueryClient(<Dashboard />);

      // Click on manual refresh tab
      const manualRefreshTab = screen.getByText('Manuální načítání');
      fireEvent.click(manualRefreshTab);

      // Verify that the Manufacture Difficulty button is present
      expect(screen.getByText('Manufacture Difficulty')).toBeInTheDocument();
      expect(screen.getByText('Obnovit náročnost výroby')).toBeInTheDocument();
      
      // Find all "Načíst" buttons and use the last one (Manufacture Difficulty is last in the list)
      const refreshButtons = screen.getAllByText('Načíst');
      expect(refreshButtons.length).toBeGreaterThan(0);
      
      const manufactureDifficultyButton = refreshButtons[refreshButtons.length - 1];
      expect(manufactureDifficultyButton).toBeInTheDocument();
      expect(manufactureDifficultyButton).not.toBeDisabled();
      
      // Click the button
      fireEvent.click(manufactureDifficultyButton);

      await waitFor(() => {
        expect(mockMutateAsync).toHaveBeenCalledWith('catalog_RefreshManufactureDifficultyData');
      });
    });

    it('should show loading state when refresh is in progress', async () => {
      const mockMutateAsync = jest.fn().mockImplementation(() => new Promise(() => {})); // Never resolves
      mockUseManualCatalogRefresh.mockReturnValue({
        mutateAsync: mockMutateAsync,
        isPending: true,
        isError: false,
        isSuccess: false,
        error: null,
      } as any);

      renderWithQueryClient(<Dashboard />);

      // Click on manual refresh tab
      const manualRefreshTab = screen.getByText('Manuální načítání');
      fireEvent.click(manualRefreshTab);

      // All buttons should be disabled when any refresh is pending
      const refreshButtons = screen.getAllByText('Načíst');
      expect(refreshButtons.length).toBeGreaterThan(0);
      
      refreshButtons.forEach(button => {
        expect(button).toBeDisabled();
      });
    });

    it('should show success message when refresh completes successfully', async () => {
      const mockMutateAsync = jest.fn().mockResolvedValue({});
      mockUseManualCatalogRefresh.mockReturnValue({
        mutateAsync: mockMutateAsync,
        isPending: false,
        isError: false,
        isSuccess: true,
        error: null,
      } as any);

      renderWithQueryClient(<Dashboard />);

      // Click on manual refresh tab
      const manualRefreshTab = screen.getByText('Manuální načítání');
      fireEvent.click(manualRefreshTab);

      // Should show success message
      expect(screen.getByText('Data úspěšně načtena')).toBeInTheDocument();
      expect(screen.getByText('Operace byla dokončena úspěšně.')).toBeInTheDocument();
    });

    it('should show error message when refresh fails', async () => {
      const mockMutateAsync = jest.fn().mockRejectedValue(new Error('Network error'));
      mockUseManualCatalogRefresh.mockReturnValue({
        mutateAsync: mockMutateAsync,
        isPending: false,
        isError: true,
        isSuccess: false,
        error: { message: 'Network error' },
      } as any);

      renderWithQueryClient(<Dashboard />);

      // Click on manual refresh tab
      const manualRefreshTab = screen.getByText('Manuální načítání');
      fireEvent.click(manualRefreshTab);

      // Should show error message
      expect(screen.getByText('Chyba při načítání dat')).toBeInTheDocument();
      expect(screen.getByText('Network error')).toBeInTheDocument();
    });
  });

  describe('Tab Navigation', () => {
    beforeEach(() => {
      mockUseRecentAuditLogs.mockReturnValue({
        data: mockAuditLogs,
        isLoading: false,
        error: null,
      } as any);

      mockUseRecentAuditSummary.mockReturnValue({
        data: mockAuditSummary,
        isLoading: false,
        error: null,
      } as any);

      mockUseManualCatalogRefresh.mockReturnValue({
        mutateAsync: jest.fn(),
        isPending: false,
        isError: false,
        isSuccess: false,
        error: null,
      } as any);
    });

    it('should switch between tabs correctly', async () => {
      renderWithQueryClient(<Dashboard />);

      // Should start on overview tab
      expect(screen.getByText('Celkem požadavků')).toBeInTheDocument();

      // Click on logs tab
      const logsTab = screen.getByText('Audit logy');
      fireEvent.click(logsTab);

      expect(screen.getByText('Poslední audit logy (24 hodin)')).toBeInTheDocument();

      // Click on manual refresh tab
      const manualRefreshTab = screen.getByText('Manuální načítání');
      fireEvent.click(manualRefreshTab);

      expect(screen.getByText('Manuální načítání dat')).toBeInTheDocument();
      expect(screen.getByText('Manufacture Difficulty')).toBeInTheDocument();
    });
  });
});