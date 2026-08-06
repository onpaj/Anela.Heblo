# Plan: Fix stale E2E guidance in testing-strategy.md

## Summary

`docs/architecture/testing-strategy.md`'s E2E Testing section (lines 244–476) describes a frontend test directory layout, execution model, and environment that no longer exist, and it contradicts both `docs/testing/e2e-module-guide.md` (the authoritative module doc) and CLAUDE.md's normative E2E rule. This is a documentation-only fix: reconcile the E2E subsection of testing-strategy.md with reality and defer structural/module detail to e2e-module-guide.md instead of restating it. No code, script, or test changes are implied.

## Context

testing-strategy.md is reached first via the CLAUDE.md documentation map ("read the relevant doc before implementation work touches that area"), so its drift actively misdirects anyone (human or agent) who consults it before writing an E2E test: wrong directory (`frontend/test/ui/<x>/` instead of `frontend/test/e2e/<module>/`), wrong module list, wrong target environment (ports 3001/5001 instead of staging), and a self-contradiction within the same file about which environment is authoritative. A spec placed per the stale guidance would sit outside every Playwright `--project=<module>` and never run — silently, since the nightly suite would stay green. Verified directly against the repo:

- `frontend/test/` actual top-level dirs: `api/`, `auth/`, `e2e/`, `utils/` (plus `e2e_test_scenarios.md`, a reporter file) — no `ui/` or `integration/` dirs exist.
- `scripts/run-playwright-tests.sh` hardcodes `STAGING_URL="https://heblo.stg.anela.cz"` and always exports `PLAYWRIGHT_BASE_URL` to it — there is no 3001/5001 automation-environment code path in the actual runner.
- The real module list (`scripts/run-playwright-tests.sh` `MODULES=`) is `catalog, issued-invoices, stock-operations, transport, manufacturing, core, marketing, finance, baleni, leaflet-generator, terminal` — 11 modules. `e2e-module-guide.md` currently documents 7 of them (missing `finance`, `baleni`, `leaflet-generator`, `terminal`) and is otherwise the closer/authoritative source; testing-strategy.md's list (`layout, auth, catalog, purchase, manufacturing, analytics`) matches neither.
- The stale "automation environment" narrative appears in **four** places in testing-strategy.md, not just the two cited in the issue: Environment Requirements (~248), Test Organization Structure (~251–264), Manual Test Execution comment "uses automation environment" (~450) and VS Code launch config guidance "Launch Automation Environment" 3001/5001 (~464–467), plus Port Configuration Matrix (~479–486) and Frontend/Backend env-var blocks (~517–545), and finally the Best Practices Summary line (~711) "Always use automation environment (ports 3001/5001)".

Note: the e2e-module-guide.md module-count gap (7 documented vs 11 real modules) is a related but separate doc-drift issue — out of scope here; flagged as a follow-up rather than folded into this fix, to keep this change surgical to the reported contradiction.

## Functional requirements

**FR-1 — Remove the fabricated directory structure.**
Replace the "Test Organization Structure" block (testing-strategy.md:251–264, the `frontend/test/ui/…`, `integration/`, `e2e/` tree) with a short pointer to the real layout and to `docs/testing/e2e-module-guide.md` as the authoritative source for module boundaries, instead of restating a module list.
- Acceptance: no reference to `frontend/test/ui/` or `frontend/test/integration/` remains anywhere in testing-strategy.md. The section links to `docs/testing/e2e-module-guide.md`.

**FR-2 — Make the target environment single-sourced and correct.**
Fix "Environment Requirements" (~248–249) and Best Practices Summary (~711) so both say the same thing, matching the actual runner: E2E tests target the deployed staging environment (`https://heblo.stg.anela.cz`), not ports 3001/5001. Correct the stale `http://` scheme to `https://` while at it, since that's what the runner uses.
- Acceptance: grep for "3001" and "5001" in the E2E-specific prose (Environment Requirements, Manual Test Execution, VS Code Launch Configurations, Best Practices Summary → E2E Testing) returns nothing that claims E2E tests run against those ports. Every environment statement in the E2E section agrees with `scripts/run-playwright-tests.sh`.

**FR-3 — Fix the "uses automation environment" comment and VS Code config claims.**
Manual Test Execution (~450) and VS Code Launch Configurations (~463–467) currently imply E2E tests run against a local automation environment. Update both to reflect the staging-only runner behavior (or remove the VS Code config claims if no such launch configs actually exist in the repo — verify against `.vscode/launch.json` before deciding which).
- Acceptance: text matches verified repo state (either the launch configs exist and are described accurately, or the section is removed/corrected if they don't).

**FR-4 — Decide fate of Port Configuration Matrix / env var blocks (~477–545).**
These describe dev/automation/test/prod port and env-var setups that are plausible for backend/frontend dev workflows generally, but the "Automation/Testing 3001/5001" row is what's actively contradicted for E2E purposes. Scope decision: keep the matrix for general dev-environment reference (it's not necessarily wrong for non-E2E automation/unit-test contexts) but add a one-line clarification that Playwright E2E specifically targets staging regardless of this matrix, so a reader doesn't re-infer the wrong thing from FR-2's fix sitting a few hundred lines above this table.
- Acceptance: a reader going straight to the Port Configuration Matrix without reading Environment Requirements first cannot conclude E2E tests use ports 3001/5001.

**FR-5 — Preserve everything outside the E2E section.**
Backend testing, frontend unit/component testing, CI/CD integration, security/credentials, and the rest of Best Practices Summary (Unit/Integration/Maintenance) are not implicated by the issue and were not found to be wrong — leave them untouched.
- Acceptance: diff touches only the E2E Testing subsection (~244–476), the Port Configuration Matrix clarification (FR-4), and the E2E line of Best Practices Summary (~709–713). No other section changes.

## Non-functional requirements

- **Consistency over completeness**: don't duplicate e2e-module-guide.md's module table into testing-strategy.md — link to it. Duplication is exactly how this drift happened (two sources of truth for the same fact).
- **No process/tooling changes**: this is a documentation correction; it must not alter `scripts/run-playwright-tests.sh`, CI workflows, or test files.

## Data model

Not applicable — documentation-only change, no code/data entities involved.

## Interfaces

Not applicable — no endpoints, events, or UI. The only "interface" is the doc itself and its consumers (CLAUDE.md doc map, developers, and future agents reading testing-strategy.md before touching E2E code).

## Dependencies and scope

**Depends on / must match:**
- `docs/testing/e2e-module-guide.md` (authoritative for module structure — testing-strategy.md must defer to it, not restate it)
- `scripts/run-playwright-tests.sh` (ground truth for actual E2E execution: staging URL, module list, invocation)
- CLAUDE.md E2E rule ("E2E tests live in their module folder under `frontend/test/e2e/<module>/`")

**In scope:**
- testing-strategy.md E2E Testing subsection (directory structure, environment requirements, manual execution, VS Code config claims)
- Port Configuration Matrix / env-var blocks only insofar as they mislead about E2E target environment
- Best Practices Summary's E2E line

**Out of scope:**
- Fixing e2e-module-guide.md's own module-count gap (7 vs 11 real modules) — separate issue, flag as follow-up
- Any change to test code, scripts, or CI
- Backend/frontend unit and integration testing guidance in the same file (not shown to be wrong)
- Re-verifying every other CLAUDE.md-referenced doc for similar drift (out of this issue's blast radius)

## Rough plan

1. Re-read the full current E2E Testing subsection of testing-strategy.md (lines ~244–476) plus Port Configuration Matrix (~477–545) and the E2E Best Practices line (~709–713) to capture exact current text before editing.
2. Verify `.vscode/launch.json` (or equivalent) for FR-3 to determine whether the named launch configurations actually exist, so the fix states fact rather than another guess.
3. Rewrite "Test Organization Structure" to point at `docs/testing/e2e-module-guide.md` instead of listing a directory tree (FR-1).
4. Correct "Environment Requirements" and the Best Practices Summary E2E line to state staging (`https://heblo.stg.anela.cz`) consistently (FR-2).
5. Correct "Manual Test Execution" comment and VS Code Launch Configurations text per the verification in step 2 (FR-3).
6. Add the one-line staging clarification near/in the Port Configuration Matrix so it can't be misread in isolation (FR-4).
7. Diff the whole file to confirm only the intended sections changed (FR-5) — no incidental edits to backend/unit-testing/CI sections.
8. Cross-check the edited section against `docs/testing/e2e-module-guide.md` and CLAUDE.md's E2E rule one more time for consistency (no rule this doc states should be reachable-but-wrong after the edit).
9. Since this is docs-only, standard BE/FE build/test validation is not applicable; the acceptance check is a manual re-read of the diff against FR-1..FR-5's acceptance criteria.

## Open questions

- **VS Code launch configs**: the issue doesn't mention them, but they were found during verification to make the same stale "automation environment" claim. Default: fix them in this same pass (FR-3) since they're inside the same contiguous E2E section and the same defect class — but if `.vscode/launch.json` shows no such configs at all, the safer default is to delete the false claim rather than invent a replacement.
- **e2e-module-guide.md's 7-vs-11-module gap**: found during verification but not part of the reported issue. Default: leave it alone and note it as a follow-up rather than expanding scope — the reviewer/architecture step should decide if it warrants its own issue.
- **Port Configuration Matrix**: is the Development/Test/Production row data itself accurate (3000/5000, 8080/5000)? Not verified here since it's outside the reported contradiction and not clearly wrong. Default: leave those rows as-is; only clarify the Automation/Testing row's inapplicability to E2E (FR-4).
