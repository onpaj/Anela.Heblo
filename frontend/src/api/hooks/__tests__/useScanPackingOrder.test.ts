import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useScanPackingOrder } from '../useScanPackingOrder';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));
jest.mock('../../../telemetry/appInsights', () => ({
  startNewTelemetryOperation: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

describe('useScanPackingOrder', () => {
  let queryClient: QueryClient;
  let mockPackaging_ScanOrder: jest.Mock;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    mockPackaging_ScanOrder = jest.fn();
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue({
      packaging_ScanOrder: mockPackaging_ScanOrder,
    } as any);
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it('calls packaging_ScanOrder with the order code, package count and body, suppressing global toasts', async () => {
    mockPackaging_ScanOrder.mockResolvedValue({
      success: true,
      order: { code: '250001', customerName: 'Jan Novák', items: [] },
      shipment: null,
    });

    const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
    result.current.mutate({ orderCode: '250001', numberOfPackages: 2, packingUserId: 'u1' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetAuthenticatedApiClient).toHaveBeenCalledWith(false);
    expect(mockPackaging_ScanOrder).toHaveBeenCalledWith(
      '250001',
      2,
      expect.objectContaining({ packingUserId: 'u1' }),
    );
  });

  it('maps the order and shipment fields from the generated response', async () => {
    mockPackaging_ScanOrder.mockResolvedValue({
      success: true,
      order: {
        code: '250001',
        customerName: 'Jan Novák',
        shippingMethodName: 'PPL',
        cooling: 'L2',
        isCooled: true,
        customerNote: 'Dárek',
        eshopNote: null,
        shippingAddress: { street: 'Hlavní 1', city: 'Praha', zip: '11000' },
        eligibility: { isEligible: true },
        items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
      },
      shipment: {
        shipmentGuid: 'guid-1',
        packages: [{ trackingNumber: 'TR-1', labelUrl: null, labelZpl: null }],
        alreadyExisted: false,
        pendingCompletion: false,
      },
    });

    const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
    result.current.mutate({ orderCode: '250001' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual({
      order: {
        code: '250001',
        customerName: 'Jan Novák',
        shippingMethodName: 'PPL',
        shippingAddress: { street: 'Hlavní 1', city: 'Praha', zip: '11000' },
        cooling: 'L2',
        isCooled: true,
        customerNote: 'Dárek',
        eshopNote: null,
        eligibility: { isEligible: true },
        items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
      },
      shipment: {
        shipmentGuid: 'guid-1',
        packages: [{ trackingNumber: 'TR-1', labelUrl: null, labelZpl: null }],
        alreadyExisted: false,
        pendingCompletion: false,
      },
    });
  });

  it('throws the curated Czech message for a known business error code', async () => {
    mockPackaging_ScanOrder.mockResolvedValue({
      success: false,
      errorCode: 'ShoptetOrderNotFound',
    });

    const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
    result.current.mutate({ orderCode: 'missing' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Objednávka nebyla nalezena.');
  });

  it('throws a generic message for an unmapped error code', async () => {
    mockPackaging_ScanOrder.mockResolvedValue({ success: false, errorCode: 'Exception' });

    const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
    result.current.mutate({ orderCode: 'x' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Chyba při skenování objednávky.');
  });
});
