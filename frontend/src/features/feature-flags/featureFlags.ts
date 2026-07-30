export const FeatureFlagKeys = {
  LabelPrinting: "is-label-printing-enabled",
} as const;

export type FeatureFlagKey = (typeof FeatureFlagKeys)[keyof typeof FeatureFlagKeys];
