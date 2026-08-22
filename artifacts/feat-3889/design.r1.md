# Design: Remove dead `useTransportBoxTransitions` hook and its orphaned React Query plumbing

## Component Design

This change has no UI component design (arch review `## Skip Design: true`). It is a frontend dead-code removal across six files: one deletion, four source/test edits, one documentation line edit. No file is created. The backend, the generated OpenAPI client, and every transport UI component are read-only.

### Boundary summary

```
LIVE PATH (unchanged end to end)
  TransportBoxStateNode.GetAllTransitions()        domain, in-memory
        ▼ AutoMapper — TransportBoxMappingProfile:16
  TransportBoxDto.AllowedTransitions               Application/Contracts
        ▼ NSwag (not regenerated — backend contract unchanged)
  TransportBoxDto (TS).allowedTransitions          generated/api-client.ts
        ▼
  TransportBoxActions.tsx                          sole renderer of transition buttons

DEAD PATH (removed in full)
  useTransportBoxTransitions.ts  ──uses──►  QUERY_KEYS.transportBoxTransitions  (client.ts:490)
        │                                            ▲
        │ 0 importers                                └── useTransportBoxes.ts:188-190 (no-op invalidation)
        ▼                                            └── 3 jest.mock QUERY_KEYS literals
  GET /api/transport-boxes/{boxId}/allowed-transitions  → route never implemented (404)
```

---

### C-1. `frontend/src/api/hooks/useTransportBoxTransitions.ts` — DELETED (whole file, 49 lines)

**Current responsibility:** none that is reachable. It declares a React Query hook that bypasses the generated client (casting it to a file-local `ApiClientWithInternals`) and raw-fetches a backend route that does not exist.

**Removed symbols (all with zero importers — no consumer contract is broken):**

| Symbol | Line | Kind |
|---|---|---|
| `ApiClientWithInternals` | 5 | file-local interface (not exported) |
| `AllowedTransition` | 10 | exported TS interface — fictional contract |
| `GetAllowedTransitionsResponse` | 17 | exported TS interface — fictional contract |
| `useAllowedTransitionsQuery` | 24 | exported React Query hook |

**Why nothing dangles:**
- There is no `frontend/src/api/hooks/index.ts` barrel (verified — the directory contains only `use*.ts` modules and `__tests__/`), so the exports are unreachable except by direct path import, and no such import exists.
- `ApiClientWithInternals` is redeclared independently in each of the seven files that use it (`useTransportBoxes.ts:15`, `TransportBoxTypes.tsx:15`, `useBoxFill.ts`, `usePackingUsers.ts`, `printLabelPdf.ts`, `TransportBoxDetail.tsx`, and this file). Deleting this copy leaves the other six untouched and unresolvable-import-free. Per arch-review A-8, do **not** consolidate them.
- The file's two imports (`useQuery` from `@tanstack/react-query`, `getAuthenticatedApiClient`/`QUERY_KEYS` from `../client`) both have many other consumers; no dependency is orphaned.

---

### C-2. `frontend/src/api/client.ts` — one `QUERY_KEYS` member removed

**Responsibility (unchanged):** owns the shared `QUERY_KEYS` const (~40 members), the cross-cutting namespace registry that every feature hook composes its query keys from.

**Removed:** line 490 — `transportBoxTransitions: ["transportBoxTransitions"] as const,`

**Retained contract that consumers rely on:** the neighbouring member `transportBox: ["transport-boxes"] as const` (line 489) is the root namespace for **all** transport-box caching and must not be touched. `transportBoxTransitions` is a separate, unrelated member whose string value (`"transportBoxTransitions"`) never overlapped the `"transport-boxes"` namespace — so its removal cannot widen or narrow any surviving key's match set.

**Blast radius:** this is the only conflict-prone file in the change (most feature branches touch `QUERY_KEYS`). The edit is a single-line deletion with no reordering, so conflict resolution is: keep the incoming branch's key set, re-apply only this one deletion.

---

### C-3. `frontend/src/api/hooks/useTransportBoxes.ts` — one invalidation removed from `useChangeTransportBoxState.onSuccess`

**Responsibility (unchanged):** owns all transport-box query/mutation hooks and the `transportBoxKeys` factory.

**Removed:** lines 187-190 — the comment `// Also invalidate any transition-related queries` plus the three-line `queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.transportBoxTransitions, variables.boxId] })` call, together with one adjoining blank line so no double blank remains.

**Contract that must survive byte-for-byte — the key factory (lines 45-52, exported at file end):**

```ts
const transportBoxKeys = {
  all:     QUERY_KEYS.transportBox,
  lists:   () => [...QUERY_KEYS.transportBox, "list"] as const,
  list:    (filters: GetTransportBoxesRequest) => [...QUERY_KEYS.transportBox, "list", filters] as const,
  details: () => [...QUERY_KEYS.transportBox, "detail"] as const,
  detail:  (id: number) => [...QUERY_KEYS.transportBox, "detail", id] as const,
};
export { transportBoxKeys };
```

**Contract that must survive — the post-edit `onSuccess` cache protocol, exactly these five operations in this order and no others:**

| # | Operation | Key | Purpose |
|---|---|---|---|
| 1 | `invalidateQueries` | `transportBoxKeys.detail(variables.boxId)` | mark the open detail stale |
| 2 | `invalidateQueries` | `transportBoxKeys.lists()` | mark all list pages stale |
| 3 | `invalidateQueries` | `[...QUERY_KEYS.transportBox, "summary"]` | refresh the state-count summary |
| 4 | `invalidateQueries` | `[...QUERY_KEYS.transportBox, 'byCode']` | scan lookup reflects new state |
| 5 | `refetchQueries` | `transportBoxKeys.detail(variables.boxId)` | force fresh DTO (carries the new `allowedTransitions`) |

Operation 5 is what makes the transition buttons re-render correctly after a state change — the refetched `TransportBoxDto` carries the new state's `AllowedTransitions`. It is the load-bearing replacement for anything the dead hook pretended to do, and it already exists.

**Why removing the sixth operation is provably behaviour-neutral:** `invalidateQueries` marks *matching cached queries* stale and refetches the active ones. Nothing ever registered a query under `["transportBoxTransitions", …]` — the only producer of that key was the never-imported hook — so the match set was empty on every invocation since the key was introduced. Removal changes an empty operation into no operation.

**The second `onSuccess` handler in this file (lines ~238-240, a different mutation invalidating `manufacturedProductInventory`) is out of scope and untouched.**

---

### C-4. Three Jest `QUERY_KEYS` mock literals — one entry removed from each

These are `jest.mock` factories that hand-stub the `../client` module. They are test doubles of C-2's contract: whatever a component under test reads off `QUERY_KEYS` must exist in the literal.

| File | Line | Literal keys before | Literal keys after |
|---|---|---|---|
| `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` | 17 | `transportBox`, `transportBoxTransitions` | `transportBox` |
| `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` | 71 | `catalog`, `transportBox`, `transportBoxTransitions`, `stockUpOperations` | `catalog`, `transportBox`, `stockUpOperations` |
| `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` | 60 | `catalog`, `transportBox`, `transportBoxTransitions`, `stockUpOperations` | `catalog`, `transportBox`, `stockUpOperations` |

Every other key in each literal is still real and still read — do not touch them. `getAuthenticatedApiClient: jest.fn()` and the sibling `jest.mock("../../../api/generated/api-client", …)` factories are untouched.

**Hard sequencing constraint (arch-review FR-6 / Decision 3).** `useTransportBoxes.ts:189` *spreads* the key:

```ts
queryKey: [...QUERY_KEYS.transportBoxTransitions, variables.boxId],
```

If a mock literal loses `transportBoxTransitions` while that line still exists, the spread evaluates `[...undefined]` inside `onSuccess` and throws a `TypeError` at test runtime — not a silent `undefined`. Symmetrically, removing `client.ts:490` while any consumer remains is a hard TypeScript compile failure. Therefore the source-side removals must precede the mock edits, and C-1 through C-4 plus C-5 land as **one atomic commit** with no independently checkout-able intermediate state. The ordering is: (1) C-3 call site, (2) C-2 key, (3) C-1 file, (4) C-4 mocks, (5) C-5 doc.

---

### C-5. `docs/architecture/module-map.md` — one line edit, module #7 "Owns" list

**Responsibility (unchanged):** the numbered partition of the repo that `/arch-review` samples from.

```diff
-- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`, `useTransportBoxTransitions.ts`
+- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`
```

Line edit, **not** a RETIRED marker: `module-map-maintenance.md` reserves RETIRED for whole parts removed from the codebase and forbids renumbering. Module #7 keeps its number, title, size band, every other "Owns" bullet, `**Depends on:** #1, #5.`, its "Analysis notes", and its summary-table row — all byte-identical.

---

### C-6. Untouched components whose contracts this change depends on

| Component | Contract relied on |
|---|---|
| `frontend/src/components/transport/box-detail/TransportBoxActions.tsx` | Reads `transportBox.allowedTransitions` and partitions it on `transitionType === "Previous"` (backward, "Zpět") vs `!== "Previous"` (forward). Guards with `?.filter(...) \|\| []`. Never fetches. Must remain unmodified. |
| `backend/.../TransportBoxMappingProfile.cs:16` | Maps `src.TransitionNode.GetAllTransitions()` → `dest.AllowedTransitions` on **every** `TransportBox → TransportBoxDto` projection, so the field is populated by `GetTransportBoxById`, `GetTransportBoxes`, and `GetTransportBoxByCode` alike. |
| `backend/.../TransportBoxStateNode.cs` | `GetAllTransitions()` returns the read-only in-memory transition list. Not persisted — no migration. |
| `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` | Keeps exactly its 10 actions under `[Route("api/transport-boxes")]`. No `allowed-transitions` action is added. |
| `frontend/src/api/generated/api-client.ts` | Generated at build time from an unchanged backend contract — not regenerated, never hand-edited. |

---

### Follow-up deliverable (arch-review A-5, required before the work item closes)

A GitHub issue filed via `gh issue create` proposing a frontend dead-export detector (`knip` suggested, wired as a non-blocking CI step first so the pre-existing backlog does not fail the build), referencing this finding as motivation and linked from the PR description. Not implemented in this change.

## Data Schemas

**No database schema change. No migration. No persisted entity, EF configuration, or `Persistence/` file is touched** — the transition set is computed in memory by `TransportBoxStateNode` on every load and is never stored.

**No API request/response shape changes, and no event payloads exist for this feature.** No endpoint is added, removed, or modified; no OpenAPI regeneration is required.

### S-1. Surviving authoritative contract — transitions ride inline on the box DTO

This is the single source of truth for allowed transitions, before and after the change.

**Domain (in-memory, not persisted)** — `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxTransition.cs`:

```csharp
public class TransportBoxTransition
{
    public TransportBoxState NewState { get; }          // enum
    public TransitionType   TransitionType { get; }     // enum, includes Previous
    public Func<TransportBox, bool>? Condition { get; } // server-side only, NOT projected
    public bool SystemOnly { get; }
}
```

**Application contract** — `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxTransitionDto.cs`:

```csharp
public class TransportBoxTransitionDto
{
    public string NewState       { get; set; } = string.Empty;  // TransportBoxState.ToString()
    public string TransitionType { get; set; } = string.Empty;  // TransitionType.ToString()
    public bool   SystemOnly     { get; set; }
    public string Label          { get; set; } = string.Empty;  // GetStateLabel(NewState), Czech
}
```

Projection (`TransportBoxMappingProfile.cs:24-27`): `NewState` and `TransitionType` are `.ToString()`-mapped from their enums; `Label` is computed by `GetStateLabel(src.NewState)`; `SystemOnly` maps by convention. `Condition` is deliberately **not** projected — predicate evaluation stays server-side.

**Carrier** — `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxDto.cs:25`:

```csharp
public IList<TransportBoxTransitionDto> AllowedTransitions { get; set; } = new List<TransportBoxTransitionDto>();
```

Sibling fields on the same DTO that the transition UI reads alongside it: `Id`, `Code`, `State`, `DefaultReceiveState`, `Location`, `IsInTransit`, `IsInReserve`, `IsInQuarantine`, `IsReceivable`, `ItemCount`, `Items`, `StateLog`.

**Generated TypeScript mirror** — `frontend/src/api/generated/api-client.ts` (all members optional, NSwag convention):

```ts
export interface ITransportBoxTransitionDto {
    newState?: string;
    transitionType?: string;
    systemOnly?: boolean;
    label?: string;
}
export class TransportBoxTransitionDto implements ITransportBoxTransitionDto { /* init/fromJS/toJSON */ }
```

Wire shape as delivered on `GET /api/transport-boxes/{id}`:

```json
{
  "id": 123,
  "code": "B001",
  "state": "InTransit",
  "allowedTransitions": [
    { "newState": "Opened",  "transitionType": "Previous", "systemOnly": false, "label": "Otevřená" },
    { "newState": "Received", "transitionType": "Next",     "systemOnly": false, "label": "Přijatá" }
  ]
}
```

Consumer read contract (`TransportBoxActions.tsx:14-24`) — the only reader, unchanged:
- backward set = `allowedTransitions.filter(t => t.newState && t.transitionType === "Previous")`
- forward set  = `allowedTransitions.filter(t => t.newState && t.transitionType !== "Previous")`
- `systemOnly` is currently **not** read; whether system-only transitions should be filtered out of the UI is explicitly a separate concern and is not decided here.

### S-2. Deleted phantom contract — never implemented on any backend

Removed from `frontend/src/api/hooks/useTransportBoxTransitions.ts` without replacement:

```ts
export interface AllowedTransition {
  state: string;                  // ✗ no such field — real DTO has `newState`
  label: string;                  // ✓ the only field that coincides with reality
  requiresCondition: boolean;     // ✗ no such field — `Condition` is server-side and never projected
  conditionDescription?: string;  // ✗ no such field anywhere in the backend
}

export interface GetAllowedTransitionsResponse {
  success: boolean;               // ✗ envelope shape; the real read endpoints return the DTO directly
  errorMessage?: string;          // ✗
  currentState?: string;          // ✗ (the real DTO exposes `state` on the box itself)
  allowedTransitions: AllowedTransition[];
}
```

Field-by-field, this is not a stale version of `TransportBoxTransitionDto` — it is a different, fictional model. Only `label` overlaps; `transitionType` and `systemOnly` are absent, and `requiresCondition` / `conditionDescription` were never in any backend contract. It was addressed at `GET /api/transport-boxes/{boxId}/allowed-transitions`, a route with zero implementations in `backend/src/**/*.cs` — so the hook would have thrown `Failed to get allowed transitions: Not Found` on any 404 had it ever been called.

### S-3. Cache-key schema delta

`QUERY_KEYS` in `frontend/src/api/client.ts` is the frontend's key registry — the closest thing this change has to a schema.

| Key member | Value | Before | After |
|---|---|---|---|
| `transportBox` | `["transport-boxes"]` | present | **present** (root namespace for detail/list/summary/byCode) |
| `transportBoxTransitions` | `["transportBoxTransitions"]` | present | **removed** |

Derived key space under the surviving `transportBox` namespace, all unchanged:

```
["transport-boxes"]                          all
["transport-boxes","list"]                   lists()
["transport-boxes","list", <filters>]        list(filters)
["transport-boxes","detail"]                 details()
["transport-boxes","detail", <id>]           detail(id)
["transport-boxes","summary"]                summary
["transport-boxes","byCode"]                 byCode lookups
```

Retired key space, with no registered observer at any point in its life:

```
["transportBoxTransitions", <boxId>]         ← produced only by the deleted hook,
                                               invalidated only by the deleted call site
```

Because the retired key's first segment (`"transportBoxTransitions"`) never prefix-matched `"transport-boxes"`, its removal cannot alter the match set of any surviving invalidation. Cache behaviour after a state change is byte-identical to before.
