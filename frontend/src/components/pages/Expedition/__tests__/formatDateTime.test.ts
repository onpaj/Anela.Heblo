import { formatDateTime } from "../formatDateTime";

describe("formatDateTime", () => {
  it("returns an en dash placeholder for null", () => {
    expect(formatDateTime(null)).toBe("–");
  });

  it("returns an en dash placeholder for an empty string", () => {
    expect(formatDateTime("")).toBe("–");
  });

  it("formats an ISO string as cs-CZ date with time", () => {
    const result = formatDateTime("2024-03-15T10:30:00Z");
    expect(result).toMatch(/\d{2}\.\s?\d{2}\.\s?\d{4}/);
    expect(result).toMatch(/\d{2}:\d{2}/);
  });
});
