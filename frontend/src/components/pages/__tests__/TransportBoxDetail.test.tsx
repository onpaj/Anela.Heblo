import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import TransportBoxDetail from '../TransportBoxDetail';
import { useTransportBoxByIdQuery, useChangeTransportBoxState } from '../../../api/hooks/useTransportBoxes';

// Mock the hooks
jest.mock('../../../api/hooks/useTransportBoxes', () => ({
  useTransportBoxByIdQuery: jest.fn(),
  useChangeTransportBoxState: jest.fn(),
}));

// Mock the router params
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useParams: () => ({ id: '1' }),
}));

const mockUseTransportBoxByIdQuery = useTransportBoxByIdQuery as jest.Mock;
const mockUseChangeTransportBoxState = useChangeTransportBoxState as jest.Mock;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  
  return (
    <BrowserRouter>
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    </BrowserRouter>
  );
};

const mockTransportBox = {
  id: 1,
  code: 'BOX-001',
  state: 'New',
  eanCode: '1234567890123',
  createdAt: '2024-01-01T10:00:00Z',
  updatedAt: '2024-01-01T10:00:00Z',
  description: 'Test transport box',
  weight: 5.5,
  dimensions: '30x20x15cm',
  materials: [
    { id: 1, name: 'Material A', quantity: 10 },
    { id: 2, name: 'Material B', quantity: 5 }
  ]
};

describe('TransportBoxDetail', () => {
  const mockMutate = jest.fn();
  
  beforeEach(() => {
    jest.clearAllMocks();
    
    mockUseChangeTransportBoxState.mockReturnValue({
      mutate: mockMutate,
      isPending: false,
      error: null,
    });
  });

  it('should render transport box details correctly', () => {
    mockUseTransportBoxByIdQuery.mockReturnValue({
      data: { transportBox: mockTransportBox },
      isLoading: false,
      error: null,
    });

    render(<TransportBoxDetail />, { wrapper: createWrapper });

    expect(screen.getByText('BOX-001')).toBeInTheDocument();
    expect(screen.getByText('Nový')).toBeInTheDocument();
    expect(screen.getByText('1234567890123')).toBeInTheDocument();
    expect(screen.getByText('Test transport box')).toBeInTheDocument();
    expect(screen.getByText('5.5 kg')).toBeInTheDocument();
    expect(screen.getByText('30x20x15cm')).toBeInTheDocument();
  });

  it('should display loading state', () => {
    mockUseTransportBoxByIdQuery.mockReturnValue({
      data: null,
      isLoading: true,
      error: null,
    });

    render(<TransportBoxDetail />, { wrapper: createWrapper });

    expect(screen.getByText('Načítání...')).toBeInTheDocument();
  });

  it('should display error state', () => {
    mockUseTransportBoxByIdQuery.mockReturnValue({
      data: null,
      isLoading: false,
      error: new Error('Failed to fetch'),
    });

    render(<TransportBoxDetail />, { wrapper: createWrapper });

    expect(screen.getByText('Chyba při načítání detailu transportního boxu')).toBeInTheDocument();
  });

  it('should display not found state', () => {
    mockUseTransportBoxByIdQuery.mockReturnValue({
      data: { transportBox: null },
      isLoading: false,
      error: null,
    });

    render(<TransportBoxDetail />, { wrapper: createWrapper });

    expect(screen.getByText('Transportní box nenalezen')).toBeInTheDocument();
  });

  describe('State transition buttons', () => {
    it('should show next state button for New state', () => {
      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'New' } },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Otevřený')).toBeInTheDocument();
      expect(screen.queryByText('Zavřený')).not.toBeInTheDocument();
    });

    it('should show both previous and next buttons for Opened state', () => {
      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'Opened' } },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Nový')).toBeInTheDocument(); // previous
      expect(screen.getByText('V přepravě')).toBeInTheDocument(); // next
    });

    it('should show only previous button for Closed state', () => {
      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'Closed' } },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Na skladě')).toBeInTheDocument(); // previous
      expect(screen.queryByText('Nový')).not.toBeInTheDocument(); // no next
    });

    it('should call mutation when state change button is clicked', async () => {
      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'New' } },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      const openButton = screen.getByText('Otevřený');
      fireEvent.click(openButton);

      await waitFor(() => {
        expect(mockMutate).toHaveBeenCalledWith({
          boxId: 1,
          newState: 'Opened'
        });
      });
    });

    it('should disable buttons when mutation is pending', () => {
      mockUseChangeTransportBoxState.mockReturnValue({
        mutate: mockMutate,
        isPending: true,
        error: null,
      });

      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'New' } },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      const openButton = screen.getByText('Otevřený');
      expect(openButton).toBeDisabled();
    });

    it('should show loading text when mutation is pending', () => {
      mockUseChangeTransportBoxState.mockReturnValue({
        mutate: mockMutate,
        isPending: true,
        error: null,
      });

      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'New' } },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Měnění stavu...')).toBeInTheDocument();
    });
  });

  describe('Error handling', () => {
    it('should display mutation error', () => {
      mockUseChangeTransportBoxState.mockReturnValue({
        mutate: mockMutate,
        isPending: false,
        error: new Error('State transition failed'),
      });

      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: mockTransportBox },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Chyba při měnění stavu: State transition failed')).toBeInTheDocument();
    });

    it('should clear error when new mutation starts', async () => {
      const mockMutateWithSuccess = jest.fn();
      
      // First render with error
      mockUseChangeTransportBoxState.mockReturnValue({
        mutate: mockMutateWithSuccess,
        isPending: false,
        error: new Error('Previous error'),
      });

      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: { ...mockTransportBox, state: 'New' } },
        isLoading: false,
        error: null,
      });

      const { rerender } = render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Chyba při měnění stavu: Previous error')).toBeInTheDocument();

      // Simulate successful mutation
      mockUseChangeTransportBoxState.mockReturnValue({
        mutate: mockMutateWithSuccess,
        isPending: false,
        error: null,
      });

      rerender(<TransportBoxDetail />);

      expect(screen.queryByText('Chyba při měnění stavu: Previous error')).not.toBeInTheDocument();
    });
  });

  describe('Material list rendering', () => {
    it('should render materials list correctly', () => {
      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { transportBox: mockTransportBox },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Material A')).toBeInTheDocument();
      expect(screen.getByText('Material B')).toBeInTheDocument();
      expect(screen.getByText('10')).toBeInTheDocument();
      expect(screen.getByText('5')).toBeInTheDocument();
    });

    it('should show empty state when no materials', () => {
      mockUseTransportBoxByIdQuery.mockReturnValue({
        data: { 
          transportBox: { 
            ...mockTransportBox, 
            materials: [] 
          } 
        },
        isLoading: false,
        error: null,
      });

      render(<TransportBoxDetail />, { wrapper: createWrapper });

      expect(screen.getByText('Žádné materiály')).toBeInTheDocument();
    });
  });
});