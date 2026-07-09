# Code Review: decouple-issued-invoices-shell-from-grid-loading

## Summary

Reviewed the real diff (`git show HEAD`, commit `0e92470`) against the task spec and re-read the
full current contents of both touched files. The implementation matches the spec's exact code
guidance closely:

- `useIssuedInvoices.ts`: added `UseIssuedInvoicesListOptions { enabled?: boolean }`, second
  optional `options` param on `useIssuedInvoicesList`, `enabled: options?.enabled ?? true` added to
  the `useQuery` config. `queryKey` (still keyed only on `filters`), `queryFn` body, `staleTime`,
  and `gcTime` are byte-for-byte unchanged. `useIssuedInvoiceDetail` and its existing `enabled:
  !!invoiceId` pattern are untouched.
- `IssuedInvoicesPage.tsx`: call site now passes `{ enabled: activeTab === 'grid' }` as second arg
  (line 84). The page-level `if (loading) return …` / `if (error) return …` blocks (former lines
  314–334) are fully deleted — confirmed by reading the current file: nothing between the
  `getSyncStatusIcon` helper (ends ~line 312) and the `StatisticsTab` component (~line 314) now
  short-circuits rendering. `loading`/`error` remain destructured from the hook (lines 70–71) and
  are used exclusively inside the `activeTab === 'grid'` ternary (lines 569–582), which is
  otherwise untouched (same JSX/messages as the previously-dead branch described in the spec).
  `data-loading="true"` was added to exactly the grid tab's loading container (line 570) and not to
  `StatisticsTab`'s separate loading block (lines 316–325), matching the spec's explicit
  "do not add it to `StatisticsTab`" instruction. `Loader2`/`AlertCircle` imports remain used in
  multiple other places in the file (import modal, statistics tab, status badges), so no orphaned
  imports resulted from the deletion.
- Scope: `git show HEAD --stat` confirms only the two named files are touched — no unrelated
  formatting, no backend files, no `wait-helpers.ts` changes. The `if (!response.ok) throw new
  Error(...)` response handling in `queryFn` is verified identical to spec description (line 83–85
  of the current file); the `Success: false` swallowing issue was correctly left untouched, per
  Decision 4 being out of scope.
- `useEffect` gating `refetchRunningJobs()` on `activeTab === 'grid'` (lines 191–195) and
  `useIssuedInvoiceSyncStats` usage are untouched, as required.

Independently re-ran validation rather than trusting the implementation summary:
- `npm run build` → "Compiled successfully." (only pre-existing caniuse-lite/bundle-size
  informational warnings, no errors).
- `npx eslint src/pages/customer/IssuedInvoicesPage.tsx src/api/hooks/useIssuedInvoices.ts` →
  empty output, i.e. zero errors/warnings on the two changed files.
- Confirmed via grep that `useIssuedInvoicesList` has only one call site in the whole `frontend/src`
  tree (the page itself), so the "any other caller keeps eager-fetch behavior" claim, while true by
  construction (`options?.enabled ?? true` defaults to `true`), isn't currently exercised by a
  second caller — not a defect, just a note that the backward-compat guarantee is currently
  untested by any other real usage.

No functional requirement, architecture-guidance point, or acceptance criterion in the task spec is
violated. E2E validation is correctly deferred to the nightly staging run per CLAUDE.md and the
task's own validation-steps section, and is not claimed as run in the implementation summary.

## Review Result: PASS

### task: decouple-issued-invoices-shell-from-grid-loading
**Status:** PASS

## Docs to Update
None required by this task.

## Overall Notes
- The single stylistic deviation the implementer flagged themselves (call-site `enabled` option
  placed on the same line as the closing `}` of the filters object rather than the spec's
  illustrative multi-line form) is functionally identical and not a compliance issue.
- No other issues found.
