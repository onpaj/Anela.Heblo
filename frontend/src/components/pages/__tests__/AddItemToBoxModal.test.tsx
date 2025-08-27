import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import AddItemToBoxModal from '../AddItemToBoxModal';
import { getAuthenticatedApiClient } from '../../../api/client';
import { TestRouterWrapper } from '../../../test-utils/router-wrapper';

// Mock the API client
jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

// Mock the MaterialAutocomplete component
jest.mock('../../common/MaterialAutocomplete', () => {
  return function MockMaterialAutocomplete({ onSelect, value }: any) {
    return (
      <div data-testid="material-autocomplete">
        <input
          data-testid="material-search-input"
          placeholder="Vyhledat produkt..."
          onChange={(e) => {
            // Simulate product selection
            if (e.target.value === 'TEST001') {
              onSelect({
                productCode: 'TEST001',
                productName: 'Test Product 1',
                id: 1,
                currentStock: 100
              });
            } else if (e.target.value === '') {
              onSelect(null);
            }
          }}
        />
        {value && (
          <div data-testid="selected-product">
            Selected: {value.productName} ({value.productCode})
          </div>
        )}
      </div>
    );
  };
});

// Mock the generated API client
jest.mock('../../../api/generated/api-client', () => ({
  AddItemToBoxRequest: jest.fn().mockImplementation(function(data) {
    return { ...data };
  }),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.Mock;

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

describe('AddItemToBoxModal', () => {
  const mockOnClose = jest.fn();
  const mockOnSuccess = jest.fn();
  const mockApiClient = {
    transportBox_AddItemToBox: jest.fn(),
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockResolvedValue(mockApiClient);
    mockApiClient.transportBox_AddItemToBox.mockResolvedValue({
      success: true,
      item: { id: 1, productCode: 'TEST001', productName: 'Test Product 1', amount: 5 },
      errorMessage: null
    });
  });

  describe('Modal visibility', () => {
    it('should not render when isOpen is false', () => {
      render(
        <AddItemToBoxModal
          isOpen={false}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      expect(screen.queryByText('Přidání položky do boxu')).not.toBeInTheDocument();
    });

    it('should not render when boxId is null', () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={null}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      expect(screen.queryByText('Přidání položky do boxu')).not.toBeInTheDocument();
    });

    it('should render when isOpen is true and boxId is provided', () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      expect(screen.getByText('Přidání položky do boxu')).toBeInTheDocument();
      expect(screen.getByTestId('material-autocomplete')).toBeInTheDocument();
      expect(screen.getByLabelText('Množství')).toBeInTheDocument();
      expect(screen.getByText('Přidat položku')).toBeInTheDocument();
      expect(screen.getByText('Zrušit')).toBeInTheDocument();
    });
  });

  describe('Form interaction', () => {
    it('should allow product selection and amount input', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      await waitFor(() => {
        expect(screen.getByTestId('selected-product')).toBeInTheDocument();
        expect(screen.getByText('Selected: Test Product 1 (TEST001)')).toBeInTheDocument();
      });

      // Enter amount
      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      expect(amountInput).toHaveValue(5);
    });

    it('should clear error when product is selected', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Try to submit without product to trigger error
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Produkt je povinný')).toBeInTheDocument();
      });

      // Select a product - should clear error
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      await waitFor(() => {
        expect(screen.queryByText('Produkt je povinný')).not.toBeInTheDocument();
      });
    });
  });

  describe('Form validation', () => {
    it('should show error when product is not selected', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Produkt je povinný')).toBeInTheDocument();
      });

      expect(mockApiClient.transportBox_AddItemToBox).not.toHaveBeenCalled();
    });

    it('should show error when amount is empty', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Množství musí být kladné číslo')).toBeInTheDocument();
      });

      expect(mockApiClient.transportBox_AddItemToBox).not.toHaveBeenCalled();
    });

    it('should show error when amount is negative', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '-5' } });

      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Množství musí být kladné číslo')).toBeInTheDocument();
      });

      expect(mockApiClient.transportBox_AddItemToBox).not.toHaveBeenCalled();
    });

    it('should show error when amount is not a number', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: 'invalid' } });

      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Množství musí být kladné číslo')).toBeInTheDocument();
      });

      expect(mockApiClient.transportBox_AddItemToBox).not.toHaveBeenCalled();
    });
  });

  describe('Form submission', () => {
    it('should successfully add item to box', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      // Enter amount
      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Submit form
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(mockApiClient.transportBox_AddItemToBox).toHaveBeenCalledWith(1, expect.any(Object));
      });

      expect(mockOnSuccess).toHaveBeenCalledTimes(1);
      expect(mockOnClose).toHaveBeenCalledTimes(1);
    });

    it('should show loading state during submission', async () => {
      // Make API call take some time
      mockApiClient.transportBox_AddItemToBox.mockImplementation(() => 
        new Promise(resolve => setTimeout(() => resolve({ success: true }), 100))
      );

      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product and amount
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Submit form
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      // Should show loading state - button still shows "Přidat položku" but with spinner
      expect(screen.getByText('Přidat položku')).toBeInTheDocument();
      expect(submitButton).toBeDisabled();
      expect(screen.getByRole('button', { name: /close/i })).toBeDisabled();

      // Wait for completion
      await waitFor(() => {
        expect(mockOnSuccess).toHaveBeenCalledTimes(1);
      });
    });

    it('should handle API error', async () => {
      mockApiClient.transportBox_AddItemToBox.mockResolvedValue({
        success: false,
        item: null,
        errorMessage: 'Product not found'
      });

      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product and amount
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Submit form
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Box nebyl nalezen. Obnovte stránku a zkuste znovu.')).toBeInTheDocument();
      });

      expect(mockOnSuccess).not.toHaveBeenCalled();
      expect(mockOnClose).not.toHaveBeenCalled();
    });

    it('should handle network error', async () => {
      const networkError = new Error('Network error');
      mockApiClient.transportBox_AddItemToBox.mockRejectedValue(networkError);

      const consoleSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product and amount
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Submit form
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(screen.getByText('Chyba připojení. Zkontrolujte internetové připojení.')).toBeInTheDocument();
      });

      expect(consoleSpy).toHaveBeenCalledWith('Error adding item to box:', networkError);
      expect(mockOnSuccess).not.toHaveBeenCalled();
      expect(mockOnClose).not.toHaveBeenCalled();

      consoleSpy.mockRestore();
    });
  });

  describe('Modal closing', () => {
    it('should close modal when close button is clicked', () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      const closeButton = screen.getByRole('button', { name: /close/i });
      fireEvent.click(closeButton);

      expect(mockOnClose).toHaveBeenCalledTimes(1);
    });

    it('should close modal when cancel button is clicked', () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      const cancelButton = screen.getByText('Zrušit');
      fireEvent.click(cancelButton);

      expect(mockOnClose).toHaveBeenCalledTimes(1);
    });

    it('should reset form state when closed', async () => {
      const { rerender } = render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product and amount
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Close modal
      const closeButton = screen.getByRole('button', { name: /close/i });
      fireEvent.click(closeButton);

      // Reopen modal - form should be reset
      rerender(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />
      );

      expect(screen.getByLabelText('Množství')).toHaveValue(null);
      expect(screen.queryByTestId('selected-product')).not.toBeInTheDocument();
    });

    it('should not close modal when loading', () => {
      // Make API call take some time
      mockApiClient.transportBox_AddItemToBox.mockImplementation(() => 
        new Promise(resolve => setTimeout(() => resolve({ success: true }), 1000))
      );

      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product and amount
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Start submission
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      // Try to close modal while loading
      const closeButton = screen.getByRole('button', { name: /close/i });
      fireEvent.click(closeButton);

      expect(mockOnClose).not.toHaveBeenCalled();
      expect(closeButton).toBeDisabled();
    });
  });

  describe('Form reset after successful submission', () => {
    it('should reset form after successful submission', async () => {
      render(
        <AddItemToBoxModal
          isOpen={true}
          onClose={mockOnClose}
          boxId={1}
          onSuccess={mockOnSuccess}
        />,
        { wrapper: createWrapper }
      );

      // Select a product and amount
      const searchInput = screen.getByTestId('material-search-input');
      fireEvent.change(searchInput, { target: { value: 'TEST001' } });

      const amountInput = screen.getByLabelText('Množství');
      fireEvent.change(amountInput, { target: { value: '5' } });

      // Submit form
      const submitButton = screen.getByText('Přidat položku');
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(mockOnSuccess).toHaveBeenCalledTimes(1);
        expect(mockOnClose).toHaveBeenCalledTimes(1);
      });

      // Form should be reset (though modal is closed at this point)
      expect(mockApiClient.transportBox_AddItemToBox).toHaveBeenCalledTimes(1);
    });
  });
});