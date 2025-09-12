import React from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useBatchPlanningMutation, BatchPlanControlMode, getControlModeDisplayName, formatVolume, formatDays } from '../useBatchPlanning';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client
jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      {children}
    </QueryClientProvider>
  );
};

describe('useBatchPlanningMutation', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  const mockApiClient = {
    manufactureBatch_CalculateBatchPlan: jest.fn(),
  };

  it('should successfully call the batch planning API', async () => {
    const mockResponse = {
      success: true,
      semiproduct: {
        productCode: 'SEMI001',
        productName: 'Test Semiproduct',
        availableStock: 1000,
      },
      productSizes: [],
      summary: {
        totalProductSizes: 0,
        totalVolumeUsed: 0,
        totalVolumeAvailable: 1000,
        volumeUtilizationPercentage: 0,
        usedControlMode: BatchPlanControlMode.MmqMultiplier,
        effectiveMmqMultiplier: 1.5,
        actualTotalWeight: 0,
        achievedAverageCoverage: 0,
        fixedProductsCount: 0,
        optimizedProductsCount: 0,
      },
      targetDaysCoverage: 30,
      totalVolumeUsed: 0,
      totalVolumeAvailable: 1000,
    };

    mockApiClient.manufactureBatch_CalculateBatchPlan.mockResolvedValue(mockResponse);
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    const wrapper = createWrapper();
    const { result } = renderHook(() => useBatchPlanningMutation(), { wrapper });

    const request = {
      semiproductCode: 'SEMI001',
      controlMode: BatchPlanControlMode.MmqMultiplier,
      mmqMultiplier: 1.5,
    };

    result.current.mutate(request);

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    expect(result.current.data).toEqual(mockResponse);
    expect(mockApiClient.manufactureBatch_CalculateBatchPlan).toHaveBeenCalledWith(request);
  });

  it('should handle API errors', async () => {
    const errorMessage = 'Semiproduct not found';
    mockApiClient.manufactureBatch_CalculateBatchPlan.mockRejectedValue(new Error(errorMessage));
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    const wrapper = createWrapper();
    const { result } = renderHook(() => useBatchPlanningMutation(), { wrapper });

    const request = {
      semiproductCode: 'INVALID',
      controlMode: BatchPlanControlMode.MmqMultiplier,
      mmqMultiplier: 1.0,
    };

    result.current.mutate(request);

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });

    expect(result.current.error).toBeTruthy();
    expect(mockApiClient.manufactureBatch_CalculateBatchPlan).toHaveBeenCalledWith(request);
  });
});

describe('Utility Functions', () => {
  describe('getControlModeDisplayName', () => {
    it('should return correct display names', () => {
      expect(getControlModeDisplayName(BatchPlanControlMode.MmqMultiplier)).toBe('MMQ Multiplier');
      expect(getControlModeDisplayName(BatchPlanControlMode.TotalWeight)).toBe('Total Weight');
      expect(getControlModeDisplayName(BatchPlanControlMode.TargetDaysCoverage)).toBe('Target Coverage');
    });

    it('should handle unknown control mode', () => {
      expect(getControlModeDisplayName(99 as BatchPlanControlMode)).toBe('Unknown');
    });
  });

  describe('formatVolume', () => {
    it('should format volume correctly', () => {
      expect(formatVolume(1000)).toBe('1000.0 ml');
      expect(formatVolume(123.456)).toBe('123.5 ml');
      expect(formatVolume(0)).toBe('0.0 ml');
    });
  });

  describe('formatDays', () => {
    it('should format days correctly', () => {
      expect(formatDays(30.5)).toBe('30.5 days');
      expect(formatDays(0)).toBe('0.0 days');
      expect(formatDays(7.0)).toBe('7.0 days');
    });

    it('should handle infinite days', () => {
      expect(formatDays(Infinity)).toBe('∞');
      expect(formatDays(10000)).toBe('∞');
    });
  });
});