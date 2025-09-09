import { useState } from "react";
import { getAuthenticatedApiClient } from "../client";
import { CalculatedBatchSizeResponse, CalculatedBatchSizeRequest, CalculateBatchByIngredientRequest, CalculateBatchByIngredientResponse } from "../generated/api-client";

export const useManufactureBatch = () => {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const getBatchTemplate = async (
    productCode: string,
  ): Promise<CalculatedBatchSizeResponse> => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const result = await apiClient.manufactureBatch_GetBatchTemplate(productCode);
      return result;
    } catch (err) {
      const errorMessage =
        err instanceof Error ? err.message : "Unknown error occurred";
      setError(errorMessage);
      throw err;
    } finally {
      setIsLoading(false);
    }
  };

  const calculateBySize = async (
    productCode: string,
    desiredBatchSize: number,
  ): Promise<CalculatedBatchSizeResponse> => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const request = new CalculatedBatchSizeRequest({
        productCode,
        desiredBatchSize,
      });
      const result = await apiClient.manufactureBatch_CalculateBatchBySize(request);
      return result;
    } catch (err) {
      const errorMessage =
        err instanceof Error ? err.message : "Unknown error occurred";
      setError(errorMessage);
      throw err;
    } finally {
      setIsLoading(false);
    }
  };

  const calculateByIngredient = async (
    productCode: string,
    ingredientCode: string,
    desiredIngredientAmount: number,
  ): Promise<CalculateBatchByIngredientResponse> => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const request = new CalculateBatchByIngredientRequest({
        productCode,
        ingredientCode,
        desiredIngredientAmount,
      });
      const result = await apiClient.manufactureBatch_CalculateBatchByIngredient(request);
      return result;
    } catch (err) {
      const errorMessage =
        err instanceof Error ? err.message : "Unknown error occurred";
      setError(errorMessage);
      throw err;
    } finally {
      setIsLoading(false);
    }
  };

  return {
    getBatchTemplate,
    calculateBySize,
    calculateByIngredient,
    isLoading,
    error,
  };
};
