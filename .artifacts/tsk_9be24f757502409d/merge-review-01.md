# Merge review — PR #3828

**Title:** [arch-review] Catalog: CatalogMergeService mutates the live cached aggregates in place…
**Closes:** #3827
**Base:** main · **Head:** harness/tsk_1113499843cb4e21
**Size:** 31 files, +3196 / −1 · mergeable: MERGEABLE

## Verdict: REJECT — unrelated scope

The PR body, title, and issue #3827 describe exactly one thing: make
`CatalogMergeService.Merge()` produce independent `CatalogAggregate`
snapshots (deep-copy) instead of mutating the live cached instances in
place. That part of the diff is small, correct, and well-scoped:

- `CatalogMergeService.cs` — one line: `products = catalogData.Select(p => p.Clone()).ToList();`
- `CatalogAggregate.Clone()` — MemberwiseClone + explicit deep copy of the
  mutable members (`Stock` record w/ new `Lots` list, `Properties` record,
  `ManufactureDifficultySettings.Clone()`, `StockTakingHistory` list, and the
  three history-summary objects with copied dictionaries). Matches the
  approved design in `.artifacts/.../design-01.md` and issue #3827's suggested
  direction.
- `ManufactureDifficultyConfiguration.Clone()` — copies `Settings` list.
- `CatalogMergeServiceTests.cs` — +193 lines of isolation tests.

If that were the whole PR, it would be a straightforward approve.

**It is not the whole PR.** The branch also carries a completely unrelated
feature — a "test-health routine" (ReportPortal/GitHub CI digest tooling):

- `docs/routines/test-health/*.sh` + `.test.sh` — ~800 lines of Bash
  (`gh-api.sh`, `rp-query.sh`, `test-health-digest.sh`, and their tests)
- `docs/routines/test-health/fixtures/**` — 15 cached RP/GitHub API JSON fixtures
- `docs/superpowers/plans/2026-08-02-test-health-routine.md` — 1453 lines
- `docs/superpowers/specs/2026-08-02-test-health-routine-design.md` — 320 lines

Total ≈ **2557 lines across 23 files** that have nothing to do with issue
#3827 or the Catalog cache. `git log origin/main..HEAD` shows ~18
`test-health` commits interleaved with the 6 Catalog commits on this one
branch; the files are not on `origin/main`, so they *would* land if merged.

## Why this is a rejection, not a nit

Criterion 1 of unattended review: "Does the change do what its PR and issue
say it does — no more, no less? Unrelated scope is a reason to withhold, even
when the code is good." This PR would silently merge an entire second feature
onto `main` under a title/description that mentions only the Catalog fix. That
feature was never the subject of this PR and is not reviewable *as this PR* —
the reviewer artifacts (plan/design/architecture/review) all cover only the
Catalog change. The test-health scripts are `docs/routines/` shell tooling
(not compiled into the app, so low runtime blast radius), but they are still
unreviewed-in-context and shell scripts that touch external APIs and the
filesystem cache; they deserve their own PR and their own review.

A human needs to split this branch: land the Catalog fix on its own, and take
the test-health routine through its own PR. That glance costs a human a
minute; merging a mislabeled 2557-line feature onto the default branch does
not.

## Notes on the core fix (for the eventual split)
- The deep-copy Clone() correctly handles the mutable members flagged in the
  issue and design; prior architecture/review steps report build + 801 tests
  + format all clean. I did not re-run them because the verdict does not
  depend on it — the scope violation is disqualifying regardless.
- Minor: `ManufactureDifficultyConfiguration.cs` still ends with no trailing
  newline (pre-existing).

```json
{"confidence": 0.1, "reasoning": "The core Catalog fix is correct and well-scoped, but the same PR bundles ~2557 lines of an entirely unrelated 'test-health routine' (shell scripts, fixtures, 1453-line plan, spec) that is not mentioned in the PR/issue #3827 and would silently land on main. Unrelated scope of this magnitude is a reject; a human must split the branch.", "risks": ["Merging lands a whole unrelated test-health/ReportPortal feature (~2557 lines, 23 files) onto main under a PR described only as a Catalog cache fix", "The test-health shell scripts and fixtures were never reviewed in the context of this PR", "Bundled scope makes the Catalog fix and the test-health feature impossible to revert independently"]}
```
