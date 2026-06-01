import { ManufactureOrderState } from "../api/generated/api-client";

export const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800",
};

export const stateBorderColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "border-gray-200",
  [ManufactureOrderState.Planned]: "border-blue-200",
  [ManufactureOrderState.SemiProductManufactured]: "border-yellow-200",
  [ManufactureOrderState.Completed]: "border-green-200",
  [ManufactureOrderState.Cancelled]: "border-red-200",
};