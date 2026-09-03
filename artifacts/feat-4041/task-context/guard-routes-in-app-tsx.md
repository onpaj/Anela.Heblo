### task: guard-routes-in-app-tsx

**Files:**
- Modify: `frontend/src/App.tsx:415` (the `/finance/bank-statements` route)
- Modify: `frontend/src/App.tsx:445` (the `/automation/invoice-import-statistics` route)

This task depends on `regenerate-access-matrix-artifacts` being committed first (the `ACCESS_ROUTES` entries must exist before wrapping the routes, otherwise `RequireMenuPath` would redirect every user, including authorized ones, and the consistency test in the next task would still fail on the "every guard() has an ACCESS_ROUTES entry" check being satisfied but with a temporarily broken runtime).

Both routes already import their components (no import changes needed):
```
18:import BankStatementImportPage from "./pages/customer/BankStatementImportPage";
32:import InvoiceImportStatistics from "./components/pages/automation/InvoiceImportStatistics";
```
The `guard(path, element)` helper already exists at `App.tsx:292`:
```tsx
const guard = (path: string, element: React.ReactNode) => (
  <RequireMenuPath path={path}>{element}</RequireMenuPath>
);
```

- [ ] **Step 1: Confirm the two current bare routes**

Run:
```bash
grep -n 'finance/bank-statements"\|invoice-import-statistics"' frontend/src/App.tsx
```
Expected output (exact text):
```
415:                        <Route path="/finance/bank-statements" element={<BankStatementImportPage />} />
445:                        <Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
```

- [ ] **Step 2: Wrap the `/finance/bank-statements` route in `guard(...)`**

In `frontend/src/App.tsx`, find this exact line:
```tsx
                        <Route path="/finance/bank-statements" element={<BankStatementImportPage />} />
```
Replace it with:
```tsx
                        <Route path="/finance/bank-statements" element={guard("/finance/bank-statements", <BankStatementImportPage />)} />
```

- [ ] **Step 3: Wrap the `/automation/invoice-import-statistics` route in `guard(...)`**

In `frontend/src/App.tsx`, find this exact line:
```tsx
                        <Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
```
Replace it with:
```tsx
                        <Route path="/automation/invoice-import-statistics" element={guard("/automation/invoice-import-statistics", <InvoiceImportStatistics />)} />
```

- [ ] **Step 4: Verify the diff touches only these two lines**

Run:
```bash
git diff frontend/src/App.tsx
```
Expected output: exactly two changed lines (one `-`/`+` pair each), matching Steps 2 and 3 above — no other route, import, or the `guard()` definition itself is touched.

- [ ] **Step 5: Commit**
```bash
git add frontend/src/App.tsx
git commit -m "fix(auth): wrap invoice-import-statistics and bank-statements routes in guard()

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Py3c1pTCK95Y4Xion83smx"
```

---
