import { ManufactureOrderState } from "../api/generated/api-client";

export const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300",
};

export const stateBorderColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "border-gray-200 dark:border-graphite-border",
  [ManufactureOrderState.Planned]: "border-blue-200 dark:border-blue-900/40",
  [ManufactureOrderState.SemiProductManufactured]: "border-yellow-200 dark:border-amber-900/40",
  [ManufactureOrderState.Completed]: "border-green-200 dark:border-emerald-900/40",
  [ManufactureOrderState.Cancelled]: "border-red-200 dark:border-red-900/40",
};