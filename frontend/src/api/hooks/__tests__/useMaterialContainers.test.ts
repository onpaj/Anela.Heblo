import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import {
  useCreateMaterialContainers,
  useMaterialContainerByCode,
  useLastUsedLotForMaterial,
} from '../useMaterialContainers';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    materialContainers: ['materialContainers'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useCreateMaterialContainers', () => {
  let mockCreate: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockCreate = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      materialContainers_Create: mockCreate,
    } as any);
  });

  it('posts items and returns response with containers[0].code', async () => {
    const mockResponse = {
      success: true,
      containers: [
        {
          id: 1,
          code: 'M00000001',
          materialCode: 'MAT001',
          lotCode: 'LOT001',
          createdAt: new Date('2026-01-15T10:00:00Z'),
          createdBy: 'user@anela.cz',
        },
      ],
    };
    mockCreate.mockResolvedValue(mockResponse);

    const { result } = renderHook(() => useCreateMaterialContainers(), {
      wrapper: createWrapper,
    });

    await act(async () => {
      result.current.mutate({
        items: [{ code: 'M00000001', materialCode: 'MAT001', lotCode: 'LOT001' }],
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockCreate).toHaveBeenCalledTimes(1);
    expect(result.current.data?.containers?.[0].code).toBe('M00000001');
  });

  it('forwards all item fields to the generated client', async () => {
    mockCreate.mockResolvedValue({ success: true, containers: [] });

    const { result } = renderHook(() => useCreateMaterialContainers(), {
      wrapper: createWrapper,
    });

    const items = [
      {
        code: 'M00000002',
        materialCode: 'MAT002',
        lotCode: 'LOT002',
        amount: 5,
        unit: 'kg',
        purchaseOrderLineId: 42,
      },
    ];

    await act(async () => {
      result.current.mutate({ items });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockCreate).toHaveBeenCalledWith(
      expect.objectContaining({ items }),
    );
  });

  it('surfaces errors from the API', async () => {
    mockCreate.mockRejectedValue(new Error('API Error'));

    const { result } = renderHook(() => useCreateMaterialContainers(), {
      wrapper: createWrapper,
    });

    await act(async () => {
      result.current.mutate({ items: [] });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toBe('API Error');
  });
});

describe('useMaterialContainerByCode', () => {
  let mockGetByCode: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetByCode = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      materialContainers_GetByCode: mockGetByCode,
    } as any);
  });

  it('fetches container by code when code is provided', async () => {
    const mockResponse = {
      success: true,
      container: {
        id: 1,
        code: 'M00000001',
        materialCode: 'MAT001',
        lotCode: 'LOT001',
      },
    };
    mockGetByCode.mockResolvedValue(mockResponse);

    const { result } = renderHook(() => useMaterialContainerByCode('M00000001'), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetByCode).toHaveBeenCalledWith('M00000001');
    expect(result.current.data?.container?.code).toBe('M00000001');
  });

  it('does not fetch when code is null', () => {
    const { result } = renderHook(() => useMaterialContainerByCode(null), {
      wrapper: createWrapper,
    });

    expect(result.current.fetchStatus).toBe('idle');
    expect(mockGetByCode).not.toHaveBeenCalled();
  });
});

describe('useLastUsedLotForMaterial', () => {
  let mockGetLastUsedLot: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetLastUsedLot = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      materialContainers_GetLastUsedLot: mockGetLastUsedLot,
    } as any);
  });

  it('fetches last used lot when materialCode is provided', async () => {
    const mockResponse = { success: true, lotCode: 'LOT001' };
    mockGetLastUsedLot.mockResolvedValue(mockResponse);

    const { result } = renderHook(
      () => useLastUsedLotForMaterial('MAT001'),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetLastUsedLot).toHaveBeenCalledWith('MAT001');
    expect(result.current.data?.lotCode).toBe('LOT001');
  });

  it('does not fetch when materialCode is null', () => {
    const { result } = renderHook(() => useLastUsedLotForMaterial(null), {
      wrapper: createWrapper,
    });

    expect(result.current.fetchStatus).toBe('idle');
    expect(mockGetLastUsedLot).not.toHaveBeenCalled();
  });
});
