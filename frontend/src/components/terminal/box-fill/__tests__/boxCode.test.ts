import { isValidBoxCode } from "../boxCode";

describe("isValidBoxCode", () => {
  it("accepts B followed by exactly 3 digits", () => {
    expect(isValidBoxCode("B001")).toBe(true);
    expect(isValidBoxCode("b123")).toBe(true);
  });

  it("trims surrounding whitespace before validating", () => {
    expect(isValidBoxCode("  B001  ")).toBe(true);
  });

  it("rejects wrong prefixes, lengths, and non-digits", () => {
    expect(isValidBoxCode("A001")).toBe(false);
    expect(isValidBoxCode("B01")).toBe(false);
    expect(isValidBoxCode("B0012")).toBe(false);
    expect(isValidBoxCode("BXYZ")).toBe(false);
    expect(isValidBoxCode("")).toBe(false);
  });
});
