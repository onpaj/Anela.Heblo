import { czechCountForm, czechPlural } from "../czechPlural";

const PRODUCT_FORMS = {
  one: "produkt",
  few: "produkty",
  many: "produktů",
};

describe("czechCountForm", () => {
  it("uses the singular for exactly one", () => {
    // Arrange / Act / Assert
    expect(czechCountForm(1)).toBe("one");
  });

  it("uses the two-to-four form inside that range", () => {
    // Arrange / Act / Assert
    expect(czechCountForm(2)).toBe("few");
    expect(czechCountForm(4)).toBe("few");
  });

  it("uses the many form from five upwards", () => {
    // Arrange / Act / Assert
    expect(czechCountForm(5)).toBe("many");
    expect(czechCountForm(42)).toBe("many");
  });

  it("uses the many form for none, not the two-to-four form", () => {
    // Arrange / Act / Assert: "0 produkty" is wrong, "0 produktů" is right.
    expect(czechCountForm(0)).toBe("many");
  });
});

describe("czechPlural", () => {
  it("picks the form matching the count", () => {
    // Arrange / Act / Assert
    expect(czechPlural(1, PRODUCT_FORMS)).toBe("produkt");
    expect(czechPlural(3, PRODUCT_FORMS)).toBe("produkty");
    expect(czechPlural(9, PRODUCT_FORMS)).toBe("produktů");
    expect(czechPlural(0, PRODUCT_FORMS)).toBe("produktů");
  });
});
