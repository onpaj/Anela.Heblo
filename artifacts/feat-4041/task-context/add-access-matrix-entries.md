### task: add-access-matrix-entries

**Files:**
- Modify: `access-matrix.json` (repo root)

Add two new `menuPaths` entries requiring the pre-existing `Finance_MarginAnalysis` feature at `Read` level, matching the permission already enforced by `AnalyticsController`'s class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` (`backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:14`).

- [ ] **Step 1: Confirm the current `menuPaths` entry to anchor on**

Run:
```bash
grep -n '"/analytics/product-margin-summary"' access-matrix.json
```
Expected output (single line, exact text):
```
43:    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
```
(Line number may differ slightly depending on git history, but the content must match exactly.)

- [ ] **Step 2: Add the two new `menuPaths` entries**

In `access-matrix.json`, find this exact line inside the `"menuPaths"` array:
```json
    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
```
Replace it with these three lines (the original line, unchanged, followed by the two new entries):
```json
    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
    { "path": "/automation/invoice-import-statistics", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
    { "path": "/finance/bank-statements", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
```
All three entries require the same feature (`Finance_MarginAnalysis`) at the same level (`Read`), so grouping them together keeps the diff minimal and easy to review. This does not reorder or modify any existing entry — it only inserts two new lines immediately after an existing one.

- [ ] **Step 3: Validate the JSON is well-formed**

Run:
```bash
python3 -c "import json; d = json.load(open('access-matrix.json')); print(len(d['menuPaths']))"
```
Expected output: the previous count of `menuPaths` entries + 2 (no exception raised — a `json.decoder.JSONDecodeError` means a syntax mistake, e.g. a missing/extra comma, was introduced).

- [ ] **Step 4: Confirm no other line in the file changed**

Run:
```bash
git diff access-matrix.json
```
Expected output: a diff showing exactly one `+` line becoming three (i.e., 2 new `+` lines added, 0 lines removed, 0 lines changed) — no other hunk anywhere in the file.

- [ ] **Step 5: Commit**
```bash
git add access-matrix.json
git commit -m "feat(auth): add menu-path permission entries for invoice-import-statistics and bank-statements routes

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Py3c1pTCK95Y4Xion83smx"
```

---
