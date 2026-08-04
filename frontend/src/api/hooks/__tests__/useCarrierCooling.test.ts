import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useCarrierCoolingMatrix, useSetCarrierCooling } from '../useCarrierCooling';
import { SetCarrierCoolingRequest } from '../../generated/api-client';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
  return { queryClient, wrapper };
};

describe('useCarrierCoolingMatrix', () => {
  let mockGetMatrix: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetMatrix = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      carrierCooling_GetMatrix: mockGetMatrix,
    } as any);
  });

  it('calls carrierCooling_GetMatrix and maps the response to the required-field local shape', async () => {
    mockGetMatrix.mockResolvedValue({
      groups: [
        {
          carrier: 'Zasilkovna',
          rows: [
            { deliveryHandling: 'NaRuky', cooling: 'L1', coolingText: 'text' },
            { deliveryHandling: 'Box', cooling: 'None', coolingText: undefined },
          ],
        },
      ],
    });

    const { wrapper } = createWrapper();
    const { result } = renderHook(() => useCarrierCoolingMatrix(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetMatrix).toHaveBeenCalledTimes(1);
    expect(result.current.data).toEqual({
      groups: [
        {
          carrier: 'Zasilkovna',
          rows: [
            { deliveryHandling: 'NaRuky', cooling: 'L1', coolingText: 'text' },
            { deliveryHandling: 'Box', cooling: 'None', coolingText: null },
          ],
        },
      ],
    });
  });

  it('defaults groups/rows to empty arrays when the response omits them', async () => {
    mockGetMatrix.mockResolvedValue({});

    const { wrapper } = createWrapper();
    const { result } = renderHook(() => useCarrierCoolingMatrix(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual({ groups: [] });
  });
});

describe('useSetCarrierCooling', () => {
  let mockSetCooling: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockSetCooling = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      carrierCooling_SetCooling: mockSetCooling,
    } as any);
  });

  it('instantiates the generated SetCarrierCoolingRequest and calls the typed method', async () => {
    mockSetCooling.mockResolvedValue({ success: true });

    const { wrapper } = createWrapper();
    const { result } = renderHook(() => useSetCarrierCooling(), { wrapper });

    await result.current.mutateAsync({
      carrier: 'Zasilkovna',
      deliveryHandling: 'NaRuky',
      cooling: 'L1',
      coolingText: 'note',
    });

    expect(mockSetCooling).toHaveBeenCalledTimes(1);
    const calledWith = mockSetCooling.mock.calls[0][0];
    expect(calledWith).toBeInstanceOf(SetCarrierCoolingRequest);
    expect(calledWith).toMatchObject({
      carrier: 'Zasilkovna',
      deliveryHandling: 'NaRuky',
      cooling: 'L1',
      coolingText: 'note',
    });
  });

  it('rejects when the typed client call throws (e.g. a mapped 4xx/5xx SwaggerException)', async () => {
    mockSetCooling.mockRejectedValue({ status: 400, message: 'Bad Request' });

    const { wrapper } = createWrapper();
    const { result } = renderHook(() => useSetCarrierCooling(), { wrapper });

    await expect(
      result.current.mutateAsync({
        carrier: 'PPL',
        deliveryHandling: 'Box',
        cooling: 'None',
      }),
    ).rejects.toMatchObject({ status: 400 });
  });
});
