// State machine for the single-screen material receive flow ("Identifikace šarže").
// All three steps (material → lot → scan) live on one screen; this reducer tracks which
// step is active and accumulates the scanned containers. Pure and immutable — every action
// returns a new state object so the flow is easy to test and reason about.

export type ReceiveMode = 'freeform' | 'po';
export type ReceiveStep = 'material' | 'lot' | 'scan';
export type BoxStatus = 'saving' | 'saved' | 'duplicate' | 'error';

export interface ScannedBox {
  id: number;
  code: string;
  status: BoxStatus;
  message?: string;
}

export interface SelectedMaterial {
  code: string;
  name: string;
}

export interface ReceiveState {
  mode: ReceiveMode;
  activeStep: ReceiveStep;
  material: SelectedMaterial | null;
  materialLocked: boolean; // true for the PO flow — the material comes from the order line
  lotCode: string | null;
  boxes: ScannedBox[];
}

export interface ReceiveInitArg {
  mode: ReceiveMode;
  material?: SelectedMaterial | null;
}

// PO starts with the material already chosen (from the order line) and locked, so the flow
// opens directly on the lot step. Freeform starts blank on the material step.
export const init = (arg: ReceiveInitArg): ReceiveState => {
  if (arg.mode === 'po') {
    return {
      mode: 'po',
      activeStep: 'lot',
      material: arg.material ?? null,
      materialLocked: true,
      lotCode: null,
      boxes: [],
    };
  }
  return {
    mode: 'freeform',
    activeStep: 'material',
    material: null,
    materialLocked: false,
    lotCode: null,
    boxes: [],
  };
};

export type ReceiveAction =
  | { type: 'SELECT_MATERIAL'; code: string; name: string }
  | { type: 'EDIT_MATERIAL' }
  | { type: 'CONFIRM_LOT'; lot: string }
  | { type: 'EDIT_LOT' }
  | { type: 'ADD_SCAN'; id: number; code: string }
  | { type: 'RESOLVE_SCAN'; id: number; status: BoxStatus; message?: string }
  | { type: 'REJECT_SCAN'; id: number; code: string; message: string }
  | { type: 'RESET' };

// Editing material or lot clears the box list: each container was already committed to the
// backend with its specific material+lot, so the on-screen list only ever represents the
// current (material, lot) session. Changing either starts a fresh session.
export const receiveReducer = (state: ReceiveState, action: ReceiveAction): ReceiveState => {
  switch (action.type) {
    case 'SELECT_MATERIAL':
      if (state.materialLocked) return state;
      return {
        ...state,
        material: { code: action.code, name: action.name },
        activeStep: 'lot',
        lotCode: null,
        boxes: [],
      };
    case 'EDIT_MATERIAL':
      if (state.materialLocked) return state;
      return { ...state, activeStep: 'material', lotCode: null, boxes: [] };
    case 'CONFIRM_LOT':
      if (!action.lot) return state;
      return { ...state, lotCode: action.lot, activeStep: 'scan', boxes: [] };
    case 'EDIT_LOT':
      if (state.material === null) return state;
      return { ...state, activeStep: 'lot', boxes: [] };
    case 'ADD_SCAN':
      return {
        ...state,
        boxes: [...state.boxes, { id: action.id, code: action.code, status: 'saving' }],
      };
    case 'RESOLVE_SCAN':
      return {
        ...state,
        boxes: state.boxes.map((b) =>
          b.id === action.id ? { ...b, status: action.status, message: action.message } : b,
        ),
      };
    case 'REJECT_SCAN':
      return {
        ...state,
        boxes: [
          ...state.boxes,
          { id: action.id, code: action.code, status: 'error', message: action.message },
        ],
      };
    case 'RESET':
      return init({ mode: state.mode });
    default:
      return state;
  }
};

export const savedCount = (state: ReceiveState): number =>
  state.boxes.filter((b) => b.status === 'saved').length;
