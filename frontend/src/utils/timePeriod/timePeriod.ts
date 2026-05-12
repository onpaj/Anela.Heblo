export enum TimePeriod {
  PreviousQuarter = "PreviousQuarter",
  FutureQuarter = "FutureQuarter",
  Y2Y = "Y2Y",
  PreviousSeason = "PreviousSeason",
  Q9M = "Q9M",
  CustomPeriod = "CustomPeriod",
}

export interface DateRange {
  from: Date;
  to: Date;
}
