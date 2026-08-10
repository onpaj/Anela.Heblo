# Design: Correct `docs/routines/daily-arch-review.md` to describe the current map-driven arch-review mechanism

No user-facing UI component exists for this change (confirmed by `arch-review.r1.md`'s `Skip Design: true`) —
this is a single Markdown documentation file. UX/UI sections are omitted per the designer's own instructions.

## Component Design

The only "component" is the document itself. Its target section structure, carrying forward the existing file's
shape (`docs/routines/daily-arch-review.md`) with content corrected per `spec.r1.md` FR-1–FR-5:

| Section (unchanged heading) | Responsibility after the fix |
|---|---|
| `# Daily Architecture Review Routine` / `## Overview` | One-paragraph summary: reviews one module-map part per run, files issues for genuine findings. Update "29 modules" → describe the map-driven part count without hardcoding a number that will drift (state it, but attribute it to the map). |
| `## Routine details` (table) | Unchanged content (routine ID, schedule, model, repo, environment, web UI link) — spec FR-4: keep as-is, no evidence it's wrong. |
| `## Module rotation` | **FR-1.** Replace deterministic `modules[(dayOfYear - 1) % 29]` description and the inline 29-row table with: selection source is `docs/architecture/module-map.md`; mechanism is `.claude/skills/arch-review/pick-module.sh`, uniform random **with replacement** over live (non-`RETIRED`) rows; explicit contrast with the old non-repeating rotation so a reader who remembers the old doc understands what changed. Point at the map file rather than duplicating its rows. |
| `## What it reviews` | **FR-3.** Reconcile bullet list against `.agents/arch-review.md`'s actual finding criteria (rule violation / architectural outlier / tech misuse / best-practice error with concrete consequence / duplicated invariant, each grounded against the part's own normative docs and scoped to the drawn part's `Owns:` paths) and its scope rule (one part only, per `Owns:` paths). Correct only what's actually wrong; keep pointing at `.agents/arch-review.md` as the source of truth rather than inlining the persona. |
| `## Output` | **FR-2.** Replace the label description with the structural pipeline: `arch-review` + `agent` always applied by the skill's shell command; exactly one topical label from the current allowlist (`architecture, tech-debt, maintainability, design-patterns, antipattern, code-quality, duplication, documentation`); exactly one severity label from `critical, major, moderate, minor`; anything outside the allowlist is silently dropped. Reference `.claude/skills/arch-review/SKILL.md` step 5c as the source. Remove `refactoring` and `complexity` as examples. |
| `## Managing the routine` | **FR-5.** Unchanged unless a concrete break surfaces while editing neighbouring sections (e.g. a now-incorrect cross-reference). |
| `## Triage` | **FR-5.** Unchanged — the `arch-review` label used in the triage query is still correct under the new label pipeline, so no edit is needed here. |

Ordering, heading text, and the doc's role as the single per-routine record under `docs/routines/` (matching
sibling docs `pr-autoabsorb/README.md`, `telemetry-anomaly/README.md`, `test-health/README.md`,
`weekly-coverage-gap.md`) are preserved — `arch-review.r1.md` Decision 1 already settled in-place correction over
relocation/retirement.

## Data Schemas

Not applicable. No database schema, API contract, or event payload is affected — the change is confined to prose
in one Markdown file.
