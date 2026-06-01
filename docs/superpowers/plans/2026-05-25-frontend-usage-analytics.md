# Frontend Usage Analytics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Azure Application Insights to the React frontend to track page views and 5 seed business events, reusing the same AI resource as the backend so frontend and backend traces correlate.

**Architecture:** Install `@microsoft/applicationinsights-web` + `@microsoft/applicationinsights-react-js`; create a singleton `ApplicationInsights` instance that is NoOp when `REACT_APP_AI_CONNECTION_STRING` is empty; wrap `<Routes>` in an `AppInsightsProvider` that tracks page changes via `useLocation()`; expose `useTelemetry()` hook for manual event calls; wire MSAL login/logout events to `setAuthenticatedUserContext(oid)`.

**Tech Stack:** React 18, TypeScript, `@microsoft/applicationinsights-web ^3`, `@microsoft/applicationinsights-react-js ^3`, MSAL Browser 3.x, React Router v6, Jest / @testing-library/react (existing CRA setup)

---

## File map

### New files
| File | Responsibility |
|---|---|
| `frontend/src/telemetry/appInsights.ts` | Singleton AI instance + `ReactPlugin`; NoOp when connection string is empty |
| `frontend/src/telemetry/events.ts` | `TelemetryEventName` string-literal union — single source of truth for event names |
| `frontend/src/telemetry/useTelemetry.ts` | Typed hook: `trackEvent`, `trackException`, `trackMetric` |
| `frontend/src/telemetry/AppInsightsProvider.tsx` | React provider: mounts `AppInsightsContext.Provider`, tracks route changes via `useLocation()` |
| `frontend/src/telemetry/__tests__/appInsights.test.ts` | Unit tests for NoOp behaviour when connection string is empty |
| `frontend/src/telemetry/__tests__/useTelemetry.test.tsx` | Unit tests for hook against mocked AI instance |
| `docs/features/usage-analytics.md` | Event catalogue — required by every PR that adds a `trackEvent` call |

### Modified files
| File | Change |
|---|---|
| `frontend/package.json` | Add two AI SDK packages |
| `frontend/.env.example` | Add `REACT_APP_AI_CONNECTION_STRING=` |
| `frontend/.env` | Add empty var (NoOp in dev) |
| `frontend/src/config/runtimeConfig.ts` | Add `aiConnectionString` to `Config`, `loadConfig()`, and log output |
| `frontend/src/App.tsx` | Call `initAppInsights` on startup; add MSAL event callbacks; wrap `<Routes>` in `<AppInsightsProvider>` |
| `frontend/src/components/dashboard/DashboardTile.tsx` | Track `DashboardTileClicked` |
| `frontend/src/components/dashboard/__tests__/DashboardTile.test.tsx` | Update to mock `useTelemetry` |
| `frontend/src/components/marketing/photobank/BulkTagDialog.tsx` | Track `PhotobankBulkTagApplied` |
| `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx` | Track `ManufactureOrderCreated` on successful duplicate |
| `frontend/src/components/pages/PurchaseOrderList.tsx` | Track `PurchaseOrderSubmitted` in `handleCreateSuccess` |
| `frontend/src/pages/FeatureFlagsAdminPage.tsx` | Track `FeatureFlagToggled` on toggle click |
| `docs/architecture/observability.md` | Flip Frontend Monitoring row from ⏳ Planned to ✅ Implemented |
| `docs/development/setup.md` | Document new env var |

---

## Task 1: Install packages and extend environment config

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/.env.example`
- Modify: `frontend/.env`
- Modify: `frontend/src/config/runtimeConfig.ts`

- [ ] **Step 1: Install the two AI SDK packages**

```bash
cd frontend && npm install @microsoft/applicationinsights-web @microsoft/applicationinsights-react-js
```

Expected: packages appear in `package.json` dependencies (version ~3.x).

- [ ] **Step 2: Add env var to .env.example**

Open `frontend/.env.example`. After the existing `REACT_APP_USE_MOCK_AUTH=true` line, add:

```
# Azure Application Insights (leave empty to run in NoOp mode locally)
REACT_APP_AI_CONNECTION_STRING=
```

- [ ] **Step 3: Add env var to .env**

Open `frontend/.env`. Add the same line (empty value):

```
REACT_APP_AI_CONNECTION_STRING=
```

- [ ] **Step 4: Update `Config` interface in runtimeConfig.ts**

Open `frontend/src/config/runtimeConfig.ts`. The `Config` interface is at lines 7–12. Add `aiConnectionString` after `azureAuthority`:

```typescript
export interface Config {
  apiUrl: string;
  useMockAuth: boolean;
  azureClientId: string;
  azureAuthority: string;
  aiConnectionString: string;
}
```

- [ ] **Step 5: Read the env var in loadConfig()**

In `loadConfig()`, the `cachedConfig` assignment block starts at line 100. Add `aiConnectionString` after `azureAuthority`:

```typescript
  cachedConfig = {
    apiUrl: process.env.REACT_APP_API_URL || window.location.origin,
    useMockAuth: shouldUseMock,
    azureClientId: process.env.REACT_APP_AZURE_CLIENT_ID || "",
    azureAuthority: process.env.REACT_APP_AZURE_AUTHORITY || "",
    aiConnectionString: process.env.REACT_APP_AI_CONNECTION_STRING || "",
  };
```

- [ ] **Step 6: Log the new field**

In the `console.log("✅ Configuration loaded successfully:", {...})` block (around line 108), add:

```typescript
  console.log("✅ Configuration loaded successfully:", {
    apiUrl: cachedConfig.apiUrl,
    useMockAuth: cachedConfig.useMockAuth,
    azureClientId: cachedConfig.azureClientId ? "[SET]" : "[NOT SET]",
    azureAuthority: cachedConfig.azureAuthority ? "[SET]" : "[NOT SET]",
    aiConnectionString: cachedConfig.aiConnectionString ? "[SET]" : "[NOT SET]",
  });
```

- [ ] **Step 7: Verify build compiles**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: `Compiled successfully` (or `Compiled with warnings` — no errors).

- [ ] **Step 8: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/.env.example frontend/.env frontend/src/config/runtimeConfig.ts
git commit -m "feat: add REACT_APP_AI_CONNECTION_STRING env var and extend Config type"
```

---

## Task 2: Create the appInsights singleton

**Files:**
- Create: `frontend/src/telemetry/appInsights.ts`
- Create: `frontend/src/telemetry/__tests__/appInsights.test.ts`

- [ ] **Step 1: Write the failing test first**

Create `frontend/src/telemetry/__tests__/appInsights.test.ts`:

```typescript
import { initAppInsights, getAppInsights } from '../appInsights';

// Reset module state between tests
beforeEach(() => {
  jest.resetModules();
});

describe('initAppInsights', () => {
  it('returns null and does not create an instance when connection string is empty', () => {
    const result = initAppInsights('');
    expect(result).toBeNull();
    expect(getAppInsights()).toBeNull();
  });

  it('returns null when connection string is whitespace', () => {
    const result = initAppInsights('   ');
    expect(result).toBeNull();
  });

  it('returns an ApplicationInsights instance when connection string is set', () => {
    // Provide a real-looking (but unused) connection string
    const result = initAppInsights(
      'InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/'
    );
    expect(result).not.toBeNull();
    expect(getAppInsights()).not.toBeNull();
  });
});
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd frontend && npm test -- --watchAll=false src/telemetry/__tests__/appInsights.test.ts 2>&1 | tail -20
```

Expected: test fails with `Cannot find module '../appInsights'`.

- [ ] **Step 3: Create the appInsights.ts implementation**

Create `frontend/src/telemetry/appInsights.ts`:

```typescript
import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';

export const reactPlugin = new ReactPlugin();

let instance: ApplicationInsights | null = null;

export function initAppInsights(connectionString: string): ApplicationInsights | null {
  const trimmed = connectionString.trim();
  if (!trimmed) return null;
  if (instance) return instance;

  instance = new ApplicationInsights({
    config: {
      connectionString: trimmed,
      extensions: [reactPlugin],
      extensionConfig: { [reactPlugin.identifier]: {} },
      enableAutoRouteTracking: false,
      disableFetchTracking: false,
      enableCorsCorrelation: true,
      enableRequestHeaderTracking: true,
      enableResponseHeaderTracking: true,
      autoTrackPageVisitTime: true,
    },
  });
  instance.loadAppInsights();
  return instance;
}

export function getAppInsights(): ApplicationInsights | null {
  return instance;
}
```

- [ ] **Step 4: Run test — verify it passes**

```bash
cd frontend && npm test -- --watchAll=false src/telemetry/__tests__/appInsights.test.ts 2>&1 | tail -20
```

Expected: `Tests: 3 passed`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/telemetry/appInsights.ts frontend/src/telemetry/__tests__/appInsights.test.ts
git commit -m "feat: add Application Insights singleton with NoOp fallback"
```

---

## Task 3: Create the event name catalogue

**Files:**
- Create: `frontend/src/telemetry/events.ts`

No separate test needed — this is a pure type definition file. The string values are verified by the tests in Task 4 and instrumentation tasks.

- [ ] **Step 1: Create events.ts**

Create `frontend/src/telemetry/events.ts`:

```typescript
export type TelemetryEventName =
  | 'DashboardTileClicked'
  | 'PhotobankBulkTagApplied'
  | 'ManufactureOrderCreated'
  | 'PurchaseOrderSubmitted'
  | 'FeatureFlagToggled';
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/telemetry/events.ts
git commit -m "feat: add TelemetryEventName union type (seed catalogue)"
```

---

## Task 4: Create the useTelemetry hook

**Files:**
- Create: `frontend/src/telemetry/useTelemetry.ts`
- Create: `frontend/src/telemetry/__tests__/useTelemetry.test.tsx`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/telemetry/__tests__/useTelemetry.test.tsx`:

```typescript
import { renderHook, act } from '@testing-library/react';
import { useTelemetry } from '../useTelemetry';
import * as appInsights from '../appInsights';

const mockTrackEvent = jest.fn();
const mockTrackException = jest.fn();
const mockTrackMetric = jest.fn();

const mockAI = {
  trackEvent: mockTrackEvent,
  trackException: mockTrackException,
  trackMetric: mockTrackMetric,
};

describe('useTelemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('when AI instance is available', () => {
    beforeEach(() => {
      jest.spyOn(appInsights, 'getAppInsights').mockReturnValue(mockAI as any);
    });

    it('trackEvent calls ai.trackEvent with name and properties', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackEvent('DashboardTileClicked', { tileId: 'tile-1' });
      });

      expect(mockTrackEvent).toHaveBeenCalledWith(
        { name: 'DashboardTileClicked' },
        { tileId: 'tile-1' }
      );
    });

    it('trackEvent merges properties and metrics into a single properties object', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackEvent(
          'PhotobankBulkTagApplied',
          { tagCount: '3' },
          { photoCount: 12 }
        );
      });

      expect(mockTrackEvent).toHaveBeenCalledWith(
        { name: 'PhotobankBulkTagApplied' },
        { tagCount: '3', photoCount: 12 }
      );
    });

    it('trackEvent works with no properties or metrics', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackEvent('ManufactureOrderCreated');
      });

      expect(mockTrackEvent).toHaveBeenCalledWith(
        { name: 'ManufactureOrderCreated' },
        {}
      );
    });

    it('trackException calls ai.trackException with the error', () => {
      const { result } = renderHook(() => useTelemetry());
      const error = new Error('test error');

      act(() => {
        result.current.trackException(error, { context: 'test' });
      });

      expect(mockTrackException).toHaveBeenCalledWith({
        exception: error,
        properties: { context: 'test' },
      });
    });

    it('trackMetric calls ai.trackMetric with name and value', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackMetric('loadTime', 250);
      });

      expect(mockTrackMetric).toHaveBeenCalledWith({ name: 'loadTime', average: 250 });
    });
  });

  describe('when AI instance is null (NoOp mode)', () => {
    beforeEach(() => {
      jest.spyOn(appInsights, 'getAppInsights').mockReturnValue(null);
    });

    it('trackEvent does not throw when AI is null', () => {
      const { result } = renderHook(() => useTelemetry());

      expect(() => {
        act(() => {
          result.current.trackEvent('DashboardTileClicked', { tileId: 'tile-1' });
        });
      }).not.toThrow();
    });

    it('trackException does not throw when AI is null', () => {
      const { result } = renderHook(() => useTelemetry());

      expect(() => {
        act(() => {
          result.current.trackException(new Error('test'));
        });
      }).not.toThrow();
    });

    it('trackMetric does not throw when AI is null', () => {
      const { result } = renderHook(() => useTelemetry());

      expect(() => {
        act(() => {
          result.current.trackMetric('loadTime', 100);
        });
      }).not.toThrow();
    });
  });
});
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd frontend && npm test -- --watchAll=false src/telemetry/__tests__/useTelemetry.test.tsx 2>&1 | tail -20
```

Expected: fails with `Cannot find module '../useTelemetry'`.

- [ ] **Step 3: Create the hook implementation**

Create `frontend/src/telemetry/useTelemetry.ts`:

```typescript
import { getAppInsights } from './appInsights';
import type { TelemetryEventName } from './events';

type Props = Record<string, string | number | boolean>;
type Metrics = Record<string, number>;

export function useTelemetry() {
  return {
    trackEvent: (name: TelemetryEventName, properties?: Props, metrics?: Metrics) => {
      getAppInsights()?.trackEvent({ name }, { ...properties, ...metrics });
    },
    trackException: (error: Error, properties?: Props) => {
      getAppInsights()?.trackException({ exception: error, properties });
    },
    trackMetric: (name: string, value: number) => {
      getAppInsights()?.trackMetric({ name, average: value });
    },
  };
}
```

- [ ] **Step 4: Run test — verify it passes**

```bash
cd frontend && npm test -- --watchAll=false src/telemetry/__tests__/useTelemetry.test.tsx 2>&1 | tail -20
```

Expected: `Tests: 8 passed`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/telemetry/useTelemetry.ts frontend/src/telemetry/__tests__/useTelemetry.test.tsx
git commit -m "feat: add useTelemetry hook with NoOp safety"
```

---

## Task 5: Create AppInsightsProvider

**Files:**
- Create: `frontend/src/telemetry/AppInsightsProvider.tsx`

No isolated unit test for the provider — its behavior is verified via integration (App.tsx renders it) and the manual smoke test in the verification plan.

- [ ] **Step 1: Create the provider**

Create `frontend/src/telemetry/AppInsightsProvider.tsx`:

```tsx
import React, { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { AppInsightsContext } from '@microsoft/applicationinsights-react-js';
import { reactPlugin, getAppInsights } from './appInsights';

interface AppInsightsProviderProps {
  children: React.ReactNode;
}

export function AppInsightsProvider({ children }: AppInsightsProviderProps) {
  const location = useLocation();

  useEffect(() => {
    getAppInsights()?.trackPageView({ name: location.pathname });
  }, [location.pathname]);

  return (
    <AppInsightsContext.Provider value={reactPlugin}>
      {children}
    </AppInsightsContext.Provider>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/telemetry/AppInsightsProvider.tsx
git commit -m "feat: add AppInsightsProvider with React Router v6 page-view tracking"
```

---

## Task 6: Wire telemetry into App.tsx

**Files:**
- Modify: `frontend/src/App.tsx`

This task has two parts: (a) initialize the AI instance during app startup and wire MSAL identity; (b) wrap `<Routes>` in `<AppInsightsProvider>`.

- [ ] **Step 1: Add imports to App.tsx**

Open `frontend/src/App.tsx`. After the existing imports (around line 51 where `loadConfig, Config` is imported), add:

```typescript
import { initAppInsights, getAppInsights } from './telemetry/appInsights';
import { AppInsightsProvider } from './telemetry/AppInsightsProvider';
import { EventType, AccountInfo } from '@azure/msal-browser';
```

Note: `EventType` and `AccountInfo` may already be imported — check first. Only add what's missing.

- [ ] **Step 2: Initialize AI after loadConfig inside initializeApp**

In the `initializeApp` async function (inside the `useEffect` at line 105), after the line:
```typescript
const appConfig = loadConfig();
setConfig(appConfig);
```
Add:
```typescript
        // Initialize Application Insights (NoOp when connection string is empty)
        initAppInsights(appConfig.aiConnectionString);
```

- [ ] **Step 3: Register MSAL event callbacks for user identification**

After the MSAL `instance` is created (after `const instance = new PublicClientApplication(msalConfig)` at ~line 136), add the event callback registration:

```typescript
        // Wire Application Insights user context to MSAL auth events
        instance.addEventCallback((event) => {
          if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
            const account = (event.payload as { account?: AccountInfo }).account;
            const oid = (account?.idTokenClaims as { oid?: string } | undefined)?.oid;
            if (oid) {
              getAppInsights()?.setAuthenticatedUserContext(oid, undefined, true);
            }
          }
          if (event.eventType === EventType.LOGOUT_SUCCESS) {
            getAppInsights()?.clearAuthenticatedUserContext();
          }
        });

        // For users already signed in (page reload), set context immediately
        const existingAccounts = instance.getAllAccounts();
        if (existingAccounts.length > 0) {
          const oid = (existingAccounts[0].idTokenClaims as { oid?: string } | undefined)?.oid;
          if (oid) {
            getAppInsights()?.setAuthenticatedUserContext(oid, undefined, true);
          }
        }
```

- [ ] **Step 4: Wrap `<Routes>` in `<AppInsightsProvider>`**

In the JSX return (around line 356–432), `<Routes>` is currently inside `<FeatureFlagProvider>`. Wrap just `<Routes>...</Routes>` with `<AppInsightsProvider>`:

```tsx
                  <AuthGuard>
                    <FeatureFlagProvider>
                    <AppInsightsProvider>
                    <Routes>
                      {/* ... all existing routes unchanged ... */}
                    </Routes>
                    </AppInsightsProvider>
                    </FeatureFlagProvider>
                  </AuthGuard>
```

- [ ] **Step 5: Build and verify no TypeScript errors**

```bash
cd frontend && npm run build 2>&1 | tail -30
```

Expected: `Compiled successfully` or `Compiled with warnings` (no errors).

- [ ] **Step 6: Run full test suite to check for regressions**

```bash
cd frontend && npm test -- --watchAll=false 2>&1 | tail -30
```

Expected: all previously passing tests still pass.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "feat: initialize App Insights on startup and wire MSAL user identity"
```

---

## Task 7: Instrument DashboardTile

**Files:**
- Modify: `frontend/src/components/dashboard/DashboardTile.tsx`
- Modify: `frontend/src/components/dashboard/__tests__/DashboardTile.test.tsx`

- [ ] **Step 1: Write the failing test**

Open `frontend/src/components/dashboard/__tests__/DashboardTile.test.tsx`.

Add a mock for `useTelemetry` at the top of the file, after the existing `jest.mock('../tiles', ...)` block:

```typescript
const mockTrackEvent = jest.fn();
jest.mock('../../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));
```

Add a new test case inside the `describe('DashboardTile', ...)` block:

```typescript
  it('calls trackEvent with DashboardTileClicked when tile is clicked', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} />);

    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    tileElement.click();

    expect(mockTrackEvent).toHaveBeenCalledWith('DashboardTileClicked', { tileId: 'test-tile-123' });
  });
```

Also add `afterEach(() => { mockTrackEvent.mockClear(); });` inside the describe block.

- [ ] **Step 2: Run the test — verify the new case fails**

```bash
cd frontend && npm test -- --watchAll=false src/components/dashboard/__tests__/DashboardTile.test.tsx 2>&1 | tail -20
```

Expected: existing tests pass, new `DashboardTileClicked` test fails with "Expected mock function to have been called".

- [ ] **Step 3: Update DashboardTile.tsx to track the click**

Open `frontend/src/components/dashboard/DashboardTile.tsx`. Add the import after the existing imports:

```typescript
import { useTelemetry } from '../../telemetry/useTelemetry';
```

Inside the component, after the `useSortable(...)` call, add:

```typescript
  const { trackEvent } = useTelemetry();
```

In the JSX, add `onClick` to the outermost `div`:

```tsx
    <div
      ref={setNodeRef}
      style={style}
      className={`
        bg-white rounded-lg shadow-sm border border-gray-200
        hover:shadow-md transition-shadow duration-200
        flex flex-col
        ${getSizeClasses()}
        ${className}
      `}
      data-testid={`dashboard-tile-${tile.tileId}`}
      onClick={() => trackEvent('DashboardTileClicked', { tileId: tile.tileId })}
    >
```

- [ ] **Step 4: Run test — verify it passes**

```bash
cd frontend && npm test -- --watchAll=false src/components/dashboard/__tests__/DashboardTile.test.tsx 2>&1 | tail -20
```

Expected: all tests pass including the new one.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/DashboardTile.tsx frontend/src/components/dashboard/__tests__/DashboardTile.test.tsx
git commit -m "feat: track DashboardTileClicked event on tile click"
```

---

## Task 8: Instrument BulkTagDialog

**Files:**
- Modify: `frontend/src/components/marketing/photobank/BulkTagDialog.tsx`

- [ ] **Step 1: Check if a test file already exists for BulkTagDialog**

```bash
ls frontend/src/components/marketing/photobank/__tests__/BulkTagDialog.test.tsx 2>/dev/null && echo EXISTS || echo MISSING
```

- [ ] **Step 2: Create or update the test file**

Create `frontend/src/components/marketing/photobank/__tests__/BulkTagDialog.test.tsx` (or extend it if it exists):

```typescript
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import BulkTagDialog from '../BulkTagDialog';

const mockTrackEvent = jest.fn();
jest.mock('../../../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const mockMutateAsync = jest.fn();
jest.mock('../../../../api/hooks/usePhotobank', () => ({
  useBulkAddPhotoTag: () => ({ mutateAsync: mockMutateAsync, isPending: false }),
}));

const mockShowSuccess = jest.fn();
jest.mock('../../../../contexts/ToastContext', () => ({
  useToast: () => ({ showSuccess: mockShowSuccess }),
}));

const defaultProps = {
  search: '',
  selectedTagNames: [],
  totalMatching: 5,
  existingTags: [],
  onClose: jest.fn(),
};

describe('BulkTagDialog telemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('tracks PhotobankBulkTagApplied with tagCount and photoCount on successful submit', async () => {
    mockMutateAsync.mockResolvedValue({
      success: true,
      tagName: 'summer',
      addedCount: 3,
      alreadyTaggedCount: 0,
    });

    render(<BulkTagDialog {...defaultProps} />);

    const input = screen.getByRole('textbox');
    await userEvent.type(input, 'summer');
    await userEvent.click(screen.getByRole('button', { name: /přidat/i }));

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith(
        'PhotobankBulkTagApplied',
        { tagCount: '1' },
        { photoCount: 3 }
      );
    });
  });

  it('does not track when submit fails', async () => {
    mockMutateAsync.mockRejectedValue(new Error('network error'));

    render(<BulkTagDialog {...defaultProps} />);

    const input = screen.getByRole('textbox');
    await userEvent.type(input, 'summer');
    await userEvent.click(screen.getByRole('button', { name: /přidat/i }));

    await waitFor(() => {
      expect(mockTrackEvent).not.toHaveBeenCalled();
    });
  });
});
```

- [ ] **Step 3: Run test — verify it fails**

```bash
cd frontend && npm test -- --watchAll=false src/components/marketing/photobank/__tests__/BulkTagDialog.test.tsx 2>&1 | tail -20
```

Expected: fails because `trackEvent` is not called yet.

- [ ] **Step 4: Add tracking to BulkTagDialog.tsx**

Open `frontend/src/components/marketing/photobank/BulkTagDialog.tsx`. Add the import after existing imports:

```typescript
import { useTelemetry } from '../../../telemetry/useTelemetry';
```

Inside the component body, add after the `useToast()` call:

```typescript
  const { trackEvent } = useTelemetry();
```

In `handleSubmit`, after the `if (result.success)` branch's `showSuccess(...)` call and **before** `onClose()`, add:

```typescript
        trackEvent(
          'PhotobankBulkTagApplied',
          { tagCount: String(selectedTagNames.length > 0 ? selectedTagNames.length : 1) },
          { photoCount: result.addedCount ?? 0 }
        );
```

The full updated `if (result.success)` block becomes:

```typescript
      if (result.success) {
        showSuccess(
          "Štítek přidán",
          `Přidán štítek "${result.tagName}" k ${result.addedCount} fotkám (${result.alreadyTaggedCount} už ho mělo).`,
        );
        trackEvent(
          'PhotobankBulkTagApplied',
          { tagCount: String(selectedTagNames.length > 0 ? selectedTagNames.length : 1) },
          { photoCount: result.addedCount ?? 0 }
        );
        onClose();
        return;
      }
```

- [ ] **Step 5: Run test — verify it passes**

```bash
cd frontend && npm test -- --watchAll=false src/components/marketing/photobank/__tests__/BulkTagDialog.test.tsx 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/marketing/photobank/BulkTagDialog.tsx frontend/src/components/marketing/photobank/__tests__/BulkTagDialog.test.tsx
git commit -m "feat: track PhotobankBulkTagApplied on successful bulk tag submit"
```

---

## Task 9: Instrument ManufactureOrderDetail (duplicate = create)

**Files:**
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`

Note: In this codebase, manufacture orders are created by ABRA ERP sync or by duplicating an existing order. `handleDuplicate` (line 421) is the "create" action available in the UI — we track it as `ManufactureOrderCreated`.

- [ ] **Step 1: Write the failing test**

Open `frontend/src/components/manufacture/pages/__tests__/ManufactureOrderDetail.autoCalculation.test.tsx`. Check if `useDuplicateManufactureOrder` is already mocked there (it is, at line 44). Add a mock for `useTelemetry` and a test for the duplicate-tracking.

However, this test file is complex. Instead, create a focused test:

Create `frontend/src/components/manufacture/pages/__tests__/ManufactureOrderDetail.telemetry.test.tsx`:

```typescript
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ManufactureOrderDetail from '../ManufactureOrderDetail';

const mockTrackEvent = jest.fn();
jest.mock('../../../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const mockDuplicateMutateAsync = jest.fn();
jest.mock('../../../../api/hooks/useManufactureOrders', () => ({
  useManufactureOrderDetailQuery: () => ({
    data: {
      id: 1,
      orderNumber: 'TEST-001',
      state: 'Draft',
      productCode: 'P001',
      plannedDate: null,
      products: [],
      semiProducts: [],
      notes: [],
      logs: [],
      conditionReadings: [],
    },
    isLoading: false,
    error: null,
  }),
  useUpdateManufactureOrder: () => ({ mutateAsync: jest.fn() }),
  useUpdateManufactureOrderStatus: () => ({ mutateAsync: jest.fn() }),
  useConfirmSemiProductManufacture: () => ({ mutateAsync: jest.fn() }),
  useConfirmProductCompletion: () => ({ mutateAsync: jest.fn() }),
  useDuplicateManufactureOrder: () => ({ mutateAsync: mockDuplicateMutateAsync }),
  useOpenManufactureProtocol: () => ({ mutateAsync: jest.fn() }),
}));

// Mock all child components to avoid deep rendering
jest.mock('../../../../api/hooks/useManufactureOrders');

jest.mock('../../../../features/feature-flags/FeatureFlagProvider', () => ({
  useFeatureFlag: () => false,
}));

describe('ManufactureOrderDetail telemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('tracks ManufactureOrderCreated when duplicate succeeds', async () => {
    mockDuplicateMutateAsync.mockResolvedValue({ id: 99 });

    render(
      <MemoryRouter initialEntries={['/manufacturing/orders/1']}>
        <Routes>
          <Route
            path="/manufacturing/orders/:id"
            element={<ManufactureOrderDetail />}
          />
          <Route
            path="/manufacturing/orders/99"
            element={<div>New order 99</div>}
          />
        </Routes>
      </MemoryRouter>
    );

    // Find and click the duplicate button
    const duplicateButton = await screen.findByRole('button', { name: /duplikovat|duplicate/i });
    await userEvent.click(duplicateButton);

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith('ManufactureOrderCreated', { productCode: 'P001' });
    });
  });
});
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd frontend && npm test -- --watchAll=false "src/components/manufacture/pages/__tests__/ManufactureOrderDetail.telemetry.test.tsx" 2>&1 | tail -30
```

Expected: fails (test can't find duplicate button, or trackEvent not called).

**Note:** This test may be complex to get right given the component's deep dependencies. If it's failing due to rendering issues rather than the tracking logic itself, move on to Step 3 — add the tracking — then revisit the test. The core behaviour (trackEvent called in handleDuplicate) is straightforward to verify manually.

- [ ] **Step 3: Add tracking to ManufactureOrderDetail.tsx**

Open `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`. Add the import after existing imports:

```typescript
import { useTelemetry } from '../../../telemetry/useTelemetry';
```

Inside the component body, add near the top with the other hooks:

```typescript
  const { trackEvent } = useTelemetry();
```

Find the `handleDuplicate` function (line 421). Update the success branch to track the event:

```typescript
  const handleDuplicate = async () => {
    if (!orderId) return;

    try {
      const result = await duplicateOrderMutation.mutateAsync(orderId);
      
      if (result.id) {
        trackEvent('ManufactureOrderCreated', { productCode: order?.productCode ?? '' });

        const newOrderUrl = `/manufacturing/orders/${result.id}`;
        
        if (onEdit && onClose) {
          onEdit(result.id);
        } else {
          navigate(newOrderUrl);
        }
      }
    } catch (error) {
      console.error("Error duplicating order:", error);
    }
  };
```

Note: `order` is the data from `useManufactureOrderDetailQuery`. Check that the variable name matches what's used in the file — look for the destructuring of the query hook result.

- [ ] **Step 4: Build to verify no TypeScript errors**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx frontend/src/components/manufacture/pages/__tests__/ManufactureOrderDetail.telemetry.test.tsx
git commit -m "feat: track ManufactureOrderCreated on order duplication"
```

---

## Task 10: Instrument PurchaseOrderList

**Files:**
- Modify: `frontend/src/components/pages/PurchaseOrderList.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/pages/__tests__/PurchaseOrderList.telemetry.test.tsx`:

```typescript
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import PurchaseOrderList from '../PurchaseOrderList';

const mockTrackEvent = jest.fn();
jest.mock('../../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const mockRefetch = jest.fn();
jest.mock('../../../api/hooks/usePurchaseOrders', () => ({
  usePurchaseOrdersQuery: () => ({
    data: { orders: [], totalCount: 0, totalPages: 0 },
    isLoading: false,
    error: null,
    refetch: mockRefetch,
  }),
}));

// Mock the modals to avoid deep rendering
jest.mock('../PurchaseOrderDetail', () => ({
  __esModule: true,
  default: () => <div data-testid="order-detail" />,
}));
jest.mock('../PurchaseOrderForm', () => ({
  __esModule: true,
  default: ({ onSuccess }: { onSuccess: (id: number) => void }) => (
    <button onClick={() => onSuccess(42)} data-testid="mock-create-form">
      Submit
    </button>
  ),
}));

function renderList() {
  return render(
    <MemoryRouter>
      <PurchaseOrderList />
    </MemoryRouter>
  );
}

describe('PurchaseOrderList telemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('tracks PurchaseOrderSubmitted when a new order is created', async () => {
    renderList();

    // Open create modal
    const createButton = screen.getByRole('button', { name: /nová|new|vytvořit|create/i });
    await userEvent.click(createButton);

    // Trigger onSuccess (simulates form submit)
    const mockForm = screen.getByTestId('mock-create-form');
    await userEvent.click(mockForm);

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith('PurchaseOrderSubmitted', { orderId: '42' });
    });
  });
});
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd frontend && npm test -- --watchAll=false "src/components/pages/__tests__/PurchaseOrderList.telemetry.test.tsx" 2>&1 | tail -30
```

Expected: fails because `trackEvent` is not called yet.

- [ ] **Step 3: Add tracking to PurchaseOrderList.tsx**

Open `frontend/src/components/pages/PurchaseOrderList.tsx`. Add the import after existing imports:

```typescript
import { useTelemetry } from '../../telemetry/useTelemetry';
```

Inside the component body, add after the existing hooks:

```typescript
  const { trackEvent } = useTelemetry();
```

Find `handleCreateSuccess` (line 201):

```typescript
  const handleCreateSuccess = (orderId: number) => {
    // Refresh the list
    refetch();
    // Optionally open the detail of the newly created order
    console.log("Order created successfully:", orderId);
  };
```

Update it:

```typescript
  const handleCreateSuccess = (orderId: number) => {
    trackEvent('PurchaseOrderSubmitted', { orderId: String(orderId) });
    refetch();
  };
```

- [ ] **Step 4: Run test — verify it passes**

```bash
cd frontend && npm test -- --watchAll=false "src/components/pages/__tests__/PurchaseOrderList.telemetry.test.tsx" 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/PurchaseOrderList.tsx frontend/src/components/pages/__tests__/PurchaseOrderList.telemetry.test.tsx
git commit -m "feat: track PurchaseOrderSubmitted when a new purchase order is created"
```

---

## Task 11: Instrument FeatureFlagsAdminPage

**Files:**
- Modify: `frontend/src/pages/FeatureFlagsAdminPage.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/__tests__/FeatureFlagsAdminPage.telemetry.test.tsx`:

```typescript
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FeatureFlagsAdminPage from '../FeatureFlagsAdminPage';

const mockTrackEvent = jest.fn();
jest.mock('../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const mockUpsertMutate = jest.fn();
jest.mock('../../api/hooks/useFeatureFlagsAdmin', () => ({
  useFeatureFlagsAdmin: () => ({
    data: [
      {
        key: 'show-new-dashboard',
        description: 'Show the new dashboard layout',
        currentValue: false,
        defaultValue: false,
        isOverridden: false,
        updatedBy: null,
        updatedAt: null,
      },
    ],
    isLoading: false,
    error: null,
  }),
  useUpsertFlagOverride: () => ({ mutate: mockUpsertMutate, isPending: false }),
  useClearFlagOverride: () => ({ mutate: jest.fn(), isPending: false }),
}));

describe('FeatureFlagsAdminPage telemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('tracks FeatureFlagToggled with flagKey and enabled when toggle is clicked', async () => {
    render(<FeatureFlagsAdminPage />);

    const toggle = screen.getByRole('button', { name: /toggle show-new-dashboard/i });
    await userEvent.click(toggle);

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith('FeatureFlagToggled', {
        flagKey: 'show-new-dashboard',
        enabled: 'true',
      });
    });
  });
});
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd frontend && npm test -- --watchAll=false "src/pages/__tests__/FeatureFlagsAdminPage.telemetry.test.tsx" 2>&1 | tail -30
```

Expected: fails because `trackEvent` is not called yet.

- [ ] **Step 3: Add tracking to FeatureFlagsAdminPage.tsx**

Open `frontend/src/pages/FeatureFlagsAdminPage.tsx`. Add the import after existing imports:

```typescript
import { useTelemetry } from '../telemetry/useTelemetry';
```

Inside the component body, add after the existing hooks:

```typescript
  const { trackEvent } = useTelemetry();
```

Find the toggle button's `onClick` handler (around line 54–56):

```typescript
                onClick={() =>
                  upsert.mutate({ key: flag.key!, isEnabled: !flag.currentValue })
                }
```

Update it to also call `trackEvent`:

```typescript
                onClick={() => {
                  const newEnabled = !flag.currentValue;
                  trackEvent('FeatureFlagToggled', {
                    flagKey: flag.key ?? '',
                    enabled: String(newEnabled),
                  });
                  upsert.mutate({ key: flag.key!, isEnabled: newEnabled });
                }}
```

- [ ] **Step 4: Run test — verify it passes**

```bash
cd frontend && npm test -- --watchAll=false "src/pages/__tests__/FeatureFlagsAdminPage.telemetry.test.tsx" 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 5: Run the full test suite**

```bash
cd frontend && npm test -- --watchAll=false 2>&1 | tail -30
```

Expected: all tests pass (no regressions).

- [ ] **Step 6: Run lint and build**

```bash
cd frontend && npm run lint 2>&1 | tail -20 && npm run build 2>&1 | tail -20
```

Expected: no errors in lint or build.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/FeatureFlagsAdminPage.tsx frontend/src/pages/__tests__/FeatureFlagsAdminPage.telemetry.test.tsx
git commit -m "feat: track FeatureFlagToggled with flagKey and enabled on admin toggle"
```

---

## Task 12: Update documentation

**Files:**
- Modify: `docs/architecture/observability.md`
- Create: `docs/features/usage-analytics.md`
- Modify: `docs/development/setup.md`

- [ ] **Step 1: Update observability.md Frontend Monitoring row**

Open `docs/architecture/observability.md`. Find the table row:
```
| **Frontend Monitoring** | React error tracking | ⏳ Planned |
```
Replace it with:
```
| **Frontend Monitoring** | React analytics via Application Insights JS SDK | ✅ Implemented |
```

Also find the `### Frontend Setup (Planned)` heading (around line 424) and change it to:
```
### Frontend Setup (✅ Implemented)
```

And add a link after the heading:
```markdown
See [docs/features/usage-analytics.md](../features/usage-analytics.md) for the full event catalogue and KQL starter queries.
```

- [ ] **Step 2: Create usage-analytics.md**

Create `docs/features/usage-analytics.md` with the following content:

````markdown
# Frontend Usage Analytics

## Purpose

We track which parts of the application users actually use — from the user's perspective, not the API's. This answers questions like: which features are used, how often, by whom, and how intensively. Telemetry runs through the same Azure Application Insights resource as the backend, so frontend events correlate with backend traces.

## Architecture

```
React component
  → useTelemetry() hook
    → ApplicationInsights JS SDK
      → Azure Application Insights resource (shared with backend)
```

All auto-tracked telemetry (page views, Core Web Vitals, AJAX dependencies, unhandled exceptions) goes to the same resource. Custom events use `trackEvent` calls defined in this document.

## Identity model

Users are identified by their Entra ID `oid` claim — an opaque GUID, never a name or email address. Identification is set via `setAuthenticatedUserContext(oid)` on MSAL `LOGIN_SUCCESS` and cleared on `LOGOUT_SUCCESS`. On page reload (already-signed-in), the context is set during app init.

No PII (name, email, UPN) is ever included in event properties.

## Auto-tracked telemetry

The `reactPlugin` from `@microsoft/applicationinsights-react-js` and the SDK auto-track:

| Signal | What is captured |
|---|---|
| Page views | Route pathname on every `useLocation()` change |
| AJAX dependencies | Fetch calls — URL, duration, status code |
| Unhandled exceptions | JS errors with stack traces |
| Core Web Vitals | FCP, LCP, CLS (auto-tracked by the SDK) |

## Event catalogue

Every PR that adds a `trackEvent` call **must** add a row to this table in the same PR (enforced by reviewer).

| Event Name | Trigger (file path) | Properties | Metrics | Why we track it |
|---|---|---|---|---|
| `DashboardTileClicked` | `components/dashboard/DashboardTile.tsx` | `tileId: string` | — | Which dashboard widgets do users open from the grid? |
| `PhotobankBulkTagApplied` | `components/marketing/photobank/BulkTagDialog.tsx` | `tagCount: string` | `photoCount: number` | Adoption of the bulk-tag workflow vs. single-photo tagging. |
| `ManufactureOrderCreated` | `components/manufacture/pages/ManufactureOrderDetail.tsx` | `productCode: string` | — | Core workflow completion rate (triggered on order duplication). |
| `PurchaseOrderSubmitted` | `components/pages/PurchaseOrderList.tsx` | `orderId: string` | — | Purchase pipeline volume. |
| `FeatureFlagToggled` | `pages/FeatureFlagsAdminPage.tsx` | `flagKey: string`, `enabled: string` | — | Audit trail for admin flag changes. |

## Naming conventions

- Event names: PascalCase, mirrors backend `TelemetryService.TrackBusinessEvent` convention.
- Property keys: camelCase strings.
- No PII in properties: never include user names, emails, free-text input, or token claims.
- Metrics (second argument to `trackMetric` parameter in `trackEvent`): numeric values only.

## How to query

### Feature usage in last 30 days

```kusto
customEvents
| where timestamp > ago(30d)
| summarize count() by name
| order by count_ desc
```

### Unique users per feature

```kusto
customEvents
| where timestamp > ago(30d)
| summarize dcount(user_AuthenticatedId) by name
| order by dcount_user_AuthenticatedId desc
```

### Funnel: users who visited Dashboard but never opened Photobank

```kusto
let dashboard_users = customEvents
    | where timestamp > ago(30d) and name == "DashboardTileClicked"
    | distinct user_AuthenticatedId;
let photobank_users = pageViews
    | where timestamp > ago(30d) and name contains "/marketing/photobank"
    | distinct user_AuthenticatedId;
dashboard_users
| where user_AuthenticatedId !in (photobank_users)
| summarize count()
```

## How to add a new event

1. Pick a `TelemetryEventName` that doesn't exist yet (PascalCase, descriptive).
2. Add the literal to `frontend/src/telemetry/events.ts`.
3. Call `useTelemetry().trackEvent(name, properties?, metrics?)` at the interaction point.
4. **Add a row to the catalogue table above in the same PR.**
5. Write a unit test verifying the call (mock `useTelemetry` and assert `trackEvent` was called with correct args).

## Future improvements (backlog)

- Build a "Feature usage" Azure Workbook with a bar chart (events by name) and a table (unique users by feature).
- Add `time-on-screen` tracking once there is a clear question to answer with it.
- Add session-level engagement score (events per session, sessions per user per week).
- Consider PostHog for funnels and retention analysis without writing KQL.
- Wire up cookie consent banner if the privacy policy requires it.
- Export raw events to blob storage after 30 days for long-term analysis.

## Out of scope

Session replay, heatmaps, A/B testing infrastructure (A/B testing is handled by OpenFeature today).
````

- [ ] **Step 3: Update setup.md to document the new env var**

Open `docs/development/setup.md`. Find the section describing environment variables (look for `REACT_APP_AZURE_CLIENT_ID` or similar). Add a note about the new var:

```markdown
### Application Insights (optional)

Set `REACT_APP_AI_CONNECTION_STRING` in `frontend/.env` to the connection string from the Azure Application Insights resource. Leave it empty (the default) for local development — the SDK runs in NoOp mode and no telemetry is sent.
```

- [ ] **Step 4: Commit all documentation changes**

```bash
git add docs/architecture/observability.md docs/features/usage-analytics.md docs/development/setup.md
git commit -m "docs: add usage-analytics feature doc and update observability status to implemented"
```

---

## Self-review: spec coverage check

| Spec requirement | Covered by task |
|---|---|
| Install `@microsoft/applicationinsights-web` + `@microsoft/applicationinsights-react-js` | Task 1 |
| NoOp when connection string is empty | Task 2 (tests + implementation) |
| Singleton AI instance with `ReactPlugin` | Task 2 |
| `TelemetryEventName` union type | Task 3 |
| `useTelemetry()` hook | Task 4 |
| `AppInsightsProvider` wrapping `<Routes>` | Task 5 + Task 6 |
| Page-view tracking on route change | Task 5 (useLocation) |
| MSAL login/logout → `setAuthenticatedUserContext(oid)` | Task 6 |
| Already-signed-in users → set context on init | Task 6 |
| `REACT_APP_AI_CONNECTION_STRING` env var | Task 1 |
| `aiConnectionString` in `Config` + log `[SET]/[NOT SET]` | Task 1 |
| `DashboardTileClicked` event | Task 7 |
| `PhotobankBulkTagApplied` event | Task 8 |
| `ManufactureOrderCreated` event | Task 9 |
| `PurchaseOrderSubmitted` event | Task 10 |
| `FeatureFlagToggled` event | Task 11 |
| `docs/features/usage-analytics.md` with full event catalogue | Task 12 |
| Observability doc updated | Task 12 |
| Setup doc updated | Task 12 |
| No PII in events (only Entra `oid`) | Task 6 (oid only, not name/email) |
| Unit tests for NoOp behaviour | Task 2 |
| Unit tests for `useTelemetry` | Task 4 |
| Build + lint green | Verified in Task 11 |

All spec requirements covered.
