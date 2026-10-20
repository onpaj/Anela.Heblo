// Which products the summary band adds up. The grid's own "work group" checkbox is
// deliberately separate: what you look at and what you measure are different questions.
export type PricingSummaryScope = "filter" | "all" | "workGroup";

export const PRICING_SUMMARY_SCOPE_OPTIONS: ReadonlyArray<{
  value: PricingSummaryScope;
  label: string;
}> = [
  { value: "all", label: "Všechny produkty" },
  { value: "filter", label: "Filtrované produkty" },
  { value: "workGroup", label: "Pracovní skupina" },
];

export const DEFAULT_PRICING_SUMMARY_SCOPE: PricingSummaryScope = "all";
