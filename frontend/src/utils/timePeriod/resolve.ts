import { DateRange, TimePeriod } from "./timePeriod";

export function resolveTimePeriod(
  period: TimePeriod,
  customFrom?: Date,
  customTo?: Date,
): { ranges: DateRange[]; primary: DateRange | null } {
  const now = new Date();

  switch (period) {
    case TimePeriod.PreviousQuarter: {
      const startOfCurrentMonth = new Date(
        now.getFullYear(),
        now.getMonth(),
        1,
      );
      const endOfPreviousMonth = new Date(startOfCurrentMonth.getTime() - 1);
      const startOfPreviousQuarter = new Date(
        startOfCurrentMonth.getFullYear(),
        startOfCurrentMonth.getMonth() - 3,
        1,
      );
      const range: DateRange = {
        from: startOfPreviousQuarter,
        to: endOfPreviousMonth,
      };
      return { ranges: [range], primary: range };
    }

    case TimePeriod.FutureQuarter: {
      const startOfFutureQuarterLastYear = new Date(
        now.getFullYear() - 1,
        now.getMonth(),
        1,
      );
      const endOfFutureQuarterLastYear = new Date(
        now.getFullYear() - 1,
        now.getMonth() + 3,
        0,
      );
      const range: DateRange = {
        from: startOfFutureQuarterLastYear,
        to: endOfFutureQuarterLastYear,
      };
      return { ranges: [range], primary: range };
    }

    case TimePeriod.Y2Y: {
      const startOfY2Y = new Date(now.getFullYear(), now.getMonth() - 12, 1);
      const endOfY2Y = new Date(now.getFullYear(), now.getMonth(), 0);
      const range: DateRange = { from: startOfY2Y, to: endOfY2Y };
      return { ranges: [range], primary: range };
    }

    case TimePeriod.PreviousSeason: {
      const seasonStart = new Date(now.getFullYear() - 1, 9, 1); // October (0-indexed)
      const seasonEnd = new Date(now.getFullYear(), 0, 31); // January 31
      const range: DateRange = { from: seasonStart, to: seasonEnd };
      return { ranges: [range], primary: range };
    }

    case TimePeriod.Q9M: {
      const sixMonthsAgo = new Date(
        now.getFullYear(),
        now.getMonth() - 6,
        now.getDate(),
      );
      const oneYearAgo = new Date(
        now.getFullYear() - 1,
        now.getMonth(),
        now.getDate(),
      );
      const oneYearAgoPlus3 = new Date(
        now.getFullYear() - 1,
        now.getMonth() + 3,
        now.getDate(),
      );
      const range1: DateRange = { from: sixMonthsAgo, to: now };
      const range2: DateRange = { from: oneYearAgo, to: oneYearAgoPlus3 };
      return { ranges: [range1, range2], primary: range1 };
    }

    case TimePeriod.CustomPeriod: {
      if (customFrom !== undefined && customTo !== undefined) {
        const range: DateRange = { from: customFrom, to: customTo };
        return { ranges: [range], primary: range };
      }
      return { ranges: [], primary: null };
    }

    default:
      throw new Error(`Unknown time period: ${period}`)
  }
}
