import { renderHook, waitFor } from '@testing-library/react';
import { useDqtRuns, useDqtRunDetail, useRunDqt } from '../useDataQuality';
import { DqtTestType, DqtRunStatus, RunDqtRequest } from '../../generated/api-client';
import { mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useDqtRuns', () => {
    let mockClient: {
        dataQuality_GetRuns: jest.Mock;
    };

    beforeEach(() => {
        jest.clearAllMocks();
        mockClient = {
            dataQuality_GetRuns: jest.fn(),
        };
        mockAuthenticatedApiClient(mockClient);
    });

    it('calls dataQuality_GetRuns with the given params', async () => {
        mockClient.dataQuality_GetRuns.mockResolvedValue({
            success: true,
            items: [],
            totalCount: 0,
            pageNumber: 1,
            pageSize: 20,
            totalPages: 0,
        });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(
            () => useDqtRuns({ testType: DqtTestType.ProductPairing, status: DqtRunStatus.Completed, pageNumber: 2, pageSize: 20 }),
            { wrapper },
        );

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockClient.dataQuality_GetRuns).toHaveBeenCalledWith(
            DqtTestType.ProductPairing,
            DqtRunStatus.Completed,
            2,
            20,
        );
    });

    it('passes undefined for omitted params', async () => {
        mockClient.dataQuality_GetRuns.mockResolvedValue({
            success: true,
            items: [],
            totalCount: 0,
            pageNumber: 1,
            pageSize: 20,
            totalPages: 0,
        });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useDqtRuns(), { wrapper });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockClient.dataQuality_GetRuns).toHaveBeenCalledWith(undefined, undefined, undefined, undefined);
    });

    it('returns the typed response from the generated client', async () => {
        const response = {
            success: true,
            items: [{ id: 'run-1', testType: 'IssuedInvoiceComparison', status: 'Completed' }],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 20,
            totalPages: 1,
        };
        mockClient.dataQuality_GetRuns.mockResolvedValue(response);

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useDqtRuns(), { wrapper });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(result.current.data).toEqual(response);
    });
});

describe('useDqtRunDetail', () => {
    let mockClient: {
        dataQuality_GetRunDetail: jest.Mock;
    };

    beforeEach(() => {
        jest.clearAllMocks();
        mockClient = {
            dataQuality_GetRunDetail: jest.fn(),
        };
        mockAuthenticatedApiClient(mockClient);
    });

    it('calls dataQuality_GetRunDetail with runId and paging', async () => {
        mockClient.dataQuality_GetRunDetail.mockResolvedValue({
            success: true,
            run: null,
            results: [],
            driftResults: [],
            totalDriftResults: 0,
        });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useDqtRunDetail('run-1', 2, 25), { wrapper });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockClient.dataQuality_GetRunDetail).toHaveBeenCalledWith('run-1', 2, 25);
    });

    it('does not fire when runId is null', async () => {
        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useDqtRunDetail(null), { wrapper });

        expect(result.current.fetchStatus).toBe('idle');
        expect(mockClient.dataQuality_GetRunDetail).not.toHaveBeenCalled();
    });
});

describe('useRunDqt', () => {
    let mockClient: {
        dataQuality_RunDqt: jest.Mock;
    };

    beforeEach(() => {
        jest.clearAllMocks();
        mockClient = {
            dataQuality_RunDqt: jest.fn(),
        };
        mockAuthenticatedApiClient(mockClient);
    });

    it('calls dataQuality_RunDqt with the given request', async () => {
        mockClient.dataQuality_RunDqt.mockResolvedValue({ success: true, dqtRunId: 'run-42' });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useRunDqt(), { wrapper });

        const request = new RunDqtRequest({
            testType: DqtTestType.IssuedInvoiceComparison,
            dateFrom: new Date(2026, 0, 1),
            dateTo: new Date(2026, 0, 31),
        });

        result.current.mutate(request);

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockClient.dataQuality_RunDqt).toHaveBeenCalledWith(request);
        expect(result.current.data).toEqual({ success: true, dqtRunId: 'run-42' });
    });
});
