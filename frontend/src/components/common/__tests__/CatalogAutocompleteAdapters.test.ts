import { catalogItemToMaterial } from "../CatalogAutocompleteAdapters";
import { CatalogItemDto, ProductType } from "../../../api/generated/api-client";

describe("CatalogAutocompleteAdapters", () => {
  describe("catalogItemToMaterial", () => {
    it("maps CurrentPurchasePrice to lastPurchasePrice correctly", () => {
      const catalogItem = new CatalogItemDto({
        productCode: "TEST001",
        productName: "Test Material",
        type: ProductType.Material,
        location: "A1-B2",
        stock: { available: 100 },
        minimalOrderQuantity: "10",
        price: {
          currentPurchasePrice: 25.1234,
          currentSellingPrice: 35.0000,
        },
      });

      const material = catalogItemToMaterial(catalogItem);

      expect(material).toEqual({
        productCode: "TEST001",
        productName: "Test Material",
        productType: "Material",
        location: "A1-B2",
        currentStock: 100,
        minimalOrderQuantity: "10",
        lastPurchasePrice: 25.1234,
      });
    });

    it("handles missing price data gracefully", () => {
      const catalogItem = new CatalogItemDto({
        productCode: "TEST002",
        productName: "Test Material 2",
        type: ProductType.Material,
        location: "A1-B3",
        stock: { available: 50 },
        minimalOrderQuantity: "5",
        price: {
          currentPurchasePrice: undefined,
          currentSellingPrice: undefined,
        },
      });

      const material = catalogItemToMaterial(catalogItem);

      expect(material).toEqual({
        productCode: "TEST002",
        productName: "Test Material 2",
        productType: "Material", 
        location: "A1-B3",
        currentStock: 50,
        minimalOrderQuantity: "5",
        lastPurchasePrice: undefined,
      });
    });

    it("handles null/undefined price object", () => {
      const catalogItem = new CatalogItemDto({
        productCode: "TEST003",
        productName: "Test Material 3",
        type: ProductType.Material,
        location: "A1-B4",
        stock: { available: 25 },
        minimalOrderQuantity: "1",
        price: undefined,
      });

      const material = catalogItemToMaterial(catalogItem);

      expect(material).toEqual({
        productCode: "TEST003", 
        productName: "Test Material 3",
        productType: "Material",
        location: "A1-B4",
        currentStock: 25,
        minimalOrderQuantity: "1",
        lastPurchasePrice: undefined,
      });
    });

    it("handles zero price correctly", () => {
      const catalogItem = new CatalogItemDto({
        productCode: "TEST004",
        productName: "Test Material 4",
        type: ProductType.Material,
        location: "A1-B5",
        stock: { available: 75 },
        minimalOrderQuantity: "2",
        price: {
          currentPurchasePrice: 0,
          currentSellingPrice: 0,
        },
      });

      const material = catalogItemToMaterial(catalogItem);

      expect(material).toEqual({
        productCode: "TEST004",
        productName: "Test Material 4",
        productType: "Material",
        location: "A1-B5",
        currentStock: 75,
        minimalOrderQuantity: "2",
        lastPurchasePrice: undefined, // Zero is falsy, so becomes undefined
      });
    });

    it("handles different product types", () => {
      const catalogItem = new CatalogItemDto({
        productCode: "GOODS001",
        productName: "Test Goods",
        type: ProductType.Goods,
        location: "B1-C2",
        stock: { available: 200 },
        minimalOrderQuantity: "20",
        price: {
          currentPurchasePrice: 15.9876,
          currentSellingPrice: 25.50,
        },
      });

      const material = catalogItemToMaterial(catalogItem);

      expect(material).toEqual({
        productCode: "GOODS001",
        productName: "Test Goods",
        productType: "Goods",
        location: "B1-C2",
        currentStock: 200,
        minimalOrderQuantity: "20",
        lastPurchasePrice: 15.9876,
      });
    });

    it("handles very precise decimal prices", () => {
      const catalogItem = new CatalogItemDto({
        productCode: "PRECISE001",
        productName: "Precise Material",
        type: ProductType.Material,
        location: "C1-D2",
        stock: { available: 10 },
        minimalOrderQuantity: "1",
        price: {
          currentPurchasePrice: 123.456789012345,
          currentSellingPrice: 200.0,
        },
      });

      const material = catalogItemToMaterial(catalogItem);

      expect(material.lastPurchasePrice).toBe(123.456789012345);
      expect(typeof material.lastPurchasePrice).toBe("number");
    });
  });
});