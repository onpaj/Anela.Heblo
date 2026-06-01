import { TimePeriod } from "./timePeriod";

export function getTimePeriodDisplayText(period: TimePeriod): string {
  switch (period) {
    case TimePeriod.PreviousQuarter:
      return "Minulý kvartal";
    case TimePeriod.FutureQuarter:
      return "Budoucí kvartal";
    case TimePeriod.Y2Y:
      return "Y2Y (12 měsíců)";
    case TimePeriod.PreviousSeason:
      return "Předchozí sezona";
    case TimePeriod.Q9M:
      return "9M";
    case TimePeriod.CustomPeriod:
      return "Vlastní období";
    default:
      return period;
  }
}
