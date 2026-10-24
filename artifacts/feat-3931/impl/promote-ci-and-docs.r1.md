# Implementation: promote-ci-and-docs

## What was implemented
- `npm run knip` (frontend) exits 0, so the knip CI step in `.github/workflows/ci-feature-branch.yml` is now blocking: `continue-on-error: true` removed, step renamed (no longer "non-blocking"), and `set -o pipefail` added so the `| tee knip-report.txt` pipe does not mask knip's exit code. The "Persist knip report" upload step (`if: success() || failure()`) is unchanged.
- `python3 scripts/process-docs/check.py index` regenerates INDEX.md with no diff; `check` reports OK (144 docs, only INFO-level staleness unrelated to this change). No owning process doc for the knip step, so none updated.

## Verify
- knip exit 0; `npm run build` exit 0; `npm test -- --watchAll=false`: 422 suites passed.
- `npm run lint`: 251 errors, all pre-existing test-file issues (testing-library rules) noted in earlier tasks; none in files touched by this feature.
- Diff touches only the workflow file (plus artifacts); no `src/api/generated/**` changes.

## Status
DONE
