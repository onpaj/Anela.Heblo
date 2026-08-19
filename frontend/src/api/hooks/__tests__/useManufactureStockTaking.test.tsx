import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useSubmitManufactureStockTaking } from '../useManufactureStockTaking';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../../client';

jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));

jest.mock('../../../contexts/ToastContext', () => ({
  useToast: () => ({ showError: jest.fn(), showSuccess: jest.fn(), showInfo: jest.fn() }),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

const PRODUCT_CODE = 'MAT001';

describe('useSubmitManufactureStockTaking', () => {
  let queryClient: QueryClient;

  const createWrapper = () => ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );

  beforeEach(() => {
    jest.clearAllMocks();
    queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    mockGetAuthenticatedApiClient.mockReturnValue({
      manufactureStockTaking_SubmitManufactureStockTaking: jest.fn().mockResolvedValue({
        code: PRODUCT_CODE,
        amountNew: 42.5,
        amountOld: 10,
      }),
    } as any);
  });

  const submit = async () => {
    const { result } = renderHook(() => useSubmitManufactureStockTaking(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await result.current.mutateAsync({ productCode: PRODUCT_CODE, targetAmount: 42.5 });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  };

  it('invalidates the catalog detail query that feeds the inventory modal', async () => {
    // Arrange - catalog detail is keyed [catalog, "detail", productCode, monthsBack]
    const detailKey = [...QUERY_KEYS.catalog, 'detail', PRODUCT_CODE, 1];
    queryClient.setQueryData(detailKey, { item: { productCode: PRODUCT_CODE } });

    // Act
    await submit();

    // Assert
    expect(queryClient.getQueryState(detailKey)?.isInvalidated).toBe(true);
  });

  it('invalidates the manufacture inventory list query', async () => {
    // Arrange
    const listKey = [...QUERY_KEYS.catalog, 'manufacture-inventory', { pageNumber: 1 }];
    queryClient.setQueryData(listKey, { items: [], totalCount: 0 });

    // Act
    await submit();

    // Assert
    expect(queryClient.getQueryState(listKey)?.isInvalidated).toBe(true);
  });

  it('invalidates the stock taking history query of the product', async () => {
    // Arrange
    const historyKey = [...QUERY_KEYS.stockTaking, 'history', PRODUCT_CODE, 1, 20];
    queryClient.setQueryData(historyKey, { items: [] });

    // Act
    await submit();

    // Assert
    expect(queryClient.getQueryState(historyKey)?.isInvalidated).toBe(true);
  });

  it('leaves an unrelated product\'s cached detail alone', async () => {
    // Arrange
    const otherKey = [...QUERY_KEYS.catalog, 'detail', 'MAT999', 1];
    queryClient.setQueryData(otherKey, { item: { productCode: 'MAT999' } });

    // Act
    await submit();

    // Assert
    expect(queryClient.getQueryState(otherKey)?.isInvalidated).toBe(false);
  });
});
