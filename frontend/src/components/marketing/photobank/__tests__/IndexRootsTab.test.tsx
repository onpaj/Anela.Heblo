import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import IndexRootsTab from '../settings/IndexRootsTab';
import * as hooks from '../../../../api/hooks/usePhotobankSettings';

jest.mock('../../../../api/hooks/usePhotobankSettings', () => ({
  useIndexRoots: jest.fn(),
  useAddIndexRoot: jest.fn(),
  useDeleteIndexRoot: jest.fn(),
}));

const mockUseIndexRoots = hooks.useIndexRoots as jest.Mock;
const mockUseAddIndexRoot = hooks.useAddIndexRoot as jest.Mock;
const mockUseDeleteIndexRoot = hooks.useDeleteIndexRoot as jest.Mock;

const createWrapper = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
};

describe('IndexRootsTab', () => {
  beforeEach(() => {
    mockUseIndexRoots.mockReturnValue({ data: [], isLoading: false, error: null });
    mockUseAddIndexRoot.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
    mockUseDeleteIndexRoot.mockReturnValue({ mutate: jest.fn(), isPending: false });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test('renders empty state when no roots', () => {
    // Arrange & Act
    render(<IndexRootsTab />, { wrapper: createWrapper() });

    // Assert
    expect(screen.getByText('Žádné kořeny nejsou nakonfigurovány.')).toBeInTheDocument();
  });

  test('renders roots list with all columns', () => {
    // Arrange
    const roots = [
      {
        id: 1,
        sharePointPath: '/sites/anela/Fotky',
        displayName: 'Fotky produktů',
        driveId: 'drive-abc',
        rootItemId: 'item-123',
        isActive: true,
        createdAt: '2026-01-01T00:00:00Z',
        lastIndexedAt: null,
      },
      {
        id: 2,
        sharePointPath: '/sites/anela/Archiv',
        displayName: null,
        driveId: 'drive-def',
        rootItemId: 'item-456',
        isActive: false,
        createdAt: '2026-01-02T00:00:00Z',
        lastIndexedAt: '2026-03-15T12:00:00Z',
      },
    ];
    mockUseIndexRoots.mockReturnValue({ data: roots, isLoading: false, error: null });

    // Act
    render(<IndexRootsTab />, { wrapper: createWrapper() });

    // Assert — column values visible
    expect(screen.getByText('/sites/anela/Fotky')).toBeInTheDocument();
    expect(screen.getByText('Fotky produktů')).toBeInTheDocument();
    expect(screen.getByText('drive-abc')).toBeInTheDocument();
    expect(screen.getByText('item-123')).toBeInTheDocument();
    expect(screen.getByText('/sites/anela/Archiv')).toBeInTheDocument();
    expect(screen.getByText('drive-def')).toBeInTheDocument();
    expect(screen.getByText('item-456')).toBeInTheDocument();
    // isActive badge
    const anoBadges = screen.getAllByText('Ano');
    expect(anoBadges.length).toBeGreaterThanOrEqual(1);
    const neBadges = screen.getAllByText('Ne');
    expect(neBadges.length).toBeGreaterThanOrEqual(1);
  });

  test('delete button calls useDeleteIndexRoot with correct id', () => {
    // Arrange
    const mutateFn = jest.fn();
    mockUseDeleteIndexRoot.mockReturnValue({ mutate: mutateFn, isPending: false });
    mockUseIndexRoots.mockReturnValue({
      data: [
        {
          id: 42,
          sharePointPath: '/sites/anela/Test',
          displayName: 'Test',
          driveId: 'drive-xyz',
          rootItemId: 'item-xyz',
          isActive: true,
          createdAt: '2026-01-01T00:00:00Z',
          lastIndexedAt: null,
        },
      ],
      isLoading: false,
      error: null,
    });

    // Act
    render(<IndexRootsTab />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByLabelText('Smazat kořen /sites/anela/Test'));

    // Assert
    expect(mutateFn).toHaveBeenCalledWith(42, expect.any(Object));
  });

  test('add form: submit with valid data calls useAddIndexRoot', async () => {
    // Arrange
    const mutateAsyncFn = jest.fn().mockResolvedValue({});
    mockUseAddIndexRoot.mockReturnValue({ mutateAsync: mutateAsyncFn, isPending: false });

    render(<IndexRootsTab />, { wrapper: createWrapper() });

    // Act
    fireEvent.change(screen.getByLabelText(/Cesta \*/), { target: { value: '/sites/anela/Fotky' } });
    fireEvent.change(screen.getByLabelText(/Drive ID \*/), { target: { value: 'b!abc123' } });
    fireEvent.change(screen.getByLabelText(/Root Item ID \*/), { target: { value: '01ABCDEF' } });
    fireEvent.click(screen.getByRole('button', { name: 'Přidat kořen' }));

    // Assert
    await waitFor(() => {
      expect(mutateAsyncFn).toHaveBeenCalledWith({
        sharePointPath: '/sites/anela/Fotky',
        displayName: null,
        driveId: 'b!abc123',
        rootItemId: '01ABCDEF',
      });
    });
  });

  test('add form: submit blocked when required fields empty', async () => {
    // Arrange
    const mutateAsyncFn = jest.fn();
    mockUseAddIndexRoot.mockReturnValue({ mutateAsync: mutateAsyncFn, isPending: false });

    render(<IndexRootsTab />, { wrapper: createWrapper() });

    // Act — do not fill required fields, click submit
    const submitButton = screen.getByRole('button', { name: 'Přidat kořen' });
    fireEvent.click(submitButton);

    // Assert — button is disabled, mutateAsync not called
    expect(submitButton).toBeDisabled();
    expect(mutateAsyncFn).not.toHaveBeenCalled();
  });
});
