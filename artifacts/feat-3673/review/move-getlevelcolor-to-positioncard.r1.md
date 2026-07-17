# Code Review: Move getLevelColor from OrgChartPage into PositionCard

## Summary
The diff matches the task context exactly: `getLevelColor` was removed from `PositionCardProps` and from `OrgChartPage.tsx`, and re-added inside `PositionCard.tsx` as a private, non-exported module-level function with byte-identical switch/case logic. The recursive self-render and the `OrgChartPage` call site both drop the `getLevelColor` prop, tests drop the `stubLevelColor` stub, and the snapshot diff shows only the expected class-string changes (`level-N` → real Tailwind color classes). I independently re-ran `PositionCard.test.tsx` and confirmed both tests pass cleanly against the regenerated snapshot.

## Review Result: PASS

### task: move-getlevelcolor-to-positioncard
**Status:** PASS

## Docs to Update
(None — this is an internal-only refactor with no public-facing surface change.)

## Overall Notes
- Repo-wide grep for `getLevelColor` in `frontend/src` returns exactly two hits, both in `PositionCard.tsx`: the module-level definition and its single call site in the `className` template — no leftover references in `OrgChartPage.tsx` or the test file.
- Verified independently: `CI=true npx react-scripts test src/components/OrgChart/__tests__/PositionCard.test.tsx --watchAll=false` → 2/2 tests pass, 2/2 snapshots pass.
- `npx tsc --noEmit` in this worktree fails, but only inside `node_modules/react-i18next/*.d.ts` (TS5-only syntax against the project's pinned TypeScript 4.9.5), and `git diff` confirms `package.json`/`package-lock.json` are untouched by this change — this is a pre-existing environment/dependency issue, not something introduced by the diff, consistent with the implementer's own disclosure about the worktree's `node_modules` state.
- Lint pre-existing-debt claim in the implementation summary was not independently re-verified line-by-line, but the diff itself is small and mechanical enough (prop/const relocation, no new logic) that it's very unlikely to introduce new lint violations; not a blocker.
- Diff is scoped exactly to the four files named in the task context, with no stray edits to `OrgChartPage.tsx` data fetching, filtering, zoom, or connection-line logic.
