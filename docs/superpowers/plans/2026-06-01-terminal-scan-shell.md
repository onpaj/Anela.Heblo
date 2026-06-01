# Terminal "Scan Shell" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor every Terminal workflow onto one shared "Scan Shell" UI skeleton (subject header → body → scan strip → docked action + full-bleed flash on each scan), then build the two unbuilt workflows on it.

**Architecture:** A single `ScanProvider` owns one hidden keyboard-wedge `<input>` and the flash dispatch, persisting across routes inside `TerminalLayout`. A `ScanShell` lays out the fixed zones; each workflow supplies only a *subject*, a *body*, *actions*, and an *onScan* handler via the `useScanScreen` hook. A `FlashOverlay` renders the per-scan colour wash. Existing TanStack Query hooks (`useTransportBoxByCodeQuery`, `useChangeTransportBoxState`, `useBoxFill` family) are reused unchanged.

**Tech Stack:** React 18 + TypeScript, React Router v6 (nested routes), Tailwind (existing theme tokens), lucide-react icons, TanStack Query. Tests: **Jest + @testing-library/react** via `react-scripts test` (NOT vitest).

---

## Provenance, scope & gates (read before starting)

**Source of truth.** This plan derives from the design handoff at
`docs/superpowers/specs/2026-06-01-terminal-scan-shell-design.md` (the original
README) plus a thorough read of the live codebase. The handoff README referenced
an **authoritative** `spec.html` (§5 TS prop contracts, §8 per-workflow detail,
§10 token table, §12 acceptance criteria) and a runnable HTML/JSX prototype —
**those files were not in the delivered ZIP.** The TypeScript contracts below are
therefore *reconstructed* from the README and the existing code. Where a step says
**[confirm vs spec.html]**, the prop/acceptance detail is a best-effort
reconstruction; request the missing spec before treating it as final.

**Codebase facts that correct the README:**
- The README says to *add* feedback colours. In `frontend/tailwind.config.js` the
  **base** `success #10B981`, `warning #F59E0B`, `error #EF4444`, `info #06B6D4`
  already exist as flat tokens. Only the **pale** backgrounds and the **scan
  accent** are missing. Task 1 adds `success-pale`/`warning-pale`/`error-pale`/
  `scan-accent` as *new* tokens (no edits to existing ones — additive, immutable).
- `TransportBoxStateBadge` lives at
  `frontend/src/components/transport/box-detail/components/TransportBoxStateBadge.tsx`
  (the README dropped the `components/` segment). `stateLabels`/`stateColors` are
  in `frontend/src/components/transport/box-detail/TransportBoxTypes.tsx`.
- Test runner is Jest (`react-scripts test`), not vitest. Run a single file with:
  `cd frontend && CI=true npx react-scripts test src/path/to/file.test.tsx --watchAll=false`.

**Phase gate (CRITICAL).** Tasks 2–11 (Phases 0–5: foundation, shell kit, three
refactors, home) are fully specifiable now and have no backend dependency.
Tasks 12–13 (Phase 6: **Inventura** and **Identifikace šarže**) require backend
APIs (stocktake materials-by-lot / expected-qty / submit; lot registration) that
**may not exist yet** — README Open Question #2. Task 12 is a **discovery gate**:
do not write UI for the net-new workflows until those endpoints are confirmed.
If they are missing, stop and report; that becomes a separate backend plan.

**Migration order** (from README §"Migration plan"): foundation (no visual
change) → shell kit in isolation → Kontrola boxu (lowest risk) → Příjem boxu →
Plnění boxu → Home → net-new workflows → cleanup. Each phase ships independently
and leaves the app working.

---

## File structure

All paths relative to `frontend/src/components/terminal/` unless noted.

**New — `shell/` folder (one responsibility each):**
| File | Responsibility |
|---|---|
| `shell/types.ts` | Shared types: `FlashTone`, `FlashState`, context value shapes, `ScanShellProps`, `DockAction`. |
| `shell/ScanProvider.tsx` | Owns the singleton hidden wedge `<input>`, the keystroke buffer, focus/yield model, the active-screen scan-handler registry, and `flash()`. Exposes two contexts (stable actions + volatile echo) to avoid per-keystroke re-renders of workflow bodies. |
| `shell/FlashOverlay.tsx` | Full-bleed, `pointer-events:none`, `aria-live` colour wash + glyph. Visible-by-default, animates as polish. Mounted once in `TerminalLayout`. |
| `shell/ScanShell.tsx` | Lays out zones B–E (subject header, body, scan strip, docked action). Workflow bodies render *into* it. |
| `shell/SubjectHeader.tsx` | Zone B — code + `TransportBoxStateBadge` + key facts, or an empty-prompt before first scan. |
| `shell/ScanStrip.tsx` | Zone D — persistent wedge surface: ready → live buffer + caret → last-code echo with last tone. Subscribes to the volatile echo context only. |
| `shell/DockedAction.tsx` | Zone E — 1 action = full width, 2 = split, optional FAB variant. |
| `shell/BottomSheet.tsx` | Reusable bottom sheet; while open with an input, sets `yieldFocus` so the wedge stops stealing focus, and reclaims on close. |
| `shell/useScanScreen.ts` | Hook a workflow calls to register its `onScan` handler (auto-unregisters on unmount / route change) and to read `flash`, `lastCode`, focus controls. |
| `shell/__tests__/*` | Jest tests per component. |

**Changed:**
| File | Change |
|---|---|
| `tailwind.config.js` (frontend root) | Add `success-pale`, `warning-pale`, `error-pale`, `scan-accent` tokens. |
| `TerminalLayout.tsx` | Wrap `<Outlet/>` in `<ScanProvider>`; mount `<FlashOverlay/>` + the wedge once here; drop the `max-w-md` scroll container (the shell owns layout). Keep app bar + manifest swap. |
| `TerminalHome.tsx` | 2-col grid tiles; remove `comingSoon` from stocktake & lot-id; wedge-live scan-first routing by box state. |
| `TransportBoxCheck.tsx` | Render via `ScanShell`; body = existing `TransportBoxDetail` tabs. |
| `TransportBoxReceive.tsx` | Render via `ScanShell`; split confirm/reject dock; gate on `isReceivable`. |
| `box-fill/BoxFillWorkflow.tsx` + `box-fill/AddItemsStep.tsx` | Collapse the `scan → add-items` step machine into shell subject/body states; reuse `AmountEntrySheet`/`OverdraftSheet` via `BottomSheet`. Keep `useBoxFill`/`useSendBoxToTransit` logic. |
| `App.tsx` (frontend src) | Replace the two `ComingSoonPage` route elements with the new screens (Phase 6). |

**Retired (after migration completes):**
- `ScanInput.tsx` — wedge logic moves to `ScanProvider`/`ScanStrip`.
- `ComingSoonPage.tsx` — removed once both new screens ship.

**Reused unchanged:** `TransportBoxDetail.tsx`, `TransportBoxStateBadge.tsx`,
`TransportBoxTypes.tsx` (`stateLabels`/`stateColors`), `terminalErrors.ts`,
`useScreenView`, all `api/hooks/*`. Keep `data-testid` + telemetry conventions.

---

## TypeScript contracts (the shell's public surface) — [confirm vs spec.html]

These are referenced by every task. Defined for real in **Task 2**.

```typescript
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
```

---

## Phase 0 — Foundation (no visual change)

### Task 1: Add feedback-scale + scan-accent design tokens

**Files:**
- Modify: `frontend/tailwind.config.js` (colors block, after the existing `'info'` line)

- [ ] **Step 1: Add the new tokens (additive — do not touch existing tokens)**

In `frontend/tailwind.config.js`, inside `theme.extend.colors`, immediately after
`'info': '#06B6D4',` add:

```js
        // Scan Shell feedback scale (pale backgrounds for flash echo / banners)
        'success-pale': '#ECFDF5',
        'warning-pale': '#FFFBEB',
        'error-pale': '#FEF2F2',
        // Scan accent (yellow SCAN affordance)
        'scan-accent': '#FACC15',
```

- [ ] **Step 2: Verify the build picks up the tokens**

Run: `cd frontend && npx tailwindcss --version >/dev/null && echo OK`
Then a smoke check that the classes resolve:
Run: `cd frontend && node -e "const c=require('./tailwind.config.js');console.log(c.theme.extend.colors['scan-accent'], c.theme.extend.colors['success-pale'])"`
Expected: `#FACC15 #ECFDF5`

- [ ] **Step 3: Commit**

```bash
git add frontend/tailwind.config.js
git commit -m "feat(terminal): add scan-shell feedback-pale + scan-accent tokens"
```

---

## Phase 1 — Shell kit (built in isolation, not yet wired)

> Build and unit-test every `shell/` component before touching any workflow.
> Nothing in this phase changes the running app until Phase 0's provider is
> mounted in Task 4.

### Task 2: `shell/types.ts`

**Files:**
- Create: `frontend/src/components/terminal/shell/types.ts`

- [ ] **Step 1: Write the file** — paste the full contract from the "TypeScript
contracts" section above into `shell/types.ts`. (No test needed; types only.)

- [ ] **Step 2: Verify it compiles**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep shell/types || echo "no type errors in shell/types"`
Expected: `no type errors in shell/types`

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/terminal/shell/types.ts
git commit -m "feat(terminal): add scan-shell shared types"
```

### Task 3: `shell/ScanProvider.tsx` + `shell/useScanScreen.ts`

This is the heart: one hidden wedge input, keystroke buffering, terminator
dispatch, the focus/yield model, and `flash()`. Two contexts keep workflow
bodies from re-rendering on every keystroke.

**Files:**
- Create: `frontend/src/components/terminal/shell/ScanProvider.tsx`
- Create: `frontend/src/components/terminal/shell/useScanScreen.ts`
- Test: `frontend/src/components/terminal/shell/__tests__/ScanProvider.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// shell/__tests__/ScanProvider.test.tsx
import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { ScanProvider } from '../ScanProvider';
import { useScanScreen } from '../useScanScreen';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

function Probe({ onScan }: { onScan: (c: string) => void }) {
  useScanScreen({ onScan });
  return <div>probe</div>;
}

function typeCode(code: string) {
  const input = document.querySelector('input[data-testid="wedge-input"]') as HTMLInputElement;
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: 'Enter' });
}

describe('ScanProvider wedge', () => {
  it('dispatches trimmed, uppercased buffer to the active screen on Enter', () => {
    const onScan = jest.fn();
    render(<ScanProvider><Probe onScan={onScan} /></ScanProvider>);
    act(() => typeCode('  b001  '));
    expect(onScan).toHaveBeenCalledWith('B001');
  });

  it('clears the buffer after dispatch', () => {
    const onScan = jest.fn();
    render(<ScanProvider><Probe onScan={onScan} /></ScanProvider>);
    act(() => typeCode('b001'));
    const input = document.querySelector('input[data-testid="wedge-input"]') as HTMLInputElement;
    expect(input.value).toBe('');
  });

  it('keeps a focused capture field by default', () => {
    render(<ScanProvider><Probe onScan={jest.fn()} /></ScanProvider>);
    const input = document.querySelector('input[data-testid="wedge-input"]');
    expect(document.activeElement).toBe(input);
  });
});
```

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/ScanProvider.test.tsx --watchAll=false`
Expected: FAIL — `Cannot find module '../ScanProvider'`.

- [ ] **Step 3: Implement `ScanProvider.tsx`**

```tsx
// shell/ScanProvider.tsx
import React, {
  createContext, useCallback, useEffect, useMemo, useRef, useState, type ReactNode,
} from 'react';
import type { FlashState, FlashTone, ScanActions, ScanEcho } from './types';

export const ScanActionsContext = createContext<ScanActions | null>(null);
export const ScanEchoContext = createContext<ScanEcho>({ buffer: '', lastCode: null, lastTone: null });
export const FlashContext = createContext<FlashState | null>(null);

const REFOCUS_DELAY_MS = 100;
const SAFETY_REFOCUS_MS = 2000;

/** A terminator confirms the buffer. DataWedge default = Enter; Tab kept as a guard. */
function isTerminator(key: string): boolean {
  return key === 'Enter' || key === 'Tab';
}

export const ScanProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const handlerRef = useRef<((code: string) => void) | null>(null);
  const yieldRef = useRef(false);
  const flashId = useRef(0);

  const [echo, setEcho] = useState<ScanEcho>({ buffer: '', lastCode: null, lastTone: null });
  const [flash, setFlash] = useState<FlashState | null>(null);

  const focusWedge = useCallback(() => {
    if (yieldRef.current) return;
    const active = document.activeElement;
    // Never steal focus from a real input/textarea (e.g. an open sheet field).
    if (active && active !== inputRef.current &&
        (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA')) return;
    inputRef.current?.focus({ preventScroll: true });
  }, []);

  // Refocus on mount + low-frequency safety interval.
  useEffect(() => {
    focusWedge();
    const id = window.setInterval(focusWedge, SAFETY_REFOCUS_MS);
    return () => window.clearInterval(id);
  }, [focusWedge]);

  const handleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setEcho((prev) => ({ ...prev, buffer: e.target.value }));
  }, []);

  const handleKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isTerminator(e.key)) return;
    e.preventDefault();
    const code = inputRef.current?.value.trim().toUpperCase() ?? '';
    if (inputRef.current) inputRef.current.value = '';
    setEcho((prev) => ({ ...prev, buffer: '' }));
    if (!code) return;
    setEcho((prev) => ({ ...prev, lastCode: code }));
    handlerRef.current?.(code);
  }, []);

  const handleBlur = useCallback(() => {
    window.setTimeout(focusWedge, REFOCUS_DELAY_MS);
  }, [focusWedge]);

  const actions = useMemo<ScanActions>(() => ({
    registerScanHandler: (handler) => {
      handlerRef.current = handler;
      return () => { if (handlerRef.current === handler) handlerRef.current = null; };
    },
    flash: (tone: FlashTone, code?: string) => {
      flashId.current += 1;
      setFlash({ id: flashId.current, tone, code });
      setEcho((prev) => ({ ...prev, lastTone: tone, lastCode: code ?? prev.lastCode }));
    },
    setYieldFocus: (shouldYield: boolean) => {
      yieldRef.current = shouldYield;
      if (!shouldYield) window.setTimeout(focusWedge, REFOCUS_DELAY_MS);
    },
    refocus: () => window.setTimeout(focusWedge, REFOCUS_DELAY_MS),
  }), [focusWedge]);

  return (
    <ScanActionsContext.Provider value={actions}>
      <ScanEchoContext.Provider value={echo}>
        <FlashContext.Provider value={flash}>
          {/* Singleton hidden wedge: captures keystrokes, no soft keyboard, no caret, off-screen. */}
          <input
            ref={inputRef}
            data-testid="wedge-input"
            inputMode="none"
            autoComplete="off"
            autoCapitalize="off"
            aria-hidden="true"
            tabIndex={-1}
            onChange={handleChange}
            onKeyDown={handleKeyDown}
            onBlur={handleBlur}
            className="absolute opacity-0 h-px w-px -z-10 pointer-events-none"
          />
          <div onClick={() => focusWedge()}>{children}</div>
        </FlashContext.Provider>
      </ScanEchoContext.Provider>
    </ScanActionsContext.Provider>
  );
};
```

> Note: the controlled-vs-uncontrolled handling above reads `inputRef.current.value`
> directly on terminator so a wedge that types + Enters in one burst is captured
> even if React hasn't flushed `buffer` state. `buffer` state exists only to feed
> the ScanStrip caret echo.

- [ ] **Step 4: Implement `useScanScreen.ts`**

```ts
// shell/useScanScreen.ts
import { useContext, useEffect, useRef } from 'react';
import { ScanActionsContext } from './ScanProvider';
import type { ScanActions } from './types';

interface UseScanScreenOptions {
  onScan: (code: string) => void;
}

/** Register the calling screen's scan handler for as long as it is mounted. */
export function useScanScreen({ onScan }: UseScanScreenOptions): ScanActions {
  const actions = useContext(ScanActionsContext);
  if (!actions) throw new Error('useScanScreen must be used within a ScanProvider');

  // Keep the latest handler without re-registering every render.
  const handlerRef = useRef(onScan);
  handlerRef.current = onScan;

  useEffect(() => {
    const unregister = actions.registerScanHandler((code) => handlerRef.current(code));
    return unregister;
  }, [actions]);

  return actions;
}
```

- [ ] **Step 5: Run the test — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/ScanProvider.test.tsx --watchAll=false`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/shell/ScanProvider.tsx \
        frontend/src/components/terminal/shell/useScanScreen.ts \
        frontend/src/components/terminal/shell/__tests__/ScanProvider.test.tsx
git commit -m "feat(terminal): add ScanProvider wedge singleton + useScanScreen"
```

### Task 4: `shell/FlashOverlay.tsx`

**Files:**
- Create: `frontend/src/components/terminal/shell/FlashOverlay.tsx`
- Test: `frontend/src/components/terminal/shell/__tests__/FlashOverlay.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// shell/__tests__/FlashOverlay.test.tsx
import React, { useContext } from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { ScanProvider, ScanActionsContext } from '../ScanProvider';
import { FlashOverlay } from '../FlashOverlay';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

function FlashButton() {
  const actions = useContext(ScanActionsContext)!;
  return <button onClick={() => actions.flash('err', 'B999')}>flash</button>;
}

it('renders a non-blocking, aria-live overlay with the err tone after flash()', () => {
  render(<ScanProvider><FlashOverlay /><FlashButton /></ScanProvider>);
  act(() => { fireEvent.click(screen.getByText('flash')); });
  const overlay = screen.getByTestId('flash-overlay');
  expect(overlay).toHaveAttribute('aria-live');
  expect(overlay.className).toContain('pointer-events-none');
  expect(overlay.dataset.tone).toBe('err');
});
```

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/FlashOverlay.test.tsx --watchAll=false`
Expected: FAIL — `Cannot find module '../FlashOverlay'`.

- [ ] **Step 3: Implement**

```tsx
// shell/FlashOverlay.tsx
import React, { useContext, useEffect, useState } from 'react';
import { Check, AlertTriangle, X } from 'lucide-react';
import { FlashContext } from './ScanProvider';
import type { FlashTone } from './types';

const TONE_MS: Record<FlashTone, number> = { ok: 950, warn: 950, err: 1200 };
const TONE_BG: Record<FlashTone, string> = {
  ok: 'bg-success', warn: 'bg-warning', err: 'bg-error',
};
const ToneGlyph: Record<FlashTone, React.ReactNode> = {
  ok: <Check className="h-24 w-24 text-white" />,
  warn: <AlertTriangle className="h-24 w-24 text-white" />,
  err: <X className="h-24 w-24 text-white" />,
};

/** Full-bleed colour wash on each scan. Visible-by-default (backgrounded tabs
 *  freeze animations); the fade is polish only, never a visibility gate. */
export const FlashOverlay: React.FC = () => {
  const flash = useContext(FlashContext);
  const [active, setActive] = useState<typeof flash>(null);

  useEffect(() => {
    if (!flash) return;
    setActive(flash);
    const id = window.setTimeout(() => setActive(null), TONE_MS[flash.tone]);
    return () => window.clearTimeout(id); // new flash cancels the prior timer
  }, [flash]);

  if (!active) return null;

  return (
    <div
      data-testid="flash-overlay"
      data-tone={active.tone}
      aria-live="assertive"
      role="status"
      className={`fixed inset-0 z-50 flex items-center justify-center pointer-events-none ${TONE_BG[active.tone]} opacity-90 transition-opacity duration-200`}
    >
      {ToneGlyph[active.tone]}
    </div>
  );
};
```

- [ ] **Step 4: Run — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/FlashOverlay.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/shell/FlashOverlay.tsx \
        frontend/src/components/terminal/shell/__tests__/FlashOverlay.test.tsx
git commit -m "feat(terminal): add FlashOverlay scan-feedback wash"
```

### Task 5: `shell/SubjectHeader.tsx`

**Files:**
- Create: `frontend/src/components/terminal/shell/SubjectHeader.tsx`
- Test: `frontend/src/components/terminal/shell/__tests__/SubjectHeader.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// shell/__tests__/SubjectHeader.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { SubjectHeader } from '../SubjectHeader';

it('renders the empty prompt when no code', () => {
  render(<SubjectHeader emptyPrompt="Naskenujte box" />);
  expect(screen.getByText('Naskenujte box')).toBeInTheDocument();
});

it('renders code + state badge + facts when given a subject', () => {
  render(<SubjectHeader code="B001" state="Opened" facts={<span>3 položky</span>} />);
  expect(screen.getByText('B001')).toBeInTheDocument();
  expect(screen.getByText('Otevřený')).toBeInTheDocument(); // stateLabels[Opened]
  expect(screen.getByText('3 položky')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/SubjectHeader.test.tsx --watchAll=false`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```tsx
// shell/SubjectHeader.tsx
import React, { type ReactNode } from 'react';
import { ScanLine } from 'lucide-react';
import TransportBoxStateBadge from '../../transport/box-detail/components/TransportBoxStateBadge';

interface SubjectHeaderProps {
  /** scanned code "in hand"; absence renders the empty prompt */
  code?: string | null;
  /** transport-box state key (drives the badge via stateColors/stateLabels) */
  state?: string;
  /** key facts line(s) — item count, expiry, etc. */
  facts?: ReactNode;
  /** prompt shown before the first scan */
  emptyPrompt?: string;
}

export const SubjectHeader: React.FC<SubjectHeaderProps> = ({
  code, state, facts, emptyPrompt = 'Naskenujte kód',
}) => {
  if (!code) {
    return (
      <div data-testid="subject-empty"
           className="flex items-center gap-3 px-4 py-4 text-neutral-gray">
        <ScanLine className="h-6 w-6" />
        <span className="text-base font-medium">{emptyPrompt}</span>
      </div>
    );
  }
  return (
    <div data-testid="subject-header"
         className="flex items-center justify-between gap-3 px-4 py-3 bg-white border-b border-border-light">
      <div className="min-w-0">
        <div className="font-mono text-lg font-semibold text-neutral-slate truncate">{code}</div>
        {facts && <div className="text-sm text-neutral-gray truncate">{facts}</div>}
      </div>
      {state && <TransportBoxStateBadge state={state} size="md" />}
    </div>
  );
};
```

- [ ] **Step 4: Run — expect PASS** (and confirm `TransportBoxStateBadge`'s default
export path; it is a default export at
`transport/box-detail/components/TransportBoxStateBadge.tsx`).

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/SubjectHeader.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/shell/SubjectHeader.tsx \
        frontend/src/components/terminal/shell/__tests__/SubjectHeader.test.tsx
git commit -m "feat(terminal): add SubjectHeader zone component"
```

### Task 6: `shell/ScanStrip.tsx`

**Files:**
- Create: `frontend/src/components/terminal/shell/ScanStrip.tsx`
- Test: `frontend/src/components/terminal/shell/__tests__/ScanStrip.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// shell/__tests__/ScanStrip.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { ScanEchoContext } from '../ScanProvider';
import { ScanStrip } from '../ScanStrip';

it('shows the ready state when buffer + lastCode are empty', () => {
  render(
    <ScanEchoContext.Provider value={{ buffer: '', lastCode: null, lastTone: null }}>
      <ScanStrip />
    </ScanEchoContext.Provider>,
  );
  expect(screen.getByTestId('scan-strip')).toHaveTextContent(/Připraveno ke skenování/i);
});

it('echoes the last code with its tone', () => {
  render(
    <ScanEchoContext.Provider value={{ buffer: '', lastCode: 'B001', lastTone: 'ok' }}>
      <ScanStrip />
    </ScanEchoContext.Provider>,
  );
  expect(screen.getByText('B001')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/ScanStrip.test.tsx --watchAll=false`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```tsx
// shell/ScanStrip.tsx
import React, { useContext } from 'react';
import { ScanLine } from 'lucide-react';
import { ScanEchoContext } from './ScanProvider';
import type { FlashTone } from './types';

const TONE_TEXT: Record<FlashTone, string> = {
  ok: 'text-success', warn: 'text-warning', err: 'text-error',
};

/** Persistent wedge surface. Subscribes only to the volatile echo context, so it
 *  re-renders on keystrokes without re-rendering workflow bodies. */
export const ScanStrip: React.FC = () => {
  const { buffer, lastCode, lastTone } = useContext(ScanEchoContext);

  return (
    <div data-testid="scan-strip"
         className="flex items-center gap-3 px-4 py-3 bg-neutral-slate text-white">
      <ScanLine className="h-5 w-5 text-scan-accent flex-shrink-0" />
      {buffer ? (
        <span className="font-mono text-base">
          {buffer}<span className="animate-pulse">▌</span>
        </span>
      ) : lastCode ? (
        <span className={`font-mono text-base ${lastTone ? TONE_TEXT[lastTone] : 'text-white'}`}>
          {lastCode}
        </span>
      ) : (
        <span className="text-sm text-white/70">Připraveno ke skenování…</span>
      )}
    </div>
  );
};
```

- [ ] **Step 4: Run — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/ScanStrip.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/shell/ScanStrip.tsx \
        frontend/src/components/terminal/shell/__tests__/ScanStrip.test.tsx
git commit -m "feat(terminal): add ScanStrip persistent wedge surface"
```

### Task 7: `shell/DockedAction.tsx`

**Files:**
- Create: `frontend/src/components/terminal/shell/DockedAction.tsx`
- Test: `frontend/src/components/terminal/shell/__tests__/DockedAction.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// shell/__tests__/DockedAction.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { DockedAction } from '../DockedAction';

it('renders nothing when no actions', () => {
  const { container } = render(<DockedAction actions={[]} />);
  expect(container.firstChild).toBeNull();
});

it('renders a single full-width action and fires onClick', () => {
  const onClick = jest.fn();
  render(<DockedAction actions={[{ label: 'Odeslat', onClick, testId: 'send' }]} />);
  fireEvent.click(screen.getByTestId('send'));
  expect(onClick).toHaveBeenCalled();
});

it('disables an action when disabled', () => {
  render(<DockedAction actions={[{ label: 'Odeslat', onClick: jest.fn(), disabled: true, testId: 'send' }]} />);
  expect(screen.getByTestId('send')).toBeDisabled();
});
```

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/DockedAction.test.tsx --watchAll=false`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```tsx
// shell/DockedAction.tsx
import React from 'react';
import { Loader2 } from 'lucide-react';
import type { DockAction } from './types';

const VARIANT: Record<NonNullable<DockAction['variant']>, string> = {
  primary: 'bg-primary-blue text-white hover:bg-accent-blue-bright disabled:bg-gray-200 disabled:text-neutral-gray',
  success: 'bg-success text-white hover:opacity-90 disabled:bg-gray-200 disabled:text-neutral-gray',
  danger: 'bg-error text-white hover:opacity-90 disabled:bg-gray-200 disabled:text-neutral-gray',
  ghost: 'border border-border-light text-neutral-slate hover:border-primary-blue',
};

interface DockedActionProps { actions: DockAction[]; }

export const DockedAction: React.FC<DockedActionProps> = ({ actions }) => {
  if (!actions.length) return null;
  return (
    <div className="flex gap-3 px-4 py-3 bg-white border-t border-border-light">
      {actions.map((a, i) => (
        <button
          key={i}
          type="button"
          data-testid={a.testId}
          onClick={a.onClick}
          disabled={a.disabled || a.loading}
          className={`flex-1 h-14 flex items-center justify-center gap-2 rounded-xl font-semibold transition-colors disabled:cursor-not-allowed ${VARIANT[a.variant ?? 'primary']}`}
        >
          {a.loading ? <Loader2 className="h-5 w-5 animate-spin" /> : a.icon}
          {a.label}
        </button>
      ))}
    </div>
  );
};
```

- [ ] **Step 4: Run — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/DockedAction.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/shell/DockedAction.tsx \
        frontend/src/components/terminal/shell/__tests__/DockedAction.test.tsx
git commit -m "feat(terminal): add DockedAction zone component"
```

### Task 8: `shell/BottomSheet.tsx` + `shell/ScanShell.tsx`

`BottomSheet` yields wedge focus while open; `ScanShell` assembles zones B–E.

**Files:**
- Create: `frontend/src/components/terminal/shell/BottomSheet.tsx`
- Create: `frontend/src/components/terminal/shell/ScanShell.tsx`
- Test: `frontend/src/components/terminal/shell/__tests__/BottomSheet.test.tsx`
- Test: `frontend/src/components/terminal/shell/__tests__/ScanShell.test.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
// shell/__tests__/BottomSheet.test.tsx
import React, { useContext } from 'react';
import { render, screen } from '@testing-library/react';
import { ScanProvider, ScanActionsContext } from '../ScanProvider';
import { BottomSheet } from '../BottomSheet';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

it('sets yieldFocus while open and releases on unmount', () => {
  const spy = jest.fn();
  function Capture() {
    const a = useContext(ScanActionsContext)!;
    // wrap setYieldFocus to observe calls
    const orig = a.setYieldFocus;
    a.setYieldFocus = (v: boolean) => { spy(v); orig(v); };
    return null;
  }
  const { rerender } = render(
    <ScanProvider>
      <Capture />
      <BottomSheet open onClose={jest.fn()} hasInput><input /></BottomSheet>
    </ScanProvider>,
  );
  expect(spy).toHaveBeenCalledWith(true);
  rerender(
    <ScanProvider>
      <Capture />
      <BottomSheet open={false} onClose={jest.fn()} hasInput><input /></BottomSheet>
    </ScanProvider>,
  );
  expect(spy).toHaveBeenCalledWith(false);
});
```

```tsx
// shell/__tests__/ScanShell.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { ScanProvider } from '../ScanProvider';
import { ScanShell } from '../ScanShell';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

it('renders subject, body, scan strip and a docked action in order', () => {
  render(
    <ScanProvider>
      <ScanShell
        subject={<div data-testid="subj">subj</div>}
        actions={[{ label: 'Go', onClick: jest.fn(), testId: 'go' }]}
      >
        <div data-testid="body">body</div>
      </ScanShell>
    </ScanProvider>,
  );
  expect(screen.getByTestId('subj')).toBeInTheDocument();
  expect(screen.getByTestId('body')).toBeInTheDocument();
  expect(screen.getByTestId('scan-strip')).toBeInTheDocument();
  expect(screen.getByTestId('go')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run them — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/BottomSheet.test.tsx src/components/terminal/shell/__tests__/ScanShell.test.tsx --watchAll=false`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement `BottomSheet.tsx`**

```tsx
// shell/BottomSheet.tsx
import React, { useContext, useEffect, type ReactNode } from 'react';
import { ScanActionsContext } from './ScanProvider';

interface BottomSheetProps {
  open: boolean;
  onClose: () => void;
  /** when the sheet contains an input the wedge must yield focus */
  hasInput?: boolean;
  children: ReactNode;
  testId?: string;
}

export const BottomSheet: React.FC<BottomSheetProps> = ({
  open, onClose, hasInput = false, children, testId,
}) => {
  const actions = useContext(ScanActionsContext);

  useEffect(() => {
    if (!actions || !hasInput) return;
    actions.setYieldFocus(open);
    return () => actions.setYieldFocus(false);
  }, [actions, hasInput, open]);

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-40 flex flex-col justify-end" role="dialog" data-testid={testId}>
      <div className="absolute inset-0 bg-black/30" onClick={onClose} />
      <div className="relative bg-white rounded-t-2xl max-w-md mx-auto w-full p-4 shadow-hover">
        {children}
      </div>
    </div>
  );
};
```

- [ ] **Step 4: Implement `ScanShell.tsx`**

```tsx
// shell/ScanShell.tsx
import React from 'react';
import { ScanStrip } from './ScanStrip';
import { DockedAction } from './DockedAction';
import type { ScanShellProps } from './types';

/** Zones B–E. Zone A (app bar) is TerminalLayout; FlashOverlay is mounted there too. */
export const ScanShell: React.FC<ScanShellProps> = ({ subject, children, actions = [] }) => {
  return (
    <div className="flex flex-col h-full min-h-0">
      {/* Zone B — subject header (or empty prompt) */}
      {subject}
      {/* Zone C — workflow body, the only scrolling region */}
      <div className="flex-1 min-h-0 overflow-y-auto">
        <div className="max-w-md mx-auto w-full p-4">{children}</div>
      </div>
      {/* Zone D — persistent scan strip */}
      <ScanStrip />
      {/* Zone E — docked action */}
      <DockedAction actions={actions} />
    </div>
  );
};
```

- [ ] **Step 5: Run — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/shell/__tests__/BottomSheet.test.tsx src/components/terminal/shell/__tests__/ScanShell.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/shell/BottomSheet.tsx \
        frontend/src/components/terminal/shell/ScanShell.tsx \
        frontend/src/components/terminal/shell/__tests__/BottomSheet.test.tsx \
        frontend/src/components/terminal/shell/__tests__/ScanShell.test.tsx
git commit -m "feat(terminal): add BottomSheet + ScanShell layout"
```

### Task 9: Mount the shell foundation in `TerminalLayout` (no workflow change yet)

**Files:**
- Modify: `frontend/src/components/terminal/TerminalLayout.tsx`
- Test: `frontend/src/components/terminal/__tests__/TerminalLayout.test.tsx` (extend)

- [ ] **Step 1: Add a failing test** — assert the wedge input and flash overlay
mount once and persist.

```tsx
// add to TerminalLayout.test.tsx
it('mounts the wedge input and flash overlay once', () => {
  // render TerminalLayout inside a MemoryRouter at /terminal (match existing test setup)
  // ...existing render helper...
  expect(document.querySelector('input[data-testid="wedge-input"]')).toBeInTheDocument();
  expect(document.querySelector('[data-testid="flash-overlay"]')).not.toBeInTheDocument(); // hidden until a flash
});
```

> Match the existing test's router/render harness (see the current
> `TerminalLayout.test.tsx`); do not invent a new harness.

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TerminalLayout.test.tsx --watchAll=false`
Expected: FAIL — no wedge input yet.

- [ ] **Step 3: Modify `TerminalLayout.tsx`**

Replace the `<main>` block. Wrap `<Outlet/>` in `<ScanProvider>`, mount
`<FlashOverlay/>`, and drop the `max-w-md` scroll container (the shell owns the
body layout now). Keep the header and the manifest swap exactly as-is.

```tsx
// imports — add:
import { ScanProvider } from './shell/ScanProvider';
import { FlashOverlay } from './shell/FlashOverlay';

// replace the <main> ... </main> block with:
      <ScanProvider>
        <main className="flex-1 min-h-0 overflow-hidden">
          <Outlet />
        </main>
        <FlashOverlay />
      </ScanProvider>
```

> Rationale: the shell's body is the scroll region; the layout `<main>` becomes a
> non-scrolling flex child (`overflow-hidden`, `min-h-0`) so the docked action and
> scan strip stay pinned. Screens not yet migrated still render their own markup
> inside `<main>` — they keep working because they don't depend on `max-w-md`
> (they set their own `space-y-4` containers). Verify each route visually after
> this step.

- [ ] **Step 4: Run the test — expect PASS, then full terminal suite**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal --watchAll=false`
Expected: PASS — all existing terminal tests still green (no workflow migrated yet).

- [ ] **Step 5: Build gate**

Run: `cd frontend && npm run build && npm run lint`
Expected: build + lint succeed.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/TerminalLayout.tsx \
        frontend/src/components/terminal/__tests__/TerminalLayout.test.tsx
git commit -m "feat(terminal): mount ScanProvider + FlashOverlay in TerminalLayout"
```

---

## Phase 2 — Migrate Kontrola boxu (lowest risk)

### Task 10: Render `TransportBoxCheck` via the shell

**Files:**
- Modify: `frontend/src/components/terminal/TransportBoxCheck.tsx`
- Test: `frontend/src/components/terminal/__tests__/TransportBoxCheck.test.tsx` (update)

Current behaviour to preserve (see `TransportBoxCheck.tsx`): scan a code → query
fires (`useTransportBoxByCodeQuery`) → render `TransportBoxDetail`; on error,
re-scanning the same code calls `refetch()`. The wedge now lives in the provider,
so the local `ScanInput` and its sticky container are removed; `onScan` registers
via `useScanScreen`; every resolution emits one `flash()`.

- [ ] **Step 1: Update the test** to drive scans through the provider wedge (use
the `typeCode` helper from the ScanProvider test) and assert: detail renders on
success, `flash('ok')` fires, error path renders `TerminalError` and `flash('err')`.

```tsx
// TransportBoxCheck.test.tsx — sketch (fill with the existing query mock harness)
it('renders box detail and flashes ok on a successful scan', async () => {
  // mock useTransportBoxByCodeQuery → { data: box, isFetching:false, isError:false }
  // render <ScanProvider><TransportBoxCheck/></ScanProvider> inside the existing QueryClient harness
  // act(() => typeCode('B001'))
  // expect(await screen.findByTestId('subject-header')).toBeInTheDocument()
});
```

> Reuse the existing query-mock approach already in
> `__tests__/TransportBoxCheck.test.tsx`; only the input mechanism changes.

- [ ] **Step 2: Run it — expect failure** (component still uses `ScanInput`).

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TransportBoxCheck.test.tsx --watchAll=false`
Expected: FAIL.

- [ ] **Step 3: Rewrite `TransportBoxCheck.tsx`**

```tsx
import React, { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useTransportBoxByCodeQuery } from '../../api/hooks/useTransportBoxes';
import TerminalError from './TerminalError';
import { getTerminalErrorMessage } from './terminalErrors';
import BoxDetail from './TransportBoxDetail';
import { ScanShell } from './shell/ScanShell';
import { SubjectHeader } from './shell/SubjectHeader';
import { useScanScreen } from './shell/useScanScreen';
import { useScreenView } from '../../telemetry/useScreenView';

const TransportBoxCheck: React.FC = () => {
  useScreenView('Terminal', 'TerminalBoxCheck');
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data: box, isFetching, isError, error, refetch } = useTransportBoxByCodeQuery(scannedCode);

  const { flash } = useScanScreen({
    onScan: (code) => {
      if (isError && scannedCode === code) { void refetch(); return; }
      setScannedCode(code);
    },
  });

  // Emit exactly one flash when a scan resolves.
  React.useEffect(() => {
    if (!scannedCode || isFetching) return;
    if (isError) flash('err', scannedCode);
    else if (box) flash('ok', box.code ?? scannedCode);
    else flash('err', scannedCode); // not found
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scannedCode, isFetching, isError, box]);

  const subject = box
    ? <SubjectHeader code={box.code} state={box.state} facts={`${box.items?.length ?? 0} položek`} />
    : <SubjectHeader emptyPrompt="Naskenujte box ke kontrole" />;

  return (
    <ScanShell subject={subject}>
      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}
      {isError && error && (
        <TerminalError message={getTerminalErrorMessage(error)} hint="Zkontrolujte kód a naskenujte znovu" />
      )}
      {!isFetching && box && <BoxDetail box={box} />}
    </ScanShell>
  );
};

export default TransportBoxCheck;
```

> The optional "Naskenovat další" ghost dock (README §8) is intentionally omitted
> in v1 — the always-ready wedge already supports rescanning (README Open Q #5).
> Add it later as a `ghost` `DockAction` if floor procedure wants it.

- [ ] **Step 4: Run the test — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TransportBoxCheck.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Build + lint gate**

Run: `cd frontend && npm run build && npm run lint`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/TransportBoxCheck.tsx \
        frontend/src/components/terminal/__tests__/TransportBoxCheck.test.tsx
git commit -m "feat(terminal): migrate Kontrola boxu onto ScanShell"
```

---

## Phase 3 — Migrate Příjem boxu (split dock + gating)

### Task 11: Render `TransportBoxReceive` via the shell with a split dock

**Files:**
- Modify: `frontend/src/components/terminal/TransportBoxReceive.tsx`
- Test: `frontend/src/components/terminal/__tests__/TransportBoxReceive.test.tsx` (update)

Preserve current logic (`TransportBoxReceive.tsx`): scan → query; `box.isReceivable`
gates the confirm action; confirm calls `useChangeTransportBoxState` →
`TransportBoxState.Received`; scanning the in-hand receivable box auto-confirms;
`SwaggerException` is swallowed (global toast). Map to shell:
- subject = scanned box (state badge shows receivability context)
- body = receivability banner (when `!canReceive`) + read-only `TransportBoxDetail`
- dock = split: `{ label: 'Zamítnout', variant: 'ghost', onClick: handleReject }`
  and `{ label: canReceive ? 'Potvrdit příjem' : 'Nelze přijmout', variant: 'success', disabled: !canReceive || pending, loading: pending, onClick: handleAccept }`
- flash: `ok` on confirm success, `warn` when scanned box is not receivable, `err`
  on not-found/error.

- [ ] **Step 1: Update the test** — assert split dock renders, confirm disabled
when `!isReceivable`, success path flashes `ok` and resets, not-receivable flashes
`warn`. Reuse the existing mutation/query mock harness in the current test file.

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TransportBoxReceive.test.tsx --watchAll=false`
Expected: FAIL.

- [ ] **Step 3: Rewrite `TransportBoxReceive.tsx`**

```tsx
import React, { useEffect, useState } from 'react';
import { Loader2, Check, X } from 'lucide-react';
import BoxDetail from './TransportBoxDetail';
import {
  useTransportBoxByCodeQuery,
  useChangeTransportBoxState,
} from '../../api/hooks/useTransportBoxes';
import { TransportBoxState, SwaggerException } from '../../api/generated/api-client';
import { ScanShell } from './shell/ScanShell';
import { SubjectHeader } from './shell/SubjectHeader';
import { useScanScreen } from './shell/useScanScreen';
import type { DockAction } from './shell/types';
import { useScreenView } from '../../telemetry/useScreenView';

const TransportBoxReceive: React.FC = () => {
  useScreenView('Terminal', 'TerminalReceive');
  const [scannedCode, setScannedCode] = useState<string | null>(null);

  const { data: box, isFetching, isError, refetch } = useTransportBoxByCodeQuery(scannedCode);
  const changeState = useChangeTransportBoxState();
  const canReceive = box?.isReceivable === true;

  const { flash } = useScanScreen({
    onScan: (code) => {
      if (box?.isReceivable === true && box.code?.toUpperCase() === code && !changeState.isPending) {
        void handleAccept(); return;
      }
      if (isError && scannedCode === code) { void refetch(); return; }
      setScannedCode(code);
    },
  });

  // One flash per resolved scan (skip while a box is in hand and we auto-confirm).
  useEffect(() => {
    if (!scannedCode || isFetching) return;
    if (isError) flash('err', scannedCode);
    else if (!box) flash('err', scannedCode);
    else if (!box.isReceivable) flash('warn', box.code ?? scannedCode);
    else flash('ok', box.code ?? scannedCode);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scannedCode, isFetching, isError, box]);

  const handleAccept = async () => {
    if (!box || box.isReceivable !== true || changeState.isPending || !box.id) return;
    try {
      await changeState.mutateAsync({ boxId: box.id, newState: TransportBoxState.Received });
      flash('ok', box.code ?? undefined);
      setScannedCode(null);
    } catch (error: unknown) {
      if (!(error instanceof SwaggerException)) throw error;
      flash('err', box.code ?? undefined);
    }
  };

  const handleReject = () => setScannedCode(null);

  const subject = box
    ? <SubjectHeader code={box.code} state={box.state}
        facts={canReceive ? 'Připraveno k příjmu' : 'Nelze přijmout v tomto stavu'} />
    : <SubjectHeader emptyPrompt="Naskenujte box k příjmu" />;

  const actions: DockAction[] = box ? [
    { label: 'Zamítnout', variant: 'ghost', onClick: handleReject, testId: 'reject-box', icon: <X className="h-5 w-5" /> },
    { label: canReceive ? 'Potvrdit příjem' : 'Nelze přijmout', variant: 'success',
      onClick: handleAccept, disabled: !canReceive || changeState.isPending,
      loading: changeState.isPending, testId: 'accept-box', icon: <Check className="h-5 w-5" /> },
  ] : [];

  return (
    <ScanShell subject={subject} actions={actions}>
      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}
      {!canReceive && box && (
        <div data-testid="not-receivable"
             className="bg-error-pale border border-red-200 rounded-xl p-3 text-sm text-red-700 mb-3">
          Tento box nelze přijmout. Pro příjem musí být ve stavu V přepravě, V rezervě nebo V karanténě.
        </div>
      )}
      {!isFetching && box && <BoxDetail box={box} />}
    </ScanShell>
  );
};

export default TransportBoxReceive;
```

> The old inline success card (`receive-success`) is replaced by the green
> `flash('ok')` + the scan strip echo. If E2E asserts `data-testid="receive-success"`,
> update that assertion to the flash overlay / subject reset. **[confirm vs spec.html §12]**

- [ ] **Step 4: Run the test — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TransportBoxReceive.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Build + lint gate**

Run: `cd frontend && npm run build && npm run lint`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/TransportBoxReceive.tsx \
        frontend/src/components/terminal/__tests__/TransportBoxReceive.test.tsx
git commit -m "feat(terminal): migrate Příjem boxu onto ScanShell with split dock"
```

---

## Phase 4 — Migrate Plnění boxu (collapse step machine, sheet focus-yield)

### Task 12: Render box-fill via the shell; reuse sheets through `BottomSheet`

**Files:**
- Modify: `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`
- Modify: `frontend/src/components/terminal/box-fill/AddItemsStep.tsx`
- Modify: `frontend/src/components/terminal/box-fill/AmountEntrySheet.tsx` (wrap in `BottomSheet`)
- Modify: `frontend/src/components/terminal/box-fill/OverdraftSheet.tsx` (wrap in `BottomSheet`)
- Test: `box-fill/__tests__/*` (update the affected ones)

Collapse the `"scan" | "add-items"` step machine: with no box in hand the shell
shows the empty-prompt subject + the available-inventory body; scanning a box code
opens/resumes it (`useOpenOrResumeBox`) and sets the subject; scanning a product
opens `AmountEntrySheet` (via `BottomSheet hasInput` → wedge yields focus);
over-stock opens `OverdraftSheet` (flash `warn` on add-with-negative / add-remaining);
the primary dock "Odeslat do přepravy" stays disabled while the box is empty and
sends via `useSendBoxToTransit`. Keep all `useBoxFill` mutation logic
(`useAddBoxItem`, `useRemoveBoxItem`, `useSendBoxToTransit`) unchanged — only the
container/layout and the scan entry point move.

- [ ] **Step 1: Update tests** — drive box + product scans through the provider
wedge; assert: empty→box subject transition, `AmountEntrySheet` opens on product
scan and the wedge yields focus (`setYieldFocus(true)`), overdraft path flashes
`warn`, send disabled while empty, send success resets to empty subject. Reuse the
existing box-fill mock harness.

- [ ] **Step 2: Run them — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/box-fill --watchAll=false`
Expected: FAIL.

- [ ] **Step 3: Wrap the two sheets in `BottomSheet`**

In `AmountEntrySheet.tsx` and `OverdraftSheet.tsx`, replace the hand-rolled
`fixed inset-0 ...` overlay wrapper with `<BottomSheet open onClose={onCancel}
hasInput testId="amount-entry-dialog">…</BottomSheet>` (AmountEntry has an input →
`hasInput`; Overdraft has no input → omit `hasInput`). Keep the inner content,
validation, and `data-testid`s (`amount-entry-input`, `amount-entry-confirm`,
`overdraft-add-negative`, `overdraft-add-remaining`) exactly as they are.

- [ ] **Step 4: Rewrite `BoxFillWorkflow.tsx` to a single shell-driven screen**

Merge `ScanBoxStep` + `AddItemsStep` orchestration into one component that renders
`ScanShell`. Subject = open box (or empty prompt). Body = available inventory list +
"V boxu" list (move the JSX out of `AddItemsStep` largely intact). `onScan`:
- if no box in hand → treat code as a box code → `useOpenOrResumeBox`; flash `ok`
  on success (`warn` if resumed), `err` on invalid/failed.
- if a box is in hand and code === box.code && items > 0 → send to transit.
- otherwise → look the code up against inventory and open `AmountEntrySheet`
  (or `err` flash if not a known product). **[confirm exact product-scan matching
  rule vs spec.html §8 — README summarises but does not pin the lookup key]**

Primary dock: `{ label: 'Odeslat do přepravy', onClick: handleTransit,
disabled: !box || box.items.length === 0 || sendToTransit.isPending,
loading: sendToTransit.isPending, testId: 'proceed-to-transit' }`.

> This is the largest single migration. Keep `AddItemsStep`'s inventory/box-item
> rendering as a child component (`BoxFillBody`) to respect the <800-line / one-
> responsibility rule — do not inline 200 lines into the workflow. The step-machine
> `step` state is deleted; `ScanBoxStep.tsx` is removed (its `isValidBoxCode` guard
> moves into the `onScan` box branch; keep `box-fill/boxCode.ts`).

- [ ] **Step 5: Run the tests — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/box-fill --watchAll=false`
Expected: PASS.

- [ ] **Step 6: Build + lint gate**

Run: `cd frontend && npm run build && npm run lint`
Expected: success.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/terminal/box-fill
git commit -m "feat(terminal): migrate Plnění boxu onto ScanShell with BottomSheet sheets"
```

---

## Phase 5 — Home grid + scan-first routing

### Task 13: 2-column tile grid, un-stub new routes, wedge-driven routing

**Files:**
- Modify: `frontend/src/components/terminal/TerminalHome.tsx`
- Test: `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx` (update)

Home is wedge-live: scanning a valid box infers the operation and navigates with
the box state. Mapping (README §"Home & scan-first routing"):

| Scanned box state | Route |
|---|---|
| `InTransit` / `Reserve` / `Quarantine` | `receive` |
| `Opened` / `New` | `fill` |
| anything else (`Stocked`, `Closed`, …) | `check` |
| unknown / invalid | stay, `flash('err')` |

> **[confirm vs spec.html + Open Q #3]** Route path names in `App.tsx` today are
> `box-check`, `box-fill`, `receive` (not `check`/`fill`). Use the existing path
> strings. Validate the state→workflow precedence with floor procedure before
> shipping.

- [ ] **Step 1: Update the test** — assert: 5 tiles render in a 2-col grid, no
`comingSoon` tiles remain, scanning a box in `Opened` state navigates to
`/terminal/box-fill`, an unknown code flashes `err` and stays on `/terminal`.
Mock `useTransportBoxByCodeQuery` + a router spy.

- [ ] **Step 2: Run it — expect failure**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TerminalHome.test.tsx --watchAll=false`
Expected: FAIL.

- [ ] **Step 3: Implement** — convert the tile list to a 2-col grid
(`grid grid-cols-2 gap-4`), drop `comingSoon` from the stocktake & lot-id tiles,
and add an `onScan` (via `useScanScreen`) that looks up the scanned code and
routes per the table, flashing `err` for unknown/invalid. Keep
`data-testid="workflow-tile-${id}"` and `useScreenView('Terminal', 'TerminalHome')`.

> The lookup needs the box state before navigating. Use a one-shot query: set the
> scanned code, and in an effect on resolution `navigate(...)` per state (or
> `flash('err')`). To avoid a stale query between scans, key the navigation on the
> resolved `box.code === scannedCode`.

- [ ] **Step 4: Run the test — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test src/components/terminal/__tests__/TerminalHome.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Build + lint gate**

Run: `cd frontend && npm run build && npm run lint`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/TerminalHome.tsx \
        frontend/src/components/terminal/__tests__/TerminalHome.test.tsx
git commit -m "feat(terminal): grid home + scan-first routing by box state"
```

---

## Phase 6 — Net-new workflows (BACKEND-GATED — do not start without API confirmation)

### Task 14: Discovery gate — confirm backend APIs exist

**Files:** none (investigation only)

- [ ] **Step 1: Verify the stocktake + lot-registration endpoints exist**

Search the backend for: a stocktake/inventura query (materials by lot, expected
qty) and a submit endpoint; a lot-registration command. Then check the generated
client.

Run: `grep -rin "stocktak\|inventur\|invent\b" backend/src --include=*.cs -l | head`
Run: `grep -rin "lotregist\|registerlot\|lotidentif" backend/src --include=*.cs -l | head`
Run: `cd frontend && grep -rin "stocktak\|inventur\|registerLot" src/api/generated/api-client.ts | head`

- [ ] **Step 2: Decide the gate**

- **If the endpoints + generated client methods exist:** record their exact
  names/signatures here, then proceed to Tasks 15–16, writing real
  `useStocktake*` / `useLotRegistration*` hooks against them (mirroring the
  `useBoxFill.ts` hook shape) and the two screens on `ScanShell` with `CountSheet`
  / `RegisterSheet` via `BottomSheet`.
- **If they are missing:** STOP. These two workflows cannot be built. Report back
  that Phase 6 needs a backend plan first (new use cases + controllers + OpenAPI
  regen). Do **not** scaffold UI against nonexistent APIs. Leave the
  `ComingSoonPage` stubs in place for `stocktake` + `lot-identification` and keep
  `ComingSoonPage.tsx` (defer Task 17's removal of it).

> Because `spec.html` (§8 workflow detail) and the backend contracts are both
> unavailable, full TDD steps for Tasks 15–16 cannot be written without
> placeholders. They are intentionally left as gated stubs below, to be expanded
> into bite-sized steps once Step 2 resolves. This is a known, declared gap — not
> an oversight.

### Task 15: Inventura screen (expand after Task 14 unblocks) — **[needs spec.html §8 + stocktake API]**

Shell screen: session subject (counted/total); scan material lot → `CountSheet`
(shows expected, computes signed variance live; flash `ok` if matches else `warn`);
primary dock "Uložit inventuru". Route `stocktake` replaces its `ComingSoonPage`.

### Task 16: Identifikace šarže screen (expand after Task 14 unblocks) — **[needs spec.html §8 + lot-registration API]**

Shell screen: session subject (n registered); scan material → `RegisterSheet`
(lot #, expiry, quantity); primary dock "Dokončit příjem". Route
`lot-identification` replaces its `ComingSoonPage`.

---

## Phase 7 — Cleanup

### Task 17: Retire `ScanInput` and (conditionally) `ComingSoonPage`

**Files:**
- Delete: `frontend/src/components/terminal/ScanInput.tsx`
- Delete: `frontend/src/components/terminal/__tests__/ScanInput.test.tsx`
- Delete (only if Phase 6 shipped): `frontend/src/components/terminal/ComingSoonPage.tsx`
- Modify (only if Phase 6 shipped): `frontend/src/App.tsx` route elements

- [ ] **Step 1: Confirm `ScanInput` has no remaining importers**

Run: `cd frontend && grep -rln "ScanInput" src --include=*.tsx --include=*.ts | grep -v "ScanInput.tsx" | grep -v "ScanInput.test"`
Expected: no output (every workflow migrated off it).

- [ ] **Step 2: Delete `ScanInput` + its test**

```bash
git rm frontend/src/components/terminal/ScanInput.tsx \
       frontend/src/components/terminal/__tests__/ScanInput.test.tsx
```

- [ ] **Step 3: If and only if Phase 6 shipped both screens**, confirm
`ComingSoonPage` is unused and remove it + its route imports/elements in
`App.tsx` (wire the new screens at routes `stocktake` / `lot-identification`).

Run: `cd frontend && grep -rln "ComingSoonPage" src --include=*.tsx`
Expected (post-Phase 6): only `App.tsx` (to be edited) — then `git rm` the component.

- [ ] **Step 4: Build + lint + full terminal suite**

Run: `cd frontend && npm run build && npm run lint && CI=true npx react-scripts test src/components/terminal --watchAll=false`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add -A frontend/src/components/terminal frontend/src/App.tsx
git commit -m "chore(terminal): retire ScanInput (and ComingSoonPage if new screens shipped)"
```

---

## Acceptance criteria (from README §"Acceptance criteria" — [confirm full list vs spec.html §12])

- [ ] A focused capture field exists at all times; a typed code + terminator scans
      without tapping a field.
- [ ] Focus returns to the wedge after every scan / blur / route change, but yields
      to an open `BottomSheet` input and reclaims on close.
- [ ] Every scan produces exactly one correctly-toned, non-blocking flash.
- [ ] All migrated workflows render zones A–E (app bar, subject, body, scan strip,
      docked action) in identical positions.
- [ ] Scanning a box on Home routes per the state map (Task 13).
- [ ] No workflow supplies its own header, scan field, or feedback — only subject,
      body, actions, and an `onScan`.

**E2E (per CLAUDE.md):** after the FE gates pass, run the terminal E2E module
against staging: `./scripts/run-playwright-tests.sh` (terminal tests live under
`frontend/test/e2e/<terminal-module>/`; auth via `navigateToApp()`; use fixtures
from `frontend/test/e2e/fixtures/test-data.ts`). Update any E2E selector that
depended on the retired inline success card / `ScanInput`.

---

## Self-review (run before handing off)

**Spec coverage.** Every README section maps to a task: shell zones → Tasks 2–8;
wedge model → Task 3; flash contract → Task 4; tokens → Task 1; Kontrola → 10;
Příjem → 11; Plnění → 12; Home/scan-first → 13; Inventura/Identifikace → 14–16
(gated); cleanup → 17. **Gaps:** Inventura/Identifikace cannot be fully specified
(missing spec.html §8 + backend APIs) — explicitly gated at Task 14, not silently
dropped.

**Type consistency.** `FlashTone` = `'ok'|'warn'|'err'` everywhere; `flash(tone,
code?)`, `registerScanHandler`, `setYieldFocus`, `refocus` names match across
`types.ts`, `ScanProvider`, `useScanScreen`, `BottomSheet`. `DockAction` shape
identical in `types.ts`, `DockedAction`, and every workflow's `actions` array.

**Placeholder scan.** The only deliberately-unfilled steps are Tasks 15–16, which
are *declared* blocked on missing spec.html + backend APIs (Task 14 gate) — fill
them with bite-sized TDD steps once unblocked. Everything in Phases 0–5 + 7 has
concrete code/commands.

---

## Open questions (carry to the team — README §"Open questions")

1. **Scanner terminator** — confirm DataWedge keystroke-output mode + terminator
   (Enter vs Tab). `ScanProvider` accepts both today; pin it.
2. **Backend APIs** for stocktake + lot registration — the Phase 6 gate (Task 14).
3. **Scan-first mapping** precedence — validate with floor procedure (Task 13).
4. **Audio / haptic** — v1 or deferred? The single `flash()` dispatch is the
   attach point.
5. **"Naskenovat další"** — explicit reset dock, or is the always-ready wedge
   enough? (v1 omits it — Task 10 note.)
6. **Offline / queueing** — any requirement to buffer scans/submissions on
   connectivity loss? (Not addressed in this plan.)
7. **Naming** — align `ScanProvider`/`ScanShell`/`useScanScreen`/`DockAction`
   names with existing conventions before merge.
8. **Missing `spec.html`** — request the authoritative spec (§5 contracts, §8
   workflow detail, §10 token table, §12 acceptance) and reconcile every
   **[confirm vs spec.html]** marker above.
```
