/**
 * @jest-environment jsdom
 */
import { renderHook, waitFor } from "@testing-library/react";
import { useManufactureBatch } from "../useManufactureBatch";

// Mock the authenticated API client
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: "http://localhost:5001",
  http: {
    fetch: mockFetch,
  },
};

// Cast the mocked import to access the mocked function
const { getAuthenticatedApiClient } = require("../../client");

describe("useManufactureBatch", () => {
  beforeEach(() => {
    jest.clearAllMocks();
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

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      });

      const { result } = renderHook(() => useManufactureBatch());

      // Act
      const response = await result.current.getBatchTemplate("TEST001");

      // Assert
      expect(response).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5001/api/manufacture-batch/template/TEST001",
        {
          method: "GET",
          headers: {
            "Content-Type": "application/json",
          },
        },
      );
    });

    it("should handle fetch error", async () => {
      // Arrange
      mockFetch.mockResolvedValueOnce({
        ok: false,
        statusText: "Not Found",
      });

      const { result } = renderHook(() => useManufactureBatch());

      // Act & Assert
      await expect(
        result.current.getBatchTemplate("NONEXISTENT"),
      ).rejects.toThrow("Failed to get batch template: Not Found");

      await waitFor(() => {
        expect(result.current.error).toBe(
          "Failed to get batch template: Not Found",
        );
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

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      });

      const { result } = renderHook(() => useManufactureBatch());

      // Act
      const response = await result.current.calculateBySize("TEST001", 150.0);

      // Assert
      expect(response).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5001/api/manufacture-batch/calculate-by-size",
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            productCode: "TEST001",
            desiredBatchSize: 150.0,
          }),
        },
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

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      });

      const { result } = renderHook(() => useManufactureBatch());

      // Act
      const response = await result.current.calculateByIngredient(
        "TEST001",
        "ING001",
        75.0,
      );

      // Assert
      expect(response).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5001/api/manufacture-batch/calculate-by-ingredient",
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            productCode: "TEST001",
            ingredientCode: "ING001",
            desiredIngredientAmount: 75.0,
          }),
        },
      );
    });
  });

  describe("loading state", () => {
    it("should update loading state during API calls", async () => {
      // Arrange
      mockFetch.mockImplementationOnce(
        () =>
          new Promise((resolve) =>
            setTimeout(
              () =>
                resolve({
                  ok: true,
                  json: () => Promise.resolve({ success: true }),
                }),
              100,
            ),
          ),
      );

      const { result } = renderHook(() => useManufactureBatch());

      // Initially not loading
      expect(result.current.isLoading).toBe(false);

      // Act
      const promise = result.current.getBatchTemplate("TEST001");

      // Should be loading during the call
      await waitFor(() => {
        expect(result.current.isLoading).toBe(true);
      });

      // Wait for completion
      await promise;

      // Should not be loading after completion
      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });
    });
  });
});
