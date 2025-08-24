import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useManufacturingStockAnalysisQuery, TimePeriodFilter, calculateTimePeriodRange } from '../useManufacturingStockAnalysis';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client
jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      {children}
    </QueryClientProvider>
  );
};

describe('useManufacturingStockAnalysisQuery', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  const mockApiClient = {
    http: {
      fetch: jest.fn()
    },
    baseUrl: 'http://localhost:5001'
  };

  const mockResponse = {
    items: [
      {
        code: 'TEST001',
        name: 'Test Product',
        currentStock: 100,
        salesInPeriod: 50,
        dailySalesRate: 2.5,
        optimalDaysSetup: 20,
        stockDaysAvailable: 40,
        minimumStock: 10,
        overstockPercentage: 200,
        batchSize: '25',
        productFamily: 'TestFamily',
        severity: 'Adequate',
        isConfigured: true
      }
    ],
    summary: {
      totalProducts: 1,
      criticalCount: 0,
      majorCount: 0,
      minorCount: 0,
      adequateCount: 1,
      unconfiguredCount: 0,
      analysisPeriodStart: '2023-01-01T00:00:00Z',
      analysisPeriodEnd: '2023-03-31T23:59:59Z',
      productFamilies: ['TestFamily']
    },
    totalCount: 1,
    pageNumber: 1,
    pageSize: 20
  };

  it('fetches manufacturing stock analysis data successfully', async () => {
    mockGetAuthenticatedApiClient.mockResolvedValue(mockApiClient as any);
    mockApiClient.http.fetch.mockResolvedValue({
      ok: true,
      json: async () => mockResponse
    } as any);

    const { result } = renderHook(
      () => useManufacturingStockAnalysisQuery({
        timePeriod: TimePeriodFilter.PreviousQuarter,
        pageNumber: 1,
        pageSize: 20
      }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockResponse);
    expect(result.current.error).toBeNull();
  });

  it('handles API errors correctly', async () => {
    mockGetAuthenticatedApiClient.mockResolvedValue(mockApiClient as any);
    mockApiClient.http.fetch.mockResolvedValue({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error'
    } as any);

    const { result } = renderHook(
      () => useManufacturingStockAnalysisQuery({
        timePeriod: TimePeriodFilter.PreviousQuarter,
        pageNumber: 1,
        pageSize: 20
      }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.error).toBeTruthy();
    expect(result.current.data).toBeUndefined();
  });

  it('constructs correct API URL with parameters', async () => {
    mockGetAuthenticatedApiClient.mockResolvedValue(mockApiClient as any);
    mockApiClient.http.fetch.mockResolvedValue({
      ok: true,
      json: async () => mockResponse
    });

    const { result } = renderHook(
      () => useManufacturingStockAnalysisQuery({
        timePeriod: TimePeriodFilter.PreviousQuarter,
        pageNumber: 2,
        pageSize: 10,
        searchTerm: 'test',
        criticalItemsOnly: true,
        productFamily: 'TestFamily'
      }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    // Check that the URL contains all expected parameters
    const callArgs = mockApiClient.http.fetch.mock.calls[0];
    const url = callArgs[0] as string;
    expect(url).toContain('http://localhost:5001/api/manufacturing-stock-analysis');
    expect(url).toContain('pageNumber=2');
    expect(url).toContain('pageSize=10');
    expect(url).toContain('searchTerm=test');
    expect(url).toContain('criticalItemsOnly=true');
    expect(url).toContain('productFamily=TestFamily');
    expect(callArgs[1]).toEqual({ method: 'GET', headers: { 'Accept': 'application/json' } });
  });

  it('handles custom time period with dates', async () => {
    const customFromDate = new Date('2023-01-01');
    const customToDate = new Date('2023-03-31');

    mockGetAuthenticatedApiClient.mockResolvedValue(mockApiClient as any);
    mockApiClient.http.fetch.mockResolvedValue({
      ok: true,
      json: async () => mockResponse
    } as any);

    const { result } = renderHook(
      () => useManufacturingStockAnalysisQuery({
        timePeriod: TimePeriodFilter.CustomPeriod,
        customFromDate,
        customToDate,
        pageNumber: 1,
        pageSize: 20
      }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
      expect.stringContaining('customFromDate=2023-01-01'),
      expect.objectContaining({ method: 'GET', headers: { 'Accept': 'application/json' } })
    );
    expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
      expect.stringContaining('customToDate=2023-03-31'),
      expect.any(Object)
    );
  });
});

describe('calculateTimePeriodRange', () => {
  const now = new Date('2023-04-15'); // Mid April 2023

  beforeAll(() => {
    jest.useFakeTimers();
    jest.setSystemTime(now);
  });

  afterAll(() => {
    jest.useRealTimers();
  });

  it('calculates previous quarter correctly', () => {
    const { fromDate, toDate } = calculateTimePeriodRange(TimePeriodFilter.PreviousQuarter);
    
    expect(fromDate.getMonth()).toBe(0); // January (0-indexed)
    expect(fromDate.getFullYear()).toBe(2023);
    expect(toDate.getMonth()).toBe(2); // March (0-indexed)
    expect(toDate.getFullYear()).toBe(2023);
  });

  it('calculates future quarter correctly', () => {
    const { fromDate, toDate } = calculateTimePeriodRange(TimePeriodFilter.FutureQuarter);
    
    expect(fromDate.getMonth()).toBe(3); // April (0-indexed)
    expect(fromDate.getFullYear()).toBe(2022); // Previous year
    expect(toDate.getMonth()).toBe(5); // June (0-indexed)  
    expect(toDate.getFullYear()).toBe(2022); // Previous year
  });

  it('calculates previous season correctly', () => {
    const { fromDate, toDate } = calculateTimePeriodRange(TimePeriodFilter.PreviousSeason);
    
    expect(fromDate.getMonth()).toBe(9); // October (0-indexed)
    expect(fromDate.getFullYear()).toBe(2022); // Previous year for season
    expect(toDate.getMonth()).toBe(0); // January (0-indexed)
    expect(toDate.getFullYear()).toBe(2023); // Next year from season start
  });

  it('returns null for custom period', () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.CustomPeriod);
    
    expect(result.fromDate).toBeNull();
    expect(result.toDate).toBeNull();
  });
});