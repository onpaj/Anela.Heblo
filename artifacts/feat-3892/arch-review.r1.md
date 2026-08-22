# Architecture Review: Correct `docs/routines/daily-arch-review.md` to describe the current map-driven arch-review mechanism

## Skip Design: true

No UI, component, or visual work is involved. This is a single-file Markdown documentation correction. The
designer phase should pass through with no design artifact of substance.

## Architectural Fit Assessment

This change fits cleanly into the codebase's existing pattern of self-documenting operational tooling under
`docs/routines/` (see `docs/routines/pr-autoabsorb/README.md`, `docs/routines/telemetry-anomaly/README.md`,
`docs/routines/test-health/README.md`, `docs/routines/weekly-coverage-gap.md` as siblings). Per
`docs/architecture/module-map.md`, `docs/routines/daily-arch-review.md` falls under **part #52 — "Documentation &
Agent Tooling"** (delivery/tooling group), which the map explicitly marks as "~docs only" — no code ownership, no
runtime dependency. There is nothing to integrate; the fix is entirely self-contained to one file. I independently
re-verified every factual claim in `spec.r1.md`'s Background section against the live sources
(`docs/architecture/module-map.md`, `.claude/skills/arch-review/pick-module.sh`,
`.claude/skills/arch-review/SKILL.md`, `.agents/arch-review.md`) and confirm the spec's account is accurate: 52
live parts in 4 groups, `pick-module.sh` line 125 does `index=$(( RANDOM % count + 1 ))` (uniform random with
replacement, no memory of prior draws), and `SKILL.md`'s step 5c constructs labels as `arch-review,agent,<topical
from persona>,<severity from persona>` on the command line — never sourced from the model's draft for the two
required labels. The spec's FR set is well-scoped and requires no architectural amendment.

## Proposed Architecture

### Component Overview

```
docs/routines/daily-arch-review.md   (the ONLY file this issue touches)
        │
        ├─ describes ──▶ .claude/skills/arch-review/pick-module.sh   (selection algorithm — read, not changed)
        ├─ describes ──▶ docs/architecture/module-map.md             (module surface — read, not changed)
        ├─ describes ──▶ .claude/skills/arch-review/SKILL.md         (label pipeline, step 5c — read, not changed)
        └─ describes ──▶ .agents/arch-review.md                      (reviewer persona / finding criteria — read, not changed)
```

There is no new component. The doc is a leaf node describing four existing, unmodified sources of truth. The
architectural task is entirely about *accuracy of description*, not structure.

### Key Design Decisions

#### Decision 1: Correct in place vs. retire the doc

**Options considered:**
- Retire `docs/routines/daily-arch-review.md` and fold its content into `.claude/skills/arch-review/SKILL.md` or
  `docs/architecture/module-map.md`.
- Rewrite it in place, keeping its current position and role in `docs/routines/`.

**Chosen approach:** rewrite in place, per spec FR-1–FR-5. Confirmed by inspection: this is the only
`docs/routines/` file describing this routine, its sibling routine docs (`pr-autoabsorb`, `telemetry-anomaly`,
`test-health`, `weekly-coverage-gap`) follow the same pattern of one doc per routine, and nothing in the repo
treats `docs/routines/*` as generated or derived from the skill files. There is no evidence the routine itself is
dead (spec FR-4 already covers this honestly as an open, un-resolvable-from-repo question). Folding this into
`SKILL.md` would blur the skill's own contract (which is repository-agnostic and already serves other consuming
repos) with routine-specific operational facts (routine ID, schedule, triage link) that only make sense for
*this* repo's Claude Code cloud configuration. Keep the boundary: `SKILL.md`/`.agents/arch-review.md` are the
skill's contract; `docs/routines/daily-arch-review.md` is this repo's record of how that skill is scheduled and
triaged here.

**Rationale:** minimizes blast radius (spec's own NFR-1, scope discipline), matches the existing sibling-doc
convention, and avoids inventing a new documentation location for a fix whose brief never asked for restructuring.

#### Decision 2: How much to point-at vs. inline

**Options considered:**
- Inline the full current mechanism (copy the 52-row table, copy the label allowlists, copy persona criteria)
  so the doc is self-contained.
- Point at the source-of-truth files and describe the mechanism at a level that stays correct even as those
  files evolve in detail (counts, exact row names).

**Chosen approach:** point-at, per spec FR-1's explicit instruction not to duplicate the 52-row table. State the
*mechanism* (map-driven, N parts across 4 groups, random-with-replacement, structural label pipeline) and
reference the authoritative files by path for exact current detail.

**Rationale:** this is exactly why the doc drifted the first time — it inlined a snapshot (29 modules, deterministic
formula, an illustrative label list) that then went stale silently when the underlying mechanism changed and
nobody had a reason to revisit this doc. A description that names its sources and states the *kind* of mechanism
(vs. a frozen snapshot of it) degrades gracefully: if `module-map.md` grows to 60 parts next quarter, the doc
stays true without edits. Where a concrete number is still useful for a reader (e.g. "52 parts" as of today), state
it but attribute it to the map explicitly so a future reader knows to re-check the map rather than trust the
number blindly.

## Implementation Guidance

### Directory / Module Structure

No new files. Edit only `docs/routines/daily-arch-review.md` in place. Do not touch:
- `.claude/skills/arch-review/pick-module.sh`
- `.claude/skills/arch-review/SKILL.md`
- `.agents/arch-review.md`
- `docs/architecture/module-map.md`

These are correct today; the spec's Out of Scope section already excludes them and I concur — I found no defect
in any of them during verification, only a stale description of them.

### Interfaces and Contracts

Not applicable — no code, no API, no schema. The only "contract" at play is that the doc's claims must trace to
a sentence in one of the four source files, per spec FR-1–FR-3's acceptance criteria. Developer implementing this
should, for every factual sentence added or kept, be able to point at the exact source line it comes from.

### Data Flow

Not applicable in the runtime sense. The only "flow" worth stating for the implementer: a reader lands on this
doc → follows it to `pick-module.sh` / `module-map.md` / `SKILL.md` / `arch-review.md` for authoritative detail →
returns to this doc for the operational facts those files don't carry (routine ID, schedule, triage query, pause/
enable link). The rewritten doc should preserve that entry-point role rather than trying to become a second copy
of the skill's contract.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Rewrite re-introduces a frozen snapshot (e.g. hardcodes "52 parts" as a fact rather than "as of the map's current state") and drifts again next time the map is resized. | Moderate | Follow Decision 2: state mechanism + point at source file for exact current numbers; avoid re-inlining a table that will go stale. |
| Doc ends up asserting something about the cloud routine (`trig_01TDp4EDif36TJBkrMR2R7v2`) that cannot be verified from this repo, overstating confidence. | Minor | Follow spec FR-4 exactly: keep routine ID/schedule as documented, add the "mechanism updated underneath a stable ID" note, do not assert certainty about what currently invokes the skill. |
| Scope creep into rewriting `.agents/arch-review.md`-equivalent detail inline (duplicating the persona's finding criteria at length) instead of correcting only what's wrong in "What it reviews". | Minor | FR-3 already caps this: only correct claims that don't match the persona, keep the section pointing at `.agents/arch-review.md` as the source of truth, do not paste the persona's finding criteria wholesale. |
| Editing "Managing the routine" / "Triage" sections speculatively even though the brief and spec found no inaccuracy there. | Minor | Spec FR-5 already restricts this to concrete found inaccuracies only. No architectural reason to touch them; leave as-is unless implementation surfaces a real break. |

## Specification Amendments

None. `spec.r1.md`'s FR-1 through FR-5, NFR-1/NFR-2, Dependencies, and Out of Scope sections are architecturally
sound and require no change. The two Decisions above (rewrite-in-place, point-at-not-inline) formalize choices the
spec already implied but did not explicitly justify as architecture — no contradiction, just explicit rationale
for the implementer.

## Prerequisites

None. All four source-of-truth files the doc must describe already exist and are stable in this worktree; no
migration, config, or infrastructure change is needed before implementation can start.
