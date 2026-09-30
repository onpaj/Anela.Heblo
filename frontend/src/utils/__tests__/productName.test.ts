import {
  formatProductLabel,
  formatProductNameWithSuffix,
  normalizeProductNameSuffix,
} from "../productName";

describe("productName formatters", () => {
  describe("formatProductLabel", () => {
    it("renders the suffix inline between the name and the code", () => {
      // Arrange
      const name = "Acmella In-Tense extrakt";
      const suffix = "Gatuline Expression AF";

      // Act
      const label = formatProductLabel(name, "MAT001", suffix);

      // Assert
      expect(label).toBe(
        "Acmella In-Tense extrakt · Gatuline Expression AF (MAT001)",
      );
    });

    it("keeps the unchanged Name (CODE) label when the suffix is undefined", () => {
      expect(formatProductLabel("Hydrolát máta peprná BIO", "MAT002")).toBe(
        "Hydrolát máta peprná BIO (MAT002)",
      );
    });

    it("keeps the unchanged Name (CODE) label when the suffix is empty or whitespace", () => {
      expect(formatProductLabel("Krém", "PROD-A", "")).toBe("Krém (PROD-A)");
      expect(formatProductLabel("Krém", "PROD-A", "   ")).toBe("Krém (PROD-A)");
      expect(formatProductLabel("Krém", "PROD-A", null)).toBe("Krém (PROD-A)");
    });

    it("trims whitespace around the suffix", () => {
      expect(formatProductLabel("Hydrolát", "MAT002", "  Menthe poivree ")).toBe(
        "Hydrolát · Menthe poivree (MAT002)",
      );
    });
  });

  describe("formatProductNameWithSuffix", () => {
    it("joins name and suffix with a middle dot", () => {
      expect(
        formatProductNameWithSuffix("Hydrolát máta peprná BIO", "Menthe poivree"),
      ).toBe("Hydrolát máta peprná BIO · Menthe poivree");
    });

    it("returns the bare name without a suffix", () => {
      expect(formatProductNameWithSuffix("Krém")).toBe("Krém");
    });
  });

  describe("normalizeProductNameSuffix", () => {
    it("returns undefined for blank values", () => {
      expect(normalizeProductNameSuffix(undefined)).toBeUndefined();
      expect(normalizeProductNameSuffix(null)).toBeUndefined();
      expect(normalizeProductNameSuffix(" ")).toBeUndefined();
    });

    it("returns the trimmed suffix", () => {
      expect(normalizeProductNameSuffix(" AF ")).toBe("AF");
    });
  });
});
