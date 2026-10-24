import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { 
  CalculateBatchPlanResponse, 
  CalculateBatchPlanRequest,
  BatchPlanControlMode,
  ProductSizeConstraint,
  ManufactureType
} from "../generated/api-client";

// Re-export types from generated API client for convenience
export { CalculateBatchPlanRequest, BatchPlanControlMode, ProductSizeConstraint, ManufactureType };

export const useBatchPlanningMutation = () => {
  return useMutation({
    mutationFn: async (request: CalculateBatchPlanRequest): Promise<CalculateBatchPlanResponse> => {
      const apiClient = getAuthenticatedApiClient(true); // Enable toasts for error handling
      
      return await apiClient.manufactureBatch_CalculateBatchPlan(request);
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
