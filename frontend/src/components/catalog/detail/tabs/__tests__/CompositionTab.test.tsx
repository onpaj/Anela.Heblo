import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CompositionTab from '../CompositionTab';
import { useProductComposition } from '../../../../../api/hooks/useCatalog';

jest.mock('../../../../../api/hooks/useCatalog');

const mockUseProductComposition = useProductComposition as jest.MockedFunction<
  typeof useProductComposition
>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('CompositionTab', () => {
  it('shows loading state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    render(<CompositionTab productCode="TEST001" />, {
      wrapper: createWrapper(),
    });

    expect(screen.getByText(/Načítání složení/i)).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
    } as any);

    render(<CompositionTab productCode="TEST001" />, {
      wrapper: createWrapper(),
    });

    expect(screen.getByText(/Chyba při načítání složení/i)).toBeInTheDocument();
  });

  it('shows empty state when no ingredients', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: [] },
      isLoading: false,
      error: null,
    } as any);

    render(<CompositionTab productCode="TEST001" />, {
      wrapper: createWrapper(),
    });

    expect(
      screen.getByText(/Tento produkt nemá definované složení/i)
    ).toBeInTheDocument();
  });

  it('displays ingredients in a table', () => {
    mockUseProductComposition.mockReturnValue({
      data: {
        ingredients: [
          {
            productCode: 'ING001',
            productName: 'Bisabolol',
            amount: 50.5,
            unit: 'g',
          },
          {
            productCode: 'ING002',
            productName: 'Vitamin E',
            amount: 100.25,
            unit: 'g',
          },
        ],
      },
      isLoading: false,
      error: null,
    } as any);

    render(<CompositionTab productCode="TEST001" />, {
      wrapper: createWrapper(),
    });

    expect(screen.getByText('Bisabolol')).toBeInTheDocument();
    expect(screen.getByText('ING001')).toBeInTheDocument();
    expect(screen.getByText('Vitamin E')).toBeInTheDocument();
    expect(screen.getByText('ING002')).toBeInTheDocument();
  });
});
