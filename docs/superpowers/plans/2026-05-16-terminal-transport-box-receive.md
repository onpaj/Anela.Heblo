# Terminal Transport Box Receive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Příjem boxu" (Receive box) workflow to the mobile terminal subapp — scan a transport box barcode, view its contents/history, then Accept (receive into warehouse) or Reject (dismiss), with re-scanning the loaded box as a no-tap Accept shortcut.

**Architecture:** A new terminal screen `TransportBoxReceive` reuses the box-detail UI introduced by PR #1298 (extracted into a shared component) and the existing `ScanInput`. Receiving is a `ChangeTransportBoxState` transition to `Received`; no backend changes. Reject is a pure client-side dismiss. The "receive" route, currently a `ComingSoonPage` stub, is wired to the new screen and its home-tile is activated.

**Tech Stack:** React 18 + TypeScript, react-router-dom, @tanstack/react-query, Tailwind, lucide-react, Jest + React Testing Library.

---

## Context

PR #1298 (`claude/mobile-terminal-barcode-scanner-ZNwkv`) introduced the terminal subapp and its first workflow — **Box check** (`TransportBoxCheck.tsx`): scan a box barcode, view contents/history in two tabs. This plan adds the **second** workflow, **Receive box**, built on top of PR #1298 *before it merges to main*.

It mirrors the existing desktop receive page (`frontend/src/components/pages/TransportBoxReceive.tsx`): scan → load → Accept/Reject. The terminal version adds a scanner convenience: re-scanning the loaded box's barcode triggers Accept without tapping.

### Branch

> **IMPORTANT:** Base this work on `origin/claude/mobile-terminal-barcode-scanner-ZNwkv` (PR #1298), **not** `main`. Create the feature branch from it so the commits stack on the PR.

```bash
git fetch origin claude/mobile-terminal-barcode-scanner-ZNwkv
git checkout -b feature/terminal-box-receive origin/claude/mobile-terminal-barcode-scanner-ZNwkv
```

### Domain facts (confirmed via codebase exploration)

- **Receiving** = a `ChangeTransportBoxState` transition with `newState = TransportBoxState.Received`. There is no dedicated receive command/endpoint.
- A box is receivable only from `InTransit`, `Reserve`, or `Quarantine`. The backend `TransportBoxDto` exposes a precomputed boolean **`isReceivable`**.
- There is **no "Rejected" backend state**. Per product decision, **Reject = dismiss the box and return to scanning** (no backend call — like the desktop "Storno" button).
- `useTransportBoxByCodeQuery(code)` (added by PR #1298, in `frontend/src/api/hooks/useTransportBoxes.ts`) loads a box by barcode; returns `TransportBoxDto | null` (`null` = not found).
- `useChangeTransportBoxState()` (pre-existing, same file) performs the state change. Its `mutationFn` accepts `{ boxId, newState, description?, boxNumber?, location? }` and returns a mutation with `.mutateAsync` and `.isPending`.

### Product decisions

- **Reject**: dismiss box, return to scan-ready. No backend call.
- **Non-receivable box** (`isReceivable !== true`): show a "Box nelze přijmout" message, render Accept disabled, keep Reject enabled. Re-scanning a non-receivable box does **not** trigger Accept.
- **After a successful Accept**: show a brief success confirmation, then auto-clear back to scan-ready (~2.5 s).

## File Structure

| File | Responsibility |
|------|----------------|
| `frontend/src/components/terminal/TransportBoxDetail.tsx` | **new** — shared box-detail card + contents/history tabs, extracted from `TransportBoxCheck.tsx` (DRY) |
| `frontend/src/components/terminal/TransportBoxCheck.tsx` | **modify** — import the extracted `BoxDetail`, drop the inlined copy |
| `frontend/src/components/terminal/TransportBoxReceive.tsx` | **new** — the receive workflow screen |
| `frontend/src/components/terminal/TerminalHome.tsx` | **modify** — activate the `receive` tile |
| `frontend/src/App.tsx` | **modify** — route `receive` → `TransportBoxReceive` |
| `frontend/src/components/terminal/__tests__/TransportBoxReceive.test.tsx` | **new** — unit tests |
| `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx` | **modify** — coming-soon count 3 → 2 |

No backend changes. No new API hook. No OpenAPI regeneration.

---

## Task 1: Extract the shared `BoxDetail` component

Pure refactor — `TransportBoxCheck.tsx` currently inlines `BoxDetail`, `ContentsTab`, `HistoryTab`, `formatDate`, `formatDateTime`. Move them into a shared file so the receive screen can reuse them. Behavior must not change; the existing `TransportBoxCheck.test.tsx` is the safety net.

**Files:**
- Create: `frontend/src/components/terminal/TransportBoxDetail.tsx`
- Modify: `frontend/src/components/terminal/TransportBoxCheck.tsx`
- Test (existing, must keep passing): `frontend/src/components/terminal/__tests__/TransportBoxCheck.test.tsx`

- [ ] **Step 1: Create `TransportBoxDetail.tsx`**

Create `frontend/src/components/terminal/TransportBoxDetail.tsx` with the exact content below (this is the inlined code from `TransportBoxCheck.tsx`, now with `BoxDetail` as the default export):

```tsx
import React, { useState } from 'react';
import TransportBoxStateBadge from '../transport/box-detail/components/TransportBoxStateBadge';
import {
  TransportBoxDto,
  TransportBoxItemDto,
  TransportBoxStateLogDto,
} from '../../api/generated/api-client';

type Tab = 'contents' | 'history';

const formatDate = (d?: Date): string =>
  d ? new Date(d).toLocaleDateString('cs-CZ') : '—';

const formatDateTime = (d?: Date): string =>
  d
    ? new Date(d).toLocaleString('cs-CZ', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })
    : '—';

const ContentsTab: React.FC<{ items: TransportBoxItemDto[] }> = ({ items }) => {
  if (items.length === 0) {
    return (
      <p className="text-sm text-neutral-gray py-6 text-center">
        Box neobsahuje žádné položky
      </p>
    );
  }
  return (
    <div className="space-y-2">
      {items.map((item) => (
        <div
          key={item.id}
          className="bg-white border border-border-light rounded-xl p-3"
        >
          <div className="flex justify-between gap-2">
            <span className="font-medium text-neutral-slate">
              {item.productName}
            </span>
            <span className="font-semibold text-neutral-slate whitespace-nowrap">
              {item.amount}
            </span>
          </div>
          <div className="text-xs text-neutral-gray">{item.productCode}</div>
          {(item.lotNumber || item.expirationDate) && (
            <div className="text-xs text-neutral-gray mt-1 flex flex-wrap gap-x-3 gap-y-0.5">
              {item.lotNumber && <span>Šarže: {item.lotNumber}</span>}
              {item.expirationDate && (
                <span>Expirace: {formatDate(item.expirationDate)}</span>
              )}
            </div>
          )}
        </div>
      ))}
    </div>
  );
};

const HistoryTab: React.FC<{ log: TransportBoxStateLogDto[] }> = ({ log }) => {
  if (log.length === 0) {
    return (
      <p className="text-sm text-neutral-gray py-6 text-center">
        Žádná historie změn
      </p>
    );
  }
  const ordered = [...log].sort(
    (a, b) =>
      (b.stateDate ? new Date(b.stateDate).getTime() : 0) -
      (a.stateDate ? new Date(a.stateDate).getTime() : 0),
  );
  return (
    <div className="space-y-2">
      {ordered.map((entry) => (
        <div
          key={entry.id}
          className="bg-white border border-border-light rounded-xl p-3 space-y-1"
        >
          <div className="flex justify-between items-center gap-2">
            <TransportBoxStateBadge state={entry.state ?? ''} size="sm" />
            <span className="text-xs text-neutral-gray">
              {formatDateTime(entry.stateDate)}
            </span>
          </div>
          {entry.user && (
            <div className="text-xs text-neutral-gray">{entry.user}</div>
          )}
          {entry.description && (
            <div className="text-sm text-neutral-slate">{entry.description}</div>
          )}
        </div>
      ))}
    </div>
  );
};

const BoxDetail: React.FC<{ box: TransportBoxDto }> = ({ box }) => {
  const [activeTab, setActiveTab] = useState<Tab>('contents');
  const items = box.items ?? [];
  const log = box.stateLog ?? [];

  const tabClass = (tab: Tab) =>
    `flex-1 py-2 text-sm font-medium border-b-2 transition-colors ${
      activeTab === tab
        ? 'border-primary-blue text-primary-blue'
        : 'border-transparent text-neutral-gray'
    }`;

  return (
    <div className="space-y-3">
      <div className="bg-white border border-border-light rounded-xl p-4 shadow-soft space-y-2">
        <div className="flex items-center justify-between gap-2">
          <span className="text-lg font-bold text-neutral-slate">
            {box.code}
          </span>
          <TransportBoxStateBadge state={box.state ?? ''} size="lg" />
        </div>
        {box.location && (
          <div className="text-sm text-neutral-gray">
            Umístění: {box.location}
          </div>
        )}
        {box.description && (
          <div className="text-sm text-neutral-slate">{box.description}</div>
        )}
        <div className="text-sm text-neutral-gray">
          Počet položek: {items.length}
        </div>
      </div>

      <div className="flex border-b border-border-light">
        <button
          type="button"
          data-testid="tab-contents"
          className={tabClass('contents')}
          onClick={() => setActiveTab('contents')}
        >
          Obsah ({items.length})
        </button>
        <button
          type="button"
          data-testid="tab-history"
          className={tabClass('history')}
          onClick={() => setActiveTab('history')}
        >
          Historie ({log.length})
        </button>
      </div>

      {activeTab === 'contents' ? (
        <ContentsTab items={items} />
      ) : (
        <HistoryTab log={log} />
      )}
    </div>
  );
};

export default BoxDetail;
```

- [ ] **Step 2: Replace `TransportBoxCheck.tsx` with the slimmed version**

Overwrite `frontend/src/components/terminal/TransportBoxCheck.tsx` with:

```tsx
import React, { useState } from 'react';
import { Loader2, PackageX } from 'lucide-react';
import ScanInput from './ScanInput';
import { useTransportBoxByCodeQuery } from '../../api/hooks/useTransportBoxes';
import BoxDetail from './TransportBoxDetail';

const TransportBoxCheck: React.FC = () => {
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data: box, isFetching } = useTransportBoxByCodeQuery(scannedCode);

  const showNotFound = !!scannedCode && !isFetching && !box;

  return (
    <div className="space-y-4">
      <div className="sticky top-0 z-10 bg-background-gray pb-3">
        <ScanInput
          label="Kód boxu"
          onScan={setScannedCode}
          suppressKeyboard
          allowKeyboardToggle
        />
      </div>

      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}

      {showNotFound && (
        <div
          data-testid="box-not-found"
          className="bg-white border border-border-light rounded-xl p-6 flex flex-col items-center text-center gap-2"
        >
          <PackageX className="h-10 w-10 text-neutral-gray" />
          <p className="font-semibold text-neutral-slate">
            Box {scannedCode} nenalezen
          </p>
          <p className="text-sm text-neutral-gray">
            Zkontrolujte kód a naskenujte znovu
          </p>
        </div>
      )}

      {!isFetching && box && <BoxDetail box={box} />}
    </div>
  );
};

export default TransportBoxCheck;
```

- [ ] **Step 3: Run the existing box-check test to verify the refactor is behavior-neutral**

Run: `cd frontend && npm test -- --watchAll=false TransportBoxCheck`
Expected: PASS — all 4 `TransportBoxCheck` tests still pass.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/terminal/TransportBoxDetail.tsx frontend/src/components/terminal/TransportBoxCheck.tsx
git commit -m "refactor(terminal): extract shared BoxDetail component"
```

---

## Task 2: Build the `TransportBoxReceive` screen (TDD)

**Files:**
- Create: `frontend/src/components/terminal/TransportBoxReceive.tsx`
- Test: `frontend/src/components/terminal/__tests__/TransportBoxReceive.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/terminal/__tests__/TransportBoxReceive.test.tsx`. The mocking + fake-timer pattern mirrors `TransportBoxCheck.test.tsx`; the accept flow is async so it uses `await act(async () => ...)`.

```tsx
import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import TransportBoxReceive from '../TransportBoxReceive';
import {
  useTransportBoxByCodeQuery,
  useChangeTransportBoxState,
} from '../../../api/hooks/useTransportBoxes';

jest.mock('../../../api/hooks/useTransportBoxes', () => ({
  useTransportBoxByCodeQuery: jest.fn(),
  useChangeTransportBoxState: jest.fn(),
}));
jest.mock('@tanstack/react-query', () => ({
  ...jest.requireActual('@tanstack/react-query'),
  useQueryClient: () => ({ invalidateQueries: jest.fn() }),
}));

const mockByCode = useTransportBoxByCodeQuery as jest.Mock;
const mockChangeState = useChangeTransportBoxState as jest.Mock;

const receivableBox = {
  id: 1,
  code: 'B001',
  state: 'InTransit',
  isReceivable: true,
  location: 'Kumbal',
  items: [{ id: 10, productCode: 'MED001', productName: 'Obvazy', amount: 5 }],
  stateLog: [{ id: 1, state: 'InTransit', stateDate: new Date('2026-05-10'), user: 'jan' }],
};

const nonReceivableBox = { ...receivableBox, state: 'Stocked', isReceivable: false };

let mutateAsync: jest.Mock;

beforeEach(() => {
  jest.useFakeTimers();
  mockByCode.mockReset();
  mockChangeState.mockReset();
  mutateAsync = jest.fn().mockResolvedValue({});
  mockChangeState.mockReturnValue({ mutateAsync, isPending: false });
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

const scan = (code: string) => {
  fireEvent.change(screen.getByRole('textbox'), { target: { value: code } });
  fireEvent.submit(screen.getByRole('textbox').closest('form')!);
};

const byCodeFor = (target: string, box: unknown) => (code: string | null) =>
  code === target
    ? { data: box, isFetching: false }
    : { data: undefined, isFetching: false };

describe('TransportBoxReceive', () => {
  it('renders a focused scan input on mount', () => {
    mockByCode.mockReturnValue({ data: undefined, isFetching: false });
    render(<TransportBoxReceive />);
    expect(screen.getByRole('textbox')).toHaveFocus();
  });

  it('shows box detail and an enabled Accept button after a receivable scan', () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('b001'));

    expect(screen.getByText('B001')).toBeInTheDocument();
    expect(screen.getByText('Obvazy')).toBeInTheDocument();
    expect(screen.getByTestId('accept-box')).toBeEnabled();
    expect(screen.getByTestId('reject-box')).toBeEnabled();
  });

  it('accepts a box: calls the state-change mutation and shows the success banner', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    await act(async () => {
      fireEvent.click(screen.getByTestId('accept-box'));
    });

    expect(mutateAsync).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' });
    expect(screen.getByTestId('receive-success')).toBeInTheDocument();
    expect(screen.getByText('Box B001 přijat')).toBeInTheDocument();
  });

  it('re-scanning the loaded box code triggers Accept without a tap', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    await act(async () => {
      scan('B001');
    });

    expect(mutateAsync).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' });
  });

  it('Reject clears the box without calling the backend', () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    act(() => {
      fireEvent.click(screen.getByTestId('reject-box'));
    });

    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.queryByText('B001')).not.toBeInTheDocument();
  });

  it('disables Accept and shows a warning for a non-receivable box', () => {
    mockByCode.mockImplementation(byCodeFor('B002', nonReceivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B002'));

    expect(screen.getByTestId('accept-box')).toBeDisabled();
    expect(screen.getByTestId('not-receivable')).toBeInTheDocument();

    // re-scanning a non-receivable box must NOT trigger Accept
    act(() => scan('B002'));
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('shows a not-found message for an unknown code', () => {
    mockByCode.mockImplementation((code: string | null) =>
      code ? { data: null, isFetching: false } : { data: undefined, isFetching: false },
    );
    render(<TransportBoxReceive />);
    act(() => scan('B999'));

    expect(screen.getByTestId('box-not-found')).toBeInTheDocument();
    expect(screen.getByText('Box B999 nenalezen')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- --watchAll=false TransportBoxReceive`
Expected: FAIL — `Cannot find module '../TransportBoxReceive'`.

- [ ] **Step 3: Implement `TransportBoxReceive.tsx`**

Create `frontend/src/components/terminal/TransportBoxReceive.tsx`:

```tsx
import React, { useEffect, useState } from 'react';
import { Loader2, PackageX, Check, X, CheckCircle2 } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import ScanInput from './ScanInput';
import BoxDetail from './TransportBoxDetail';
import {
  useTransportBoxByCodeQuery,
  useChangeTransportBoxState,
} from '../../api/hooks/useTransportBoxes';
import { QUERY_KEYS } from '../../api/client';
import { TransportBoxState } from '../../api/generated/api-client';

const SUCCESS_DISPLAY_MS = 2500;

const TransportBoxReceive: React.FC = () => {
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const [receivedCode, setReceivedCode] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const { data: box, isFetching } = useTransportBoxByCodeQuery(scannedCode);
  const changeState = useChangeTransportBoxState();

  const showNotFound = !!scannedCode && !isFetching && !box;
  const canReceive = box?.isReceivable === true;

  // Auto-dismiss the success banner back to scan-ready.
  useEffect(() => {
    if (!receivedCode) return;
    const timer = setTimeout(() => setReceivedCode(null), SUCCESS_DISPLAY_MS);
    return () => clearTimeout(timer);
  }, [receivedCode]);

  const handleAccept = async () => {
    if (!box || box.isReceivable !== true || changeState.isPending) return;
    try {
      await changeState.mutateAsync({
        boxId: box.id!,
        newState: TransportBoxState.Received,
      });
      // The byCode cache is stale after a receive — drop it so a re-scan refetches.
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, 'byCode'],
      });
      setReceivedCode(box.code ?? null);
      setScannedCode(null);
    } catch {
      // Backend errors surface via the global toast handler; keep the box
      // on screen so the user can retry.
    }
  };

  const handleReject = () => {
    setScannedCode(null);
    setReceivedCode(null);
  };

  const handleScan = (value: string) => {
    // Re-scanning the already-loaded receivable box acts as Accept.
    if (box?.isReceivable === true && box.code === value && !changeState.isPending) {
      void handleAccept();
      return;
    }
    setReceivedCode(null);
    setScannedCode(value);
  };

  return (
    <div className="space-y-4">
      <div className="sticky top-0 z-10 bg-background-gray pb-3">
        <ScanInput
          label="Kód boxu"
          onScan={handleScan}
          loading={changeState.isPending}
          suppressKeyboard
          allowKeyboardToggle
        />
      </div>

      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}

      {receivedCode && !box && (
        <div
          data-testid="receive-success"
          className="bg-green-50 border border-green-200 rounded-xl p-6 flex flex-col items-center text-center gap-2"
        >
          <CheckCircle2 className="h-10 w-10 text-green-600" />
          <p className="font-semibold text-neutral-slate">
            Box {receivedCode} přijat
          </p>
          <p className="text-sm text-neutral-gray">Naskenujte další box</p>
        </div>
      )}

      {showNotFound && (
        <div
          data-testid="box-not-found"
          className="bg-white border border-border-light rounded-xl p-6 flex flex-col items-center text-center gap-2"
        >
          <PackageX className="h-10 w-10 text-neutral-gray" />
          <p className="font-semibold text-neutral-slate">
            Box {scannedCode} nenalezen
          </p>
          <p className="text-sm text-neutral-gray">
            Zkontrolujte kód a naskenujte znovu
          </p>
        </div>
      )}

      {!isFetching && box && (
        <div className="space-y-3">
          <BoxDetail box={box} />

          {!canReceive && (
            <div
              data-testid="not-receivable"
              className="bg-red-50 border border-red-200 rounded-xl p-3 text-sm text-red-700"
            >
              Tento box nelze přijmout. Pro příjem musí být ve stavu V přepravě,
              V rezervě nebo V karanténě.
            </div>
          )}

          <div className="flex gap-3 pt-1">
            <button
              type="button"
              data-testid="reject-box"
              onClick={handleReject}
              className="flex-1 h-14 flex items-center justify-center gap-2 rounded-xl border border-border-light text-neutral-slate font-semibold hover:border-primary-blue transition-colors"
            >
              <X className="h-5 w-5" />
              Zamítnout
            </button>
            <button
              type="button"
              data-testid="accept-box"
              onClick={handleAccept}
              disabled={!canReceive || changeState.isPending}
              className="flex-1 h-14 flex items-center justify-center gap-2 rounded-xl bg-green-600 text-white font-semibold hover:bg-green-700 transition-colors disabled:bg-gray-200 disabled:text-neutral-gray disabled:cursor-not-allowed"
            >
              {changeState.isPending ? (
                <Loader2 className="h-5 w-5 animate-spin" />
              ) : (
                <Check className="h-5 w-5" />
              )}
              {canReceive ? 'Potvrdit příjem' : 'Nelze přijmout'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default TransportBoxReceive;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npm test -- --watchAll=false TransportBoxReceive`
Expected: PASS — all 7 `TransportBoxReceive` tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/TransportBoxReceive.tsx frontend/src/components/terminal/__tests__/TransportBoxReceive.test.tsx
git commit -m "feat(terminal): add transport box receive workflow screen"
```

---

## Task 3: Wire the route and activate the home tile

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/terminal/TerminalHome.tsx`
- Modify: `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx`

- [ ] **Step 1: Update the failing `TerminalHome` test first**

In `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx`, the `receive` tile is no longer a stub. Change the coming-soon count in the last test:

Replace:
```tsx
  it('shows coming-soon label only on the stub tiles', () => {
    renderHome();
    const labels = screen.getAllByText('Brzy k dispozici');
    expect(labels).toHaveLength(3);
  });
```
With:
```tsx
  it('shows coming-soon label only on the stub tiles', () => {
    renderHome();
    const labels = screen.getAllByText('Brzy k dispozici');
    expect(labels).toHaveLength(2);
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- --watchAll=false TerminalHome`
Expected: FAIL — "shows coming-soon label only on the stub tiles" expects 2 but receives 3 (the `receive` tile is still `comingSoon: true`).

- [ ] **Step 3: Activate the `receive` tile in `TerminalHome.tsx`**

In `frontend/src/components/terminal/TerminalHome.tsx`, in the `WORKFLOWS` array, change the `receive` entry's `comingSoon` flag.

Replace:
```tsx
  {
    id: 'receive',
    title: 'Příjem boxu',
    description: 'Naskenujte kód boxu a potvrďte příjem zásilky do skladu',
    href: '/terminal/receive',
    icon: Package,
    comingSoon: true,
  },
```
With:
```tsx
  {
    id: 'receive',
    title: 'Příjem boxu',
    description: 'Naskenujte kód boxu a potvrďte příjem zásilky do skladu',
    href: '/terminal/receive',
    icon: Package,
    comingSoon: false,
  },
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npm test -- --watchAll=false TerminalHome`
Expected: PASS — all `TerminalHome` tests pass.

- [ ] **Step 5: Wire the route in `App.tsx`**

In `frontend/src/App.tsx`, add the import directly after the existing `TransportBoxCheck` import (line ~69):

```tsx
import TransportBoxReceive from "./components/terminal/TransportBoxReceive";
```

Then, in the terminal `<Route>` block, replace:
```tsx
                        <Route path="receive" element={<ComingSoonPage title="Příjem boxu" />} />
```
With:
```tsx
                        <Route path="receive" element={<TransportBoxReceive />} />
```

(Leave the `ComingSoonPage` import — it is still used by the `stocktake` and `lot-identification` routes.)

- [ ] **Step 6: Verify the build compiles**

Run: `cd frontend && npm run build`
Expected: build succeeds with no TypeScript errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/App.tsx frontend/src/components/terminal/TerminalHome.tsx frontend/src/components/terminal/__tests__/TerminalHome.test.tsx
git commit -m "feat(terminal): wire receive route and activate home tile"
```

---

## Task 4: Full verification

**No new files — verification only.**

- [ ] **Step 1: Lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: build succeeds.

- [ ] **Step 3: Run all touched + new test suites**

Run: `cd frontend && npm test -- --watchAll=false TransportBoxReceive TransportBoxCheck TerminalHome ScanInput`
Expected: PASS — all suites green.

- [ ] **Step 4: Manual smoke test**

Start the app, open `/terminal`, tap **Příjem boxu**:
- scan a receivable box (state `InTransit`/`Reserve`/`Quarantine`) → detail loads with contents/history tabs, Accept enabled;
- tap **Potvrdit příjem** → success banner "Box <code> přijat", then auto-returns to scan-ready;
- scan a box, then scan the same code again → box is received without tapping Accept;
- tap **Zamítnout** → screen clears, no state change;
- scan a non-receivable box (e.g. `Stocked`) → "Tento box nelze přijmout" message, Accept disabled;
- scan an unknown code → "Box <code> nenalezen".

- [ ] **Step 5: E2E (optional follow-up)**

If `frontend/test/e2e/fixtures/test-data.ts` contains a receivable transport-box fixture, add a Playwright spec under `frontend/test/e2e/logistics/` covering scan → Accept and scan → Reject (auth via `navigateToApp()`). If no such fixture exists, note it as a follow-up — the E2E suite runs nightly, not in PR CI.

---

## Self-Review

- **Spec coverage:** scan + load (Task 2 Step 3 `handleScan`/`useTransportBoxByCodeQuery`); contents + history (Task 1 `BoxDetail`); Accept button → `Received` transition (Task 2); Reject = dismiss, no backend (Task 2 `handleReject`); re-scan-to-Accept (Task 2 `handleScan`); non-receivable handling + success confirmation (Task 2, product decisions); route + tile activation (Task 3). All covered.
- **Placeholder scan:** no TBD/TODO; every code step contains complete code.
- **Type consistency:** `BoxDetail` exported as default from `TransportBoxDetail.tsx`, imported as default in both `TransportBoxCheck.tsx` and `TransportBoxReceive.tsx`. `useChangeTransportBoxState().mutateAsync` is called with `{ boxId, newState }` — a subset of its `{ boxId, newState, description?, boxNumber?, location? }` signature (optional fields omitted). `TransportBoxState.Received` serializes to the string `"Received"`, matching the test's `expect(...).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' })`. `isReceivable` is the boolean field on `TransportBoxDto`.
