### task: harden-transport-box-list-error-state

**Goal (FR-3):** Today, `TransportBoxList.tsx` has an early `if (error) { return (...) }` (lines 269–291) that replaces the *entire* page with a standalone red alert box — no `<h1>`, no "Otevřít nový box" button. Even after Task 1 fixes the permission gap, any future transient 403/500/timeout on `GET /api/transport-boxes` will reproduce the exact same "h1/button never found" signature. Restructure so the header (`<h1>Transportní boxy</h1>`, currently lines 298–303) and the primary action buttons (currently lines 480–497, inside the collapsible controls block) render in a shell shared by all three query states (`isLoading` / `error` / success), with only the content region (table/summary vs. error message) swapped underneath. Per the architecture review's Decision 3, this is a surgical JSX reposition — no new components, no change to the `isLoading` or empty-results (`data.items.length === 0`) branches, no change to the collapsible filters/summary-cards logic itself (only its container loses the now-redundant action-button pair).

**File to modify:** `frontend/src/components/pages/TransportBoxList.tsx`

**Step 1 — write the failing test first.**

File: `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx`

This file already has a `describe("Error state", ...)` block (starting at line 239) with two tests (`"should show error message when data loading fails"` and `"should retry loading when retry button is clicked"`), both of which already pass today and must continue to pass unmodified (they don't currently assert on `<h1>` or the create-box button, so they're compatible with either the old or new structure). Add a **new** third test in that same `describe` block that captures FR-3's acceptance criteria and fails against the current code.

Read the block first to get exact context (it ends right before `describe("Filtering functionality", ...)` at line 273). Use the Edit tool with this `old_string`/`new_string` pair:

old_string:
```typescript
    it("should retry loading when retry button is clicked", () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error("Network error"),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const retryButton = screen.getByText("Zkusit znovu");
      fireEvent.click(retryButton);

      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });
  });
```

new_string:
```typescript
    it("should retry loading when retry button is clicked", () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error("Network error"),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const retryButton = screen.getByText("Zkusit znovu");
      fireEvent.click(retryButton);

      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });

    it("should still render the page header and primary action button when the query errors", () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error("Network error"),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(
        screen.getByRole("heading", { level: 1, name: "Transportní boxy" }),
      ).toBeInTheDocument();
      expect(screen.getByText("Otevřít nový box")).toBeInTheDocument();
      expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
    });
  });
```

**Run the test before the fix (must fail):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList.test.tsx --watchAll=false
```

Expected output before the fix: the new test `"should still render the page header and primary action button when the query errors"` FAILS with something like `Unable to find role="heading"` (the current error branch returns only the red alert box, no `<h1>`). The other Error-state tests still pass.

**Step 2 — apply the component fix.**

Current file structure (verified against the live file): lines 269–291 hold the early `if (error) return (...)`; the main `return (...)` starts at line 293; the header `<div className="flex-shrink-0 mb-3">...<h1>...</h1></div>` is at lines 298–303; then the `showIndicator` block, then `{showTouchPanel ? (<TransportBoxTouchPanel .../>) : (<>...controls block + results table...</>)}` runs from line 313 to line 905; then `<TransportBoxDetail .../>` and the closing `</div>` end the component.

Make three edits with the Edit tool, in this order:

**Edit A — remove the early return, and turn the header into a header+action-bar row that opens the new `error ? ... : (<>...`:**

old_string:
```typescript
  if (error) {
    return (
      <div className="p-6 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-lg">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-5 w-5 text-red-600 dark:text-red-400" />
          <div>
            <h3 className="text-red-800 dark:text-red-300 font-semibold">
              Chyba při načítání transportních boxů
            </h3>
            <p className="text-red-600 dark:text-red-400 text-sm mt-1">
              {error instanceof Error ? error.message : "Neznámá chyba"}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 transition-colors text-sm"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Transportní boxy
        </h1>
      </div>

      {/* StockUp Status Indicator */}
      {showIndicator && (
        <StockUpOperationStatusIndicator
          summary={stockUpSummary}
          sourceType={StockUpSourceType.TransportBox}
        />
      )}

      {showTouchPanel ? (
```

new_string:
```typescript
  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed. The h1 and primary action buttons must render
          regardless of isLoading/error/success so the page chrome survives
          API failures (feature-3542 FR-3). */}
      <div className="flex-shrink-0 mb-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Transportní boxy
        </h1>
        <div className="flex items-center space-x-2">
          <button
            onClick={handleOpenNewBox}
            className="flex items-center px-3 py-1 border border-transparent rounded-md shadow-sm text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700"
          >
            <Plus className="h-3 w-3 mr-1" />
            Otevřít nový box
          </button>
          <button
            onClick={() => refetch()}
            disabled={isLoading}
            className="flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50"
          >
            <RefreshCw
              className={`h-3 w-3 mr-1 ${isLoading ? "animate-spin" : ""}`}
            />
            Obnovit
          </button>
        </div>
      </div>

      {error ? (
        <div className="flex-1 p-6 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-lg">
          <div className="flex items-center gap-2">
            <AlertCircle className="h-5 w-5 text-red-600 dark:text-red-400" />
            <div>
              <h3 className="text-red-800 dark:text-red-300 font-semibold">
                Chyba při načítání transportních boxů
              </h3>
              <p className="text-red-600 dark:text-red-400 text-sm mt-1">
                {error instanceof Error ? error.message : "Neznámá chyba"}
              </p>
              <button
                onClick={() => refetch()}
                className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 transition-colors text-sm"
              >
                Zkusit znovu
              </button>
            </div>
          </div>
        </div>
      ) : (
        <>
          {/* StockUp Status Indicator */}
          {showIndicator && (
            <StockUpOperationStatusIndicator
              summary={stockUpSummary}
              sourceType={StockUpSourceType.TransportBox}
            />
          )}

          {showTouchPanel ? (
```

Notes on this edit:
- The `<h1>` text ("Transportní boxy") is preserved verbatim — do not rename it, `TransportBoxList.test.tsx`, `TransportBoxList.stockUpGate.test.tsx`, `TransportBoxList.touch.test.tsx`, and several E2E specs locate it by this exact string.
- The "Otevřít nový box" button text and its `onClick={handleOpenNewBox}` handler are preserved verbatim (same handler, same accessible text) — only its JSX position moves from inside the collapsible controls block to this always-rendered header row.
- The `isControlsCollapsed ? "" : "..."` text-hiding ternary that these two buttons used in their old location is intentionally dropped: they now live in the always-visible header row (unrelated to the collapsible filter/summary panel), not squeezed next to filter chips, so there is no longer a reason to shorten them to icon-only. Always show the full label.
- `handleOpenNewBox`, `refetch`, and `isLoading` are all already in scope at this point in the component (defined earlier via `useTransportBoxesQuery` and the `handleOpenNewBox` function) — no new imports or state needed. `AlertCircle`, `Plus`, and `RefreshCw` are already imported at the top of the file (lines 4–18).

**Edit B — remove the now-duplicated action-button pair from inside the collapsible controls block** (this pair used to be the only occurrence; leaving it in place after Edit A would create two "Otevřít nový box" buttons on screen simultaneously in the success path, breaking `getByText` / strict-mode Playwright locators that expect exactly one match):

old_string:
```typescript
              )}

              {/* Action buttons - always visible */}
              <button
                onClick={handleOpenNewBox}
                className="flex items-center px-3 py-1 border border-transparent rounded-md shadow-sm text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700"
              >
                <Plus className="h-3 w-3 mr-1" />
                {isControlsCollapsed ? "" : "Otevřít nový box"}
              </button>
              <button
                onClick={() => refetch()}
                disabled={isLoading}
                className="flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50"
              >
                <RefreshCw
                  className={`h-3 w-3 mr-1 ${isLoading ? "animate-spin" : ""}`}
                />
                {isControlsCollapsed ? "" : "Obnovit"}
              </button>
            </div>
```

new_string:
```typescript
              )}
            </div>
```

This leaves the surrounding `<div className="flex items-center space-x-3">...</div>` container (which still holds the `{isControlsCollapsed && (...)}` summary-chips/search-field block) intact — only the two `<button>` elements that followed it are removed. Do not touch the `{isControlsCollapsed && (...)}` block itself or anything above/below this container.

**Edit C — close the new `error ? ... : (<>` wrapper opened in Edit A, right before the `TransportBoxDetail` modal** (which must render in all three states, unchanged):

old_string:
```typescript
        </>
      )}

      {/* Transport Box Detail Modal */}
```

new_string:
```typescript
        </>
      )}
        </>
      )}

      {/* Transport Box Detail Modal */}
```

The first `</>` / `)}` pair (unchanged) closes the pre-existing `showTouchPanel` ternary's non-touch fragment. The second, newly added `</>` / `)}` pair closes the `<>` fragment and the `error ? (...) : (...)` ternary opened in Edit A. Indentation of the newly-added closing lines does not need to match surrounding code exactly (JSX/JS does not require it) — do not reformat the rest of the file.

**Run the test after the fix (must pass):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList.test.tsx --watchAll=false
```

Expected output: all tests in this file pass, including the new one, e.g. `Tests: N passed, N total` with 0 failed.

**Run the full regression suite named in the spec's acceptance criteria (must all still pass unmodified):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList --watchAll=false
```

This runs `TransportBoxList.test.tsx`, `TransportBoxList.stockUpGate.test.tsx`, and `TransportBoxList.touch.test.tsx` together (Jest's default test-file matching on the `TransportBoxList` prefix within `__tests__`). All must pass with zero changes to those other two files — this is the risk-mitigation gate the architecture review calls out explicitly (Decision 3's risk table: "Run existing TransportBoxList.test.tsx, TransportBoxList.stockUpGate.test.tsx, TransportBoxList.touch.test.tsx unmodified as a regression gate").

**Full validation:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
npm run build
npm run lint
```

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add frontend/src/components/pages/TransportBoxList.tsx frontend/src/components/pages/__tests__/TransportBoxList.test.tsx
git commit -m "$(cat <<'EOF'
Harden TransportBoxList error state to keep header/action bar visible

Previously any query error blanked the entire page (no h1, no "Otevřít
nový box" button), reproducing the same "h1/button never found" E2E
failure signature for any future transient 403/500/timeout, independent
of the permission-gap root cause. The header and primary action buttons
now render in a shell shared by isLoading/error/success states; only the
error message replaces the table/summary content underneath.
EOF
)"
```

---

