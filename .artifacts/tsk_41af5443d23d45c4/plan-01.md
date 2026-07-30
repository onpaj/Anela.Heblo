# Plan: Reconcile frontend feature-flag mirror with backend registry

## Summary

`frontend/src/features/feature-flags/featureFlags.ts` and
`backend/.../FeatureFlagRegistry.cs` have drifted: three frontend keys
(`TransportBoxTracking`, `StockTaking`, `BackgroundRefresh`) have no backend
registry entry, and the backend's two `DeliveredOrderCompletion` flags have no
frontend mirror. Fix by deleting the orphaned frontend constants and their
matching `appsettings.json` entries, and add a lightweight regression test so
the two lists can't silently diverge again.

## Context

`docs/development/feature-flags.md` names `FeatureFlagRegistry.cs` as the
single source of truth and specifies a 3-step add procedure (registry →
appsettings → frontend mirror) plus a lifecycle procedure for removal
(constant → registry entry → appsettings line → DB override → call sites).
The three drifted frontend keys are leftovers that were never deleted when
their backend counterparts were removed (or were added speculatively and
never wired up) — they are confirmed **unreferenced** anywhere in frontend
code (verified via grep for `FeatureFlagKeys.TransportBoxTracking` /
`.StockTaking` / `.BackgroundRefresh` — zero matches). Today this is inert:
`useFeatureFlag` silently falls back to its caller-supplied default because
`GET /api/feature-flags` only ever returns keys from `FeatureFlagRegistry.All`
(`EvaluateFlagsForClientHandler.cs:20`). But it is a live trap for the next
person who wires one of them up expecting it to be controllable from the
admin UI or `appsettings.json`.

## Functional requirements

**FR-1 — Remove orphaned frontend flag keys**
Delete `TransportBoxTracking`, `StockTaking`, and `BackgroundRefresh` from
`FeatureFlagKeys` in `frontend/src/features/feature-flags/featureFlags.ts`,
leaving only `LabelPrinting` (the one key that matches a backend registry
entry, `FeatureFlagKeys.LabelPrintingEnabled`).
- Acceptance: `featureFlags.ts` contains exactly one entry, `LabelPrinting:
  "is-label-printing-enabled"`; `npm run build` and `npm run lint` pass with
  no leftover references to the removed constants (grep for
  `TransportBoxTracking|StockTaking|BackgroundRefresh` under `frontend/src`
  returns no `FeatureFlagKeys.*` usages before and after — confirm no call
  site needs updating).

**FR-2 — Prune the matching orphaned `appsettings.json` entries**
Remove `is-transport-box-tracking-enabled`, `is-stock-taking-enabled`, and
`is-background-refresh-enabled` from the `FeatureManagement` section of
`backend/src/Anela.Heblo.API/appsettings.json` (the only appsettings file
that has them — `appsettings.Staging.json` only has `is-label-printing-enabled`
already).
- Acceptance: base `appsettings.json` `FeatureManagement` section contains
  exactly the three keys present in `FeatureFlagRegistry.All`
  (`is-delivered-order-completion-enabled`,
  `is-delivered-order-completion-test-source-enabled`,
  `is-label-printing-enabled`). `dotnet build` succeeds (no code binds to the
  removed config keys — confirm via grep before removing).

**FR-3 — Do not add frontend mirrors for `DeliveredOrderCompletion` /
`DeliveredOrderCompletionTestSource`**
These two backend flags gate a server-side background job
(`CompleteDeliveredOrdersJob`) only; there is no UI consumer today. Per the
"mirror when a UI needs it" principle in the docs, do not speculatively add
frontend constants for them — that would just create two more entries nobody
calls, the same class of problem this task is fixing. Note this explicitly as
a deliberate non-change so a reviewer doesn't flag it as incomplete.
- Acceptance: confirmed via grep that neither key is referenced anywhere in
  `frontend/src`; no frontend change made for these two flags.

**FR-4 — Guard against future drift**
Add an automated check that fails CI when the frontend mirror and backend
registry disagree, since nothing currently enforces this and the docs'
"mirror must match the registry" rule is currently just a convention. Two
viable shapes, pick the lighter one during implementation:
  (a) A backend test that reads `frontend/src/features/feature-flags/featureFlags.ts`
      (simple regex/string extraction, no TS parser needed) and asserts every
      value it declares exists in `FeatureFlagRegistry.All`'s key set — mirrors
      the existing style of `FeatureFlagsControllerLintTests.cs`.
  (b) The same check as a Jest/frontend test, asserting the frontend key set
      is a subset of a fixture list committed alongside (lower value, since it
      can't see the C# registry directly).
  Prefer (a): the backend test can read the frontend file by relative path
  without needing a build step, and keeps the "registry is source of truth,
  frontend must be a subset" invariant enforced from the source-of-truth side.
- Acceptance: new test fails on the current `main` (pre-fix) state if
  temporarily run against it, and passes after FR-1; test is added under
  `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/`.

**FR-5 — appsettings ↔ registry consistency (stretch, only if FR-4 lands
cleanly)**
Optionally extend the same or a sibling test to assert
`appsettings.json`'s `FeatureManagement` keys are exactly the registry's key
set (not just a superset) — this would have caught the orphaned entries this
task removes. Only do this if it doesn't require parsing environment-specific
appsettings overlays; scope to the base `appsettings.json` file only.
- Acceptance: test added or explicitly deferred with a one-line reason in the
  architecture step if it turns out environment-specific appsettings files
  make "exact match" too strict (e.g. `appsettings.Staging.json` already
  intentionally carries only a subset).

## Non-functional requirements

- No behavior change for any currently-working flag (`LabelPrinting`,
  `DeliveredOrderCompletion*`) — this is a pure deletion of dead mirror
  entries plus a test addition.
- No DB migration needed — confirm no `FeatureFlagOverrides` row exists for
  the three removed keys before merging (dev/staging DB check as part of the
  development step, not required for local build correctness since overrides
  for unregistered keys are simply never read).

## Data model

No entity/schema changes. `FeatureFlagDefinition` (backend) and
`FeatureFlagKeys` (frontend `const` object) are unchanged in shape — only
membership changes.

## Interfaces

No API contract changes. `GET /api/feature-flags` response shape is
unaffected (it already only returns registry-backed keys); the frontend
`FeatureFlagProvider` behavior is unaffected since it already only reads
whatever the endpoint returns.

## Dependencies and scope

**In scope:**
- `frontend/src/features/feature-flags/featureFlags.ts`
- `backend/src/Anela.Heblo.API/appsettings.json`
- One new backend test (drift guard)

**Out of scope (explicitly, per the issue's "do not implement here" framing
and to keep this change minimal):**
- Full codegen of frontend flag keys from the backend registry (the "generate
  so they cannot drift" idea in the issue's suggested direction) — heavier
  than needed given there's exactly one real flag consumed by the frontend
  today; revisit if the flag count grows.
- Checking/cleaning any `FeatureFlagOverrides` DB rows for the removed keys —
  flag for the development step to check staging/production DB state, but
  don't block this PR on it (orphaned override rows for non-registry keys are
  inert, same as the appsettings entries were).
- The stale plan doc reference at
  `docs/superpowers/plans/2026-05-21-openfeature-feature-flags.md` that also
  mentions `is-transport-box-tracking-enabled` etc. — it's a historical
  planning doc, not living documentation; leave as-is unless the development
  step finds it's actively misleading.

## Rough plan

1. Delete the three orphaned constants from `featureFlags.ts` (FR-1).
2. Delete the three matching lines from `appsettings.json` (FR-2).
3. Add the backend drift-guard test reading `featureFlags.ts` and comparing
   against `FeatureFlagRegistry.All` (FR-4); evaluate FR-5 as a follow-on if
   cheap.
4. Run `dotnet build`, `dotnet format`, `npm run build`, `npm run lint`, and
   the full backend test suite (new test plus regression).
5. Grep one more time across the whole repo (not just `frontend/src`) for the
   three removed key strings to make sure nothing else (e.g. E2E fixtures,
   other docs meant to stay current) references them.

## Open questions

- **Should `DeliveredOrderCompletion*` get frontend mirrors now, speculatively,
  in case an admin UI toggle is wanted later?** Default taken: no — mirror
  only when a consumer exists, per FR-3. If the requester actually wants UI
  visibility into these two job-control flags, that's a separate feature
  request, not a drift-cleanup.
- **Is FR-4's backend-reads-frontend-file approach acceptable, or does the
  team prefer a small Node/TS script run via `npm run lint` instead?** Default
  taken: backend test (FR-4 option a), consistent with the existing
  `FeatureFlagsControllerLintTests.cs` pattern already in the codebase and
  avoiding a second toolchain for a one-line assertion.
- **appsettings.json overlay semantics for FR-5** — `appsettings.Staging.json`
  currently carries only `is-label-printing-enabled`, which is presumably
  intentional (ASP.NET config layering means Staging inherits + overrides
  base). An "exact match" test on the base file only should be safe, but flag
  this for the architecture step to confirm assumptions about config layering
  before writing the assertion.
