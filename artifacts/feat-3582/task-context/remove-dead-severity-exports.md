### task: remove-dead-severity-exports

**Files:**
- Modify: `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`

- [ ] Open `frontend/src/api/hooks/usePurchaseStockAnalysis.ts` and locate the block starting at the comment `// Helper function to get severity color class` (currently line 71) and ending at the closing brace of `getSeverityDisplayText` (currently line 109), followed by a blank line (line 110). This block currently reads:

  ```ts
  // Helper function to get severity color class
  export const getSeverityColorClass = (
    severity: StockSeverity | undefined,
  ): string => {
    switch (severity) {
      case StockSeverity.Critical:
        return "text-red-600 bg-red-50";
      case StockSeverity.Low:
        return "text-orange-600 bg-orange-50";
      case StockSeverity.Optimal:
        return "text-green-600 bg-green-50";
      case StockSeverity.Overstocked:
        return "text-blue-600 bg-blue-50";
      case StockSeverity.NotConfigured:
        return "text-gray-600 bg-gray-50";
      default:
        return "text-gray-600 bg-gray-50";
    }
  };

  // Helper function to get severity display text
  export const getSeverityDisplayText = (
    severity: StockSeverity | undefined,
  ): string => {
    switch (severity) {
      case StockSeverity.Critical:
        return "Kritický";
      case StockSeverity.Low:
        return "Nízký";
      case StockSeverity.Optimal:
        return "Optimální";
      case StockSeverity.Overstocked:
        return "Přeskladněno";
      case StockSeverity.NotConfigured:
        return "Nezkonfigurováno";
      default:
        return "Neznámý";
    }
  };

  ```

  Delete this entire block (both function definitions, both preceding comments, and the blank line that separates the second function from the next comment). Do not touch anything before or after it — the file must go directly from the closing `};` of `usePurchaseStockAnalysisQuery` straight to the `// Helper function to format Czech number` comment that precedes `formatNumber`.

  Concretely, use this Edit (old_string includes one line of context on each side to anchor the match uniquely):

  old_string:
  ```
  };

  // Helper function to get severity color class
  export const getSeverityColorClass = (
    severity: StockSeverity | undefined,
  ): string => {
    switch (severity) {
      case StockSeverity.Critical:
        return "text-red-600 bg-red-50";
      case StockSeverity.Low:
        return "text-orange-600 bg-orange-50";
      case StockSeverity.Optimal:
        return "text-green-600 bg-green-50";
      case StockSeverity.Overstocked:
        return "text-blue-600 bg-blue-50";
      case StockSeverity.NotConfigured:
        return "text-gray-600 bg-gray-50";
      default:
        return "text-gray-600 bg-gray-50";
    }
  };

  // Helper function to get severity display text
  export const getSeverityDisplayText = (
    severity: StockSeverity | undefined,
  ): string => {
    switch (severity) {
      case StockSeverity.Critical:
        return "Kritický";
      case StockSeverity.Low:
        return "Nízký";
      case StockSeverity.Optimal:
        return "Optimální";
      case StockSeverity.Overstocked:
        return "Přeskladněno";
      case StockSeverity.NotConfigured:
        return "Nezkonfigurováno";
      default:
        return "Neznámý";
    }
  };

  // Helper function to format Czech number
  ```

  new_string:
  ```
  };

  // Helper function to format Czech number
  ```

- [ ] Verify the two removed identifiers no longer appear in the file:
  ```bash
  grep -n "getSeverityColorClass\|getSeverityDisplayText" frontend/src/api/hooks/usePurchaseStockAnalysis.ts
  ```
  Expected: no output (zero matches).

- [ ] Verify `formatNumber` and `formatCurrency` are still present and unchanged, and that the file still exports the query hook:
  ```bash
  grep -n "export const usePurchaseStockAnalysisQuery\|export const formatNumber\|export const formatCurrency" frontend/src/api/hooks/usePurchaseStockAnalysis.ts
  ```
  Expected: three matches, one per identifier.

- [ ] Run a project-wide grep to confirm there are no other references to the removed functions anywhere in the frontend source (there should not be, per the spec/arch-review verification, but this is the final acceptance check from FR-1/FR-2):
  ```bash
  cd frontend && grep -rn "getSeverityColorClass\|getSeverityDisplayText" src
  ```
  Expected: no output (zero matches). If this reports a match outside the file just edited, stop and investigate before proceeding — it means a consumer was missed by prior analysis.

- [ ] Confirm the diff is scoped exactly as expected (only the two functions and their comments removed, nothing else touched):
  ```bash
  git diff -- frontend/src/api/hooks/usePurchaseStockAnalysis.ts
  ```
  Expected: a diff showing only deleted lines (the two function bodies, their comments, and one blank line) — no added lines, no changes elsewhere in the file.

- [ ] Build the frontend to confirm no broken imports or type errors resulted from the removal:
  ```bash
  cd frontend && npm run build
  ```
  Expected: build succeeds with no errors.

- [ ] Lint the frontend:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: lint passes with no new errors.

- [ ] Commit the change:
  ```bash
  git add frontend/src/api/hooks/usePurchaseStockAnalysis.ts
  git commit -m "$(cat <<'EOF'
  Remove dead getSeverityColorClass/getSeverityDisplayText exports

  Both functions had zero consumers anywhere in frontend/src (verified
  by grep) and getSeverityColorClass additionally lacked dark-mode
  variants, a latent ADR-006 violation had it ever been wired up.
  PurchaseStockAnalysis.tsx implements its own inline severity styling
  and never referenced either export. formatNumber/formatCurrency and
  all other exports in the file are unaffected.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  EOF
  )"
  ```
  Expected: commit succeeds; `git status` shows a clean working tree for this file.
