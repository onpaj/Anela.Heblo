import { Carriers } from "../api/generated/api-client";

export const CARRIER_LABELS: Record<Carriers, string> = {
  [Carriers.Zasilkovna]: "Zásilkovna",
  [Carriers.PPL]: "PPL",
  [Carriers.GLS]: "GLS",
  [Carriers.Osobak]: "Osobní odběr",
};
