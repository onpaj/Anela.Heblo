# Review: Consolidate GiftPackageManufacture to a single sync endpoint

## Scope of diff reviewed

`git diff b2ed892a HEAD -- backend frontend` (9 files, +7/-242 lines). This is the
actual scope of the task — the full `git diff main` also shows ~130 unrelated files
because `main` has advanced with other work (Photobank refactor, arch-review artifact
cleanup, etc.) since this branch forked; none of that is part of this change.

## Conformance to spec / architecture

The finding gave two options. The implementation chose **Option A** (remove the
misleading `/enqueue` endpoint), matching plan-01.md, design-01.md, and the approved
architecture-01.md exactly:

- Backend: `EnqueueGiftPackageManufactureHandler.cs`, `...Request.cs`, `...Response.cs`
  deleted; `LogisticsController.EnqueueGiftPackageManufacture` action, its route, XML
  doc comment, and the now-unused `using` all removed. The sync
  `POST /api/logistics/gift-packages/manufacture` endpoint is untouched and is now the
  sole entry point — matches the "honest" endpoint the finding recommended keeping.
- Frontend: `useEnqueueGiftPackageManufacture` hook removed; `GiftPackageManufacturingDetail`'s
  `onEnqueueManufacture` prop replaced by reusing `onManufacture` (same call shape,
  handler renamed `handleManufacture`); `index.tsx` rewires the "Zadat k výrobě" button
  onto the existing `useCreateGiftPackageManufacture`-backed handler and drops the
  enqueue mutation/import. Button label, icon, and disabled logic are unchanged, as
  design-01.md specified.
- Generated client: `api-client.ts` diff removes exactly the `EnqueueGiftPackageManufacture*`
  method/classes/interfaces, nothing else — consistent with development-01.md's explanation
  of a scoped extraction to avoid pulling in unrelated pre-existing NSwag drift, and matches
  the "surgical changes" project rule.
- Tests: `StockUpGate.test.tsx` updated to drop the enqueue mock/import; the three `it()`
  blocks that assert `useStockUpOperationsSummary` gating are otherwise unchanged.

## Completeness

Repo-wide grep for `EnqueueGiftPackageManufacture` across `backend/` and `frontend/src`
(and the whole worktree) returns zero hits — no dangling references, no orphaned DI
registration (MediatR handlers are assembly-scanned; `GiftPackageManufactureModule.cs`
never referenced the deleted handler by name), no stale imports.

## Correctness / verification performed independently

- `frontend`: `npx eslint` on all four changed files — clean.
- `frontend`: `npx react-scripts test --testPathPattern="GiftPackageManufacturing"` —
  **18/18 passed**, matching development-01.md's claim.
- `npx tsc --noEmit` in this sandbox fails, but the failure is entirely inside
  `node_modules/react-i18next/*.d.ts` (TS1139/TS1005 syntax errors in third-party type
  declarations, unrelated to any file touched by this diff) — a pre-existing environment/
  toolchain issue, not something introduced by this change. None of the errors reference
  any file in this diff.
- `dotnet` is not available in this review sandbox, so the backend build/test run could
  not be independently re-executed. The backend diff itself is small and mechanical
  (pure deletions plus one `using` removal), the deleted handler/request/response were
  verified to have zero remaining references anywhere in the tree, and development-01.md
  reports `dotnet build` (0 errors) and the Logistics/GiftPackage test filter (236/236
  passed) — consistent with a diff of this shape.

No functional requirement is unmet, no architecture conflict, no missing required test,
no logic bug found. This is a clean, minimal, purely subtractive fix that removes the
misleading async-sounding endpoint exactly as the finding's Option A specified.

## Outcome

done
