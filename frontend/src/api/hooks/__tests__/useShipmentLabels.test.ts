import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useShipmentLabels } from '../useShipmentLabels';
import * as clientModule from '../../client';
import { ErrorCodes } from '../../generated/api-client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    shipmentLabels: ['shipmentLabels'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const mockApiClient = {
  shipmentLabels_GetLabels: jest.fn(),
};

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

beforeEach(() => {
  jest.clearAllMocks();
  mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
});

describe('useShipmentLabels', () => {
  it('does not fetch when enabled is false', () => {
    renderHook(() => useShipmentLabels('250001', false), { wrapper: createWrapper });
    expect(mockApiClient.shipmentLabels_GetLabels).not.toHaveBeenCalled();
  });

  it('does not fetch when orderCode is null', () => {
    renderHook(() => useShipmentLabels(null, true), { wrapper: createWrapper });
    expect(mockApiClient.shipmentLabels_GetLabels).not.toHaveBeenCalled();
  });

  it('returns labels on success', async () => {
    mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
      success: true,
      labels: [
        { shipmentGuid: 'guid-1', packageName: 'Zásilka 1', labelUrl: 'https://x.com/1.pdf' },
      ],
    });

    const { result } = renderHook(() => useShipmentLabels('250001', true), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
    expect(result.current.data![0].packageName).toBe('Zásilka 1');
  });

  it('throws error with Czech message for ShipmentLabelsNoShipmentFound', async () => {
    mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
      success: false,
      errorCode: ErrorCodes.ShipmentLabelsNoShipmentFound,
      labels: [],
    });

    const { result } = renderHook(() => useShipmentLabels('250001', true), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toBe(
      'Štítek nelze vytisknout — zásilka zatím nebyla vytvořena'
    );
  });

  it('throws error with Czech message for ShipmentLabelsNotGenerated', async () => {
    mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
      success: false,
      errorCode: ErrorCodes.ShipmentLabelsNotGenerated,
      labels: [],
    });

    const { result } = renderHook(() => useShipmentLabels('250001', true), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toBe(
      'Štítky zatím nebyly vygenerovány'
    );
  });

  it('throws generic error message for unknown error codes', async () => {
    mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
      success: false,
      errorCode: 'InternalServerError',
      labels: [],
    });

    const { result } = renderHook(() => useShipmentLabels('250001', true), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toBe(
      'Štítek se nepodařilo načíst'
    );
  });
});
