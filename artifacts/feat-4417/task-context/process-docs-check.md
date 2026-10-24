### task: process-docs-check

**Files:**
- Modify: `docs/processes/module-invoice-classification.md` (frontmatter `verified_at`)

- [ ] **Step 1: Confirm behaviour documented** — run `grep -n -i "details\|404" docs/processes/module-invoice-classification.md docs/processes/feed-invoice-classification.md`. If either describes the details endpoint returning 404 on failures, correct that sentence to say infrastructure errors return HTTP 500 and only a missing invoice returns 404; otherwise no text change.

- [ ] **Step 2: Bump `verified_at`** in `docs/processes/module-invoice-classification.md` frontmatter to the current short HEAD SHA (`git rev-parse --short HEAD`), quoted as in the existing value.

- [ ] **Step 3: Run the index check**

Run: `python3 scripts/process-docs/check.py index`
Expected: exits 0.

- [ ] **Step 4: Commit**

```bash
git add docs/processes
git commit -m "docs: re-verify invoice classification process docs (#4417)"
```
