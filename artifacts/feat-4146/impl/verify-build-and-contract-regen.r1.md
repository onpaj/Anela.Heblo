# Implementation: verify-build-and-contract-regen

## What was implemented

Verification-only task confirming the backend `[Required]` additions on
`CreateJournalEntryRequest.Title` and `UpdateJournalEntryRequest.Title`
build cleanly, pass the Journal test suite, and regenerate the frontend
OpenAPI client with `title` now marked required.

## Files created/modified

- `frontend/src/api/generated/api-client.ts` — regenerated (build output,
  not hand-edited). `title` on `CreateJournalEntryRequest`,
  `ICreateJournalEntryRequest`, `UpdateJournalEntryRequest`, and
  `IUpdateJournalEntryRequest` changed from optional (`title?: string;`)
  to required (`title!: string;` on classes, `title: string;` on
  interfaces), matching the existing `content!: string;` pattern.

## Tests

No new tests. Ran the existing Journal-scoped suite (108 tests,
`CreateJournalEntryHandlerTests` and `UpdateJournalEntryHandlerTests`
included) — all passed.

## How to verify

- `dotnet build` — succeeds, 0 errors. Backend build produces the same
  warning baseline as before this change (256 pre-existing warnings
  unrelated to the two `[Required]` additions; no warnings in either
  edited DTO file).
- `dotnet format --verify-no-changes` — exits 0, no formatting violations.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" -v minimal` —
  `Passed! - Failed: 0, Passed: 108, Skipped: 0, Total: 108`.
- `dotnet tool restore && cd backend/src/Anela.Heblo.API && dotnet msbuild -t:GenerateFrontendClientManual` —
  regenerates `frontend/src/api/generated/api-client.ts` from the
  rebuilt OpenAPI spec.
- `grep -n "title" frontend/src/api/generated/api-client.ts` around the
  `CreateJournalEntryRequest`/`UpdateJournalEntryRequest`
  classes/interfaces — confirms `title!: string;` (classes) and
  `title: string;` (interfaces), no more `title?: string;` for these two
  types.
- `npm run build` — compiles successfully.
- `npm run lint` — 236 pre-existing errors / 13 warnings across the
  codebase (mostly `testing-library` rules in unrelated test files), none
  attributable to this change; no lint output at all for
  `api-client.ts` or any Journal DTO/handler source file.

## Notes

- The task context's `cd backend && dotnet build`/`dotnet format`/`dotnet test`
  instructions assume a solution file directly under `backend/`. In this
  repo the solution (`Anela.Heblo.sln`) lives at the repo root, so the
  equivalent commands were run from the repo root instead. Same intent,
  adjusted working directory only — no scope change.
- `docs/development/api-client-generation.md` describes a `prebuild` npm
  script (`npm run generate-client`) that runs before `npm run build`.
  The frontend `package.json` in this checkout has no such `prebuild`
  script currently wired up (pre-existing doc/reality drift, out of scope
  for this task) — worked around per the docs' own "Manually regenerate"
  path: `dotnet tool restore` (nswag) then
  `dotnet msbuild -t:GenerateFrontendClientManual` directly, followed by
  `npm run build` to confirm the frontend still compiles against the
  regenerated client.
- The worktree had no `node_modules`; `npm ci` failed on a pre-existing
  `knip`/`@types/node` peer-dependency conflict unrelated to this change,
  so dependencies were installed with `npm install --legacy-peer-deps`,
  matching this repo's own CI workflows
  (`.github/workflows/ci-feature-branch.yml`,
  `ci-main-branch.yml`).
- The first `dotnet test --filter Journal` run hung (MSBuild node-reuse
  lock contention against the just-completed full `dotnet build`'s
  lingering build-server nodes); it was killed, `dotnet build-server
  shutdown` was run, and the test was re-run successfully with
  `-p:UseSharedCompilation=false /nodeReuse:false`. No code or config
  change resulted from this — environment-only.
- `frontend/src/api/generated/api-client.ts` is already tracked in git
  history from prior feature work, confirming this repo's convention is
  to commit the regenerated client — committed per Step 7.

## PR Summary

Verified that the two `[Required]` attribute additions on
`CreateJournalEntryRequest.Title` and `UpdateJournalEntryRequest.Title`
(from the two prior tasks in this feature) build cleanly, keep all 108
Journal tests passing, and regenerate the frontend OpenAPI client with
`title` correctly marked as a required (non-optional) field on both the
generated classes and their interfaces — closing out the contract-accuracy
requirement (NFR-1) from the spec.

### Changes
- `frontend/src/api/generated/api-client.ts` — regenerated; `title` on
  `CreateJournalEntryRequest`/`UpdateJournalEntryRequest` (and their
  `I...` interfaces) is now emitted as non-optional.

## Status
DONE
