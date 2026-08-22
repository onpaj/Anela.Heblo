# Specification: Correct `docs/routines/daily-arch-review.md` to describe the current map-driven arch-review mechanism

## Summary

`docs/routines/daily-arch-review.md` documents the automated architecture-review routine, but describes a
superseded implementation: a deterministic `modules[(dayOfYear - 1) % 29]` rotation over a fixed 29-module list,
and an ad-hoc secondary label set. The routine actually running today draws from `docs/architecture/module-map.md`
(52 parts) via `.claude/skills/arch-review/pick-module.sh` — uniform random selection **with replacement** — and
files issues through a structurally-enforced label set defined in `.claude/skills/arch-review/SKILL.md` and
`.agents/arch-review.md`. This is a documentation-only correction: rewrite the doc's factual claims to match the
mechanism that is actually in place, so it stops misleading anyone using it to debug or tune the routine's
observed behaviour.

## Background

The doc at `docs/routines/daily-arch-review.md` predates the current module-map-driven architecture review
system. Evidence of drift, all confirmed against the current repo state:

- **Module set / count.** The doc lists 29 flat business modules (lines 22–40). The live selection surface is
  `docs/architecture/module-map.md`, which partitions the codebase into **52 numbered parts** across four groups
  (business domain #1–33, platform/cross-cutting #34–43, integration adapters #44–47, delivery/tooling #48–52).
- **Selection mechanism.** The doc claims a deterministic day-of-year rotation (`modules[(dayOfYear - 1) % 29]`,
  line 20) that by construction cannot repeat a part within a cycle. `.claude/skills/arch-review/pick-module.sh`
  actually does `index = RANDOM % count + 1` — uniform random **with replacement** — and the skill's own docs
  (`.claude/skills/arch-review/SKILL.md`, "Notes") say explicitly: "Selection is uniform random with
  replacement... some parts drawn several times before others are drawn once." These are opposite mechanisms:
  one structurally cannot repeat within a cycle, the other can and does.
- **Labels.** The doc's Output section (line 55) says issues are labelled `arch-review` plus one secondary label
  from `tech-debt, refactoring, code-quality, architecture, complexity, etc.`. The real label pipeline
  (`.claude/skills/arch-review/SKILL.md` step 5c, `.agents/arch-review.md`) is structural, not a free choice:
  every issue always gets `arch-review` + `agent` (required, applied by the skill's shell command, never by the
  model), plus exactly one topical label from `architecture, tech-debt, maintainability, design-patterns,
  antipattern, code-quality, duplication, documentation`, plus exactly one severity label from `critical, major,
  moderate, minor`. `refactoring` and `complexity` are not in the current allowlist — the skill's own comment
  notes a label outside the allowlist is silently dropped, not filed. A real filed issue confirms the current
  behaviour (issue #3891: labels `major, antipattern, agent, arch-review`).
- **Routine identity.** The doc still names Claude Code cloud routine `trig_01TDp4EDif36TJBkrMR2R7v2` as what
  fires the review daily at `0 23 * * *` UTC. Nothing inside this repository can confirm or deny that this
  specific routine ID is still the thing invoking `.claude/skills/arch-review`, since routine configuration lives
  in the Claude Code cloud product, not in the repo. There is also no in-repo scheduler (no GitHub Actions
  workflow, no cron config) that currently invokes `arch-review` — confirmed by inspecting `.github/workflows/`
  and `docs/routines/`. The one piece of repo-visible evidence is issue #3891, whose labels match the *current*
  skill's structural label set exactly, meaning whatever fires the routine today (this trigger ID or a successor)
  is already invoking the current mechanism, not the deterministic-rotation one the doc describes.
- **Historical context (not to be conflated with the fix).** An older, now-abandoned generation of this same idea
  existed as a `harness_v2` process (`docs/superpowers/plans/2026-07-30-heblo-arch-review-process.md`), targeting
  a completely different, non-cloud, non-Claude-Code-routine mechanism (`~/harness-root`, `GithubIssueTracker`,
  `harness:todo` label, on a different machine/user path). That plan is unrelated tooling history and out of
  scope for this fix; it is mentioned only so the architect/designer don't mistake it for the routine this doc
  describes.

Why it matters: this doc is the first place a developer would look to understand or debug the daily review
routine's observed behaviour (e.g. "why was the same part reviewed twice this week?"). As written, it actively
misleads — the described mechanism structurally forbids exactly the behaviour the real mechanism produces.

## Functional Requirements

### FR-1: Replace the "Module rotation" section with the current map-driven mechanism

Rewrite lines ~18–40 of `docs/routines/daily-arch-review.md` to describe:
- The selection surface is `docs/architecture/module-map.md` (52 parts across 4 groups: business domain,
  platform/cross-cutting, integration adapters, delivery/tooling), not a fixed 29-item business-module list.
- Selection is performed by `.claude/skills/arch-review/pick-module.sh`: **uniform random draw with replacement**
  over the map's live (non-`RETIRED`) summary-table rows — not a deterministic day-of-year formula.
- The practical consequence for a reader debugging behaviour: a part can be drawn more than once before every
  part has been drawn once; roughly two-thirds of parts are touched over a full cycle of runs (per
  `.claude/skills/arch-review/SKILL.md`'s own documented expectation), not a clean non-repeating 29-day cycle.
- Do not reproduce the full 52-row module table inline — point to `docs/architecture/module-map.md` as the source
  of truth instead of duplicating a list that will drift again. (Mirrors how the doc already treats other
  external truths, e.g. it does not inline the reviewer persona's full rule set today.)

**Acceptance criteria:**
- The rewritten section names `docs/architecture/module-map.md` as the module source and states its part count
  and four groups.
- The rewritten section states selection is uniform random with replacement, attributes it to
  `.claude/skills/arch-review/pick-module.sh`, and explicitly contrasts it with the old deterministic-rotation
  behaviour being replaced (so a reader who remembers the old doc understands what changed and why observed
  behaviour differs).
- No stale reference to "29 modules" or the day-of-year formula remains anywhere in the file.

### FR-2: Correct the "Output" section's label description

Rewrite line ~55 to describe the actual structural label pipeline:
- Every filed issue always carries `arch-review` and `agent` (applied by the skill's own shell command, not
  chosen by the model).
- Plus exactly one topical label from: `architecture, tech-debt, maintainability, design-patterns, antipattern,
  code-quality, duplication, documentation`.
- Plus exactly one severity label from: `critical, major, moderate, minor`.
- A label outside this allowlist is silently dropped, never filed as-is.

**Acceptance criteria:**
- `refactoring` and `complexity` no longer appear as example labels anywhere in the file.
- The four-label structure (2 required + 1 topical + 1 severity) is stated explicitly, not implied by an
  "etc." list.
- The source of the allowlist (`.claude/skills/arch-review/SKILL.md` step 5c and/or `.agents/arch-review.md`) is
  referenced so the doc doesn't drift out of sync again silently — a reader who suspects drift knows where to
  re-verify.

### FR-3: Reconcile "What it reviews" against the current reviewer persona

Verify each bullet in the existing "What it reviews" section (lines 42–51) against
`.agents/arch-review.md`'s actual review criteria (section "3. What counts as a finding" and the scope rules in
section "1. Your scope is one part, and only one part"). Correct or reframe anything that no longer matches —
in particular:
- The current persona scopes every finding to the single drawn part's `Owns:` paths from the map (not a
  cross-cutting sweep implied by the doc's current per-layer phrasing).
- The current persona's finding criteria are: a documented-rule violation, an outlier from this repo's prevailing
  architecture, a misuse of a depended-on technology, a best-practice error with a concretely stated consequence,
  or a duplicated invariant — grounded against normative documents the map names (or discovers) for that part,
  not a fixed checklist of "SOLID / KISS-YAGNI / Clean-Architecture-layer" categories asserted independently of
  what the part's own governing docs say.
- Keep this section's edits minimal: only correct claims that are actually wrong per the persona; do not restate
  the whole persona inline (the doc should point at `.agents/arch-review.md` as the source of truth, as it
  already does for the routine's operational mechanics).

**Acceptance criteria:**
- Every remaining claim in "What it reviews" is directly traceable to a sentence in `.agents/arch-review.md`.
- Any claim not supported by the current persona is either removed or rewritten to match.

### FR-4: Resolve or flag the routine-identity claim

Address the brief's stated ambiguity about whether `trig_01TDp4EDif36TJBkrMR2R7v2` is still the routine invoking
this mechanism:
- Keep the routine ID, schedule, and management links **as documented today**, since nothing in this repository
  contradicts them and issue #3891 is consistent with *some* routine already firing the current mechanism.
- Add an explicit note that the routine's prompt/mechanism was updated at some point to invoke
  `.claude/skills/arch-review` (module-map + `pick-module.sh` + `.agents/arch-review.md`) rather than the
  original deterministic logic, so a reader understands the routine ID is stable even though its underlying
  mechanism changed underneath it.
- Do not claim certainty this repo cannot support (e.g. do not assert the exact date the routine's prompt was
  updated, or assert with confidence that no other trigger could be involved).

**Acceptance criteria:**
- The routine ID/schedule/links are not removed or replaced without evidence.
- The doc no longer implies the routine ID's mechanism is the day-of-year rotation described in FR-1.
- The doc is honest about the boundary of what can be verified from inside this repository (routine config is
  external/cloud-side).

### FR-5: Leave "Managing the routine" and "Triage" sections as-is unless proven stale

These sections describe operational mechanics (pause/enable/delete link, manual trigger, triage query, staleness
policy) that were not flagged as inaccurate in the brief and were not found inaccurate during spec drafting.

**Acceptance criteria:**
- No change to these sections unless the architect/designer phase finds a concrete inaccuracy while implementing
  FR-1–FR-4 (e.g. a cross-reference that breaks once other sections are rewritten). If changed, the change must
  be traceable to a concrete found inaccuracy, not a stylistic rewrite.

## Non-Functional Requirements

### NFR-1: Scope discipline

This is a documentation-correction issue, not a feature. The fix touches only
`docs/routines/daily-arch-review.md`. No source code, skill, script, or persona file changes. No new files unless
the architect/designer phase determines the doc should be split or relocated — and if so, that must be justified
against the existing single-file structure, not assumed.

### NFR-2: Internal consistency

Every path, filename, and label referenced in the rewritten doc must exist and be spelled exactly as it appears
in the repo today: `docs/architecture/module-map.md`, `.claude/skills/arch-review/pick-module.sh`,
`.claude/skills/arch-review/SKILL.md`, `.agents/arch-review.md`. Do not invent or approximate paths.

## Data Model

Not applicable — this is a documentation fix with no code or data changes.

## API / Interface Design

Not applicable.

## Dependencies

- `docs/architecture/module-map.md` — source of truth for the module/part list (52 parts, 4 groups). The rewrite
  must describe it by reference, not duplicate its contents.
- `.claude/skills/arch-review/pick-module.sh` — source of truth for the selection algorithm.
- `.claude/skills/arch-review/SKILL.md` — source of truth for the label-filing pipeline (step 5c) and the
  "selection is uniform random with replacement" behavioural note.
- `.agents/arch-review.md` — source of truth for the reviewer persona's actual review criteria and the topical/
  severity label allowlists.

## Out of Scope

- Any change to `.claude/skills/arch-review/pick-module.sh`, `.claude/skills/arch-review/SKILL.md`,
  `.agents/arch-review.md`, or `docs/architecture/module-map.md` themselves — they are correct; only the doc
  describing them is wrong.
- Retiring or renaming `docs/routines/daily-arch-review.md`, or merging it into another doc — the brief offers
  retirement as an alternative to rewriting, but rewriting is the safer default absent evidence the routine
  itself is dead (see FR-4); retirement is not pursued in this spec.
- Any change to the actual cloud routine configuration (routine ID, schedule, prompt) — out of reach of this
  repository and out of scope for a documentation fix.
- The unrelated `harness_v2`-based historical plan (`docs/superpowers/plans/2026-07-30-heblo-arch-review-process.md`)
  — mentioned in Background for context only; not touched.

## Open Questions

- Is `trig_01TDp4EDif36TJBkrMR2R7v2` still the exact routine driving `.claude/skills/arch-review`, or has it been
  replaced by a different trigger/mechanism entirely? Not verifiable from inside this repository. **Assumption
  made for this spec (FR-4):** keep the documented routine ID/schedule as-is since nothing in-repo contradicts
  it, and note explicitly that its underlying mechanism was updated to the current skill-driven approach.

## Status: HAS_QUESTIONS
