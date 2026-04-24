import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import TagRulesTab from '../settings/TagRulesTab';
import * as hooks from '../../../../api/hooks/usePhotobankSettings';

jest.mock('../../../../api/hooks/usePhotobankSettings', () => ({
  useTagRules: jest.fn(),
  useAddTagRule: jest.fn(),
  useDeleteTagRule: jest.fn(),
  useReapplyTagRules: jest.fn(),
}));

const mockUseTagRules = hooks.useTagRules as jest.Mock;
const mockUseAddTagRule = hooks.useAddTagRule as jest.Mock;
const mockUseDeleteTagRule = hooks.useDeleteTagRule as jest.Mock;
const mockUseReapplyTagRules = hooks.useReapplyTagRules as jest.Mock;

const createWrapper = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
};

describe('TagRulesTab', () => {
  beforeEach(() => {
    mockUseTagRules.mockReturnValue({ data: [], isLoading: false, error: null });
    mockUseAddTagRule.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
    mockUseDeleteTagRule.mockReturnValue({ mutate: jest.fn(), isPending: false });
    mockUseReapplyTagRules.mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({ photosUpdated: 0 }),
      mutate: jest.fn(),
      isPending: false,
      isError: false,
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test('renders empty state when no rules', () => {
    // Arrange & Act
    render(<TagRulesTab />, { wrapper: createWrapper() });

    // Assert
    expect(screen.getByText('Žádná pravidla nejsou nakonfigurována.')).toBeInTheDocument();
  });

  test('renders rules list ordered by sortOrder', () => {
    // Arrange
    const rules = [
      {
        id: 1,
        pathPattern: '/Fotky/Archiv/*',
        tagName: 'archiv',
        isActive: true,
        sortOrder: 2,
      },
      {
        id: 2,
        pathPattern: '/Fotky/Produkty/*',
        tagName: 'produkty',
        isActive: true,
        sortOrder: 1,
      },
    ];
    mockUseTagRules.mockReturnValue({ data: rules, isLoading: false, error: null });

    // Act
    render(<TagRulesTab />, { wrapper: createWrapper() });

    // Assert — rule with sortOrder 1 appears before rule with sortOrder 2
    const rows = screen.getAllByRole('row');
    // rows[0] = header, rows[1] = first data row, rows[2] = second data row
    expect(rows[1]).toHaveTextContent('produkty');
    expect(rows[1]).toHaveTextContent('1');
    expect(rows[2]).toHaveTextContent('archiv');
    expect(rows[2]).toHaveTextContent('2');
  });

  test('delete button calls useDeleteTagRule with correct id', () => {
    // Arrange
    const mutateFn = jest.fn();
    mockUseDeleteTagRule.mockReturnValue({ mutate: mutateFn, isPending: false });
    mockUseTagRules.mockReturnValue({
      data: [
        {
          id: 99,
          pathPattern: '/Fotky/Test/*',
          tagName: 'test',
          isActive: true,
          sortOrder: 0,
        },
      ],
      isLoading: false,
      error: null,
    });

    // Act
    render(<TagRulesTab />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByLabelText('Smazat pravidlo /Fotky/Test/*'));

    // Assert
    expect(mutateFn).toHaveBeenCalledWith(99, expect.any(Object));
  });

  test('add form: submit calls useAddTagRule', async () => {
    // Arrange
    const mutateAsyncFn = jest.fn().mockResolvedValue({});
    mockUseAddTagRule.mockReturnValue({ mutateAsync: mutateAsyncFn, isPending: false });

    render(<TagRulesTab />, { wrapper: createWrapper() });

    // Act
    fireEvent.change(screen.getByLabelText(/Vzor cesty \*/), {
      target: { value: '/Fotky/Produkty/*' },
    });
    fireEvent.change(screen.getByLabelText(/Štítek \*/), { target: { value: 'produkty' } });
    fireEvent.click(screen.getByRole('button', { name: 'Přidat pravidlo' }));

    // Assert
    await waitFor(() => {
      expect(mutateAsyncFn).toHaveBeenCalledWith({
        pathPattern: '/Fotky/Produkty/*',
        tagName: 'produkty',
        sortOrder: 0,
      });
    });
  });

  test('reapply button calls mutate when clicked', () => {
    // Arrange
    const mutateAsyncFn = jest.fn().mockResolvedValue({ photosUpdated: 5 });
    mockUseReapplyTagRules.mockReturnValue({
      mutateAsync: mutateAsyncFn,
      mutate: jest.fn(),
      isPending: false,
      isError: false,
    });

    render(<TagRulesTab />, { wrapper: createWrapper() });

    // Act
    fireEvent.click(screen.getByRole('button', { name: /Re-aplikovat pravidla/ }));

    // Assert
    expect(mutateAsyncFn).toHaveBeenCalled();
  });
});
