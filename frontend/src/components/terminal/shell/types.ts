// shell/types.ts
import type { ReactNode } from 'react';

export type FlashTone = 'ok' | 'warn' | 'err';

/** One scan resolution = exactly one flash. id forces re-trigger on repeats. */
export interface FlashState {
  id: number;
  tone: FlashTone;
  /** short code/label echoed in the scan strip after the wash fades */
  code?: string;
}

/** Stable across renders — safe for workflow bodies to depend on. */
export interface ScanActions {
  /** Register the active screen's scan handler; returns an unregister fn. */
  registerScanHandler: (handler: (code: string) => void) => () => void;
  /** Fire exactly one flash per scan resolution. */
  flash: (tone: FlashTone, code?: string) => void;
  /** Tell the wedge to stop reclaiming focus (a sheet input is open). */
  setYieldFocus: (shouldYield: boolean) => void;
  /** Imperatively reclaim wedge focus (e.g. after a sheet closes). */
  refocus: () => void;
}

/** Volatile — changes per keystroke. Only ScanStrip should subscribe. */
export interface ScanEcho {
  /** live characters the wedge is accumulating, for the caret echo */
  buffer: string;
  /** last completed code dispatched to a screen */
  lastCode: string | null;
  /** tone of the last flash, for the quiet strip echo */
  lastTone: FlashTone | null;
}

/** A single docked action. 1 = full width, 2 = split. */
export interface DockAction {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  loading?: boolean;
  /** visual intent → colour. default 'primary'. */
  variant?: 'primary' | 'success' | 'ghost' | 'danger';
  testId?: string;
  icon?: ReactNode;
}

export interface ScanShellProps {
  /** Zone B. Pass null for the empty-prompt state. */
  subject?: ReactNode;
  /** Zone C — the only workflow-specific scrolling region. */
  children: ReactNode;
  /** Zone E. 0, 1, or 2 actions. */
  actions?: DockAction[];
}
