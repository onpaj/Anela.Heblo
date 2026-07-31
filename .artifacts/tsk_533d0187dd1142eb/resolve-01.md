# Merge conflict resolution — PR #3776

## Context

Merging `origin/main` into `harness/tsk_948b4012e7494490` produced a single
conflicted file:

- `docs/superpowers/specs/2026-07-30-heblo-arch-review-process-design.md`

Three conflict hunks (lines ~215, ~289–340, ~434 in the pre-resolution tree).

## Analysis

This is a narrative spec document that gets appended to in place as the
`arch-review` process it describes runs and is corrected. Comparing the full
HEAD vs. `origin/main` blobs (`git show <rev>:<path>`) showed `origin/main`'s
version is a strict evolution of HEAD's:

- Hunk 1 and hunk 3: HEAD's side was empty; `origin/main` added new
  paragraphs (a note on the `labels` floor added in harness_v2 1.7.0, and a
  "this gap is now measured" addendum to G2). Purely additive.
- Hunk 2: HEAD carried an earlier, shorter "Correction, found in the first
  live run" note (about issue #3768). `origin/main` carried a longer,
  updated note documenting that the first fix *didn't* hold (a second run,
  #3770, repeated the bug) and that the problem was ultimately resolved
  structurally via a harness_v2 upgrade. `origin/main`'s text supersedes
  HEAD's — it's the same narrative continued to its actual conclusion, not
  an independent edit — so no content from HEAD's shorter note is lost by
  preferring `origin/main`.

No other section of the file differs between the two versions.

## Resolution

Took `origin/main`'s full version of the file for all three hunks (verified
by diffing the resolved file against `git show origin/main:<path>` — exact
match, zero conflict markers remaining).

## Verification

- `git diff --diff-filter=U` — no unmerged paths remain.
- `dotnet build Anela.Heblo.sln` — 0 errors (250 pre-existing nullability
  warnings, unrelated to this change).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  --filter "FullyQualifiedName~FeatureFlag"` — 9/9 passed, including the
  new `FeatureFlagRegistryFrontendMirrorTests` carried in by the merge.
- The only other changes brought in by the merge
  (`backend/src/Anela.Heblo.API/appsettings.json`,
  `frontend/src/features/feature-flags/featureFlags.ts`, the new
  `FeatureFlagRegistryFrontendMirrorTests.cs`) were already staged
  cleanly by git with no conflicts — reviewed and left as-is (retired
  feature flags removed consistently on both sides of the config/registry
  pair).

Working tree is clean of conflict markers and ready to commit.
