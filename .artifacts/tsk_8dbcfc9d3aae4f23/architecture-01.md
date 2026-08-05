# Architecture Review: Fix stale E2E guidance in testing-strategy.md

## Verdict

**Approved, with one required wording adjustment before implementation** (Finding 1). Everything else in plan-01.md / design-01.md checks out against the live repo and existing doc-ownership conventions.

## What I verified against the current tree (HEAD, clean working tree)

All factual inputs the plan/design rely on were re-checked directly, not taken on trust:

- `scripts/run-playwright-tests.sh:27,29,81`: `STAGING_URL="https://heblo.stg.anela.cz"`, `TEST_DIR="$FRONTEND_DIR/test/e2e"`, `MODULES=(catalog issued-invoices stock-operations transport manufacturing core marketing finance baleni leaflet-generator terminal)` (11). No 3001/5001 branch anywhere in the script. Matches plan/design claims exactly.
- `frontend/playwright.config.ts`: `baseURL: process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'` — confirms staging is the hard default, not just a runner default.
- `.vscode/launch.json` does not exist (`ls .vscode` → no such directory). Deleting rather than rewriting the "VS Code Launch Configurations" subsection is correct; a grep across `docs/` and `CLAUDE.md` shows no other file references that heading or its claims, so removing it orphans nothing.
- `docs/testing/e2e-module-guide.md` documents 7 modules; the runner has 11. Confirmed — correctly flagged as a separate, out-of-scope gap in the plan.
- Anchor link `#environment-requirements` used in the Port Configuration Matrix note (design step 5) resolves correctly against the heading `### Environment Requirements` under GitHub's slug rules — no dead link introduced.
- No incoming links elsewhere in `docs/` or `CLAUDE.md` to `#test-organization-structure` or `#vs-code-launch-configurations` — safe to restructure/delete those sections without orphaning a cross-reference.
- FR-5's scope claim holds: nothing outside the E2E subsection, the Port Configuration Matrix, and the Best Practices Summary's E2E line was shown to be wrong.

## Finding 1 (must fix before implementing) — the proposed wording creates a *new* cross-doc contradiction

The design's replacement text for "Environment Requirements" (design-01.md §1) asserts:

> There is no local "automation environment" (ports 3001/5001) code path for E2E — that port pair is a general dev/backend concept **unrelated to Playwright execution**.

This is stronger than the evidence supports and conflicts with `docs/architecture/environments.md`, which CLAUDE.md's doc map separately designates as authoritative for "port mappings, CORS, Azure config." That file states, in its own Port Configuration table and prose:

- `environments.md:18`: `| **Local Automation/Playwright** | 3001 | 5001 | ... | Separate servers (testing) |`
- `environments.md:26`: "**Local Automation**: Frontend dev server (3001) → Backend dev server (5001) **for Playwright testing**"
- `environments.md:40-46`: a `.env.automation` block described as "Local Automation/Playwright" "for testing isolation"
- `environments.md:87-90, 110-112`: CORS and Azure AD redirect URI sections for "Local Automation/Playwright"

So one authoritative doc (`environments.md`) says 3001/5001 exists specifically *for Playwright testing*, while the proposed fix would make a second doc (`testing-strategy.md`) assert the opposite — that these ports are "unrelated to Playwright execution." That's not fixing the reported contradiction, it's relocating it one file over. It is the same defect class the issue is about (issue #2351-class doc drift), just moved.

Note this doesn't invalidate the plan's core diagnosis — I confirmed independently that `environments.md`'s "Local Automation/Playwright" row is itself the stale one: there's no `.env.automation` file in the repo, `playwright.config.ts` has no fallback to `localhost:3001/5001`, and `docs/testing/playwright-e2e-testing.md` (the doc CLAUDE.md's map calls out as authoritative for "E2E setup, authentication, commands") never mentions these ports at all — it only ever says staging. So the *conclusion* (E2E is staging-only) is right; only the *phrasing* overreaches.

**Required change:** narrow the claim to what's actually verified — the runner's behavior — rather than a categorical statement about what the ports mean:

```markdown
### Environment Requirements

**CRITICAL**: Playwright tests MUST ALWAYS use the deployed staging environment:
- **Test URL**: `https://heblo.stg.anela.cz`

`scripts/run-playwright-tests.sh` hardcodes this URL and has no code path that targets ports 3001/5001. See `docs/testing/e2e-module-guide.md` for module boundaries.
```

Drop the "unrelated to Playwright execution" / "general dev/backend concept" characterization — it's an assertion about `environments.md`'s territory that this task didn't verify and that current wording elsewhere. Same softening applies to the Port Configuration Matrix note (design §5): keep it scoped to "the current runner never uses these ports for E2E," not "these ports have nothing to do with Playwright."

**Follow-up to flag, not fold in:** `environments.md`'s "Local Automation/Playwright" row/prose (lines 18, 26, 40-46, 87-90, 110-112) is stale in the same way testing-strategy.md was — no code path uses it. Same treatment as the plan's already-flagged e2e-module-guide.md 7-vs-11 module gap: note it as a separate follow-up issue, don't expand this task's diff to fix it. Fixing it here would violate the plan's own explicit scope boundary (testing-strategy.md only) and CLAUDE.md's "surgical changes" rule.

## Finding 2 (informational, no action needed now) — duplicate ownership is the root cause, but it's pre-existing and correctly out of scope

`docs/testing/playwright-e2e-testing.md` already covers, in more current form, most of what testing-strategy.md's E2E subsection restates: target URL, module structure (deferring to e2e-module-guide.md itself), the full `navigateToApp()` vs `createE2EAuthSession()` authentication rules, test patterns, and execution commands. Two docs independently maintaining the same facts is exactly the mechanism that produced this bug (one copy drifted, the other didn't). The design does not consolidate this — it only fixes the specific facts the issue reported wrong (directory tree, environment, VS Code config, ports).

This is the right call for *this* task: the duplicated Authentication Setup / Test Patterns content in testing-strategy.md was checked and is still factually correct (matches CLAUDE.md's own E2E rule about `navigateToApp()`), so it isn't part of the reported defect, and the plan's FR-5 / "surgical changes" boundary correctly leaves it alone. Recommend a follow-up issue ("consolidate E2E documentation ownership between testing-strategy.md and playwright-e2e-testing.md") rather than expanding this diff — consistent with how the plan already handled the module-count gap.

## Everything else in the design

- FR-1 (directory structure → defer to e2e-module-guide.md): correct, matches the "single source of truth" principle the design already states, verified no other doc restates the fabricated tree.
- FR-3 / VS Code Launch Configurations deletion: correct given `.vscode/launch.json` doesn't exist — deletion, not a rewrite, is the only honest option.
- FR-4 (Port Configuration Matrix note, not a row rewrite): correct scope call — the Development/Test/Production rows weren't shown wrong, only the "Playwright E2E tests" purpose text tied to the Automation/Testing row needed disarming, and a note does that without rewriting environments.md's territory (once Finding 1's wording fix is applied).
- FR-5 (leave everything else untouched): verified — no other section was shown wrong, and the diff surface described (E2E subsection + port matrix note + best-practices line) matches exactly what's broken.

## What implementation should do differently from design-01.md

Only the wording in design-01.md §1 (Environment Requirements) and the note in §5 (Port Configuration Matrix) needs adjustment per Finding 1 above. §2 (Test Organization Structure), §3 (Manual Test Execution comment), §4 (VS Code deletion), and §6 (Best Practices line) are approved as written.
