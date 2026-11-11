import { ManufactureOrderState, ManufactureType } from "../../../api/generated/api-client";

export interface StateTransitions {
  next: ManufactureOrderState | null;
  previous: ManufactureOrderState | null;
}

// Display ranking only; legality of transitions comes from the backend.
const STATE_ORDER: Record<ManufactureOrderState, number> = {
  [ManufactureOrderState.Draft]: 0,
  [ManufactureOrderState.Planned]: 1,
  [ManufactureOrderState.SemiProductManufactured]: 2,
  [ManufactureOrderState.Completed]: 3,
  [ManufactureOrderState.Cancelled]: 4,
};

export const isStateTransitionBackward = (
  currentState: ManufactureOrderState,
  newState: ManufactureOrderState
): boolean => STATE_ORDER[newState] < STATE_ORDER[currentState];

export const deriveStateTransitions = (
  current: ManufactureOrderState,
  available: ManufactureOrderState[] | undefined,
  manufactureType: ManufactureType | undefined
): StateTransitions => {
  const none: StateTransitions = { next: null, previous: null };
  if (!available || available.length === 0 || current === ManufactureOrderState.Cancelled) {
    return none;
  }

  const candidates = available.filter(
    (s) =>
      s !== ManufactureOrderState.Cancelled &&
      !(
        manufactureType === ManufactureType.SinglePhase &&
        s === ManufactureOrderState.SemiProductManufactured
      )
  );
  const rank = STATE_ORDER[current];
  const above = candidates
    .filter((s) => STATE_ORDER[s] > rank)
    .sort((a, b) => STATE_ORDER[a] - STATE_ORDER[b]);
  const below = candidates
    .filter((s) => STATE_ORDER[s] < rank)
    .sort((a, b) => STATE_ORDER[b] - STATE_ORDER[a]);

  return { next: above[0] ?? null, previous: below[0] ?? null };
};
