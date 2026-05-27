import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CompositionTab from '../CompositionTab';
import { useProductComposition } from '../../../../../api/hooks/useCatalog';
import { useUpdateProductCompositionOrder } from '../../../../../api/hooks/useUpdateProductCompositionOrder';

jest.mock('../../../../../api/hooks/useCatalog');
jest.mock('../../../../../api/hooks/useUpdateProductCompositionOrder');

const mockUseProductComposition = useProductComposition as jest.MockedFunction<
  typeof useProductComposition
>;
const mockUseUpdateOrder = useUpdateProductCompositionOrder as jest.MockedFunction<
  typeof useUpdateProductCompositionOrder
>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

const sampleIngredients = [
  { productCode: 'ING001', productName: 'Bisabolol', amount: 50.5, unit: 'g', order: 1, phaseLabel: null },
  { productCode: 'ING002', productName: 'Vitamin E', amount: 100.25, unit: 'g', order: 2, phaseLabel: null },
];

beforeEach(() => {
  mockUseUpdateOrder.mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ success: true }),
    isPending: false,
  } as any);
});

describe('CompositionTab', () => {
  it('shows loading state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByText(/Načítání složení/i)).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByText(/Chyba při načítání složení/i)).toBeInTheDocument();
  });

  it('shows empty state when no ingredients', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: [] },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(
      screen.getByText(/Tento produkt nemá definované složení/i),
    ).toBeInTheDocument();
  });

  it('displays ingredients with order column', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByText('Bisabolol')).toBeInTheDocument();
    expect(screen.getByText('ING001')).toBeInTheDocument();
    // Order numbers visible
    expect(screen.getByText('1')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
  });

  it('shows "Upravit pořadí" button by default', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByRole('button', { name: /Upravit pořadí/i })).toBeInTheDocument();
  });

  it('enters edit mode and shows Uložit / Zrušit', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));

    expect(screen.getByRole('button', { name: /Uložit/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Zrušit/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Upravit pořadí/i })).not.toBeInTheDocument();
  });

  it('Zrušit exits edit mode without calling mutation', () => {
    const mutateAsync = jest.fn();
    mockUseUpdateOrder.mockReturnValue({ mutateAsync, isPending: false } as any);
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));
    fireEvent.click(screen.getByRole('button', { name: /Zrušit/i }));

    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /Upravit pořadí/i })).toBeInTheDocument();
  });

  it('Uložit calls mutation with current draft order', async () => {
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    mockUseUpdateOrder.mockReturnValue({ mutateAsync, isPending: false } as any);
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));
    fireEvent.click(screen.getByRole('button', { name: /Uložit/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      productCode: 'TEST001',
      order: [
        { ingredientProductCode: 'ING001', sortOrder: 1, phaseLabel: null },
        { ingredientProductCode: 'ING002', sortOrder: 2, phaseLabel: null },
      ],
    });
  });

  it('Uložit forwards phaseLabel when ingredient has a phase', async () => {
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    mockUseUpdateOrder.mockReturnValue({ mutateAsync, isPending: false } as any);
    mockUseProductComposition.mockReturnValue({
      data: {
        ingredients: [
          { productCode: 'ING001', productName: 'Bisabolol', amount: 50.5, unit: 'g', order: 1, phaseLabel: 'A' },
          { productCode: 'ING002', productName: 'Vitamin E', amount: 100.25, unit: 'g', order: 2, phaseLabel: null },
        ],
      },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));
    fireEvent.click(screen.getByRole('button', { name: /Uložit/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      productCode: 'TEST001',
      order: [
        { ingredientProductCode: 'ING001', sortOrder: 1, phaseLabel: 'A' },
        { ingredientProductCode: 'ING002', sortOrder: 2, phaseLabel: null },
      ],
    });
  });
});
