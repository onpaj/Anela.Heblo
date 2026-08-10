# Architecture Review: Remove dead `useTransportBoxTransitions` hook and its orphaned React Query plumbing

## Skip Design: true

No UI component, screen, layout, or visual decision changes. `TransportBoxActions.tsx` — the only renderer of transition buttons — is explicitly untouched, and the deleted code never executed. This is a pure frontend dead-code removal plus a one-line documentation edit. Nothing for a designer to do.

## Architectural Fit Assessment

**The feature aligns with the codebase's existing, working design and removes the deviation from it.**

Verified against the tree at `/home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti` (branch `feature/3889-Arch-Review-Transportboxes-Usetransportboxtransiti`):

| Spec claim | Verified | Evidence in this worktree |
|---|---|---|
| Backend has no `allowed-transitions` route | ✅ | `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` — `[Route("api/transport-boxes")]` at line 21 with exactly 10 actions (lines 37, 49, 61, 80, 95, 115, 136, 162, 183, 202). Repo-wide grep for `allowed-transitions` across `backend/src/**/*.cs` returns zero hits. |
| Transitions ship inline on the DTO | ✅ | `backend/src/Anela.Heblo.Application/Features/Logistics/TransportBoxMappingProfile.cs:16` maps `src.TransitionNode.GetAllTransitions()` → `TransportBoxDto.AllowedTransitions` (`.../Logistics/Contracts/TransportBoxDto.cs:25`) for every projection. |
| The hook has zero importers | ✅ | The only non-artifact references to `useTransportBoxTransitions` / `useAllowedTransitionsQuery` / `GetAllowedTransitionsResponse` are inside the file itself (lines 17, 24, 30) and `docs/architecture/module-map.md:258`. No `frontend/src/api/hooks/index.ts` barrel exists. |
| Orphaned query-key plumbing | ✅ | `frontend/src/api/client.ts:490` (`transportBoxTransitions: ["transportBoxTransitions"] as const`), consumed only by `frontend/src/api/hooks/useTransportBoxes.ts:189` and the dead hook at line 29. |
| `TransportBoxActions.tsx` reads the DTO field | ✅ | `frontend/src/components/transport/box-detail/TransportBoxActions.tsx:14-24` filters `transportBox.allowedTransitions` on `transitionType === "Previous"`. |
| Contract mismatch | ✅ | Hook declares `{ state, label, requiresCondition, conditionDescription? }`; the real generated `TransportBoxTransitionDto` is `{ newState, transitionType, systemOnly, label }`. The hook encodes an API shape that never existed. |
| No dead-export tooling | ✅ | `frontend/package.json` has no `knip`, `ts-prune`, or `depcheck`. |

**Integration points (all read-only or delete-only):**

1. `frontend/src/api/client.ts` — the shared `QUERY_KEYS` const (~40 keys). One entry removed. This file is imported by most hooks, so it is the sole conflict-prone surface.
2. `frontend/src/api/hooks/useTransportBoxes.ts` — the `useChangeTransportBoxState` mutation's `onSuccess` cache-invalidation block (lines 176-200). One of five invalidations removed; the other four plus the `refetchQueries` are load-bearing and stay.
3. Three Jest `jest.mock("../../client")` literals that hand-stub `QUERY_KEYS`.
4. `docs/architecture/module-map.md:258` — module #7 (Transport Boxes) "Owns" list.
5. **Backend: not touched at all.** No OpenAPI regeneration, no migration, no `dotnet` work.

**Fit verdict:** the project's established pattern is *one* source of truth for allowed transitions, computed server-side by `TransportBoxStateNode` and delivered inline on every box DTO. The dead hook is the only artefact suggesting a second, HTTP-level source. Removing it makes the architecture match the documentation and the running code. There is no architectural tension to resolve — only scope and sequencing to pin down.

One conventions note: `ApiClientWithInternals` is **redeclared locally in each of the 7 files that use it** (verified: `useTransportBoxes.ts:15`, `TransportBoxTypes.tsx:15`, `useBoxFill.ts`, `usePackingUsers.ts`, `printLabelPdf.ts`, `TransportBoxDetail.tsx`, and the dead hook). Deleting the hook therefore leaves no dangling shared type — the other six copies are independent.

## Proposed Architecture

### Component Overview

```
BEFORE
──────
  TransportBoxStateNode.GetAllTransitions()          [domain, in-memory]
            │
            ▼ AutoMapper — TransportBoxMappingProfile:16
  TransportBoxDto.AllowedTransitions                 [Application/Contracts]
            │
            ▼ NSwag → frontend/src/api/generated/api-client.ts
  TransportBoxDto (TS).allowedTransitions
            │
            ▼
  TransportBoxActions.tsx  ── renders "Zpět" / forward buttons   ← THE LIVE PATH

  ┌─ DEAD PARALLEL PATH ─────────────────────────────────────────────┐
  │  useTransportBoxTransitions.ts                                    │
  │    useAllowedTransitionsQuery(boxId)                              │
  │      queryKey: [...QUERY_KEYS.transportBoxTransitions, boxId] ────┼──┐
  │      GET {baseUrl}/api/transport-boxes/{boxId}/allowed-transitions│  │
  │                     └── 404: route never implemented              │  │
  │      returns GetAllowedTransitionsResponse                        │  │
  │               └── fictional shape (state/requiresCondition/…)     │  │
  │  consumers: NONE                                                  │  │
  └───────────────────────────────────────────────────────────────────┘  │
                                                                          │
  client.ts:490  QUERY_KEYS.transportBoxTransitions  ────────────────────┘
        │
        └─► useTransportBoxes.ts:187-190  invalidateQueries(...)   ← no-op forever
                (nothing ever registers a query under this key)
        │
        └─► 3 jest.mock QUERY_KEYS literals (test doubles only)

  docs/architecture/module-map.md:258 — lists the file under module #7


AFTER
─────
  TransportBoxStateNode.GetAllTransitions()
            │
            ▼ AutoMapper (unchanged)
  TransportBoxDto.AllowedTransitions
            │
            ▼ generated client (unchanged, not regenerated)
  TransportBoxActions.tsx  ── unchanged, still the only renderer

  The dead parallel path and every reference to it: gone.
  useTransportBoxes.ts onSuccess retains exactly 4 invalidations + 1 refetch:
      transportBoxKeys.detail(boxId)
      transportBoxKeys.lists()
      [...QUERY_KEYS.transportBox, "summary"]
      [...QUERY_KEYS.transportBox, "byCode"]
      refetchQueries(transportBoxKeys.detail(boxId))
```

### Key Design Decisions

#### Decision 1: Delete the hook; do not implement the missing endpoint

**Options considered:**
- (a) Delete the hook, keep the DTO-embedded field as the single source of truth.
- (b) Implement `GET /api/transport-boxes/{id}/allowed-transitions` so the hook works.
- (c) Leave the file with a `@deprecated` comment.

**Chosen approach:** (a) — delete.

**Rationale:** the transition set is a pure function of the box's current state, computed by `TransportBoxStateNode` and already materialised on every `TransportBoxDto` projection at zero marginal cost. A dedicated endpoint would be a second read path for identical data, meaning a second contract to keep in sync and a second cache to invalidate on every state change — precisely the class of duplication that produced this finding. Option (c) preserves the copy-paste hazard, which is the whole reason the finding was filed. Nothing in the codebase consumes or needs a standalone fetch, and no UI flow exists where a box is on screen without its DTO.

#### Decision 2: Full plumbing removal is in scope — FR-2 and FR-3 stand (resolves Open Question 1)

**Options considered:**
- (a) Strictly minimal: delete one file, leave `QUERY_KEYS.transportBoxTransitions`, its invalidation call site, and the three test mock entries in place.
- (b) Delete the file plus the key and its single call site (FR-2), leave the test mocks (FR-3 dropped).
- (c) Full removal: file + key + call site + all three test mock entries.

**Chosen approach:** (c) — the spec as written.

**Rationale:** the finding is not "a file is unused"; it is "the repo carries a coherent-looking mechanism that cannot work". Option (a) leaves the misleading signal intact in a more insidious form: a `QUERY_KEYS` member whose only remaining evidence of purpose is a comment in `useTransportBoxes.ts` reading *"Also invalidate any transition-related queries"* — an invalidation for queries that can no longer exist even in principle. The next developer would either restore a hook to match it or spend time proving it dead again. Option (b) leaves three test files advertising a key that no longer exists, which quietly breaks the mock-vs-reality correspondence those literals depend on. All four removals are mechanically trivial, provably behaviour-neutral (React Query's `invalidateQueries` over a key with no registered observers is a no-op), and reviewable in a single sitting. Scope discipline here means *removing the whole dead mechanism*, not *touching the fewest files*.

#### Decision 3: All edits land in one atomic commit, in dependency order

**Options considered:**
- (a) Split into per-FR commits (delete file / remove key / clean mocks / update doc).
- (b) One commit covering FR-1 through FR-4.

**Chosen approach:** (b) — single commit, and the ordering constraint below is **mandatory** regardless.

**Rationale:** the pieces are not independently safe. `useTransportBoxes.ts:189` spreads the key: `[...QUERY_KEYS.transportBoxTransitions, variables.boxId]`. If FR-3 (removing `transportBoxTransitions` from the three `jest.mock` `QUERY_KEYS` literals) lands while FR-2's source-side removal has not, that spread evaluates `[...undefined]` inside the mutation's `onSuccess` and throws a `TypeError` at test runtime. Symmetrically, removing `client.ts:490` without removing the call site is a hard TypeScript compile failure. The safe order within the change is:

1. Remove the `invalidateQueries` block at `useTransportBoxes.ts:186-190` (the only consumer).
2. Remove `client.ts:490`.
3. Delete `frontend/src/api/hooks/useTransportBoxTransitions.ts`.
4. Remove the key from the three test mock literals.
5. Edit `docs/architecture/module-map.md:258`.

Steps 1-4 must not be split across commits that could be independently checked out or reverted.

#### Decision 4: Module map gets a line edit, not a RETIRED marker

**Options considered:**
- (a) Mark module #7 (or part of it) RETIRED per `docs/architecture/module-map-maintenance.md`.
- (b) Edit the "Owns" bullet at line 258 in place.

**Chosen approach:** (b).

**Rationale:** `module-map-maintenance.md` reserves RETIRED markers for *parts* removed from the codebase — "Part numbers are permanent identifiers. Never reuse, never renumber." A single file dropping out of a part's "Owns" list is an ordinary line edit under Step 3 ("Find dead references": *paths the map claims exist but don't*). Module #7 keeps its number, title, size band, routes, dependencies, summary-table row, and every other bullet. The removal is ~46 LOC out of a multi-thousand-LOC part, so no re-sizing, split, or merge threshold is crossed. No other section of the map needs touching.

#### Decision 5: Gate on frontend build + lint + unit suite; do not gate on E2E (resolves Open Question 3)

**Options considered:**
- (a) Block the PR on a manual `./scripts/run-playwright-tests.sh` run.
- (b) Gate on `npm run build`, `npm run lint`, and the frontend Jest suite; let the nightly E2E run cover regression.

**Chosen approach:** (b).

**Rationale:** this is not a preference — `scripts/run-playwright-tests.sh:27` hardcodes `STAGING_URL="https://heblo.stg.anela.cz"` and exports it as `PLAYWRIGHT_BASE_URL` (line 77), and `docs/architecture/testing-strategy.md:248-251` states the suite "MUST ALWAYS use the deployed staging environment" with "no code path that targets ports 3001/5001". A pre-merge E2E run therefore exercises *the currently deployed staging build*, not the PR branch — it can neither confirm nor refute this change. Combined with the fact that the deleted code is unreachable (zero importers, no route), an E2E gate would produce a green signal carrying no information about the change. The CI feature-branch workflow (`.github/workflows/ci-feature-branch.yml:45`) already runs `npm test -- --coverage --watchAll=false`, which is the meaningful gate. The nightly staging run remains the regression backstop.

#### Decision 6: File a follow-up issue for dead-export tooling; do not add it here (resolves Open Question 2)

**Options considered:**
- (a) Add `knip` or `ts-prune` to the frontend toolchain in this PR.
- (b) Keep it out of scope and silently drop it.
- (c) Keep it out of scope and file a tracked follow-up issue.

**Chosen approach:** (c).

**Rationale:** adding a dead-export detector changes CI behaviour repo-wide and will almost certainly surface a backlog of existing unused exports across a ~40-key `QUERY_KEYS` and hundreds of modules — an unbounded remediation scope that would swallow a 5-line deletion PR. But dropping it silently guarantees the same finding recurs; the systemic cause of this bug is precisely that nothing detects unreachable modules. The follow-up issue is the deliverable that closes the loop without coupling the two changes. It should be filed via `gh issue create` (per `CLAUDE.md`: GitHub access via `gh` CLI only, never MCP GitHub tools) and referenced from the PR description. **The follow-up issue is a required output of this work item, not an optional nicety.**

## Implementation Guidance

### Directory / Module Structure

**Files deleted (1):**
```
frontend/src/api/hooks/useTransportBoxTransitions.ts        (49 lines — delete entirely)
```

**Files edited (5):**
```
frontend/src/api/hooks/useTransportBoxes.ts                 remove lines 187-190 + the preceding comment (186)
frontend/src/api/client.ts                                  remove line 490
frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts  remove line 17
frontend/src/components/pages/__tests__/TransportBoxList.test.tsx           remove line 71
frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx  remove line 60
docs/architecture/module-map.md                             edit line 258
```

**Files created: none.** No new hook, no replacement module, no new test file. A dead-code removal that adds a file has failed.

**Explicitly not touched:**
- All of `backend/` — read-only for this work. No `dotnet build` / `dotnet format` requirement beyond confirming nothing changed.
- `frontend/src/api/generated/api-client.ts` — generated, and the backend contract is unchanged. Never hand-edit.
- `frontend/src/components/transport/box-detail/TransportBoxActions.tsx` and every other transport UI component.
- `docs/superpowers/plans/**` — point-in-time records. Note: the spec names the wrong files here; see Amendment A-3.
- The other six local `ApiClientWithInternals` declarations.

### Interfaces and Contracts

**Removed from the frontend public surface** (all with zero external consumers — not a breaking change for any caller):

| Symbol | Location | Kind |
|---|---|---|
| `useAllowedTransitionsQuery` | `useTransportBoxTransitions.ts:24` | React Query hook |
| `AllowedTransition` | `useTransportBoxTransitions.ts:10` | TS interface (fictional contract) |
| `GetAllowedTransitionsResponse` | `useTransportBoxTransitions.ts:17` | TS interface (fictional contract) |
| `ApiClientWithInternals` (this copy) | `useTransportBoxTransitions.ts:5` | file-local interface |
| `QUERY_KEYS.transportBoxTransitions` | `client.ts:490` | query-key const member |

**Unchanged and load-bearing — the contract implementers must preserve:**

```ts
// frontend/src/api/hooks/useTransportBoxes.ts:45-52  — keep exactly as-is
const transportBoxKeys = {
  all:     QUERY_KEYS.transportBox,
  lists:   () => [...QUERY_KEYS.transportBox, "list"] as const,
  list:    (filters: GetTransportBoxesRequest) => [...QUERY_KEYS.transportBox, "list", filters] as const,
  details: () => [...QUERY_KEYS.transportBox, "detail"] as const,
  detail:  (id: number) => [...QUERY_KEYS.transportBox, "detail", id] as const,
};
```

The `useChangeTransportBoxState` `onSuccess` handler must retain, after the edit, exactly these five cache operations and no others:
`invalidateQueries(transportBoxKeys.detail(boxId))`, `invalidateQueries(transportBoxKeys.lists())`, `invalidateQueries([...QUERY_KEYS.transportBox, "summary"])`, `invalidateQueries([...QUERY_KEYS.transportBox, 'byCode'])`, `refetchQueries(transportBoxKeys.detail(boxId))`.

**Backend contract — unchanged, single source of truth:**
```
TransportBoxDto.AllowedTransitions : IList<TransportBoxTransitionDto>
TransportBoxTransitionDto = { newState, transitionType, systemOnly, label }
```
`TransportBoxController` keeps its 10 actions. No OpenAPI regeneration.

**Module map target text** (`docs/architecture/module-map.md:258`):
```diff
-- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`, `useTransportBoxTransitions.ts`
+- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`
```

### Data Flow

**Use case 1 — render transition buttons on box detail (unchanged, must not regress):**
```
User opens /transport-boxes/{id}
  → useTransportBoxDetail  →  ApiClient.transportBox_GetTransportBoxById(id)
  → GET /api/transport-boxes/{id}
  → handler loads TransportBox (EF) → AutoMapper (TransportBoxMappingProfile:16)
        TransitionNode.GetAllTransitions() → dto.AllowedTransitions
  → TransportBoxDto over the wire, cached under transportBoxKeys.detail(id)
  → TransportBoxActions.tsx partitions allowedTransitions:
        transitionType === "Previous"  → "Zpět" buttons
        transitionType !== "Previous"  → forward buttons
```
No network call is made for transitions at any point — before or after this change.

**Use case 2 — state change and cache refresh (one invalidation removed, behaviour identical):**
```
Click a transition button
  → useChangeTransportBoxState.mutate({ boxId, newState, ... })
  → PUT /api/transport-boxes/{boxId}/state
  → onSuccess:
        invalidate detail(boxId), lists(), [transport-boxes,"summary"], [transport-boxes,'byCode']
        (REMOVED: invalidate [transportBoxTransitions, boxId] — zero registered observers, always a no-op)
        refetch detail(boxId)
  → refetched DTO carries the NEW state's AllowedTransitions
  → TransportBoxActions re-renders with the new button set
```
The removed line cannot affect this flow: React Query's `invalidateQueries` marks matching cached queries stale and refetches active ones. No query was ever registered under `["transportBoxTransitions", …]`, so the match set was empty on every invocation since the key was introduced.

**Use case 3 — the dead flow, for completeness:** `useAllowedTransitionsQuery` → raw `apiClient.http.fetch` → `GET /api/transport-boxes/{id}/allowed-transitions` → 404 → `throw new Error("Failed to get allowed transitions: Not Found")`. Never invoked, because nothing imports it. Deleted.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Partial application: test mocks stripped (FR-3) while `useTransportBoxes.ts:189` still spreads the key → `[...undefined]` `TypeError` in `onSuccess` at test runtime | Medium | Decision 3: single atomic commit, source-side removal (FR-2) before mock cleanup (FR-3). Never split steps 1-4 across commits. |
| Removing `client.ts:490` without the call site → TypeScript compile failure | Low | Caught immediately by `npm run build` (CRA type-checks and fails the build). Follow the Decision 3 ordering. |
| Over-deletion in `onSuccess` — an implementer removes an adjacent load-bearing `invalidateQueries` while excising the transitions block | Medium | The exact five-operation post-state is enumerated under *Interfaces and Contracts*. Reviewer diffs `useTransportBoxes.ts` lines 176-200 against that list. `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` must pass unchanged. |
| Merge conflict on `frontend/src/api/client.ts` (`QUERY_KEYS`, ~40 entries, touched by most feature branches) or on the `onSuccess` handler | Medium | Land promptly; rebase on `main` before merge; keep the diff to 6 files so conflict resolution is trivial. If `QUERY_KEYS` conflicts, keep the incoming branch's keys and re-apply only the single-line deletion. |
| Someone later genuinely needs a standalone transitions endpoint and re-adds a similar hook | Low | Decision 1's rationale is recorded here and in the spec's Out of Scope. The DTO field is documented as the single source of truth. If a real need appears, it is a new feature with a real backend contract — not a resurrection of this file. |
| `docs/architecture/module-map.md` edit drifts (renumbering, other bullets touched), breaking `/arch-review` part references | Medium | `module-map-maintenance.md` forbids renumbering. Acceptance: the map diff is exactly one line; module #7's number, title, summary-table row, "Depends on: #1, #5", and "Analysis notes" are byte-identical. |
| Recurrence — the next unused hook accumulates the same way | Medium | Decision 6: file the `knip`/`ts-prune` follow-up issue via `gh` in the same work session and link it from the PR body. Not implemented here. |
| E2E green signal misread as validating the change | Low | Decision 5: the harness targets deployed staging, not the PR branch. Do not gate on it; state this explicitly in the PR description. |
| Reviewer expects the missing endpoint to be implemented instead | Low | Decision 1 is the recorded, opinionated answer. The spec's Out of Scope already says so; the PR description should restate it in one sentence. |

## Specification Amendments

**A-1 — Open Question 1 (scope of FR-2/FR-3): RESOLVED — keep both in scope.** The spec's assumption is correct and is hereby ratified. FR-1 through FR-5 all stand as written. Do **not** fall back to the "strictly minimal one-file deletion" variant. Rationale in Decision 2. Delete this open question from any successor spec revision.

**A-2 — New requirement FR-6 (atomicity and ordering), derived from Decision 3.** Add to the spec:

> **FR-6: The removal must be atomic and correctly ordered.** All edits in FR-1, FR-2 and FR-3 land in a single commit, applied in this order: (1) remove the `invalidateQueries` block and its comment at `frontend/src/api/hooks/useTransportBoxes.ts:186-190`; (2) remove `frontend/src/api/client.ts:490`; (3) delete `frontend/src/api/hooks/useTransportBoxTransitions.ts`; (4) remove the `transportBoxTransitions` entry from the three `jest.mock` `QUERY_KEYS` literals; (5) edit `docs/architecture/module-map.md:258`.
> **Acceptance criteria:** the branch contains no intermediate commit in which `QUERY_KEYS.transportBoxTransitions` is absent from a `jest.mock` literal while `useTransportBoxes.ts` still references it, nor one in which `client.ts:490` is removed while any consumer remains. `git log --oneline` on the branch shows the removal as one commit.

Justification: `useTransportBoxes.ts:189` uses spread syntax (`[...QUERY_KEYS.transportBoxTransitions, variables.boxId]`), so a missing mock key yields `[...undefined]` → `TypeError`, not a silent `undefined`. The spec's FR-3 preamble ("These are mocks, so they will not fail after FR-2") is correct only *after* FR-2; it is unsafe before it. This ordering constraint was implicit and is now explicit.

**A-3 — Factual correction to FR-4's "do not edit" list.** The spec names `docs/superpowers/plans/2026-05-16-box-detail-product-thumbnails.md` and `2026-03-11-quarantine-state.md` as historical plans referencing the hook. Verified: **neither contains any reference.** The only `docs/superpowers/plans/` file mentioning the symbol is `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md:629`, and it mentions `transportBoxTransitions` inside a quoted `jest.mock` `QUERY_KEYS` literal — not `useTransportBoxTransitions`. The instruction itself is unchanged and correct: **do not edit anything under `docs/superpowers/plans/`.** Only the cited filenames were wrong.

**A-4 — Correction to FR-2 and FR-4 acceptance criteria (grep expectations).** As written, two criteria are ambiguous or unachievable:
- FR-4's *"`grep -rn "useTransportBoxTransitions" docs/` returns no hits"* — **achievable and correct as stated**; `module-map.md:258` is the only hit today. Keep it.
- FR-2's *"`grep -rn "transportBoxTransitions" frontend/src frontend/test` returns no hits in non-test source"* — tighten to the stronger, fully achievable form: **`grep -rn "transportBoxTransitions" frontend/src frontend/test` returns zero hits, including tests** (FR-3 removes the last three). Note that `grep -rn "transportBoxTransitions" docs/` will still return one hit — `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md:629` — which is **expected and must not be edited** (A-3). Do not write an acceptance criterion that greps `docs/` for the bare `transportBoxTransitions` string.

**A-5 — Open Question 2 (dead-export guard): RESOLVED — file a follow-up issue, do not implement.** Adding `knip`/`ts-prune` remains out of scope for this PR (Decision 6). But the follow-up is **required, not optional**: before this work item closes, create a GitHub issue via `gh issue create` proposing a frontend dead-export detector (suggested: `knip`, run as a non-blocking CI step initially so the existing backlog does not fail the build), referencing this finding as motivation, and link it from the PR description. Add to the spec's Out of Scope entry: *"…belongs in its own issue — which this work item must file."*

**A-6 — Open Question 3 (E2E gating): RESOLVED — no E2E gate.** The merge gate for this PR is exactly: `npm run build` succeeds, `npm run lint` produces no new warnings, and the frontend Jest suite passes via `npm test -- --coverage --watchAll=false` (matching `.github/workflows/ci-feature-branch.yml:45`). A pre-merge `./scripts/run-playwright-tests.sh` run is **not** required and must not block the PR, because the harness hardcodes `https://heblo.stg.anela.cz` (`scripts/run-playwright-tests.sh:27,77`) and therefore exercises the deployed staging build rather than the branch — it can produce no evidence about this change. Amend FR-5's acceptance criteria accordingly: replace *"Existing transport E2E specs pass against staging"* with *"The nightly staging E2E run for the `transport` project is green on the first run after this change is deployed; a pre-merge run is not required and is not a gate."* The transport specs listed in FR-5 do exist (`frontend/test/e2e/transport/box-workflow.spec.ts`, `box-management.spec.ts`, `boxes-basic.spec.ts` — verified) and remain the post-deploy regression backstop.

**A-7 — Clarify FR-1's line count.** The spec says the hook is 46 lines; the file is **49 lines**. Cosmetic, but the acceptance criterion is existence-based, not length-based, so no criterion changes. Noted so the implementer does not think they are looking at the wrong file.

**A-8 — Add an explicit no-dangling-type note to FR-1.** `ApiClientWithInternals` is declared independently in seven files (`useTransportBoxes.ts:15`, `TransportBoxTypes.tsx:15`, `useBoxFill.ts`, `usePackingUsers.ts`, `printLabelPdf.ts`, `TransportBoxDetail.tsx`, and the deleted hook). Deleting the hook removes only its own copy and leaves no unresolved import anywhere. Do **not** attempt to consolidate the remaining six into a shared type as part of this change — that is a separate refactor and violates the surgical-changes rule.

**A-9 — Confirm no coverage-threshold risk.** `frontend/package.json`'s `jest` block contains only `transformIgnorePatterns`; there is no `coverageThreshold`. Deleting an uncovered file cannot fail CI on a coverage gate. No spec change needed — recorded so the implementer does not add one defensively.

**A-10 — Status transition.** With A-1, A-5 and A-6 resolving all three open questions, the spec's `## Status: HAS_QUESTIONS` should be read as **RESOLVED** by downstream stages. No question remains for a human. Proceed directly to design-skip and planning.

## Prerequisites

**None.** Verified as of this review:

- **No database migration.** No entity, configuration, or `Persistence/` file is touched. `TransportBoxStateNode` and its transitions are in-memory, never persisted.
- **No configuration or secrets.** No Key Vault entry, App Setting, connection string, or environment variable. Nothing in `kv-heblo-stg`.
- **No feature flag.** This is an unconditional deletion; `docs/development/feature-flags.md` is not in play.
- **No OpenAPI regeneration.** The backend contract is unchanged, so `frontend/src/api/generated/api-client.ts` is untouched. It is generated on build — do not hand-edit it, and do not commit a regenerated version.
- **No infrastructure or deployment change.** Single Docker image, unchanged.
- **No dependency change.** `@tanstack/react-query` usage is only reduced; no version bump, no install.
- **No blocking branch.** The work is independent. The only coupling is potential merge conflict on `frontend/src/api/client.ts` (`QUERY_KEYS`) and `frontend/src/api/hooks/useTransportBoxes.ts` (`onSuccess`) — mitigate by landing promptly and rebasing before merge.

**Required before the work item closes** (not before it starts): the `knip`/`ts-prune` follow-up issue from A-5, filed with `gh issue create` and linked from the PR description.
