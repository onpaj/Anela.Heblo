# `heblo-arch-review` — a scheduled, map-driven architecture review process

**Date:** 2026-07-30
**Status:** approved, ready for implementation
**Scope:** a new harness_v2 Process that reviews one randomly chosen part of the
application module map every two hours and files a GitHub issue per finding.

---

## Purpose

`docs/architecture/module-map.md` partitions the codebase into 52 stable, numbered
analysis parts, explicitly so it can be **iterated over**: pick a part, analyse it,
move on. Today that iteration is manual. This process automates it.

Every two hours the harness picks a part at random, runs a senior-architect review
scoped strictly to that part, and files one GitHub issue per architectural finding.
Findings carry `harness:todo`, so each one flows into the existing `development`
workflow on its own.

**Finding nothing is a correct and expected outcome.** Some parts are well written.
The process is designed so that a clean part costs one agent call and files nothing,
and so that nothing in the pipeline pressures the reviewer toward inventing findings.

## Prior art this must fit

This is not greenfield. `[arch-review]` issues are already live in the repository
(#3609, #3693, #3730, #3749, #3756, …), filed by hand or ad-hoc, with an
established convention the automated process adopts unchanged:

- Label `arch-review`, plus a topical label (`architecture`, `tech-debt`,
  `maintainability`, `design-patterns`, `antipattern`, `code-quality`,
  `duplication`) and a severity label (`critical` / `major` / `moderate` / `minor`).
- Title format `[arch-review] <Area>: <headline>`.

`harness-root/processes/telemetry-anomaly.json` is a structural twin already running
against this repository: a `command` check on a cron cadence, `per-interval` dedup,
`repository: Anela.Heblo`, and a persona whose prompt already says *"Typically 0–5
issues; zero is fine."* The new process follows the same shape, with one deliberate
upgrade — filing is done by the harness, not by the persona (see *Filing*, below).

## Architecture

```
every 2h  →  command check   →  workflow: arch-review  →  open-issue finisher
             (picker script)     (1 step, 1 persona)       (harness files 0..N issues)
```

Four new files. **No change to harness_v2 core.**

| File | Home | Versioned |
|---|---|---|
| `scripts/pick-module.sh` | this repo | yes |
| `processes/heblo-arch-review.json` | `~/harness-root` | no — recorded below |
| `workflows/arch-review.json` | `~/harness-root` | no — recorded below |
| `agents/arch-review.json` | `~/harness-root` | no — recorded below |

`~/harness-root` is not a git repository; it is machine-local operator config. The
full content of the three JSON files is therefore reproduced in this spec so the
design survives the machine.

---

## Component 1 — the picker (`scripts/pick-module.sh`)

Lives in this repository, next to the map it parses, so it moves with the map.

**Contract:**

1. `git -C <repo> pull --ff-only -q` first, so the review reads current `main`
   (the same opening move as `telemetry-anomaly.json`). **A pull failure is a
   warning, never fatal** — see below.
2. Parse the summary tables of `docs/architecture/module-map.md` for rows of the
   form `| <n> | <name> | …`.
3. Drop any row whose name contains `RETIRED`.
4. Pick one uniformly at random (`$RANDOM % count`).
5. Print **exactly one line** on stdout and exit 0:

   ```
   Architecture review of module map part #17 — Manufacture Inventory, Lots & Settings
   ```

6. On any failure — map missing, zero rows parsed, pull failure — exit non-zero and
   print nothing.

That printed line becomes the observation's `data.title`, which
`behaviors/agent.py` feeds to the persona as its request.

**Why a non-zero exit is the right failure mode:** `CommandCheck.evaluate()` yields
`[]` for a non-zero exit, so a broken picker produces no task at all. A parse bug
that silently emitted a malformed line would instead spend an agent call reviewing
nothing. Fail closed.

**But the `git pull` is the deliberate exception, and must never be fatal.**
`--ff-only` fails on any diverged clone — including one holding a single unpushed
local commit, which is an ordinary state for a working checkout. A fail-closed pull
would therefore let one local commit **silently stop the entire process forever**:
no task, no issue, no failure on the board, nothing in the log but a check quietly
returning `[]` every two hours. The pull is an optimisation; the map on disk is the
real input. On failure the picker warns to stderr and reviews the tree as it stands.
Verified against a synthetic diverged clone: warns, still prints a valid pick,
exits 0.

**Determinism of the row set:** the script must parse the summary tables (the
`| # | Part |` blocks), not the per-part `## N.` headings, because retired parts keep
their heading. Rows are collected in document order before the random index is drawn.

## Component 2 — the process (`processes/heblo-arch-review.json`)

```json
{
  "name": "heblo-arch-review",
  "trigger": {
    "interval": "7200s"
  },
  "action": {
    "check": "command",
    "params": {
      "command": "/Users/rem/Anela.Heblo/scripts/pick-module.sh",
      "timeout": 120
    }
  },
  "target": {
    "workflow": "arch-review"
  },
  "repository": "Anela.Heblo",
  "dedup": "per-interval",
  "sink": {
    "kind": "none"
  }
}
```

**`dedup: per-interval` is load-bearing.** `CommandCheck` sets each observation's
`state_key` to the printed line. Under `per-state`, `SourcePoller._seen` remembers
every line ever ingested, so the second time the picker chose a given part the fire
would be suppressed — permanently. Pure-random selection guarantees repeats, so
`per-state` would silently strangle the process within weeks, with no error anywhere.
`per-interval` keys on the 2h occurrence bucket and ignores `state_key`
(`drivers/scheduled_trigger.py`, `_dedup_key`).

`repository: Anela.Heblo` resolves through `harness-root/repos.json`, giving the step
a worktree the ordinary way (harness_v2 invariant #15).

## Component 3 — the workflow (`workflows/arch-review.json`)

```json
{
  "name": "arch-review",
  "start": "arch-review",
  "transitions": [
    {
      "from": "arch-review",
      "on": "findings",
      "to": "end",
      "hint": "one or more real violations found and drafted"
    },
    {
      "from": "arch-review",
      "on": "clean",
      "to": "end",
      "hint": "the part is sound — nothing worth filing"
    }
  ],
  "descriptions": {
    "arch-review": "review one module-map part against the documented architecture; draft an issue per finding"
  },
  "finishers": {
    "arch-review": {
      "kind": "open-issue",
      "label": "harness:todo",
      "allowed_labels": [
        "arch-review",
        "architecture",
        "tech-debt",
        "maintainability",
        "design-patterns",
        "antipattern",
        "code-quality",
        "duplication",
        "critical",
        "major",
        "moderate",
        "minor"
      ]
    }
  }
}
```

**One step, two terminal outcomes.** `findings` and `clean` both route to `end`;
the distinction exists so the board and the task history read honestly. A clean
part is a successful run, not an absence of one.

**No `from_step`.** Omitting it selects `OpenIssueBehavior`'s *wrapping* shape: the
step's own persona runs first, then the finisher parses that step's artifact and
files its drafts. harness_v2's own `CLAUDE.md` reserves this shape for exactly
"a future 0..N-issue architecture-review process".

### Filing, and the `harness:todo` guarantee

Every filed issue **must** carry `harness:todo`. This is a hard requirement, so it is
satisfied structurally rather than by instruction.

`harness:todo` is the finisher's **scope `label`**, not an `allowed_labels` entry,
because `GithubIssueTracker.open_issue` welds the scope label onto every issue it
creates (`all_labels = (*labels, scope_label)`). A persona that forgets every label
it was asked to supply still produces a correctly-labelled issue. Had `harness:todo`
been merely allowlisted, the guarantee would have rested on the LLM remembering it.

`arch-review` and the topical/severity labels come from the persona's own per-draft
`labels`, filtered against `allowed_labels`. A hallucinated label is dropped rather
than 422-ing the whole step.

**Known, accepted consequence:** the idempotency search
(`search_issue_by_marker`) scopes to *open* issues carrying the scope label, and the
`harness-todo` ingestion process swaps `harness:todo` → `harness:queued` within ~30s.
So a review task that failed and was restarted *after* that swap would refile its
drafts. This requires a task failure plus an operator restart, and the retry would
re-run the persona and likely produce different drafts anyway. Accepted. The durable
fix, if it ever bites, is an always-applied `labels` list on `OpenIssueBehavior`
(~10 lines plus a test in harness_v2) — deliberately not done now.

## Component 4 — the persona (`agents/arch-review.json`)

Named **`arch-review`**, not `architecture`: `harness-root/agents/architecture.json`
is already the forward-looking design step of the `development` workflow, and writing
that file would silently break it.

**Role.** A very senior architect reviewing existing architecture. Read-only. Never
edits code, never commits, never opens a PR, never creates a worktree or branch.

**Scope — the hard constraint.** The request names one part. The persona resolves
that part's `Owns:` paths from `docs/architecture/module-map.md` and every finding
must sit inside them. A defect in a dependency is out of scope unless *this* part
misuses it. This is the single most important instruction in the prompt: without it
a reviewer of part #2 wanders into part #1 and refiles the same catalog findings
every cycle.

**Normative corpus, read before judging:**

- `docs/architecture/development_guidelines.md` — **this is where ADR-001…ADR-006
  live.** There is no `docs/adr/` directory in this repository; a reviewer told to
  "check the ADRs" without this pointer will find none and fall back to generic
  opinion.
- `docs/📘 Architecture Documentation – MVP Work.md`
- `docs/architecture/filesystem.md`, `testing-strategy.md`, `localization.md`,
  `observability.md`, `DateTime_StandardizationGuide.md`, `Dev_Guidelines_time.md`
- root `CLAUDE.md`
- the part's own **Analysis notes** in the module map, which frequently point at the
  known soft spot.

**What counts as a finding:**

- a violation of a documented rule or an ADR;
- an outlier from the prevailing architecture — vertical slice organisation, MediatR
  use cases, the generated API client, the established persistence patterns;
- a misuse of a technology or framework;
- an architectural best-practice error with a concrete consequence.

**What does not:** style and naming preference, missing tests that are not themselves
an architectural gap, speculative refactors, anything already tracked.

**Finding nothing is a correct result.** Stated explicitly, with `clean` as a
first-class outcome. There is no target count and no implication that an empty review
is a failed one.

**Self-dedup before drafting.** Run
`gh issue list --repo onpaj/Anela.Heblo --label arch-review --state all --limit 200`
and drop any finding that matches an existing issue — **open or closed**. Open means
already filed; closed means already fixed, or already rejected, and refiling a
rejected finding is worse than missing a real one. This is the only defence against
cross-run duplicates (see *Known gaps*, G2).

**Output contract.** An analysis artifact ending in a mandatory fenced ```json array
of drafts — `[]` when clean. Each draft: `title` in the house format
`[arch-review] <Area>: <headline>`, a `body` carrying evidence (`file:line`), the rule
or ADR violated with a quotation, why it matters, and a suggested direction; `labels`
naming the topical and severity labels.

**The empty-block trap, hard-coded into the prompt:** an *empty* artifact parses to
zero drafts and settles cleanly, but a *non-empty* artifact with no fenced JSON block
raises `DraftError` and **fails the task** (`harness.issue_drafts.parse_drafts`). A
clean review must therefore still end with an explicit `[]`.

**Config:** `allowed_tools` `["Read", "Grep", "Glob", "Bash"]`, `model` `opus`,
`allowed_outcomes` **`["done"]`**.

> **Correction, found during implementation.** `allowed_outcomes` in an
> `agents/*.json` file is validated against `{done, request_changes}` only
> (`drivers/fs_agents.py:53`); `["findings", "clean"]` raises
> `AgentNotFound: unknown outcome 'findings'` at load. This is harmless: per
> invariant #42 the *workflow's* edges are the live vocabulary and the agent
> field is only the workflow-less fallback. The existing `heal` and `dedup`
> agents do exactly this — both declare `["done"]` while their workflow drives
> them with `file`/`skip` and `unique`/`duplicate`.

---

## Data flow

1. `SourcePoller` ticks; the compiled `ScheduledTrigger` sees a new 2h occurrence.
2. `CommandCheck` runs the picker → one `Observation`, `data.title` = the picked part.
3. The trigger mints a `Task` with `repository: Anela.Heblo`,
   `workflow_template: arch-review`, `dedup_key` = the occurrence bucket.
4. The dispatcher routes it to the `arch-review` step.
5. `OpenIssueBehavior` (wrapping) runs `ClaudeCliBehavior` with the `arch-review`
   persona in a fresh worktree; the persona writes its artifact and returns
   `findings` or `clean`.
6. The finisher parses the artifact and calls `IssueTracker.open_issue` once per
   draft — zero times for a clean part.
7. The worker commits the artifact; the dispatcher routes the outcome to `end`.
8. Each filed issue carries `harness:todo`; `processes/harness-todo.json` ingests it
   within 30s and it enters the `development` workflow on its own.

## Error handling

| Failure | Behaviour |
|---|---|
| Picker exits non-zero | no observation, no task, no cost. Next fire in 2h. |
| Picker times out (>120s) | same — `CommandCheck` returns `[]` on timeout. |
| Persona writes no artifact | zero drafts, settles `done`. Indistinguishable from clean. |
| Persona writes a report with no JSON block | `DraftError` → task fails into `failed/`. |
| `IssueTracker` HTTP failure | `IssueError` → task fails into `failed/`. |
| Task fails | `failed/` → the autoheal process claims it, and files a harness issue if it is a real fault. Guarded against recursion by the existing `<!-- harness-issue: -->` body marker. |

## Verification

Ordered so that nothing reaches GitHub until the pieces before it are proven.

1. **Picker, standalone** — run it ~200× and assert: exactly one line each time,
   exit 0, every part number seen is within the map's live set, no `RETIRED` row ever
   selected, and the distribution covers substantially all parts.
2. **Process compiles** — `compile_process` accepts the file; the harness starts with
   no `ProcessValidationError` and no `warning:` naming `arch-review`.
3. **Workflow validates** — the `open-issue` binding is accepted at startup;
   confirm no `UnknownFinisherKind` and no dropped-workflow warning.
4. **Dry run, clean path** — submit one task by hand against a part known to be
   sound; confirm the artifact ends in `[]`, the summary reads *"no issues to file"*,
   and nothing is created on GitHub.
5. **Dry run, findings path** — submit one task against a part with a known issue;
   confirm the issue is created, carries `harness:todo` **and** `arch-review`, and
   that `harness-todo` ingests it.
6. **Enable the process** and watch two consecutive fires.

Step 4 is the one that must not be skipped: the clean path is the common case, and
the `DraftError` trap makes it the easiest one to get wrong.

---

## Known gaps and accepted risks

**G1 — worktree disk growth. The material operational risk.**
`~/harness-root/worktrees` already holds **36 GB across 156 worktrees**, and nothing
ever removes them (harness_v2 invariant #30, deliberate: a harness-authored branch
must stay checked out in its original worktree). An Anela.Heblo working tree is
186 MB. Twelve reviews a day is **~2.2 GB/day, ~67 GB/month**, on top of every other
process on this machine. Against 307 GB free that is roughly four months to a full
disk — and the review needs the worktree only to *read* code it never modifies.

Mitigation, to be built alongside this process: a janitor `command`-check Process that
prunes worktrees belonging to tasks in `done/` or `archived/` older than N days. It
lives entirely in `~/harness-root`, touches no harness core, and does not violate
invariant #30, which constrains `src/harness`, not the operator. It must never touch a
worktree whose task is still live.

**G2 — no cross-run idempotency marker.** `marker_for()` is
`<task id>:<sha1(title)>`, so the harness cannot recognise the same finding across two
sweeps; every fire is a new task id. Covered by persona self-dedup against open *and*
closed issues, which is an instruction, not a guarantee. The durable fix is a
draft-supplied stable marker (`part-17:<sha1(title)>`) in harness_v2, deferred.

**G3 — automation reaches all the way to merge.** `harness:todo` means each finding
enters `development` and then `automerge`, which is **armed live** on this repository
(`dry_run: false`, confidence threshold 0.8, no required status checks). One agent's
architectural opinion can therefore become a merged PR with no human gate. This is
the explicit, twice-confirmed intent of the design; recorded here so the blast radius
is on the record rather than discovered later.

**G4 — coverage is lumpy.** Selection is uniform random *with replacement*, as
specified. Expect roughly two thirds of the 52 parts touched after a full 52-fire
cycle (~4.5 days), with some parts reviewed four or five times before others are seen
once. Uniform in the limit, uneven in any given week. A shuffled-cursor picker would
give full coverage every ~4.5 days and remains a drop-in replacement for the script if
the unevenness proves annoying.

**G5 — review quality is unmeasured.** Nothing in this design distinguishes a real
architectural finding from a plausible-sounding one, and G3 means a wrong finding can
reach `main`. The practical feedback loop is the closed-issue history the persona
reads during self-dedup: a rejected finding, once closed, is never refiled. That is
weak, and worth revisiting once there is a body of accepted and rejected findings to
judge from.
