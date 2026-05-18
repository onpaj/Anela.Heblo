# Balení Device Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `/baleni` PWA route for a landscape touch PC packing station, with a home screen showing 3 dummy tiles (Balení, Zásilky, Statistiky) each leading to a "Coming soon" placeholder — no backend work.

**Architecture:** Mirror the existing `terminal` device module — a standalone React route group under `BaleniLayout` with its own PWA manifest (`manifest.baleni.json`). The manifest is swapped on mount via `useEffect` and restored on unmount. All components live in `frontend/src/components/baleni/`. The key landscape difference from terminal: `max-w-5xl` content container (vs `max-w-md`) and a 3-column tile grid.

**Tech Stack:** React 18, React Router v6, Tailwind CSS (custom Heblo tokens), lucide-react icons, Jest + React Testing Library (via react-scripts)

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `memory/patterns/baleni-module-touch-design.md` | Create | Project memory — binding touch/landscape rule for this module |
| `frontend/public/manifest.baleni.json` | Create | PWA manifest for `/baleni` scope with `orientation: landscape` |
| `frontend/public/index.html` | Modify | Add `else if` branch to inline script that switches the manifest on hard load |
| `frontend/src/components/baleni/BaleniPlaceholder.tsx` | Create | Reusable "Coming soon" card: accepts `title` prop, shows "Brzy k dispozici" |
| `frontend/src/components/baleni/__tests__/BaleniPlaceholder.test.tsx` | Create | Tests title and message render correctly |
| `frontend/src/components/baleni/BaleniHome.tsx` | Create | Home screen: "Vyberte operaci" heading + 3-column grid of 3 touch tiles |
| `frontend/src/components/baleni/__tests__/BaleniHome.test.tsx` | Create | Tests 3 tiles render with correct labels and hrefs |
| `frontend/src/components/baleni/BaleniLayout.tsx` | Create | Route layout: sticky h-14 header, manifest swap, `max-w-5xl` content area, `<Outlet />` |
| `frontend/src/components/baleni/__tests__/BaleniLayout.test.tsx` | Create | Tests header title, back button visibility, manifest swap on mount/unmount |
| `frontend/src/App.tsx` | Modify | Add 3 baleni imports + `/baleni` route group after the terminal group |

---

### Task 1: Static assets and project memory

**Files:**
- Create: `memory/patterns/baleni-module-touch-design.md`
- Create: `frontend/public/manifest.baleni.json`

- [ ] **Step 1: Create memory file**

Save the following to `memory/patterns/baleni-module-touch-design.md`:

```markdown
---
name: baleni-module-touch-design
description: Balení module is touch-first and landscape-oriented — binding UI constraints for all work in frontend/src/components/baleni/
metadata:
  type: project
---

All UI in `frontend/src/components/baleni/` must be:

- **Touch-first**: interactive elements ≥44px, tiles ≥160px tall, no hover-only affordances
- **Landscape-oriented**: wide containers (`max-w-5xl`), multi-column grids (`grid-cols-3`), landscape PWA manifest (`orientation: landscape`)
- **PWA scope**: `/baleni` served by `manifest.baleni.json`

**Why:** This module targets a landscape touch PC at the packing station, not a portrait handheld like the terminal module.

**How to apply:** Every component added under this path must honor these constraints. Never apply portrait/narrow layouts or small touch targets here.
```

- [ ] **Step 2: Create PWA manifest**

Save the following to `frontend/public/manifest.baleni.json`:

```json
{
  "name": "Heblo Balení",
  "short_name": "Balení",
  "description": "Balicí stanice pro expedici zásilek",
  "icons": [
    {
      "src": "favicon.ico",
      "sizes": "64x64 32x32 24x24 16x16",
      "type": "image/x-icon"
    },
    {
      "src": "logo192.png",
      "type": "image/png",
      "sizes": "192x192"
    },
    {
      "src": "logo512.png",
      "type": "image/png",
      "sizes": "512x512"
    }
  ],
  "id": "/baleni",
  "start_url": "/baleni",
  "scope": "/baleni",
  "display": "standalone",
  "orientation": "landscape",
  "theme_color": "#2563EB",
  "background_color": "#F8FAFC"
}
```

- [ ] **Step 3: Commit**

```bash
git add memory/patterns/baleni-module-touch-design.md frontend/public/manifest.baleni.json
git commit -m "feat(baleni): add touch-landscape memory rule and PWA manifest"
```

---

### Task 2: BaleniPlaceholder component (TDD)

**Files:**
- Create: `frontend/src/components/baleni/__tests__/BaleniPlaceholder.test.tsx`
- Create: `frontend/src/components/baleni/BaleniPlaceholder.tsx`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/baleni/__tests__/BaleniPlaceholder.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import BaleniPlaceholder from '../BaleniPlaceholder';

describe('BaleniPlaceholder', () => {
  it('renders the passed title', () => {
    render(<BaleniPlaceholder title="Balení" />);
    expect(screen.getByText('Balení')).toBeInTheDocument();
  });

  it('renders "Brzy k dispozici" message', () => {
    render(<BaleniPlaceholder title="Zásilky" />);
    expect(screen.getByText('Brzy k dispozici')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to confirm it fails**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="BaleniPlaceholder"
```

Expected: FAIL — "Cannot find module '../BaleniPlaceholder'"

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/baleni/BaleniPlaceholder.tsx`:

```tsx
import React from 'react';
import { Wrench } from 'lucide-react';

interface BaleniPlaceholderProps {
  title: string;
}

const BaleniPlaceholder: React.FC<BaleniPlaceholderProps> = ({ title }) => (
  <div
    className="flex flex-col items-center justify-center py-20 text-center"
    data-testid="baleni-placeholder"
  >
    <div className="w-16 h-16 bg-secondary-blue-pale rounded-full flex items-center justify-center mb-4">
      <Wrench className="h-8 w-8 text-primary-blue" />
    </div>
    <h2 className="text-xl font-bold text-neutral-slate mb-2">{title}</h2>
    <p className="text-sm text-neutral-gray">Brzy k dispozici</p>
  </div>
);

export default BaleniPlaceholder;
```

- [ ] **Step 4: Run to confirm it passes**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="BaleniPlaceholder"
```

Expected: PASS — 2 tests pass

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/BaleniPlaceholder.tsx \
        frontend/src/components/baleni/__tests__/BaleniPlaceholder.test.tsx
git commit -m "feat(baleni): BaleniPlaceholder coming-soon stub"
```

---

### Task 3: BaleniHome component (TDD)

**Files:**
- Create: `frontend/src/components/baleni/__tests__/BaleniHome.test.tsx`
- Create: `frontend/src/components/baleni/BaleniHome.tsx`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/baleni/__tests__/BaleniHome.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import BaleniHome from '../BaleniHome';

const renderHome = () =>
  render(
    <MemoryRouter>
      <BaleniHome />
    </MemoryRouter>,
  );

describe('BaleniHome', () => {
  it('renders heading', () => {
    renderHome();
    expect(screen.getByText('Vyberte operaci')).toBeInTheDocument();
  });

  it('renders Balení tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-baleni');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/baleni/baleni');
  });

  it('renders Zásilky tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-zasilky');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/baleni/zasilky');
  });

  it('renders Statistiky tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-statistiky');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/baleni/statistiky');
  });

  it('renders exactly 3 tiles', () => {
    renderHome();
    expect(screen.getAllByTestId(/^baleni-tile-/)).toHaveLength(3);
  });
});
```

- [ ] **Step 2: Run to confirm it fails**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="BaleniHome.test"
```

Expected: FAIL — "Cannot find module '../BaleniHome'"

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/baleni/BaleniHome.tsx`:

```tsx
import React from 'react';
import { Link } from 'react-router-dom';
import { Package, Truck, BarChart3 } from 'lucide-react';

interface BaleniTile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
}

const TILES: BaleniTile[] = [
  {
    id: 'baleni',
    title: 'Balení',
    description: 'Zabalení zásilky pro odeslání',
    href: '/baleni/baleni',
    icon: Package,
  },
  {
    id: 'zasilky',
    title: 'Zásilky',
    description: 'Přehled zásilek a jejich stav',
    href: '/baleni/zasilky',
    icon: Truck,
  },
  {
    id: 'statistiky',
    title: 'Statistiky',
    description: 'Statistiky balicí stanice',
    href: '/baleni/statistiky',
    icon: BarChart3,
  },
];

const BaleniHome: React.FC = () => (
  <div className="pt-4">
    <h1 className="text-2xl font-bold text-neutral-slate mb-6">Vyberte operaci</h1>
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
      {TILES.map(({ id, title, description, href, icon: Icon }) => (
        <Link
          key={id}
          to={href}
          data-testid={`baleni-tile-${id}`}
          className="flex flex-col items-center justify-center gap-4 bg-white border border-border-light rounded-xl p-6 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[160px]"
        >
          <div className="w-16 h-16 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
            <Icon className="h-8 w-8 text-primary-blue" />
          </div>
          <div className="text-center">
            <p className="text-lg font-semibold text-neutral-slate">{title}</p>
            <p className="text-sm text-neutral-gray mt-1">{description}</p>
          </div>
        </Link>
      ))}
    </div>
  </div>
);

export default BaleniHome;
```

- [ ] **Step 4: Run to confirm it passes**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="BaleniHome.test"
```

Expected: PASS — 5 tests pass

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/BaleniHome.tsx \
        frontend/src/components/baleni/__tests__/BaleniHome.test.tsx
git commit -m "feat(baleni): BaleniHome 3-tile landscape grid"
```

---

### Task 4: BaleniLayout component (TDD)

**Files:**
- Create: `frontend/src/components/baleni/__tests__/BaleniLayout.test.tsx`
- Create: `frontend/src/components/baleni/BaleniLayout.tsx`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/baleni/__tests__/BaleniLayout.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import BaleniLayout from '../BaleniLayout';

jest.mock('../../auth/UserProfile', () => ({
  __esModule: true,
  default: () => <div data-testid="user-profile" />,
}));

const renderWithRouter = (initialPath: string) =>
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/baleni/*" element={<BaleniLayout />}>
          <Route index element={<div>Home content</div>} />
          <Route path="baleni" element={<div>Balení content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );

// The manifest <link> lives in document.head, outside the render tree, so
// Testing Library queries cannot reach it — direct DOM access is required here.
const getManifestHref = () =>
  // eslint-disable-next-line testing-library/no-node-access
  document.head.querySelector('link[rel="manifest"]')?.getAttribute('href');

describe('BaleniLayout', () => {
  beforeEach(() => {
    // eslint-disable-next-line testing-library/no-node-access
    document.head.querySelectorAll('link[rel="manifest"]').forEach((link) => link.remove());
    const link = document.createElement('link');
    link.setAttribute('rel', 'manifest');
    link.setAttribute('href', '/manifest.json');
    document.head.appendChild(link);
  });

  it('renders the app title', () => {
    renderWithRouter('/baleni');
    expect(screen.getByText('Heblo Balení')).toBeInTheDocument();
  });

  it('hides back button on /baleni (home)', () => {
    renderWithRouter('/baleni');
    expect(screen.queryByRole('button', { name: /zpět/i })).not.toBeInTheDocument();
  });

  it('shows back button on sub-routes', () => {
    renderWithRouter('/baleni/baleni');
    expect(screen.getByRole('button', { name: /zpět/i })).toBeInTheDocument();
  });

  it('renders child route content via Outlet', () => {
    renderWithRouter('/baleni/baleni');
    expect(screen.getByText('Balení content')).toBeInTheDocument();
  });

  it('renders user profile', () => {
    renderWithRouter('/baleni');
    expect(screen.getByTestId('user-profile')).toBeInTheDocument();
  });

  it('links the baleni manifest while mounted', () => {
    renderWithRouter('/baleni');
    expect(getManifestHref()).toBe('/manifest.baleni.json');
  });

  it('restores the main manifest on unmount', () => {
    const { unmount } = renderWithRouter('/baleni');
    expect(getManifestHref()).toBe('/manifest.baleni.json');

    unmount();
    expect(getManifestHref()).toBe('/manifest.json');
  });
});
```

- [ ] **Step 2: Run to confirm it fails**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="BaleniLayout"
```

Expected: FAIL — "Cannot find module '../BaleniLayout'"

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/baleni/BaleniLayout.tsx`:

```tsx
import React, { useEffect } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import UserProfile from '../auth/UserProfile';

const BALENI_ROOT = '/baleni';

const BaleniLayout: React.FC = () => {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const isHome = pathname === BALENI_ROOT || pathname === `${BALENI_ROOT}/`;

  useEffect(() => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    link?.setAttribute('href', '/manifest.baleni.json');
    return () => {
      link?.setAttribute('href', '/manifest.json');
    };
  }, []);

  return (
    <div className="min-h-screen flex flex-col bg-background-gray">
      <header className="h-14 sticky top-0 z-10 bg-white border-b border-border-light flex items-center px-4 gap-3">
        {!isHome && (
          <button
            onClick={() => navigate(BALENI_ROOT)}
            aria-label="Zpět"
            className="p-2 -ml-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale transition-colors"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <span className="flex-1 text-base font-semibold text-neutral-slate select-none">
          Heblo Balení
        </span>
        <UserProfile compact={true} />
      </header>

      <main className="flex-1 overflow-y-auto p-4">
        <div className="max-w-5xl mx-auto w-full">
          <Outlet />
        </div>
      </main>
    </div>
  );
};

export default BaleniLayout;
```

- [ ] **Step 4: Run to confirm it passes**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="BaleniLayout"
```

Expected: PASS — 7 tests pass

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/BaleniLayout.tsx \
        frontend/src/components/baleni/__tests__/BaleniLayout.test.tsx
git commit -m "feat(baleni): BaleniLayout header, manifest swap, landscape container"
```

---

### Task 5: Wire routes and static manifest switch

**Files:**
- Modify: `frontend/public/index.html` (lines 14–23 — the inline manifest-switch script)
- Modify: `frontend/src/App.tsx` (lines 70–75 — add 3 imports; lines 351–361 — add route group after terminal)

- [ ] **Step 1: Extend the manifest-switch script in index.html**

In `frontend/public/index.html`, replace:

```html
    <script>
      if (
        location.pathname === '/terminal' ||
        location.pathname.startsWith('/terminal/')
      ) {
        document
          .querySelector('link[rel="manifest"]')
          .setAttribute('href', '/manifest.terminal.json');
      }
    </script>
```

With:

```html
    <script>
      var p = location.pathname;
      if (p === '/terminal' || p.startsWith('/terminal/')) {
        document.querySelector('link[rel="manifest"]').setAttribute('href', '/manifest.terminal.json');
      } else if (p === '/baleni' || p.startsWith('/baleni/')) {
        document.querySelector('link[rel="manifest"]').setAttribute('href', '/manifest.baleni.json');
      }
    </script>
```

- [ ] **Step 2: Add imports to App.tsx**

In `frontend/src/App.tsx`, replace:

```tsx
import TerminalLayout from "./components/terminal/TerminalLayout";
import TerminalHome from "./components/terminal/TerminalHome";
import TransportBoxCheck from "./components/terminal/TransportBoxCheck";
import TransportBoxReceive from "./components/terminal/TransportBoxReceive";
import ComingSoonPage from "./components/terminal/ComingSoonPage";
import BoxFillWorkflow from "./components/terminal/box-fill/BoxFillWorkflow";
```

With:

```tsx
import TerminalLayout from "./components/terminal/TerminalLayout";
import TerminalHome from "./components/terminal/TerminalHome";
import TransportBoxCheck from "./components/terminal/TransportBoxCheck";
import TransportBoxReceive from "./components/terminal/TransportBoxReceive";
import ComingSoonPage from "./components/terminal/ComingSoonPage";
import BoxFillWorkflow from "./components/terminal/box-fill/BoxFillWorkflow";
import BaleniLayout from "./components/baleni/BaleniLayout";
import BaleniHome from "./components/baleni/BaleniHome";
import BaleniPlaceholder from "./components/baleni/BaleniPlaceholder";
```

- [ ] **Step 3: Add route group to App.tsx**

In `frontend/src/App.tsx`, replace the terminal route group:

```tsx
                      {/* Mobile terminal — no sidebar, no topbar */}
                      <Route path="/terminal" element={<TerminalLayout />}>
                        <Route index element={<TerminalHome />} />
                        <Route path="box-check" element={<TransportBoxCheck />} />
                        <Route path="box-fill" element={<BoxFillWorkflow />} />
                        <Route path="receive" element={<TransportBoxReceive />} />
                        <Route path="stocktake" element={<ComingSoonPage title="Inventura" />} />
                        <Route
                          path="lot-identification"
                          element={<ComingSoonPage title="Identifikace šarže" />}
                        />
                      </Route>
```

With:

```tsx
                      {/* Mobile terminal — no sidebar, no topbar */}
                      <Route path="/terminal" element={<TerminalLayout />}>
                        <Route index element={<TerminalHome />} />
                        <Route path="box-check" element={<TransportBoxCheck />} />
                        <Route path="box-fill" element={<BoxFillWorkflow />} />
                        <Route path="receive" element={<TransportBoxReceive />} />
                        <Route path="stocktake" element={<ComingSoonPage title="Inventura" />} />
                        <Route
                          path="lot-identification"
                          element={<ComingSoonPage title="Identifikace šarže" />}
                        />
                      </Route>

                      {/* Balení device module — landscape touch PC, no sidebar */}
                      <Route path="/baleni" element={<BaleniLayout />}>
                        <Route index element={<BaleniHome />} />
                        <Route path="baleni" element={<BaleniPlaceholder title="Balení" />} />
                        <Route path="zasilky" element={<BaleniPlaceholder title="Zásilky" />} />
                        <Route path="statistiky" element={<BaleniPlaceholder title="Statistiky" />} />
                      </Route>
```

- [ ] **Step 4: Run all baleni tests**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="components/baleni"
```

Expected: PASS — 14 tests pass (2 + 5 + 7)

- [ ] **Step 5: Build and lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: both commands exit with code 0, no errors

- [ ] **Step 6: Commit**

```bash
git add frontend/public/index.html frontend/src/App.tsx
git commit -m "feat(baleni): register /baleni routes and static manifest switch"
```
