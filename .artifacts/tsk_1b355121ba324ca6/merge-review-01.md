# Merge review — PR #3832

**PR title:** [arch-review] InvoiceClassification: rule reorder collides with the unique Order index and fails at runtime
**Base:** `main` · **Head:** `harness/tsk_7a9ca88de65c4f54` · **Closes:** #3831
**Stat:** 117 files changed, +5174 / −6 · mergeable: yes

## Verdict: REJECT

The intended fix is correct and well-tested. But the branch is stacked on ~49 commits
of an **entirely unrelated feature** that is not on `main`, so merging this PR would land
that whole feature into the default branch under a PR that describes only a rule-reorder
bugfix. That is a scope violation, and it includes files with real blast radius (a harness
installer + harness process/agent configs). I would not merge this myself without a human
splitting the two changes apart.

## What the PR claims vs. what it actually merges

The PR body describes exactly one change: rewriting `ReorderRulesAsync` to fix a unique-index
collision on rule reorder (Closes #3831). That part is real and good.

But `git diff origin/main...HEAD` (merge base `378d56c5`) shows the branch adds, on top of
that fix, the **complete "test-health routine" feature** — 49 commits, ~5000 lines — none of
which is on `main` and none of which is mentioned in the PR body:

- `docs/routines/test-health/` — `test-health-digest.sh` (627 lines), `gh-api.sh`,
  `rp-query.sh` and their `.test.sh` siblings — shell scripts that make **live GitHub REST
  and ReportPortal API calls**.
- `docs/routines/test-health/harness/` — `install.sh` (an **installer**),
  `test-health.process.json`, `test-health.agent.json` — **harness/automation configuration**.
- `docs/superpowers/plans/2026-08-02-test-health-routine.md` (1594 lines),
  `.../specs/2026-08-02-test-health-routine-design.md`.
- ~90 JSON fixture files under the routine's test tree.

This work belongs to a different task (`tsk_7a9ca88...`'s branch was cut from a base that
already contained the test-health series, rather than from clean `main`). The
InvoiceClassification commits (`50f61a29`…`ae4afb2a`) sit directly on top of the test-health
commits (`ad6e8001`…`33105809`). Merging #3832 as-is merges both.

Review rule #1 is explicit: *"Does the change do what its PR and issue say it does — no more,
no less? Unrelated scope is a reason to withhold, even when the code is good."* This PR does
far more than it says.

## The core fix (the part that IS in scope) — correct

For the record, I verified the actual InvoiceClassification change on its own:

- `ClassificationRuleRepository.ReorderRulesAsync` now redistributes the touched rows' own
  existing `Order` values via a two-phase disjoint-negative-offset update (phase 1 → negatives,
  phase 2 → sorted original values), two independent `SaveChangesAsync()` calls, **no explicit
  transaction**. This matches `architecture-01.md`'s corrected code block verbatim.
- The no-managed-transaction constraint is real and correctly honored: the repo's
  `scripts/check-no-managed-tx.sh` (wired into `ci-feature-branch.yml`) would fail any
  `BeginTransaction`/`UseTransaction`, because `ApplicationDbContext` uses a retrying
  `PollyExecutionStrategy` incompatible with caller-owned transactions.
- The algorithm is sound: `Order` is always ≥ 1 for real rows, so the negative sentinel range
  never collides; redistributing `{existing values}` instead of renumbering to `1..N` preserves
  the table-wide uniqueness invariant against untouched (e.g. inactive) rows. Both the ticket's
  transient collision and the latent inactive-row collision are addressed.
- New `ClassificationRuleRepositoryReorderIntegrationTests.cs` exercises a real Postgres unique
  index (3 cases: derangement, inactive-row non-collision, unknown-id tolerance) — exactly the
  coverage the ticket demanded (InMemory green tests explicitly not to be trusted).
- Contract unchanged (FR-6), other repository members unchanged (FR-5).

If this fix were on its own branch cut from `main`, it would be a straightforward approve.

## Why the scope issue is disqualifying, not cosmetic

- **Misrepresentation.** The PR body and issue #3831 give a reviewer (human or machine) no
  reason to expect 5000 lines of routine tooling. An unattended merge on my confidence would
  silently ship it.
- **Blast radius.** `harness/install.sh` + `test-health.process.json` / `.agent.json` are
  automation/config, and the shell scripts call external services (GitHub, ReportPortal). This
  is precisely the category the review checklist flags ("CI/release config, infrastructure") and
  none of it was reviewed under this task's plan/design/architecture/review chain — those
  artifacts only cover the InvoiceClassification fix.
- **Un-splittable by me.** I can't merge only the good part; the harness merges the PR as one
  unit. The right action is for a human to rebase the InvoiceClassification commits onto clean
  `main` (or land test-health via its own reviewed PR first), not to approve this bundle.

## Risks if merged as-is

- ~5000 lines of unrelated, out-of-band feature code enter `main` under a bugfix PR.
- Harness installer + process/agent config land unreviewed for this task.
- The test-health scripts (live API callers) become part of the default branch without the
  scrutiny an infra change warrants.

```json
{"confidence": 0.15, "reasoning": "The in-scope InvoiceClassification fix is correct and well-tested, but the branch is stacked on ~49 unrelated 'test-health routine' commits (~5000 lines, including a harness installer and process/agent configs) that are not on main and not mentioned in the PR body — merging this PR lands all of it. Unrelated scope with real blast radius is a withhold.", "risks": ["Merges an entire unrelated test-health feature (~5000 lines) into main under a bugfix PR that only mentions the reorder fix", "Includes harness installer + process/agent config JSON (automation blast radius) unreviewed by this task's plan/design/arch/review chain", "test-health shell scripts make live GitHub/ReportPortal API calls and would enter the default branch without infra-level review", "PR body materially misrepresents the change set"]}
```
