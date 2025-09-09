/**
 * @jest-environment jsdom
 */
import { renderHook, waitFor, act } from "@testing-library/react";
import { useManufactureBatch } from "../useManufactureBatch";

// Mock the authenticated API client
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockManufactureBatch_GetBatchTemplate = jest.fn();
const mockManufactureBatch_CalculateBatchBySize = jest.fn();
const mockManufactureBatch_CalculateBatchByIngredient = jest.fn();

const mockApiClient = {
  baseUrl: "http://localhost:5001",
  manufactureBatch_GetBatchTemplate: mockManufactureBatch_GetBatchTemplate,
  manufactureBatch_CalculateBatchBySize: mockManufactureBatch_CalculateBatchBySize,
  manufactureBatch_CalculateBatchByIngredient: mockManufactureBatch_CalculateBatchByIngredient,
};

// Cast the mocked import to access the mocked function
const { getAuthenticatedApiClient } = require("../../client");

describe("useManufactureBatch", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockManufactureBatch_GetBatchTemplate.mockClear();
    mockManufactureBatch_CalculateBatchBySize.mockClear();
    mockManufactureBatch_CalculateBatchByIngredient.mockClear();
    getAuthenticatedApiClient.mockResolvedValue(mockApiClient);
  });

  describe("getBatchTemplate", () => {
    it("should fetch batch template successfully", async () => {
      // Arrange
      const mockResponse = {
        success: true,
        productCode: "TEST001",
        productName: "Test Product",
        batchSize: 100.0,
        ingredients: [
          {
            productCode: "ING001",
            productName: "Ingredient 1",
            amount: 50.0,
            price: 10.0,
          },
        ],
      };

      mockManufactureBatch_GetBatchTemplate.mockResolvedValueOnce(mockResponse);

      const { result } = renderHook(() => useManufactureBatch());

      // Act
      const response = await act(async () => {
        return await result.current.getBatchTemplate("TEST001");
      });

      // Assert
      expect(response).toEqual(mockResponse);
      expect(mockManufactureBatch_GetBatchTemplate).toHaveBeenCalledWith("TEST001");
    });

    it("should handle fetch error", async () => {
      // Arrange
      const errorMessage = "Not Found";
      mockManufactureBatch_GetBatchTemplate.mockRejectedValueOnce(new Error(errorMessage));

      const { result } = renderHook(() => useManufactureBatch());

      // Act & Assert
      await act(async () => {
        await expect(
          result.current.getBatchTemplate("NONEXISTENT"),
        ).rejects.toThrow(errorMessage);
      });

      await waitFor(() => {
        expect(result.current.error).toBe(errorMessage);
      });
    });
  });

  describe("calculateBySize", () => {
    it("should calculate batch by size successfully", async () => {
      // Arrange
      const mockResponse = {
        success: true,
        productCode: "TEST001",
        productName: "Test Product",
        originalBatchSize: 100.0,
        newBatchSize: 150.0,
        scaleFactor: 1.5,
        ingredients: [
          {
            productCode: "ING001",
            productName: "Ingredient 1",
            originalAmount: 50.0,
            calculatedAmount: 75.0,
            price: 10.0,
          },
        ],
      };

      mockManufactureBatch_CalculateBatchBySize.mockResolvedValueOnce(mockResponse);

      const { result } = renderHook(() => useManufactureBatch());

      // Act
      const response = await act(async () => {
        return await result.current.calculateBySize("TEST001", 150.0);
      });

      // Assert
      expect(response).toEqual(mockResponse);
      expect(mockManufactureBatch_CalculateBatchBySize).toHaveBeenCalledWith(
        expect.objectContaining({
          productCode: "TEST001",
          desiredBatchSize: 150.0,
        })
      );
    });
  });

  describe("calculateByIngredient", () => {
    it("should calculate batch by ingredient successfully", async () => {
      // Arrange
      const mockResponse = {
        success: true,
        productCode: "TEST001",
        productName: "Test Product",
        originalBatchSize: 100.0,
        newBatchSize: 150.0,
        scaleFactor: 1.5,
        scaledIngredientCode: "ING001",
        scaledIngredientName: "Ingredient 1",
        scaledIngredientOriginalAmount: 50.0,
        scaledIngredientNewAmount: 75.0,
        ingredients: [
          {
            productCode: "ING001",
            productName: "Ingredient 1",
            originalAmount: 50.0,
            calculatedAmount: 75.0,
            price: 10.0,
          },
        ],
      };

      mockManufactureBatch_CalculateBatchByIngredient.mockResolvedValueOnce(mockResponse);

      const { result } = renderHook(() => useManufactureBatch());

      // Act
      const response = await act(async () => {
        return await result.current.calculateByIngredient(
          "TEST001",
          "ING001",
          75.0,
        );
      });

      // Assert
      expect(response).toEqual(mockResponse);
      expect(mockManufactureBatch_CalculateBatchByIngredient).toHaveBeenCalledWith(
        expect.objectContaining({
          productCode: "TEST001",
          ingredientCode: "ING001",
          desiredIngredientAmount: 75.0,
        })
      );
    });
  });

  describe("loading state", () => {
    it("should update loading state during API calls", async () => {
      // Arrange
      mockManufactureBatch_GetBatchTemplate.mockImplementationOnce(
        () =>
          new Promise((resolve) =>
            setTimeout(
              () => resolve({ success: true }),
              100,
            ),
          ),
      );

      const { result } = renderHook(() => useManufactureBatch());

      // Initially not loading
      expect(result.current.isLoading).toBe(false);

      // Act
      let promise: Promise<any>;
      act(() => {
        promise = result.current.getBatchTemplate("TEST001");
      });

      // Should be loading during the call
      await waitFor(() => {
        expect(result.current.isLoading).toBe(true);
      });

      // Wait for completion
      await act(async () => {
        await promise;
      });

      // Should not be loading after completion
      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });
    });
  });
});
