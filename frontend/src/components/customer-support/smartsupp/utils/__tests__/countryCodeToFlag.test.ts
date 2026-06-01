import { countryCodeToFlag } from "../countryCodeToFlag";

describe("countryCodeToFlag", () => {
  it("converts CZ to Czech Republic flag emoji", () => {
    expect(countryCodeToFlag("CZ")).toBe("🇨🇿");
  });

  it("converts DE to German flag emoji", () => {
    expect(countryCodeToFlag("DE")).toBe("🇩🇪");
  });

  it("converts US to US flag emoji", () => {
    expect(countryCodeToFlag("US")).toBe("🇺🇸");
  });

  it("handles lowercase input", () => {
    expect(countryCodeToFlag("cz")).toBe("🇨🇿");
  });

  it("returns empty string for empty input", () => {
    expect(countryCodeToFlag("")).toBe("");
  });

  it("returns empty string for null-ish input", () => {
    expect(countryCodeToFlag(null as unknown as string)).toBe("");
  });
});
