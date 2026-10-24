### task: process-doc-update
Update `docs/processes/feed-invoice-classification.md` and `docs/processes/module-invoice-classification.md` (whichever describes AMOUNT rules): note that AMOUNT patterns use invariant culture with a dot decimal separator (comma not accepted), bump `verified_at`, then run `python3 scripts/process-docs/check.py index`.
- Depends on: invariant-parsing-fix.
