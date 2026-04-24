import { toDateString, addDaysToDateString, daysBetween, clampDateString } from "../calendarDateUtils";

describe("calendarDateUtils", () => {
  describe("toDateString", () => {
    test("formats Date to YYYY-MM-DD", () => {
      expect(toDateString(new Date(2026, 3, 15))).toBe("2026-04-15");
    });

    test("pads single-digit month and day", () => {
      expect(toDateString(new Date(2026, 0, 5))).toBe("2026-01-05");
    });
  });

  describe("addDaysToDateString", () => {
    test("adds positive days", () => {
      expect(addDaysToDateString("2026-04-15", 3)).toBe("2026-04-18");
    });

    test("adds negative days", () => {
      expect(addDaysToDateString("2026-04-15", -5)).toBe("2026-04-10");
    });

    test("crosses month boundary", () => {
      expect(addDaysToDateString("2026-04-29", 3)).toBe("2026-05-02");
    });
  });

  describe("daysBetween", () => {
    test("returns positive for forward range", () => {
      expect(daysBetween("2026-04-10", "2026-04-15")).toBe(5);
    });

    test("returns negative for backward range", () => {
      expect(daysBetween("2026-04-15", "2026-04-10")).toBe(-5);
    });

    test("returns zero for same date", () => {
      expect(daysBetween("2026-04-10", "2026-04-10")).toBe(0);
    });
  });

  describe("clampDateString", () => {
    test("clamps when from > to, returns to", () => {
      expect(clampDateString("2026-04-20", "2026-04-15")).toBe("2026-04-15");
    });

    test("returns original when from <= to", () => {
      expect(clampDateString("2026-04-10", "2026-04-15")).toBe("2026-04-10");
    });
  });
});
