export const FeatureFlagKeys = {
  TransportBoxTracking: "is-transport-box-tracking-enabled",
  StockTaking: "is-stock-taking-enabled",
  BackgroundRefresh: "is-background-refresh-enabled",
  LabelPrinting: "is-label-printing-enabled",
} as const;

export type FeatureFlagKey = (typeof FeatureFlagKeys)[keyof typeof FeatureFlagKeys];
