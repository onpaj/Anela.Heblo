import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import TransportBoxReceive from '../TransportBoxReceive';
import { ToastProvider } from '../../../contexts/ToastContext';
import { useTransportBoxReceive } from '../../../api/hooks/useTransportBoxReceive';

// Mock the custom hook
jest.mock('../../../api/hooks/useTransportBoxReceive');

// Test wrapper with providers
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        {children}
      </ToastProvider>
    </QueryClientProvider>
  );
};

describe('TransportBoxReceive', () => {
  const mockUseTransportBoxReceive = useTransportBoxReceive as jest.MockedFunction<typeof useTransportBoxReceive>;
  const mockGetByCode = jest.fn();
  const mockReceive = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    
    mockUseTransportBoxReceive.mockReturnValue({
      getByCode: mockGetByCode,
      receive: mockReceive,
      isReceiving: false,
    });
  });

  it('renders the page correctly', () => {
    render(
      <TestWrapper>
        <TransportBoxReceive />
      </TestWrapper>
    );

    expect(screen.getByText('Příjem transportních boxů')).toBeInTheDocument();
    expect(screen.getByText('Naskenujte kód boxu pro příjem zásilky do skladu')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Naskenujte nebo zadejte kód boxu (např. B001)')).toBeInTheDocument();
  });

  it('handles barcode input correctly', () => {
    render(
      <TestWrapper>
        <TransportBoxReceive />
      </TestWrapper>
    );

    const input = screen.getByPlaceholderText('Naskenujte nebo zadejte kód boxu (např. B001)');
    
    // Test input handling
    fireEvent.change(input, { target: { value: 'b001' } });
    expect(input).toHaveValue('B001'); // Should be uppercased
  });

  it('prevents scanning with empty box code', () => {
    render(
      <TestWrapper>
        <TransportBoxReceive />
      </TestWrapper>
    );

    const scanButton = screen.getByText('Načíst box');
    
    // Button should be disabled when input is empty
    expect(scanButton).toBeDisabled();
    
    // Typing should enable the button
    const input = screen.getByPlaceholderText('Naskenujte nebo zadejte kód boxu (např. B001)');
    fireEvent.change(input, { target: { value: 'B001' } });
    expect(scanButton).not.toBeDisabled();
  });
});