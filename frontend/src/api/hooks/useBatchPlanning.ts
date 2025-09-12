import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// Define types for the batch planning API
export interface CalculateBatchPlanRequest {
  semiproductCode: string;
  fromDate?: string;
  toDate?: string;
  controlMode: BatchPlanControlMode;
  mmqMultiplier?: number;
  totalWeightToUse?: number;
  targetDaysCoverage?: number;
  productConstraints?: ProductSizeConstraint[];
}

export interface ProductSizeConstraint {
  productCode: string;
  isFixed: boolean;
  fixedQuantity?: number;
}

export enum BatchPlanControlMode {
  MmqMultiplier = 1,
  TotalWeight = 2,
  TargetDaysCoverage = 3,
}

export interface CalculateBatchPlanResponse {
  success: boolean;
  errorCode?: string;
  params?: { [key: string]: string };
  semiproduct: SemiproductInfoDto;
  productSizes: BatchPlanItemDto[];
  summary: BatchPlanSummaryDto;
  targetDaysCoverage: number;
  totalVolumeUsed: number;
  totalVolumeAvailable: number;
}

export interface SemiproductInfoDto {
  productCode: string;
  productName: string;
  availableStock: number;
}

export interface BatchPlanItemDto {
  productCode: string;
  productName: string;
  productSize: string;
  currentStock: number;
  dailySalesRate: number;
  currentDaysCoverage: number;
  recommendedUnitsToProduceHumanReadable: number;
  volumePerUnit: number;
  totalVolumeRequired: number;
  futureStock: number;
  futureDaysCoverage: number;
  minimalManufactureQuantity: number;
  isFixed: boolean;
  userFixedQuantity?: number;
  wasOptimized: boolean;
  optimizationNote: string;
}

export interface BatchPlanSummaryDto {
  totalProductSizes: number;
  totalVolumeUsed: number;
  totalVolumeAvailable: number;
  volumeUtilizationPercentage: number;
  usedControlMode: BatchPlanControlMode;
  effectiveMmqMultiplier: number;
  actualTotalWeight: number;
  achievedAverageCoverage: number;
  fixedProductsCount: number;
  optimizedProductsCount: number;
}

export const useBatchPlanningMutation = () => {
  return useMutation({
    mutationFn: async (request: CalculateBatchPlanRequest): Promise<CalculateBatchPlanResponse> => {
      const apiClient = getAuthenticatedApiClient();
      
      // Get baseUrl from the apiClient instance  
      const baseUrl = (apiClient as any).baseUrl;
      if (!baseUrl) {
        throw new Error("Base URL not found in API client");
      }
      
      const response = await fetch(`${baseUrl}/api/manufacture-batch/calculate-batch-plan`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${localStorage.getItem('token') || ''}`,
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to calculate batch plan: ${response.status} ${errorText}`);
      }

      return await response.json();
    },
  });
};

// Control mode display helpers
export const getControlModeDisplayName = (mode: BatchPlanControlMode): string => {
  switch (mode) {
    case BatchPlanControlMode.MmqMultiplier:
      return "MMQ Multiplier";
    case BatchPlanControlMode.TotalWeight:
      return "Total Weight";
    case BatchPlanControlMode.TargetDaysCoverage:
      return "Target Coverage";
    default:
      return "Unknown";
  }
};

// Utility functions
export const formatVolume = (volume: number): string => {
  return `${volume.toFixed(1)} ml`;
};

export const formatDays = (days: number): string => {
  if (days === Infinity || days > 9999) {
    return "∞";
  }
  return `${days.toFixed(1)} days`;
};

export const formatPercentage = (percentage: number): string => {
  return `${percentage.toFixed(1)}%`;
};