import React from 'react';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../../client';
import { OvertimeAdjustmentType } from '../../generated/api-client';
import {
  useMonthlyStatementsQuery,
  useCloseMonthMutation,
  useCreateAdjustmentMutation,
  useDeleteAdjustmentMutation,
  downloadOvertimeReport,
} from '../useOvertime';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { overtime: ['overtime'] },
}));

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useOvertime', () => {
  afterEach(() => jest.resetAllMocks());

  test('useMonthlyStatementsQuery fetches statements for the month', async () => {
    const mockClient = {
      overtime_GetMonthlyStatements: jest.fn().mockResolvedValue({
        success: true, year: 2026, month: 8, isClosed: false, statements: [],
      }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useMonthlyStatementsQuery(2026, 8), { wrapper: createWrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockClient.overtime_GetMonthlyStatements).toHaveBeenCalledWith(2026, 8);
  });

  test('useCloseMonthMutation passes force flag', async () => {
    const mockClient = {
      overtime_CloseMonth: jest.fn().mockResolvedValue({ success: true, closedCount: 3 }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useCloseMonthMutation(), { wrapper: createWrapper });
    await act(async () => {
      await result.current.mutateAsync({ year: 2026, month: 8, force: true });
    });

    expect(mockClient.overtime_CloseMonth).toHaveBeenCalledWith(2026, 8, true);
  });

  test('useCreateAdjustmentMutation sends the adjustment fields', async () => {
    const mockClient = {
      overtime_CreateAdjustment: jest.fn().mockResolvedValue({ success: true }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useCreateAdjustmentMutation(), { wrapper: createWrapper });
    await act(async () => {
      await result.current.mutateAsync({
        personId: 'person-1',
        year: 2026,
        month: 8,
        type: OvertimeAdjustmentType.Payout,
        hours: -4,
        note: 'Vyplaceno',
      });
    });

    expect(mockClient.overtime_CreateAdjustment).toHaveBeenCalledWith(
      expect.objectContaining({
        personId: 'person-1',
        year: 2026,
        month: 8,
        type: OvertimeAdjustmentType.Payout,
        hours: -4,
        note: 'Vyplaceno',
      }),
    );
  });

  test('useDeleteAdjustmentMutation passes the adjustment id', async () => {
    const mockClient = {
      overtime_DeleteAdjustment: jest.fn().mockResolvedValue({ success: true }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useDeleteAdjustmentMutation(), { wrapper: createWrapper });
    await act(async () => {
      await result.current.mutateAsync(42);
    });

    expect(mockClient.overtime_DeleteAdjustment).toHaveBeenCalledWith(42);
  });
});

describe('downloadOvertimeReport', () => {
  afterEach(() => jest.restoreAllMocks());

  const mockDom = () => {
    URL.createObjectURL = jest.fn().mockReturnValue('blob:test-url');
    URL.revokeObjectURL = jest.fn();
    jest.spyOn(document.body, 'appendChild').mockImplementation((node) => node);
    jest.spyOn(document.body, 'removeChild').mockImplementation((node) => node);
  };

  test('downloads using the filename from Content-Disposition', async () => {
    mockDom();
    const fetchMock = jest.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => 'attachment; filename="Overtime-2026-08.xlsx"' },
      blob: async () => new Blob(['x']),
    });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: 'http://api.test',
      http: { fetch: fetchMock },
    });
    const createElementSpy = jest.spyOn(document, 'createElement');

    await downloadOvertimeReport();

    expect(fetchMock).toHaveBeenCalledWith('http://api.test/api/overtime/export');
    const link = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLAnchorElement;
    expect(link.download).toBe('Overtime-2026-08.xlsx');
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test-url');
  });

  test('falls back to the default filename when Content-Disposition is absent', async () => {
    mockDom();
    const fetchMock = jest.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      blob: async () => new Blob(['x']),
    });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: 'http://api.test',
      http: { fetch: fetchMock },
    });
    const createElementSpy = jest.spyOn(document, 'createElement');

    await downloadOvertimeReport();

    const link = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLAnchorElement;
    expect(link.download).toBe('Evidence-prescasu.xlsx');
  });

  test('throws when the response is not ok', async () => {
    mockDom();
    const fetchMock = jest.fn().mockResolvedValue({ ok: false, status: 500 });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: 'http://api.test',
      http: { fetch: fetchMock },
    });

    await expect(downloadOvertimeReport()).rejects.toThrow('Stažení reportu selhalo (500)');
  });
});
