import { PricingRowDto } from "../../../api/generated/api-client";
import {
  PRICING_WORK_GROUP_STORAGE_KEY,
  applyWorkGroupSelection,
  filterWorkGroupRows,
  isInWorkGroup,
  loadWorkGroupProductCodes,
  saveWorkGroupProductCodes,
  toggleWorkGroupProductCode,
} from "../pricingWorkGroup";

const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    isEdited: false,
    isExcluded: false,
    ...overrides,
  }) as PricingRowDto;

describe("pricingWorkGroup", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  describe("isInWorkGroup", () => {
    it("includes a row that carries an edit even when it is not pinned", () => {
      // Arrange
      const row = buildRow({ isEdited: true });

      // Act
      const result = isInWorkGroup(row, new Set());

      // Assert
      expect(result).toBe(true);
    });

    it("includes an untouched row that the user pinned", () => {
      // Arrange
      const row = buildRow({ productCode: "PROD002" });

      // Act
      const result = isInWorkGroup(row, new Set(["PROD002"]));

      // Assert
      expect(result).toBe(true);
    });

    it("excludes an untouched row that is not pinned", () => {
      // Arrange
      const row = buildRow();

      // Act
      const result = isInWorkGroup(row, new Set(["OTHER"]));

      // Assert
      expect(result).toBe(false);
    });
  });

  describe("filterWorkGroupRows", () => {
    it("keeps only the edited and pinned rows, in their original order", () => {
      // Arrange
      const rows = [
        buildRow({ productCode: "A" }),
        buildRow({ productCode: "B", isEdited: true }),
        buildRow({ productCode: "C" }),
      ];

      // Act
      const result = filterWorkGroupRows(rows, new Set(["C"]));

      // Assert
      expect(result.map((row) => row.productCode)).toEqual(["B", "C"]);
    });

    it("leaves the input array untouched", () => {
      // Arrange
      const rows = [buildRow({ productCode: "A" }), buildRow({ productCode: "B" })];

      // Act
      filterWorkGroupRows(rows, new Set(["A"]));

      // Assert
      expect(rows).toHaveLength(2);
    });
  });

  describe("toggleWorkGroupProductCode", () => {
    it("adds a product code that is not pinned yet", () => {
      // Arrange
      const codes = ["A"];

      // Act
      const result = toggleWorkGroupProductCode(codes, "B");

      // Assert
      expect(result).toEqual(["A", "B"]);
      expect(codes).toEqual(["A"]);
    });

    it("removes a product code that is already pinned", () => {
      // Arrange
      const codes = ["A", "B"];

      // Act
      const result = toggleWorkGroupProductCode(codes, "A");

      // Assert
      expect(result).toEqual(["B"]);
      expect(codes).toEqual(["A", "B"]);
    });
  });

  describe("storage", () => {
    it("round-trips the pinned product codes through localStorage", () => {
      // Arrange
      saveWorkGroupProductCodes(["A", "B"]);

      // Act
      const result = loadWorkGroupProductCodes();

      // Assert
      expect(result).toEqual(["A", "B"]);
    });

    it("returns an empty list when nothing was stored yet", () => {
      // Act
      const result = loadWorkGroupProductCodes();

      // Assert
      expect(result).toEqual([]);
    });

    it("returns an empty list when the stored value is not valid JSON", () => {
      // Arrange
      window.localStorage.setItem(PRICING_WORK_GROUP_STORAGE_KEY, "{not json");

      // Act
      const result = loadWorkGroupProductCodes();

      // Assert
      expect(result).toEqual([]);
    });

    it("drops entries that are not product codes", () => {
      // Arrange
      window.localStorage.setItem(
        PRICING_WORK_GROUP_STORAGE_KEY,
        JSON.stringify(["A", 42, null, "", "B"]),
      );

      // Act
      const result = loadWorkGroupProductCodes();

      // Assert
      expect(result).toEqual(["A", "B"]);
    });

    it("returns an empty list when the stored value is not an array", () => {
      // Arrange
      window.localStorage.setItem(
        PRICING_WORK_GROUP_STORAGE_KEY,
        JSON.stringify({ A: true }),
      );

      // Act
      const result = loadWorkGroupProductCodes();

      // Assert
      expect(result).toEqual([]);
    });
  });

  describe("toggleWorkGroupProductCode", () => {
    it("refuses to pin a row that has no product code", () => {
      // Arrange: the empty key would tick every other code-less row with it, and the
      // override it leads to is one the server rejects outright.
      // Act
      const result = toggleWorkGroupProductCode(["A"], "");

      // Assert
      expect(result).toEqual(["A"]);
    });
  });

  describe("applyWorkGroupSelection", () => {
    it("appends the codes that are not pinned yet, keeping the existing order", () => {
      // Arrange / Act
      const result = applyWorkGroupSelection(["A", "B"], ["B", "C"], true);

      // Assert
      expect(result).toEqual(["A", "B", "C"]);
    });

    it("pins a repeated code only once", () => {
      // Arrange / Act
      const result = applyWorkGroupSelection([], ["A", "A", "B"], true);

      // Assert
      expect(result).toEqual(["A", "B"]);
    });

    it("drops the given codes when unpinning and leaves the rest alone", () => {
      // Arrange / Act
      const result = applyWorkGroupSelection(["A", "B", "C"], ["A", "C"], false);

      // Assert
      expect(result).toEqual(["B"]);
    });

    it("never pins a row that has no product code", () => {
      // Arrange: an empty key would match every other code-less row at once.
      // Act
      const result = applyWorkGroupSelection(["A"], ["", "B"], true);

      // Assert
      expect(result).toEqual(["A", "B"]);
    });

    it("leaves the pinned set untouched when nothing is selected", () => {
      // Arrange / Act
      const pinned = ["A", "B"];
      const result = applyWorkGroupSelection(pinned, [], true);

      // Assert
      expect(result).toEqual(["A", "B"]);
      expect(result).not.toBe(pinned);
    });
  });

});
