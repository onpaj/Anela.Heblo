import {
  roundUnitPrice,
  calculateLineTotal,
  calculateTotal,
  getValidLines,
  generateDefaultOrderNumber,
} from "../PurchaseOrderHelpers";
import { PurchaseOrderLineDto } from "../../../../api/generated/api-client";
import { MaterialForPurchaseDto } from "../../../../api/hooks/useMaterials";

describe("PurchaseOrderHelpers", () => {
  describe("roundUnitPrice", () => {
    it("rounds prices to 4 decimal places correctly", () => {
      expect(roundUnitPrice(25.123456789)).toBe(25.1235);
      expect(roundUnitPrice(25.12344)).toBe(25.1234);
      expect(roundUnitPrice(25.12341)).toBe(25.1234);
      expect(roundUnitPrice(25)).toBe(25);
      expect(roundUnitPrice(25.1)).toBe(25.1);
      expect(roundUnitPrice(0.00001)).toBe(0);
    });

    it("handles rounding that changes magnitude", () => {
      expect(roundUnitPrice(999.99999)).toBe(1000);
      expect(roundUnitPrice(9.99999)).toBe(10);
    });

    it("handles negative numbers", () => {
      expect(roundUnitPrice(-25.12344)).toBe(-25.1234);
      expect(roundUnitPrice(-25.12346)).toBe(-25.1235);
    });

    it("handles zero and very small numbers", () => {
      expect(roundUnitPrice(0)).toBe(0);
      expect(roundUnitPrice(0.00004)).toBe(0);
      expect(roundUnitPrice(0.00006)).toBe(0.0001);
      expect(roundUnitPrice(0.00016)).toBe(0.0002);
    });

    it("preserves existing 4 decimal places without rounding errors", () => {
      expect(roundUnitPrice(1.2345)).toBe(1.2345);
      expect(roundUnitPrice(100.0001)).toBe(100.0001);
      expect(roundUnitPrice(999.9999)).toBe(999.9999);
    });
  });

  describe("calculateLineTotal", () => {
    it("calculates line total correctly and rounds to 2 decimal places", () => {
      expect(calculateLineTotal(10, 25.1234)).toBe(251.23);
      expect(calculateLineTotal(3, 33.3333)).toBe(100);
      expect(calculateLineTotal(1.5, 20.6666)).toBe(31);
    });

    it("handles zero quantity or price", () => {
      expect(calculateLineTotal(0, 25.1234)).toBe(0);
      expect(calculateLineTotal(10, 0)).toBe(0);
      expect(calculateLineTotal(0, 0)).toBe(0);
    });

    it("handles undefined values", () => {
      expect(calculateLineTotal(undefined, 25.1234)).toBe(0);
      expect(calculateLineTotal(10, undefined)).toBe(0);
      expect(calculateLineTotal(undefined, undefined)).toBe(0);
    });
  });

  describe("calculateTotal", () => {
    it("calculates total from multiple lines", () => {
      const lines = [
        Object.assign(new PurchaseOrderLineDto(), { lineTotal: 251.23 }),
        Object.assign(new PurchaseOrderLineDto(), { lineTotal: 100.50 }),
        Object.assign(new PurchaseOrderLineDto(), { lineTotal: 75.27 }),
      ] as (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[];

      expect(calculateTotal(lines)).toBe(427);
    });

    it("handles lines with undefined lineTotal", () => {
      const lines = [
        Object.assign(new PurchaseOrderLineDto(), { lineTotal: 100 }),
        Object.assign(new PurchaseOrderLineDto(), { lineTotal: undefined }),
        Object.assign(new PurchaseOrderLineDto(), { lineTotal: 50 }),
      ] as (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[];

      expect(calculateTotal(lines)).toBe(150);
    });

    it("handles empty lines array", () => {
      expect(calculateTotal([])).toBe(0);
    });
  });

  describe("getValidLines", () => {
    const createMockLine = (
      materialId?: string,
      materialName?: string,
      quantity?: number,
      unitPrice?: number
    ) => {
      return Object.assign(new PurchaseOrderLineDto(), {
        materialId: materialId || "",
        materialName: materialName || "",
        quantity: quantity || 0,
        unitPrice: unitPrice || 0,
        selectedMaterial: materialId ? { 
          productCode: materialId, 
          productName: materialName || `Material ${materialId}` 
        } : null,
      });
    };

    it("returns only valid lines with material, quantity and price", () => {
      const lines = [
        createMockLine("MAT001", "Material 1", 10, 25.50),
        createMockLine("MAT002", "Material 2", 5, 30.00),
        createMockLine(undefined, "", 0, 0), // Invalid - no material
        createMockLine("MAT003", "Material 3", 0, 15.00), // Invalid - no quantity
        createMockLine("MAT004", "Material 4", 8, 0), // Invalid - no price
      ] as (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[];

      const validLines = getValidLines(lines);
      expect(validLines).toHaveLength(2);
      expect(validLines[0].selectedMaterial?.productCode).toBe("MAT001");
      expect(validLines[1].selectedMaterial?.productCode).toBe("MAT002");
    });

    it("handles empty lines array", () => {
      expect(getValidLines([])).toEqual([]);
    });
  });

  describe("generateDefaultOrderNumber", () => {
    it("generates order number with correct format", () => {
      const orderNumber = generateDefaultOrderNumber();
      expect(orderNumber).toMatch(/^PO\d{8}-\d{4}$/);
    });

    it("generates timestamps in order numbers", () => {
      const orderNumber = generateDefaultOrderNumber();
      const timestampPart = orderNumber.split('-')[1];
      // Should be a 4-digit number (timestamp in hours and minutes)
      expect(timestampPart).toMatch(/^\d{4}$/);
      expect(timestampPart.length).toBe(4);
    });
  });
});