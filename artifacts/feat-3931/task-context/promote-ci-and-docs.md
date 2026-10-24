### task: promote-ci-and-docs

Conditionally make the knip CI step blocking (FR-6) and satisfy process-docs gate.

Files: `.github/workflows/ci-feature-branch.yml`, any owning `docs/processes/` doc if CI process-docs check demands it.

- [ ] Run `npm run knip`; note the exit code.
- [ ] If exit code is 0: remove `continue-on-error: true` from the knip step only (keep the report artifact upload). If not 0: leave the workflow untouched and list residuals for the PR description.
- [ ] Run `python3 scripts/process-docs/check.py index` from the repo root; if the workflow edit triggers an owning doc, update it (or bump `verified_at`) in the same PR.
- [ ] Final full validation: `npm run build`, `npm run lint`, `npm test -- --watchAll=false`, `npm run knip`; confirm `git diff --stat` touches only the allowed paths and not `src/api/generated/**`.
- [ ] Commit: `ci(frontend): make knip step blocking (#3931)` (or skip commit if left non-blocking).

Verify: step is blocking iff local knip exits 0; PR notes list baseline vs final counts, kept-file justifications, and the duplicates decision.
