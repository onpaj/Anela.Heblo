import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ToastProvider } from '../../../contexts/ToastContext';
import ManufactureInventoryModal from '../ManufactureInventoryDetail';

jest.mock('../../../api/hooks/useManufactureStockTaking', () => ({
  useSubmitManufactureStockTaking: () => ({
    mutate: jest.fn(),
    mutateAsync: jest.fn().mockResolvedValue({}),
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
  const setupQuantityInput = () => {
    render(
      <ManufactureInventoryModal
        item={mockMaterialWithoutLots as any}
        isOpen={true}
        onClose={jest.fn()}
      />,
      { wrapper: createWrapper() },
    );

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

  it('increments from the value currently typed in the field', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act
    fireEvent.click(screen.getByTitle('Zvýšit množství'));

    // Assert
    expect(input.value).toBe('1501.00');
  });

  it('decrements from the value currently typed in the field', () => {
    // Arrange
    const input = setupQuantityInput();
    fireEvent.change(input, { target: { value: '' } });
    typeAtEnd(input, '1500');

    // Act
    fireEvent.click(screen.getByTitle('Snížit množství'));

    // Assert
    expect(input.value).toBe('1499.00');
  });
});
