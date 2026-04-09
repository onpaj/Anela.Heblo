# Fix: Nested Frontend Routes Break on Refresh — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every frontend route reloadable by direct URL (e.g. `/manufacturing/stock-analysis`) so a browser refresh does not produce a blank screen.

**Architecture:** The root cause is `"homepage": "."` in `frontend/package.json`, which causes Create React App to emit relative asset paths (`./static/js/main.<hash>.js`). When the browser is on a nested URL like `/manufacturing/stock-analysis`, that relative path resolves to `/manufacturing/static/js/...`, which 404s. The backend SPA fallback (`UseSpa`) returns `index.html` for the missing asset instead of a 404, so the browser receives HTML where it expected JavaScript and renders a blank screen. Changing `homepage` to `"/"` makes CRA emit absolute paths, resolving correctly from any route depth.

**Tech Stack:** Create React App (CRA), Node/npm, React Router (no changes needed), .NET 8 `UseSpa` middleware (no changes needed).

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `frontend/package.json` | **Modify line 84** | Change `"homepage": "."` → `"homepage": "/"` |

No other files need to change. The fix flows through the build: CRA reads `homepage`, substitutes `%PUBLIC_URL%` in `public/index.html`, and writes absolute paths to `build/index.html`. The backend and Docker image are unchanged.

---

### Task 1: Verify the broken state (before fix)

**Files:**
- Read: `frontend/package.json:84`
- Read: `frontend/build/index.html` (if a build exists locally)

- [ ] **Step 1: Confirm current setting**

  Open `frontend/package.json` and verify line 84 reads:
  ```json
  "homepage": "."
  ```

- [ ] **Step 2: Optionally inspect an existing build output (if build/ exists)**

  ```bash
  grep -o 'src="[^"]*static[^"]*"' frontend/build/index.html | head -5
  ```
  Expected (broken): paths starting with `./static/...`
  ```
  src="./static/js/main.<hash>.js"
  ```
  If `build/` doesn't exist, skip this step — you'll verify after the build in Task 3.

---

### Task 2: Apply the fix

**Files:**
- Modify: `frontend/package.json:84`

- [ ] **Step 1: Change the homepage value**

  In `frontend/package.json`, change line 84 from:
  ```json
  "homepage": "."
  ```
  to:
  ```json
  "homepage": "/"
  ```

  Full context around that line (so you have enough to make the edit precisely):
  ```json
    "lint-staged": {
      "*.{ts,tsx}": [
        "eslint --fix",
        "git add"
      ]
    },
    "homepage": "/"
  }
  ```

---

### Task 3: Build and verify the fix

**Files:**
- Read: `frontend/build/index.html` (after build)

- [ ] **Step 1: Run the frontend build**

  ```bash
  cd frontend && npm run build
  ```
  Expected: build completes with no errors. Output ends with something like:
  ```
  The build folder is ready to be deployed.
  ```

- [ ] **Step 2: Inspect generated index.html**

  ```bash
  grep -o 'src="[^"]*static[^"]*"' frontend/build/index.html | head -5
  ```
  Expected (fixed): absolute paths starting with `/static/...`
  ```
  src="/static/js/main.<hash>.js"
  ```
  If you still see `./static/`, the homepage value was not saved correctly — go back to Task 2.

- [ ] **Step 3: Check %PUBLIC_URL% expansions (favicon, manifest, icons)**

  ```bash
  grep -E 'href="|src="' frontend/build/index.html
  ```
  Expected: all asset references are either absolute (`/static/...`, `/favicon.ico`, `/manifest.json`, `/logo192.png`) or external. None should start with `./`.

- [ ] **Step 4: Commit**

  ```bash
  git add frontend/package.json
  git commit -m "fix(frontend): use absolute asset paths so nested routes survive browser refresh

  CRA homepage: \".\" emits relative ./static/... paths which resolve
  incorrectly on nested URLs (/manufacturing/stock-analysis etc.),
  causing a blank screen on hard refresh. Switching to \"/\" makes all
  asset paths absolute and route-depth-independent."
  ```

---

### Task 4: Smoke-test against staging or local backend (manual)

No code to write — this is a QA gate.

- [ ] **Step 1: Deploy build to a running .NET backend**

  Either:
  - Copy `frontend/build/` into `backend/src/Anela.Heblo.API/wwwroot/` and run the backend with `ASPNETCORE_ENVIRONMENT=Production` (so `UseSpa` fallback is active), or
  - Build and run the Docker image locally.

  ```bash
  # Simplest local option — copy build output into wwwroot
  cp -r frontend/build/. backend/src/Anela.Heblo.API/wwwroot/
  cd backend
  ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Anela.Heblo.API
  ```

- [ ] **Step 2: Test direct URL load on a previously-broken nested route**

  Open a new browser tab (not from the app's own navigation) and go to:
  ```
  http://localhost:5001/manufacturing/stock-analysis
  ```
  Expected: page renders (may require login redirect, which is fine — you should reach the authenticated page after login, not a blank screen).

- [ ] **Step 3: Test browser refresh on the nested route**

  With the page loaded, hit **F5** or **Cmd+R**.
  Expected: page reloads and renders correctly. **No blank screen.**

- [ ] **Step 4: Spot-check two more nested routes**

  Repeat the direct-load + refresh test for:
  - `http://localhost:5001/manufacturing/batch-planning`
  - `http://localhost:5001/purchase/orders`

  Expected: both render on direct load and survive refresh.

- [ ] **Step 5: Regression check on single-segment routes**

  - `http://localhost:5001/catalog` — must still render on direct load and refresh.
  - `http://localhost:5001/` — must still render.

  Expected: both work (they worked before; confirm nothing regressed).

---

## Self-Review Checklist

- [x] **Spec coverage:** The spec asks for all screens to work on URL-based access the same way `/catalog` does. Task 2 makes the change; Task 3 verifies the build output; Task 4 verifies end-to-end behavior. All requirements covered.
- [x] **Placeholder scan:** No TBDs, no "add appropriate handling" vagueness, no steps without concrete content.
- [x] **Type consistency:** No types/functions involved — pure config change + build verification commands.
- [x] **Risk covered:** The `%PUBLIC_URL%` expansion check in Task 3 Step 3 ensures favicon/manifest/icons aren't broken by the change.
