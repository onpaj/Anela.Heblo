# Specification: Remove dead `useTransportBoxTransitions` hook and its orphaned React Query plumbing

## Summary
`frontend/src/api/hooks/useTransportBoxTransitions.ts` exports a React Query hook (`useAllowedTransitionsQuery`) that calls `GET /api/transport-boxes/{boxId}/allowed-transitions` — an endpoint that does not exist on the backend. The hook has zero importers anywhere in `frontend/src` or `frontend/test`, and the data it claims to fetch is already delivered inline on every box fetch via `TransportBoxDto.AllowedTransitions`. This change deletes the hook file, removes the query-key plumbing that existed only to serve it, and updates the one architecture doc that lists the file — a pure dead-code removal with no runtime behaviour change.

## Background

### The working mechanism
Allowed state transitions for a transport box are computed on the backend and embedded in the box DTO:

- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateNode.cs:24` — `GetAllTransitions()` returns the box's `TransportBoxTransition` list.
- `backend/src/Anela.Heblo.Application/Features/Logistics/TransportBoxMappingProfile.cs:16` — AutoMapper maps `TransportBox.TransitionNode.GetAllTransitions()` into `TransportBoxDto.AllowedTransitions` for **every** `TransportBox → TransportBoxDto` projection.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxDto.cs:25` — `public IList<TransportBoxTransitionDto> AllowedTransitions { get; set; }`.
- `frontend/src/components/transport/box-detail/TransportBoxActions.tsx:15,21` — consumes `transportBox.allowedTransitions`, splitting it into "Zpět" (`transitionType === "Previous"`) and forward transitions.

So the data arrives free with `GetTransportBoxById` / `GetTransportBoxes` / `GetTransportBoxByCode`, and the working UI already reads it from there.

### The dead mechanism
`frontend/src/api/hooks/useTransportBoxTransitions.ts` (46 lines) bypasses the generated OpenAPI client, casts it to a hand-written `ApiClientWithInternals` shape, and issues a raw `apiClient.http.fetch` against `/api/transport-boxes/{boxId}/allowed-transitions`.

### Verification of the brief's claims
All three factual claims in `artifacts/feat-3889/brief.md` were checked against the code on this branch and are **confirmed**:

| Brief claim | Verdict | Evidence |
|---|---|---|
| `TransportBoxController` has no `allowed-transitions` action | **Confirmed** | `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` (210 lines) declares `[Route("api/transport-boxes")]` and exactly 10 actions: `GET ""`, `GET "summary"`, `GET "{id:int}"`, `PUT "{id:int}/state"`, `POST ""`, `POST "{id:int}/items"`, `DELETE "{id:int}/items/{itemId:int}"`, `PUT "{id:int}/description"`, `GET "by-code/{boxCode}"`, `POST "open-by-code"`. A repo-wide grep for `allowed-transitions` / `GetAllowedTransitions` across `backend/**/*.cs` returns zero hits. |
| The hook has zero importers | **Confirmed** | `grep -rn "useTransportBoxTransitions\|useAllowedTransitionsQuery\|GetAllowedTransitionsResponse\|AllowedTransition\b" frontend/src frontend/test` returns only the file's own definition. There is no barrel file (`frontend/src/api/hooks/index.ts` does not exist), so the export is unreachable by any other path. |
| `TransportBoxActions.tsx` reads `allowedTransitions` off the normal box DTO | **Confirmed** | See file references above. |

### Findings the brief did not mention (additions, not corrections)
1. **The hook's response contract does not match the backend's transition model at all.** The hook declares `AllowedTransition { state, label, requiresCondition, conditionDescription? }`. The real DTO (`TransportBoxTransitionDto`, generated at `frontend/src/api/generated/api-client.ts:42670`) is `{ newState, transitionType, systemOnly, label }`. `state`, `requiresCondition`, and `conditionDescription` do not exist anywhere in the backend contract. The hook is therefore not merely pointing at a missing route — it encodes an API shape that was never implemented. This strengthens the "copy-paste template hazard" argument: a developer reusing it would inherit both a 404 and a fictional contract.
2. **The hook has orphaned plumbing outside its own file.** `QUERY_KEYS.transportBoxTransitions` (`frontend/src/api/client.ts:490`) exists solely for this hook, and `frontend/src/api/hooks/useTransportBoxes.ts:187-190` invalidates that key in `onSuccess` of the state-change mutation ("Also invalidate any transition-related queries"). Because nothing ever registers a query under that key, that invalidation is already a no-op today; after the hook is deleted it becomes provably unreachable. Deleting the hook without this cleanup leaves the same trap in a different shape.
3. **One living architecture doc lists the file.** `docs/architecture/module-map.md:258` names `useTransportBoxTransitions.ts` among the files owned by module #7 (Transport Boxes). It must be updated or the map goes stale — and the map is the input to future `/arch-review` runs.
4. **The repo has no automated dead-export detection** (no `knip`, `ts-prune`, or `depcheck` in `frontend/package.json`), which is why this file survived. Adding such tooling is out of scope but noted below.

## Functional Requirements

### FR-1: Delete the dead transitions hook file
Remove `frontend/src/api/hooks/useTransportBoxTransitions.ts` in its entirety. This deletes the `ApiClientWithInternals` interface, the `AllowedTransition` and `GetAllowedTransitionsResponse` interfaces, and the `useAllowedTransitionsQuery` hook. No replacement is created — the box DTO's `allowedTransitions` field is the supported path.

**Acceptance criteria:**
- `frontend/src/api/hooks/useTransportBoxTransitions.ts` no longer exists on disk.
- `grep -rn "useTransportBoxTransitions\|useAllowedTransitionsQuery\|GetAllowedTransitionsResponse" frontend/ backend/ --exclude-dir=node_modules` returns no hits outside `artifacts/` and `docs/superpowers/plans/`.
- No new file is added under `frontend/src/api/hooks/` as part of this change.
- `npm run build` in `frontend/` succeeds (TypeScript compiles with no unresolved-import errors).

### FR-2: Remove the query-key plumbing that existed only for the deleted hook
Two call sites reference `QUERY_KEYS.transportBoxTransitions` and exist only to serve the deleted hook. They must be removed in the same change, because leaving them behind (a) keeps a key that nothing can ever register a query under, and (b) leaves a `TypeScript` reference dangling if the key is removed without the call site, or a misleading no-op if the call site is removed without the key.

Edits:
1. `frontend/src/api/hooks/useTransportBoxes.ts` — in the `onSuccess` handler of the change-state mutation, remove the block (currently lines ~186-190):
   ```ts
   // Also invalidate any transition-related queries
   queryClient.invalidateQueries({
     queryKey: [...QUERY_KEYS.transportBoxTransitions, variables.boxId],
   });
   ```
   Leave every other `invalidateQueries` / `refetchQueries` call in that handler untouched — specifically `transportBoxKeys.detail(variables.boxId)`, `transportBoxKeys.lists()`, `[...QUERY_KEYS.transportBox, "summary"]`, `[...QUERY_KEYS.transportBox, 'byCode']`, and the `refetchQueries` on `transportBoxKeys.detail`. Those drive real cache refreshes after a state change and are load-bearing.
2. `frontend/src/api/client.ts:490` — remove the entry `transportBoxTransitions: ["transportBoxTransitions"] as const,` from `QUERY_KEYS`.

**Acceptance criteria:**
- `grep -rn "transportBoxTransitions" frontend/src frontend/test` returns no hits in non-test source.
- `frontend/src/api/hooks/useTransportBoxes.ts` still invalidates the box detail, the box lists, the `summary` key, and the `byCode` key, and still calls `refetchQueries` on the box detail, after a successful state change.
- `npm run build` and `npx tsc --noEmit` (or the project's equivalent type-check step) pass — no reference to the removed `QUERY_KEYS` member remains.
- Existing tests in `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` pass unchanged in behaviour (no test asserts on the removed invalidation; verified by grep — the only occurrences of `transportBoxTransitions` in tests are inside `jest.mock` `QUERY_KEYS` literals).

### FR-3: Clean the now-unnecessary `QUERY_KEYS` mock entries in tests
Three test files stub `QUERY_KEYS` with an object literal that includes `transportBoxTransitions`. These are mocks, so they will not fail after FR-2 — but they now advertise a key that no longer exists, which is exactly the kind of stale signal that produced this finding. Remove the `transportBoxTransitions: ["transportBoxTransitions"],` line from:
- `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts:17`
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx:71`
- `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx:60`

Do not touch any other key in those mock literals (`catalog`, `transportBox`, `stockUpOperations`, …) — they are still real and still used.

**Acceptance criteria:**
- All three files no longer contain `transportBoxTransitions`.
- All three test suites pass: `npm test -- useTransportBoxes.test.ts TransportBoxList.test.tsx TransportBoxList.stockUpGate.test.tsx` (or the project's equivalent invocation) is green.
- No test assertions or mock behaviour other than the removed key are modified.

### FR-4: Update the architecture module map
`docs/architecture/module-map.md:258` currently reads:
```
- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`, `useTransportBoxTransitions.ts`
```
Change it to list only the two surviving hooks:
```
- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`
```
Do not renumber modules, do not alter any other module's "Owns"/"Depends on" list, and do not edit the historical plan documents under `docs/superpowers/plans/` (`2026-05-16-box-detail-product-thumbnails.md`, `2026-03-11-quarantine-state.md`) — those are point-in-time records, not living docs.

**Acceptance criteria:**
- `grep -rn "useTransportBoxTransitions" docs/` returns no hits.
- `docs/architecture/module-map.md` module numbering and all other bullets are byte-identical to before apart from that one line.
- `docs/superpowers/plans/` is unmodified (`git status` shows no change under that directory).

### FR-5: No behavioural regression in transport-box state navigation
The transport box detail page must continue to render backward ("Zpět") and forward transition buttons exactly as before, driven by `transportBox.allowedTransitions` from the box DTO, and a state change must still refresh the detail, the list, the summary and the by-code caches.

**Acceptance criteria:**
- `frontend/src/components/transport/box-detail/TransportBoxActions.tsx` is unmodified by this change.
- The full frontend unit/component test suite passes: `npm test` in `frontend/`.
- Existing transport E2E specs pass against staging: `./scripts/run-playwright-tests.sh` covering `frontend/test/e2e/transport/box-workflow.spec.ts`, `box-management.spec.ts`, and `boxes-basic.spec.ts` (these exercise state transitions end-to-end).
- No backend file is modified; `dotnet build` is not required to change, but if run it must still succeed.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance target changes. The deleted code never executed, so there is no measurable latency or throughput effect. Two second-order effects, both neutral-to-positive:
- Bundle size decreases marginally (one module, ~46 lines pre-minification, plus the removed `QUERY_KEYS` entry). No bundle-size budget exists in this repo, so no threshold applies; the build must simply not grow.
- One `invalidateQueries` call is removed from the change-state mutation's `onSuccess` path. Since no query was ever registered under that key, React Query's cache scan over that key was already a no-op — removing it cannot change refresh behaviour for any visible query.

### NFR-2: Security
No security surface changes. The deleted hook performed an authenticated `GET` against a non-existent route; removing it removes an unauthenticated-by-omission risk only in the negative sense (a route that was never implemented can never leak). Specifically:
- No authentication, authorization, or feature-flag configuration is touched.
- No secret, connection string, or Key Vault entry is involved.
- No new endpoint is exposed; the backend is not modified at all, so the existing per-feature authorization gate on `TransportBoxController` is unaffected.
- The removed `ApiClientWithInternals` cast bypassed the generated client's typing (though not its auth — it still went through `apiClient.http.fetch`, which carries the bearer token). Removing it reduces the number of places where the generated client's contract is circumvented.

### NFR-3: Maintainability / reviewability
The change must be a pure deletion diff: no refactors, no formatting-only edits, no renames in adjacent code. Every removed line must be traceable to the dead hook. This satisfies the repo's "surgical changes" rule in `CLAUDE.md`.

**Acceptance criteria:**
- The diff contains only deletions plus the single-line edit in `docs/architecture/module-map.md`.
- `npm run lint` in `frontend/` passes with no new warnings.
- No `dotnet format` changes are produced (backend untouched).

## Data Model

No persisted data model changes. For reference, the entities involved and their relationship after this change:

```
TransportBox (domain, EF-persisted)
  └── TransitionNode : TransportBoxStateNode        [in-memory, not persisted]
        └── GetAllTransitions() : IEnumerable<TransportBoxTransition>
                                    { NewState, TransitionType, SystemOnly }

        ▼ AutoMapper (TransportBoxMappingProfile:16)

TransportBoxDto
  ├── State            : string
  ├── AllowedTransitions : IList<TransportBoxTransitionDto>   ← the single source of truth
  │                        { newState, transitionType, systemOnly, label }
  ├── Items            : IList<TransportBoxItemDto>
  └── StateLog         : IList<TransportBoxStateLogDto>

        ▼ NSwag → frontend/src/api/generated/api-client.ts

TransportBoxDto (TS)  →  TransportBoxActions.tsx
                           previousTransitions = allowedTransitions.filter(t => t.transitionType === "Previous")
                           nextTransitions     = allowedTransitions.filter(t => t.transitionType !== "Previous")
```

The deleted `AllowedTransition` / `GetAllowedTransitionsResponse` TypeScript interfaces were a parallel, never-implemented model (`state`, `requiresCondition`, `conditionDescription`) and are removed without replacement.

## API / Interface Design

### Endpoints
No endpoint is added, removed, or modified. `TransportBoxController` keeps exactly its current 10 actions under `[Route("api/transport-boxes")]`:

| Method | Route | Action |
|---|---|---|
| GET | `` | `GetTransportBoxes` |
| GET | `summary` | `GetTransportBoxSummary` |
| GET | `{id:int}` | `GetTransportBoxById` |
| PUT | `{id:int}/state` | `ChangeTransportBoxState` |
| POST | `` | `CreateNewTransportBox` |
| POST | `{id:int}/items` | `AddItemToBox` |
| DELETE | `{id:int}/items/{itemId:int}` | `RemoveItemFromBox` |
| PUT | `{id:int}/description` | `UpdateTransportBoxDescription` |
| GET | `by-code/{boxCode}` | `GetTransportBoxByCode` |
| POST | `open-by-code` | `OpenOrResumeBoxByCode` |

The phantom `GET {id}/allowed-transitions` is *not* implemented as part of this work — the inline DTO field is the intended design and adding a second read path would duplicate it.

### Frontend interface surface removed
- Module `frontend/src/api/hooks/useTransportBoxTransitions.ts` — deleted.
- Exports removed: `useAllowedTransitionsQuery`, `AllowedTransition`, `GetAllowedTransitionsResponse`. All three had zero consumers, so this is not a breaking change for any caller.
- `QUERY_KEYS.transportBoxTransitions` — removed from the public `QUERY_KEYS` const in `frontend/src/api/client.ts`. Also zero external consumers after FR-2.

### OpenAPI client
The generated TypeScript client (`frontend/src/api/generated/api-client.ts`) is produced from the backend OpenAPI document at build time. Since no backend contract changes, **no regeneration is required and the generated file must not be hand-edited**.

### UI flows
Unchanged. Transport Box Detail → "Navigace stavu" panel renders previous/next transition buttons from `transportBox.allowedTransitions`; clicking one fires the `ChangeTransportBoxState` mutation, which invalidates the detail/list/summary/by-code caches and refetches the detail.

## Dependencies

- **`@tanstack/react-query`** — already a dependency; only usage is reduced, no version change.
- **Generated OpenAPI client** (`frontend/src/api/generated/api-client.ts`) — read-only dependency; regenerated on build from an unchanged backend contract.
- **AutoMapper + `TransportBoxMappingProfile`** — the mechanism that keeps `allowedTransitions` populated. This change depends on it continuing to work; it is not modified.
- **No external services.** No Shoptet, ABRA, Key Vault, or Azure configuration is touched.
- **No feature flag** gates this change; it is an unconditional deletion.
- **Prerequisite:** none. The work can proceed independently of any other in-flight branch, though it touches `frontend/src/api/client.ts` and `frontend/src/api/hooks/useTransportBoxes.ts`, so a merge conflict is possible if another branch edits `QUERY_KEYS` or the change-state `onSuccess` handler concurrently.

## Out of Scope

- **Implementing a real `GET /api/transport-boxes/{id}/allowed-transitions` endpoint.** The inline `AllowedTransitions` DTO field already serves this data and is the working, consumed path. Adding a standalone endpoint would create a second source of truth.
- **Refactoring `TransportBoxActions.tsx`** or any other transport-box UI component.
- **Changing the transition/state-machine model** (`TransportBoxState`, `TransportBoxTransition`, `TransportBoxStateNode`, `TransportBoxMappingProfile`). Note that `TransportBoxTransitionDto.SystemOnly` is currently not read by `TransportBoxActions.tsx` — investigating whether system-only transitions should be filtered out of the UI is a separate concern and must not be folded into this change.
- **Auditing or removing other unused `apiClient.http.fetch` / `ApiClientWithInternals` hooks.** Roughly 20 frontend modules use that raw-fetch escape hatch; whether each is justified is a separate review. Only the transport-transitions one is in scope here because it is both unused *and* points at a non-existent route.
- **Adding dead-export tooling** (`knip`, `ts-prune`) to the frontend build. Worth doing — it is the systemic fix for this class of finding — but it would change CI behaviour repo-wide and belongs in its own issue.
- **Any backend code change.** The backend is read-only for this work.
- **Editing historical plan documents** under `docs/superpowers/plans/`.

## Open Questions

1. **Scope of FR-2 (the `QUERY_KEYS` plumbing).** The brief's suggested direction is only "delete `useTransportBoxTransitions.ts`". This spec deliberately extends that to the `QUERY_KEYS.transportBoxTransitions` entry and its lone invalidation call site in `useTransportBoxes.ts`, on the reasoning that those exist *solely* for the deleted hook and leaving them would preserve the same misleading signal in a different place. **Assumption made:** the extension is desired. If the reviewer prefers a strictly minimal one-file deletion, drop FR-2 and FR-3 — FR-1, FR-4 and FR-5 stand on their own and the leftover key remains a harmless no-op.
2. **Is a lightweight guard wanted alongside the deletion?** There is no `knip`/`ts-prune` in the frontend toolchain, so nothing prevents the next unused hook from accumulating the same way. Adding one is explicitly out of scope above, but confirm whether a follow-up issue should be filed rather than silently dropped.
3. **E2E gating.** The repo's E2E suite runs nightly, not in PR CI. FR-5 lists the transport specs that should pass. Confirm whether this PR should block on a manual `./scripts/run-playwright-tests.sh` run against staging, or whether the frontend unit/component suite plus `npm run build` / `npm run lint` is sufficient gating for a pure-deletion change with no reachable code path.

## Status: HAS_QUESTIONS
