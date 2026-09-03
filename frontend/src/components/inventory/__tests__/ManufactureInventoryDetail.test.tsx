import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ToastProvider } from '../../../contexts/ToastContext';
import ManufactureInventoryModal from '../ManufactureInventoryDetail';

// Module-scope spy so the submitted payload can be asserted. The name must start
// with "mock" for Babel to allow it inside the hoisted jest.mock factory.
const mockMutateAsync = jest.fn().mockResolvedValue({});

jest.mock('../../../api/hooks/useManufactureStockTaking', () => ({
  useSubmitManufactureStockTaking: () => ({
    mutate: jest.fn(),
    mutateAsync: (...args: any[]) => mockMutateAsync(...args),
    isPending: false,
    isError: false,
    error: null,
    isSuccess: false,
    reset: jest.fn(),
  }),
  useStockTakingHistory: () => ({
    data: { items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 },
    isLoading: false,
    isError: false,
    error: null,
  }),
}));

jest.mock('../../../api/hooks/useCatalog', () => ({
  useCatalogDetail: () => ({
    data: null,
    isLoading: false,
    isError: false,
    error: null,
  }),
}));

const mockMaterialWithoutLots = {
  productCode: 'MAT-001',
  productName: 'Test Material',
  type: 2 as const,
  location: 'A1-B2',
  hasLots: false,
  stock: {
    available: 5,
    transport: 0,
    reserve: 0,
    erp: 5,
    eshop: 0,
  },
};

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>{children}</ToastProvider>
    </QueryClientProvider>
  );
};

/**
 * Simulates real typing into a controlled input: the browser puts the caret at
 * the end after every re-render, so each keystroke is appended to whatever the
 * component currently displays.
 */
const typeAtEnd = (input: HTMLInputElement, characters: string) => {
  for (const character of characters) {
    fireEvent.change(input, { target: { value: input.value + character } });
  }
};

describe('ManufactureInventoryModal - quantity input for materials without lots', () => {
  beforeEach(() => {
    mockMutateAsync.mockClear();
  });

  const renderModal = (item: unknown = mockMaterialWithoutLots) =>
    render(
      <ManufactureInventoryModal
        item={item as any}
        isOpen={true}
        onClose={jest.fn()}
      />,
      { wrapper: createWrapper() },
    );

  const setupQuantityInput = () => {
    renderModal();
    return screen.getByRole('spinbutton') as HTMLInputElement;
  };

  it('keeps the field empty while the original value is being cleared', () => {
    // Arrange
    const input = setupQuantityInput();
    expect(input.value).toBe('5.00');

    // Act
    fireEvent.change(input, { target: { value: '' } });

    // Assert
    expect(input.value).toBe('');
  });

  it('allows typing a value in the thousands', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });

    // Act
    typeAtEnd(input, '1500');

    // Assert
    expect(input.value).toBe('1500');
  });

  it('formats the typed value on blur', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act
    fireEvent.blur(input);

    // Assert
    expect(input.value).toBe('1500.00');
  });

  it('falls back to zero when the field is left empty', () => {
    // Arrange
    const input = setupQuantityInput();

    // Act
    fireEvent.change(input, { target: { value: '' } });
    fireEvent.blur(input);

    // Assert
    expect(input.value).toBe('0.00');
  });

  it('clamps a negative value to zero on blur', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '-7' } });

    // Act
    fireEvent.blur(input);

    // Assert
    expect(input.value).toBe('0.00');
  });

  it('does not decrement below zero', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '0' } });
    fireEvent.blur(input);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Snížit množství' }));

    // Assert
    expect(input.value).toBe('0.00');
  });

  it('increments from the value currently typed in the field', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Zvýšit množství' }));

    // Assert
    expect(input.value).toBe('1501.00');
  });

  it('decrements from the value currently typed in the field', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Snížit množství' }));

    // Assert
    expect(input.value).toBe('1499.00');
  });

  it('submits the typed value even when it was never committed by a blur', async () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act - click submit directly, without blurring the field first
    fireEvent.click(screen.getByText('Zinventarizovat materiál'));

    // Assert
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));
    expect(mockMutateAsync).toHaveBeenCalledWith({
      productCode: 'MAT-001',
      targetAmount: 1500,
      softStockTaking: false,
    });
  });

  it('submits the unchanged stock as a soft stock taking', async () => {
    // Arrange
    setupQuantityInput();

    // Act
    fireEvent.click(screen.getByText('Zinventarizovat materiál'));

    // Assert
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));
    expect(mockMutateAsync).toHaveBeenCalledWith({
      productCode: 'MAT-001',
      targetAmount: 5,
      softStockTaking: true,
    });
  });

  it('keeps the typed value when the item is refetched with the same stock', () => {
    // Arrange
    const { rerender } = renderModal();
    const input = screen.getByRole('spinbutton') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act - a background refetch hands down an equal but brand new object
    rerender(
      <ManufactureInventoryModal
        item={{ ...mockMaterialWithoutLots, stock: { ...mockMaterialWithoutLots.stock } } as any}
        isOpen={true}
        onClose={jest.fn()}
      />,
    );

    // Assert
    expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('1500');
  });

  it('resets the field when the stock of the shown material changes', () => {
    // Arrange
    const { rerender } = renderModal();
    const input = screen.getByRole('spinbutton') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act
    rerender(
      <ManufactureInventoryModal
        item={{ ...mockMaterialWithoutLots, stock: { ...mockMaterialWithoutLots.stock, erp: 42 } } as any}
        isOpen={true}
        onClose={jest.fn()}
      />,
    );

    // Assert
    expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('42.00');
  });

  it('shows the difference while the new value is still being typed', () => {
    // Arrange
    const input = setupQuantityInput();
    expect(screen.queryByText(/Rozdíl:/)).not.toBeInTheDocument();

    // Act
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Assert
    expect(screen.getByText(/Rozdíl: \+1495\.00/)).toBeInTheDocument();
  });
});
