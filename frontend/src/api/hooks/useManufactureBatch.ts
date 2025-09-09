import { useState } from "react";
import { getAuthenticatedApiClient } from "../client";

export interface BatchTemplateResponse {
  success: boolean;
  productCode: string;
  productName: string;
  batchSize: number;
  ingredients: BatchIngredient[];
}

export interface BatchIngredient {
  productCode: string;
  productName: string;
  amount: number;
  price: number;
}

export interface CalculateBySizeResponse {
  success: boolean;
  productCode: string;
  productName: string;
  originalBatchSize: number;
  newBatchSize: number;
  scaleFactor: number;
  ingredients: CalculatedIngredient[];
}

export interface CalculateByIngredientResponse {
  success: boolean;
  productCode: string;
  productName: string;
  originalBatchSize: number;
  newBatchSize: number;
  scaleFactor: number;
  scaledIngredientCode: string;
  scaledIngredientName: string;
  scaledIngredientOriginalAmount: number;
  scaledIngredientNewAmount: number;
  ingredients: CalculatedIngredient[];
}

export interface CalculatedIngredient {
  productCode: string;
  productName: string;
  originalAmount: number;
  calculatedAmount: number;
  price: number;
}

export const useManufactureBatch = () => {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const getBatchTemplate = async (
    productCode: string,
  ): Promise<BatchTemplateResponse> => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/manufacture-batch/template/${encodeURIComponent(productCode)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: {
          "Content-Type": "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`Failed to get batch template: ${response.statusText}`);
      }

      const data = await response.json();
      return data as BatchTemplateResponse;
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
  ): Promise<CalculateBySizeResponse> => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = "/api/manufacture-batch/calculate-by-size";
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const requestBody = {
        productCode,
        desiredBatchSize,
      };

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(requestBody),
      });

      if (!response.ok) {
        throw new Error(`Failed to calculate by size: ${response.statusText}`);
      }

      const data = await response.json();
      return data as CalculateBySizeResponse;
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
  ): Promise<CalculateByIngredientResponse> => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = "/api/manufacture-batch/calculate-by-ingredient";
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const requestBody = {
        productCode,
        ingredientCode,
        desiredIngredientAmount,
      };

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(requestBody),
      });

      if (!response.ok) {
        throw new Error(
          `Failed to calculate by ingredient: ${response.statusText}`,
        );
      }

      const data = await response.json();
      return data as CalculateByIngredientResponse;
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
