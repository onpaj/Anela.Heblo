import { resolveTimePeriod } from "../resolve";
import { TimePeriod } from "../timePeriod";

describe("resolveTimePeriod", () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2025-01-15"));
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe("PreviousQuarter", () => {
    it("returns 1 range with from < to", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.PreviousQuarter);

      // Assert
      expect(result.ranges).toHaveLength(1);
      expect(result.ranges[0].from < result.ranges[0].to).toBe(true);
      expect(result.primary).toBe(result.ranges[0]);
    });

    it("returns last 3 completed months", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.PreviousQuarter);

      // Assert
      // Now = 2025-01-15; start of current month = 2025-01-01
      // end of previous month = 2024-12-31 23:59:59.999
      // start of previous quarter = 2024-10-01
      expect(result.ranges[0].from).toEqual(new Date(2024, 9, 1)); // Oct 1 2024
      expect(result.ranges[0].to).toEqual(
        new Date(new Date(2025, 0, 1).getTime() - 1),
      ); // Dec 31 2024 23:59:59.999
    });
  });

  describe("FutureQuarter", () => {
    it("returns 1 range with from < to", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.FutureQuarter);

      // Assert
      expect(result.ranges).toHaveLength(1);
      expect(result.ranges[0].from < result.ranges[0].to).toBe(true);
      expect(result.primary).toBe(result.ranges[0]);
    });

    it("returns next 3 months from previous year", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.FutureQuarter);

      // Assert
      // Now = 2025-01-15; start = 2024-01-01, end = 2024-04-30 (last day of April 2024)
      expect(result.ranges[0].from).toEqual(new Date(2024, 0, 1)); // Jan 1 2024
      expect(result.ranges[0].to).toEqual(new Date(2024, 3, 0)); // Last day of March 2024 = Mar 31 2024
    });
  });

  describe("Y2Y", () => {
    it("returns 1 range with from < to", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Y2Y);

      // Assert
      expect(result.ranges).toHaveLength(1);
      expect(result.ranges[0].from < result.ranges[0].to).toBe(true);
      expect(result.primary).toBe(result.ranges[0]);
    });

    it("returns last 12 months", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Y2Y);

      // Assert
      // Now = 2025-01-15 (month=0); new Date(2025, 0-12, 1) = new Date(2025, -12, 1) = Jan 1 2024
      // new Date(2025, 0, 0) = Dec 31 2024
      expect(result.ranges[0].from).toEqual(new Date(2025, -12, 1)); // Jan 1 2024
      expect(result.ranges[0].to).toEqual(new Date(2025, 0, 0)); // Dec 31 2024
    });
  });

  describe("PreviousSeason", () => {
    it("returns 1 range with from < to", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.PreviousSeason);

      // Assert
      expect(result.ranges).toHaveLength(1);
      expect(result.ranges[0].from < result.ranges[0].to).toBe(true);
      expect(result.primary).toBe(result.ranges[0]);
    });

    it("returns Oct of previous year through Jan 31 of current year", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.PreviousSeason);

      // Assert
      // Now = 2025-01-15; season start = 2024-10-01, season end = 2025-01-31
      expect(result.ranges[0].from).toEqual(new Date(2024, 9, 1)); // Oct 1 2024
      expect(result.ranges[0].to).toEqual(new Date(2025, 0, 31)); // Jan 31 2025
    });
  });

  describe("Q9M", () => {
    it("returns exactly 2 ranges", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Q9M);

      // Assert
      expect(result.ranges).toHaveLength(2);
    });

    it("primary is the first range", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Q9M);

      // Assert
      expect(result.primary).toBe(result.ranges[0]);
    });

    it("range1 is from 6 months ago to now", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Q9M);

      // Assert
      // Now = 2025-01-15; 6 months ago = 2024-07-15
      expect(result.ranges[0].from).toEqual(new Date(2024, 6, 15)); // Jul 15 2024
      expect(result.ranges[0].to).toEqual(new Date("2025-01-15"));
    });

    it("range2 is from 1 year ago to 1 year ago + 3 months", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Q9M);

      // Assert
      // Now = 2025-01-15; 1 year ago = 2024-01-15; +3 months = 2024-04-15
      expect(result.ranges[1].from).toEqual(new Date(2024, 0, 15)); // Jan 15 2024
      expect(result.ranges[1].to).toEqual(new Date(2024, 3, 15)); // Apr 15 2024
    });

    it("both ranges have from < to", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.Q9M);

      // Assert
      expect(result.ranges[0].from < result.ranges[0].to).toBe(true);
      expect(result.ranges[1].from < result.ranges[1].to).toBe(true);
    });
  });

  describe("CustomPeriod", () => {
    it("returns 1 range when both dates provided", () => {
      // Arrange
      const from = new Date("2024-06-01");
      const to = new Date("2024-09-30");

      // Act
      const result = resolveTimePeriod(TimePeriod.CustomPeriod, from, to);

      // Assert
      expect(result.ranges).toHaveLength(1);
      expect(result.ranges[0].from).toBe(from);
      expect(result.ranges[0].to).toBe(to);
      expect(result.primary).toBe(result.ranges[0]);
    });

    it("returns 0 ranges when dates are missing", () => {
      // Arrange & Act
      const result = resolveTimePeriod(TimePeriod.CustomPeriod);

      // Assert
      expect(result.ranges).toHaveLength(0);
      expect(result.primary).toBeNull();
    });

    it("returns 0 ranges when only customFrom is provided", () => {
      // Arrange & Act
      const result = resolveTimePeriod(
        TimePeriod.CustomPeriod,
        new Date("2024-06-01"),
        undefined,
      );

      // Assert
      expect(result.ranges).toHaveLength(0);
      expect(result.primary).toBeNull();
    });

    it("returns 0 ranges when only customTo is provided", () => {
      // Arrange & Act
      const result = resolveTimePeriod(
        TimePeriod.CustomPeriod,
        undefined,
        new Date("2024-09-30"),
      );

      // Assert
      expect(result.ranges).toHaveLength(0);
      expect(result.primary).toBeNull();
    });
  });
});
