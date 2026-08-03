# Development: Fix stale E2E guidance in testing-strategy.md

## What changed

`docs/architecture/testing-strategy.md` — documentation-only change, single file. Applied design-01.md's replacement text with architecture-01.md's Finding 1 wording correction (narrowed the "unrelated to Playwright execution" overreach to a verified, scoped claim about the runner script only).

1. **Environment Requirements** (~246–249): `http://` → `https://heblo.stg.anela.cz` (matches `scripts/run-playwright-tests.sh` and `playwright.config.ts` exactly), added a scoped sentence: the runner "has no code path that targets ports 3001/5001" (not the broader, unverifiable claim that the port pair is "unrelated to Playwright execution" — that would have contradicted `docs/architecture/environments.md`, per the architecture review).
2. **Test Organization Structure** (~251–264): removed the fabricated `frontend/test/ui/`, `integration/` directory tree; replaced with a pointer to the real `frontend/test/e2e/<module>/` layout and a deferral to `docs/testing/e2e-module-guide.md` as the single source of truth for module boundaries (no module list duplicated here).
3. **Manual Test Execution comment** (~450): `# Run all E2E tests (uses automation environment)` → `# Run all E2E tests (targets staging: https://heblo.stg.anela.cz)`.
4. **VS Code Launch Configurations subsection** (~463–469): deleted entirely — verified `.vscode/launch.json` does not exist in the repo, so there was no true statement to substitute.
5. **Port Configuration Matrix** (~479–486): left the table rows as-is (out of scope — not shown to be wrong for general dev-environment reference) and added a blockquote note immediately after the table stating the E2E runner never uses these ports and always targets staging, linked back to Environment Requirements.
6. **Best Practices Summary → E2E Testing** (~711): `Always use automation environment (ports 3001/5001)` → `Always target the deployed staging environment (https://heblo.stg.anela.cz), never local ports`.
7. **Technology Stack** (~163) — *found during my own verification pass, not enumerated in plan/design/architecture*: `- **E2E Tests**: Playwright (automation environment only)` → `Playwright (deployed staging environment only)`. This one-line phrase was in the same "automation environment" defect class and directly contradicted the just-fixed Environment Requirements section 80 lines below it; leaving it would have kept the file self-contradictory, which is exactly the reported defect. Fixed as a minimal, same-class correction rather than expanding scope to any other section.

## Verification performed

- `git diff docs/architecture/testing-strategy.md` reviewed in full: touches only the 7 spots above, nothing else in the file changed (confirms FR-5 / surgical-scope requirement).
- `grep -n "3001\|5001\|frontend/test/ui\|frontend/test/integration\|automation environment" docs/architecture/testing-strategy.md` — remaining hits are: the intentionally-preserved Port Configuration Matrix row (`| Automation/Testing | 3001 | 5001 | Playwright E2E tests |`, left as-is per design's explicit FR-4 decision to add a note rather than rewrite the row) and the general Frontend env-var example block (`REACT_APP_API_BASE_URL=http://localhost:5001`, out of scope — describes general frontend build config, not an E2E claim). No remaining text asserts E2E tests run against ports 3001/5001.
- `grep -rn "VS Code Launch Configurations\|vs-code-launch-configurations\|frontend/test/ui/\|frontend/test/integration" docs/ CLAUDE.md` — no hits, confirming no dangling cross-references to the deleted subsection or the fabricated directory tree elsewhere in the docs tree.
- Re-read the full diff against the plan's FR-1..FR-5 acceptance criteria and the architecture review's Finding 1 requirement — all satisfied.
- No code/build/test surface exists for a docs-only change; `dotnet build`/`npm run build`/lint/E2E runner are not applicable and were not run, consistent with the plan's step 9 ("standard BE/FE build/test validation is not applicable" for a docs-only change).

## Files changed

- `docs/architecture/testing-strategy.md` (10 insertions, 25 deletions)

## How to verify

- Read the diff: `git diff docs/architecture/testing-strategy.md`.
- Confirm consistency: Environment Requirements, the Manual Test Execution comment, the Port Configuration Matrix note, the Technology Stack line, and the Best Practices Summary E2E line all now agree (staging `https://heblo.stg.anela.cz`, no ports).
- Confirm `docs/testing/e2e-module-guide.md` is the only place module structure is now claimed in either doc (testing-strategy.md defers rather than restates).
