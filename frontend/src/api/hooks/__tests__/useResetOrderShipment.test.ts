import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useResetOrderShipment } from '../useResetOrderShipment';
import { getAuthenticatedApiClient } from '../../client';
import { SwaggerException } from '../../generated/api-client';

jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

describe('useResetOrderShipment', () => {
  let queryClient: QueryClient;
  let mockPackaging_ResetShipment: jest.Mock;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    mockPackaging_ResetShipment = jest.fn();
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue({
      packaging_ResetShipment: mockPackaging_ResetShipment,
    } as any);
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  const rejectWith = (status: number, errorCode: string) =>
    mockPackaging_ResetShipment.mockRejectedValue(
      new SwaggerException(
        'An unexpected server error occurred.',
        status,
        JSON.stringify({ success: false, errorCode }),
        {},
        null,
      ),
    );

  it('maps the reset shipment onto a ScanShipment', async () => {
    // Arrange
    mockPackaging_ResetShipment.mockResolvedValue({
      success: true,
      shipment: {
        shipmentGuid: 'guid-2',
        packages: [{ trackingNumber: 'TR-9', labelUrl: null, labelZpl: null }],
        pendingCompletion: true,
      },
    });

    // Act
    const { result } = renderHook(() => useResetOrderShipment(), { wrapper });
    result.current.mutate({ orderCode: '250001', numberOfPackages: 2 });

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGetAuthenticatedApiClient).toHaveBeenCalledWith(false);
    expect(mockPackaging_ResetShipment).toHaveBeenCalledWith('250001', 2);
    expect(result.current.data).toEqual({
      shipmentGuid: 'guid-2',
      packages: [{ trackingNumber: 'TR-9', labelUrl: null, labelZpl: null }],
      alreadyExisted: false,
      pendingCompletion: true,
    });
  });

  // Every reset error code maps to a non-200 status (409/400/503/422), so the generated
  // client rejects rather than resolving — the envelope has to be read off the exception.
  it('throws the curated Czech message when the client rejects with a non-200 carrying the envelope', async () => {
    // Arrange
    rejectWith(409, 'NoShipmentToReset');

    // Act
    const { result } = renderHook(() => useResetOrderShipment(), { wrapper });
    result.current.mutate({ orderCode: '250001' });

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Žádná zásilka k invalidaci.');
  });

  it('throws a generic message for an unmapped error code', async () => {
    // Arrange
    rejectWith(500, 'Exception');

    // Act
    const { result } = renderHook(() => useResetOrderShipment(), { wrapper });
    result.current.mutate({ orderCode: '250001' });

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Chyba při invalidaci zásilky.');
  });

  it('throws a generic message when the rejection carries no error envelope', async () => {
    // Arrange
    mockPackaging_ResetShipment.mockRejectedValue(new TypeError('Failed to fetch'));

    // Act
    const { result } = renderHook(() => useResetOrderShipment(), { wrapper });
    result.current.mutate({ orderCode: '250001' });

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Chyba při invalidaci zásilky.');
  });
});
