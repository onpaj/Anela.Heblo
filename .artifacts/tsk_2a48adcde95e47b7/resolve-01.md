# Resolve merge conflict — PR #3774

## Conflict

Single file, `both added` conflict type:
`docs/superpowers/specs/2026-07-30-heblo-arch-review-process-design.md`

Both the PR branch (`HEAD`) and `origin/main` independently added this file at the
same path. The document is otherwise byte-identical between the two sides; the three
conflict hunks (lines ~215, ~287, ~422 in the pre-resolution file) all had the same
shape: `HEAD` contributed nothing (an empty side of the conflict), while
`origin/main` carried additional paragraphs that were appended to the spec after the
PR branch diverged:

1. A paragraph documenting that harness_v2 1.7.0 adds a `labels` **floor** on
   `OpenIssueBehavior`, welding `arch-review` onto every filed issue.
2. The continuation of the "Output contract" sentence plus a long correction
   blockquote describing two live runs (#3768, #3770) where the persona omitted the
   `arch-review` label despite instructions, and how the floor from (1) fixes it
   structurally.
3. A "This gap is now measured, not theoretical" + "Search shape, corrected" addendum
   to **G2**, explaining that the same persona-reliability problem applies to
   self-dedup and correcting an earlier claim about issue-search paging strategy.

These are sequential corrections/updates to the same living spec document — `main`'s
copy is strictly the later, more complete revision (it references live GitHub issues
and a version upgrade that post-date the PR branch's copy). There was no independent
content on the `HEAD` side to merge in any of the three hunks.

## Resolution

Kept `origin/main`'s text for all three conflict hunks and removed HEAD's empty
sides, since HEAD contributed nothing unique in any hunk. Removed all `<<<<<<<`,
`=======`, `>>>>>>>` markers. Verified with `grep` that no markers remain.

Read the full resulting document end-to-end to confirm it reads coherently as one
continuous spec (no orphaned sentence fragments, no duplicated corrections).

This is a documentation-only markdown file — no code, no build/test surface. Ran
`grep` to confirm zero remaining conflict markers; that is the only verification
this file type requires. `git status` shows the file staged as `modified` with "All
conflicts fixed but you are still merging" — resolution is complete and ready for
the harness to commit.

## Outcome

Resolved cleanly; no ambiguity, no lost content from either side.
