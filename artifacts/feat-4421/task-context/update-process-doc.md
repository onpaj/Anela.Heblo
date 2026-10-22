### task: update-process-doc
**Files:**
- Modify: `docs/processes/calc-product-margin-summary.md`

- [ ] Step 1: Read the doc; at line ~56 ("streams these through MarginCalculator...") add a note under Known quirks that `MarginCalculator` retains all accepted products per group in `GroupProducts` (peak memory O(products x sales history)); streaming only avoids a second full copy at fetch time.
- [ ] Step 2: Bump `verified_at` in the front matter to today's date.
- [ ] Step 3: Run `python3 scripts/process-docs/check.py index`; expect pass.
- [ ] Step 4: Commit `docs(processes): note GroupProducts retention in margin summary (#4421)`.
