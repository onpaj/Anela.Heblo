### task: refactor-import-status-icon-to-errortype

**Files:**
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx:249-264` (function `getImportStatusIcon`)
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx:494` (call site)
- Test: `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx`

This task covers the entire change: it is a single function and its single call site, with no independent sub-boundaries worth splitting into separate tasks (see arch-review.r1.md Component Overview — one function, one call site, confirmed by repo-wide grep).

- [ ] **Step 1: Write the failing tests**

Add a new `describe` block to the existing test file, right after the closing `});` of the current `describe('ImportTab filters', ...)` block (end of file, currently line 180). Keep the existing `import` lines and `jest.mock('../../../../api/client');` at the top untouched — this new block reuses them.

```tsx
describe('ImportTab status badge', () => {
  let mockGetBankStatements: jest.Mock;
  let mockGetAccounts: jest.Mock;

  function baseStatement(overrides: Partial<{
    id: number;
    transferId: string;
    account: string;
    statementDate: Date;
    importDate: Date;
    itemCount: number;
    currency: string;
    importResult: string;
    errorType: string | null | undefined;
  }>) {
    return {
      id: 1,
      transferId: 'TX-1',
      account: 'Shoptet',
      statementDate: new Date('2026-01-01'),
      importDate: new Date('2026-01-02'),
      itemCount: 3,
      currency: 'CZK',
      importResult: 'OK',
      errorType: null,
      ...overrides,
    };
  }

  beforeEach(() => {
    jest.clearAllMocks();

    mockGetAccounts = jest.fn().mockResolvedValue([]);

    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);
  });

  function renderComponentWithWrapper() {
    const { wrapper } = createQueryClientWrapper();
    return render(<ImportTab />, { wrapper });
  }

  it('renders the success badge when errorType is null, regardless of importResult text', async () => {
    mockGetBankStatements = jest.fn().mockResolvedValue({
      items: [baseStatement({ importResult: 'OK', errorType: null })],
      totalCount: 1,
    });
    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);

    renderComponentWithWrapper();

    expect(await screen.findByText('Úspěch')).toBeInTheDocument();
    expect(screen.queryByText(/Chyba/)).not.toBeInTheDocument();
  });

  it('renders the error badge with the errorType text when errorType is set', async () => {
    mockGetBankStatements = jest.fn().mockResolvedValue({
      items: [baseStatement({ importResult: 'ParseError', errorType: 'ParseError' })],
      totalCount: 1,
    });
    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);

    renderComponentWithWrapper();

    expect(await screen.findByText('ParseError')).toBeInTheDocument();
    expect(screen.queryByText('Úspěch')).not.toBeInTheDocument();
  });

  it('falls back to "Chyba" when errorType is set but empty', async () => {
    mockGetBankStatements = jest.fn().mockResolvedValue({
      items: [baseStatement({ importResult: '', errorType: '' })],
      totalCount: 1,
    });
    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);

    renderComponentWithWrapper();

    // Empty errorType is falsy, so with a naive `!errorType` success check this
    // would wrongly render "Úspěch". Per spec FR-1, only null/undefined mean
    // success; an empty string must still resolve to the error badge fallback text.
    expect(await screen.findByText('Chyba')).toBeInTheDocument();
  });
});
```

Note on the third test: it encodes a real ambiguity in spec FR-1 ("success branch renders when `errorType` is falsy (`null`, `undefined`, or `""`)") versus the backend contract, where `ErrorType` is only ever `null` or a non-empty `ImportResult` string — `""` cannot occur in practice. This test locks in the stricter, safer interpretation (`errorType == null` via `??`/loose-equality-to-null, not a truthiness check) so the badge never silently reports success for a defensively-empty string. Implement Step 3 to satisfy this test.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test ImportTab.test.tsx --watchAll=false`

Expected: the two `describe('ImportTab status badge', ...)` tests referencing `'ParseError'` text and the `'Chyba'` fallback FAIL (or the whole suite fails to compile) because `getImportStatusIcon` still reads `statement.importResult` only and the current success check (`importResult === "OK"`) does not distinguish `errorType`. (The first test, success-on-`errorType: null`, may already pass by coincidence since `importResult: 'OK'` also satisfies today's check — that's expected; Step 2 is about confirming the *other* two fail, proving the current implementation is not yet errorType-driven.)

- [ ] **Step 3: Implement the refactor**

In `frontend/src/components/customer/tabs/ImportTab.tsx`, replace lines 249–264:

```tsx
// Status indicator for import result
const getImportStatusIcon = (importResult: string | undefined) => {
  if (importResult === "OK") {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300">
        <CheckCircle className="h-3 w-3 mr-1" />
        Úspěch
      </span>
    );
  } else {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300">
        <AlertCircle className="h-3 w-3 mr-1" />
        {importResult || "Chyba"}
      </span>
    );
  }
};
```

with:

```tsx
// Status indicator for import result. Driven by errorType (null/undefined = success),
// not by comparing importResult to the backend's internal "OK" success sentinel —
// see BankStatementImportDto.ErrorType on the backend.
const getImportStatusIcon = (errorType: string | null | undefined) => {
  if (errorType == null) {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300">
        <CheckCircle className="h-3 w-3 mr-1" />
        Úspěch
      </span>
    );
  } else {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300">
        <AlertCircle className="h-3 w-3 mr-1" />
        {errorType || "Chyba"}
      </span>
    );
  }
};
```

Then update the call site at line 494:

```tsx
// before
{getImportStatusIcon(statement.importResult)}

// after
{getImportStatusIcon(statement.errorType)}
```

`errorType == null` (loose equality) intentionally catches both `null` and `undefined` while treating `""` as truthy/error, matching Step 1's third test.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test ImportTab.test.tsx --watchAll=false`

Expected: PASS — all tests in both `describe('ImportTab filters', ...)` (unchanged, must still pass) and `describe('ImportTab status badge', ...)` (new) succeed.

- [ ] **Step 5: Run the full frontend build and lint to catch any other reference to the old signature**

Run: `cd frontend && npm run build`
Run: `cd frontend && npm run lint`

Expected: both succeed with no new errors. This also confirms no other file imports or calls `getImportStatusIcon` with the old `(importResult)` signature — `arch-review.r1.md` records that line 494 is the only call site found by repo-wide search, but the build/lint pass is the mechanical double-check.

- [ ] **Step 6: Manually verify the `"OK"` literal is gone**

Run: `grep -n '"OK"' frontend/src/components/customer/tabs/ImportTab.tsx`

Expected: no output (grep exits non-zero / prints nothing) — confirms spec FR-1's acceptance criterion "the literal string `"OK"` no longer appears anywhere in `ImportTab.tsx`'s status-determination logic."

- [ ] **Step 7: Commit**

```bash
cd frontend
git add src/components/customer/tabs/ImportTab.tsx src/components/customer/tabs/__tests__/ImportTab.test.tsx
git commit -m "fix(bank): drive ImportTab status badge from errorType, not hardcoded OK sentinel"
```
