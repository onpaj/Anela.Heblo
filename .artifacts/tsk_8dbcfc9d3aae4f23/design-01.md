# Design: Fix stale E2E guidance in testing-strategy.md

No user interface is involved — this is a documentation-only change to a single Markdown file. The UX/UI section is omitted.

## Verified ground truth (inputs to this design)

- `scripts/run-playwright-tests.sh`: hardcodes `STAGING_URL="https://heblo.stg.anela.cz"`, always exports `PLAYWRIGHT_BASE_URL` to it, and reads `TEST_DIR="$FRONTEND_DIR/test/e2e"`. `MODULES=(catalog issued-invoices stock-operations transport manufacturing core marketing finance baleni leaflet-generator terminal)`. No 3001/5001 code path exists anywhere in this script.
- `.vscode/launch.json` **does not exist** in the repo. The "VS Code Launch Configurations" subsection in testing-strategy.md (lines 463–469), naming configs like "Launch Automation Environment" (5001/3001), is entirely fabricated — there is nothing to correct it against, so it must be deleted, not rewritten.
- `docs/testing/e2e-module-guide.md` is the authoritative module doc (7 modules currently documented; the runner has 11 — a separate, out-of-scope gap per the plan).
- Nothing outside the E2E Testing subsection (~244–476), the Port Configuration Matrix (~479–486), and the E2E line of Best Practices Summary (~709–713) is touched.

## Component design

Treat each Markdown section as a component with a single responsibility and an explicit interface (what it may claim vs. what it must defer elsewhere):

| Section (current lines) | Responsibility after fix | Interface / dependency |
|---|---|---|
| `### Environment Requirements` (246–249) | State the one true E2E target environment | Must match `scripts/run-playwright-tests.sh` `STAGING_URL` verbatim (scheme + host) |
| `### Test Organization Structure` (251–264) | Point to directory root and defer module detail | Must not restate module names/paths — links to `docs/testing/e2e-module-guide.md` (single source of truth for module structure) |
| Manual Test Execution comment (450) | Describe what running the script does, accurately | Must not claim "automation environment" |
| `#### VS Code Launch Configurations` (463–469) | — (removed) | No corresponding repo artifact exists; component is deleted, not replaced |
| `### Port Configuration Matrix` (479–486) | Describe general local dev/backend port tiers; explicitly scope out E2E | Must carry a note that Playwright E2E ignores this matrix entirely (prevents a reader who skips straight to the table from re-deriving the wrong port claim already fixed above) |
| Best Practices Summary → E2E bullets (709–713) | Restate the staging-only rule tersely, consistent with Environment Requirements | Must match Environment Requirements' wording, not reintroduce ports |
| Everything else in the file | Unchanged | No dependency on this fix |

This mirrors the plan's FR-1..FR-5: each row is one FR, and the "interface" column is that FR's acceptance criterion expressed as a content contract instead of prose.

## Content schema (exact replacement text)

This is documentation, so the "schema" is the literal Markdown each section must contain after the edit. These are prescriptive, not illustrative — the development step should apply them near-verbatim, adjusting only for surrounding Markdown formatting conventions already in the file.

### 1. Environment Requirements (replaces lines 246–249)

```markdown
### Environment Requirements

**CRITICAL**: Playwright tests MUST ALWAYS use the deployed staging environment:
- **Test URL**: `https://heblo.stg.anela.cz`

This is the only environment E2E tests target (see `scripts/run-playwright-tests.sh`, which hardcodes the staging URL). There is no local "automation environment" (ports 3001/5001) code path for E2E — that port pair is a general dev/backend concept unrelated to Playwright execution.
```

Changes: `http://` → `https://` (matches runner), added a sentence closing off the "automation environment" misconception at its source so later sections don't need to re-litigate it.

### 2. Test Organization Structure (replaces lines 251–264)

```markdown
### Test Organization Structure

E2E tests live under `frontend/test/e2e/<module>/`, one Playwright project per module (`--project=<module>`). Module boundaries, the current module list, and guidance for placing new tests are defined authoritatively in `docs/testing/e2e-module-guide.md` — refer to it rather than this document.
```

Removes the fabricated `ui/`, `integration/` tree entirely. Does not restate the module list (that's e2e-module-guide.md's job — duplicating it here is exactly how this drift happened per the plan's non-functional requirement).

### 3. Manual Test Execution comment (replaces line 450)

```bash
# Run all E2E tests (targets staging: https://heblo.stg.anela.cz)
```

### 4. VS Code Launch Configurations subsection (delete lines 463–469 entirely)

No replacement text — remove the `#### VS Code Launch Configurations` heading and its four-item list. Verified: no `.vscode/launch.json` exists, so there is no true statement to substitute; keeping any claim here would recreate the same defect class the issue reports.

### 5. Port Configuration Matrix clarification (insert immediately after the table, before line 487)

```markdown
> **Note:** This matrix describes general local dev/backend environment tiers. Playwright E2E tests never use any of these ports — they always run against the deployed staging environment described in [Environment Requirements](#environment-requirements), regardless of what's running locally.
```

Table rows themselves (Development/Automation/Test/Production, their port numbers) are left as-is — plan scoped this as a general dev-environment reference, not shown to be wrong for non-E2E purposes. Only the "Playwright E2E tests" purpose text on the Automation/Testing row becomes misleading in isolation, hence the note rather than a row edit.

### 6. Best Practices Summary → E2E Testing (replaces line 711)

```markdown
- Always target the deployed staging environment (`https://heblo.stg.anela.cz`), never local ports
```

## Cross-reference integrity (post-edit check, not a new component)

After applying the above, these must all agree on the same fact (E2E → staging, `https://heblo.stg.anela.cz`, no ports): Environment Requirements, Manual Test Execution comment, Port Configuration Matrix note, Best Practices Summary. And these must all agree on directory structure (defer to e2e-module-guide.md, no `ui/`/`integration/` dirs): Test Organization Structure is now the only place in testing-strategy.md that mentions directory layout at all. This is the acceptance check from the plan (FR-1..FR-5) — no new verification mechanism is introduced since this is a docs-only, non-executable change.
