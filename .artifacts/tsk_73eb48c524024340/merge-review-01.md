# Merge review — PR #3839

**Title:** [arch-review] MCP: tools serialize responses with default JsonSerializerOptions, emitting numeric enums that bypass the app-wide JsonStringEnumConverter
**Base:** `main`  · **Head:** `harness/tsk_9996477150f54ef9` · **Closes:** #3837
**Reported state:** OPEN, MERGEABLE, 130 files changed, +4766 / −45

## Verdict: REJECT

## Decisive finding — massive unrelated scope

The PR describes and is scoped to a single, well-contained change: centralize
`JsonStringEnumConverter` so MCP tools emit enums as strings (issue #3837). That
intended change is ~20 files: `Infrastructure/Json/McpJsonOptions.cs`, `Program.cs`,
7 `MCP/Tools/*.cs`, 7 `MCP/Tools/*Tests.cs`, and the 5 `.artifacts/` step docs.

But the actual diff against `origin/main` is **130 files, +4766 lines**. Beyond the
MCP fix and artifacts, it drags in **107 files of an entirely unrelated `test-health`
routine**:

```
docs/routines/test-health/README.md
docs/routines/test-health/gh-api.sh              (+ .test.sh)
docs/routines/test-health/rp-query.sh            (+ .test.sh)
docs/routines/test-health/test-health-digest.sh  (627 lines, + 305-line .test.sh)
docs/routines/test-health/harness/install.sh     (installer)
docs/routines/test-health/harness/*.json         (harness Process/Agent configs)
docs/routines/test-health/**/*.json              (~100 ReportPortal/GitHub API fixtures)
docs/superpowers/plans/2026-08-02-test-health-routine.md   (1594 lines)
docs/superpowers/specs/2026-08-02-test-health-routine-design.md (323 lines)
```

Verification:
- `git merge-base origin/main HEAD` == `origin/main` tip (`378d56c5`); none of the
  test-health tree exists on `origin/main` (`git ls-tree origin/main docs/routines/`
  shows no `test-health/`).
- `git log --first-parent origin/main..HEAD` shows ~40 `test-health` commits sitting
  **beneath** the MCP-serialization commits — this branch was cut from a point that
  already carried the whole test-health feature, which was never merged to `main`.
  The top `[land] merge main` commit merged `origin/main` in (hence MERGEABLE) but did
  nothing to remove the extra history.

Merging this unattended would publish an entire separate feature — including
CI/tooling shell scripts and a harness installer — onto `main` under the banner of a
JSON serialization fix. That is unreviewable-as-labeled scope and is exactly the kind
of blast-radius (CI/release tooling, a self-installing harness) that must not land
without a human's glance. Whether or not the test-health code is individually good, it
does not belong in this PR and was presumably intended for its own review.

Note: the upstream `review-01` step reported "done" only because it inspected the
single commit `dcd92d05` in isolation, not the full `origin/main...HEAD` diff, and so
never saw the 107 extraneous files. That is precisely the isolation trap this review
exists to catch.

## On the intended MCP change itself

The core fix (single `McpJsonOptions.Default` with one `JsonStringEnumConverter`,
referenced by `Program.cs` and all MCP serialize call sites, tests updated
symmetrically, plus a `ManufactureOrderState` regression assertion) appears sound and
matches the approved design. In a PR limited to those ~20 files I would look hard at it
and likely approve. But I cannot approve a merge that also carries 107 unrelated files,
regardless of the quality of the 20 I was asked about.

## Risks if merged
- ~4000 lines of unrelated `test-health` routine (scripts, installer, fixtures, a
  1594-line plan) land on `main` with no review of their content or intent.
- CI/tooling and a harness installer are introduced silently — real operational blast
  radius outside the stated scope.
- The PR's own review gate passed on an isolated-commit view, so the contamination is
  otherwise undetected.

## Recommendation
Rebuild this branch off current `origin/main` containing **only** the MCP
serialization change (and its artifacts), or cherry-pick the MCP commits onto a clean
branch, and re-open. Reject the current PR for unattended merge.

```json
{"confidence": 0.02, "reasoning": "PR claims a ~20-file MCP JSON serialization fix (issue #3837) but its diff against main is 130 files/+4766 lines, including 107 unrelated test-health routine files (scripts, a harness installer, ~100 fixtures, a 1594-line plan) that are not on main. Merging unattended would dump an entire unrelated feature with CI/tooling blast radius onto the default branch.", "risks": ["107 unrelated test-health files (~4000 lines) merged onto main under a serialization-fix label", "CI/tooling shell scripts and a self-installing harness introduced with no review of their content", "upstream review gate passed only because it inspected one commit in isolation, not the full base diff"]}
```
