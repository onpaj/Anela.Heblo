```markdown
# Architecture Review: Move PrintPickingList DTOs and IPickingListSource from Domain to Application

## Skip Design: true

This is a backend-only namespace relocation. No UI, screens, layouts, components, or visual decisions are involved.

## Architectural Fit Assessment

The proposed move is correct and aligns with the project's Clean Architecture intent stated in `docs/architecture/filesystem.md` and `docs/architecture/development_guidelines.md`:

- **Domain** should hold entities, value objects, and pure domain ports.
- **Application** should hold use-case DTOs (`Request`/`Response`) and use-case ports.

The three relocated types are clearly application-layer concerns: they encode I/O flags (`SendToPrinter`), workflow toggles (`ChangeOrderState`), output artefacts (`ExportedFiles`), and a port whose contract is dictated by an Application service (`ExpeditionListService`). Keeping them in Domain inverts the dependency rule.

Verified integration points (10 source/test files touch the old namespace):

- Application consumers (`ExpeditionListService`, `IExpeditionListService`, `RunExpeditionListPrintFixHandler`, `PrintPickingListJob`) — all live under `Application/Features/ExpeditionList/`.
- Adapter implementer + DI extension (`ShoptetApiExpeditionListSource`, `ShoptetApiAdapterServiceCollectionExtensions`).
- Five test files across two test projects.

The Application project already references Domain, so the move does not create a new project dependency. The `Carriers` enum referenced by `PrintPickingListRequest` correctly remains in Domain.

**One non-trivial architectural choice deserves explicit attention** (see Decision 1 below): the spec proposes putting the types under `Application/Features/Logistics/Picking/`, but the *only* consumer feature is `ExpeditionList`, and project convention says "DTOs live in `contracts/` of the specific module".

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
└── Features/Logistics/
    ├── Carriers.cs                 (unchanged — domain enum)
    ├── CarrierCoolingSetting.cs    (unchanged)
    ├── ICarrierCoolingRepository.cs (unchanged)
    ├── IShippingMethodCatalog.cs   (unchanged)
    └── Picking/                    ← DELETED (empty after move)

Anela.Heblo.Application
└── Features/Logistics/Picking/     ← NEW
    ├── PrintPickingListRequest.cs  ← moved
    ├── PrintPickingListResult.cs   ← moved
    └── IPickingListSource.cs       ← moved

         ▲
         │ used by
         │
Anela.Heblo.Application/Features/ExpeditionList/
    ├── Services/ExpeditionListService.cs        (using update)
    ├── Services/IExpeditionListService.cs       (using update)
    ├── UseCases/RunExpeditionListPrintFix/…     (using update)
    └── Infrastructure/Jobs/PrintPickingListJob  (using update)

         ▲
         │ implements IPickingListSource
         │
Adapters/Anela.Heblo.Adapters.ShoptetApi/
    ├── Expedition/ShoptetApiExpeditionListSource.cs (using update)
    └── ShoptetApiAdapterServiceCollectionExtensions.cs (using update)
```

### Key Design Decisions

#### Decision 1: Target location — `Application/Features/Logistics/Picking/` vs `Application/Features/ExpeditionList/Contracts/`

**Options considered:**

- **(A)** `Application/Features/Logistics/Picking/` — what the spec proposes; mirrors the existing Domain path 1:1.
- **(B)** `Application/Features/ExpeditionList/Contracts/` — co-locate DTOs and port with the feature that actually consumes them, matching the `Contracts/` convention documented in `docs/architecture/development_guidelines.md` and already used by `Application/Features/Logistics/Contracts/` (`TransportBoxDto.cs`, `IInventoryReservationService.cs`, etc.).

**Chosen approach:** Stay with the spec's choice — **(A) `Application/Features/Logistics/Picking/`**.

**Rationale:**

- The brief and spec both explicitly call out this path; the goal is a surgical relocation, not a feature reorganization.
- `IPickingListSource` is *implemented* by a Shoptet adapter and is conceptually a Logistics-bounded concern (picking lists pull from carriers/orders). Putting it inside `ExpeditionList` would couple the adapter to a sibling feature's namespace.
- The naming `Picking` is shared vocabulary across `ExpeditionList`, the Shoptet adapter, and the recurring Hangfire job — a Logistics subfolder reflects that better than burying it in one consumer's `Contracts/`.
- Option (B) is a reasonable future refinement but expands the diff beyond what the brief authorizes ("Out of Scope: Splitting `PrintPickingListRequest`…", "Reorganizing other types in `Domain.Features.Logistics`…").

**Note for spec:** the spec is internally consistent on this; no amendment required, but **call out Option (B) as a future improvement** so the next architecture review pass can revisit if `ExpeditionList` grows additional picking-related DTOs.

#### Decision 2: Delete the empty `Domain/Features/Logistics/Picking/` directory

**Options considered:** leave it empty (with a `.gitkeep` or untouched) vs delete it.

**Chosen approach:** delete (matches FR-6).

**Rationale:** an empty folder under Domain is misleading — it signals a domain concept that no longer exists there. Git tracks files, not directories, so deletion is a no-op in version control once the three files are removed. Confirm with `git status` after the move that the directory disappears.

#### Decision 3: Preserve file-scoped namespaces and existing style

**Chosen approach:** change only the namespace token; do not reformat, reorder members, or convert between brace and file-scoped namespace forms.

**Rationale:** matches the project's "surgical changes" rule in `CLAUDE.md` and NFR-4. `dotnet format` is run only on the changed files.

## Implementation Guidance

### Directory / Module Structure

Create the new folder and three files in this order to minimize broken-build windows:

1. `backend/src/Anela.Heblo.Application/Features/Logistics/` already exists. Create subdirectory `Picking/`.
2. Move (rename) — preferably via `git mv` to preserve history:
   - `Domain/Features/Logistics/Picking/PrintPickingListRequest.cs` → `Application/Features/Logistics/Picking/PrintPickingListRequest.cs`
   - `Domain/Features/Logistics/Picking/PrintPickingListResult.cs` → `Application/Features/Logistics/Picking/PrintPickingListResult.cs`
   - `Domain/Features/Logistics/Picking/IPickingListSource.cs` → `Application/Features/Logistics/Picking/IPickingListSource.cs`
3. Update each moved file's namespace declaration to `Anela.Heblo.Application.Features.Logistics.Picking`.
4. Inside `PrintPickingListRequest.cs`, add `using Anela.Heblo.Domain.Features.Logistics;` (to keep `Carriers` resolvable, since the file no longer sits inside the Domain.Logistics namespace tree where `Carriers` was implicitly accessible via the parent namespace).
5. Update the 6 production `using` directives and 5 test `using` directives enumerated in FR-4/FR-5.
6. Remove the now-empty `Domain/Features/Logistics/Picking/` directory.

### Interfaces and Contracts

No signature changes. To make this auditable, the implementer should diff each moved file before/after and confirm only the namespace line and (in `PrintPickingListRequest`) the new `using` for `Carriers` differ.

Specifically:

- **`PrintPickingListRequest`** — currently references `Carriers` as a bare type (resolvable because of the parent namespace `Anela.Heblo.Domain.Features.Logistics`). After the move, either:
  - add `using Anela.Heblo.Domain.Features.Logistics;` at the top, **or**
  - fully-qualify both `Carriers` references (`IList<Anela.Heblo.Domain.Features.Logistics.Carriers>` and the `DefaultCarriers` initializer).
  Prefer the `using` directive — less noise, matches existing project style. Note that the current file uses `Logistics.Carriers.Zasilkovna` in `DefaultCarriers`; after adding the using directive this can stay as-is, but a developer should verify there is no name collision with `Anela.Heblo.Application.Features.Logistics`. (There is no `Carriers` symbol in the Application/Logistics namespace today — verified.)

- **`IPickingListSource`** — exact signature preserved. `ShoptetApiExpeditionListSource` already implicitly will pick up the new namespace once its `using` is updated.

### Data Flow

No change in runtime data flow. Compile-time: the type identity changes (`System.Type.FullName` differs), but no callers serialize these by full type name:

- `PrintPickingListJob` constructs `PrintPickingListRequest` in-process and passes it to `IExpeditionListService.PrintPickingListAsync` — Hangfire stores only the *job* identifier (`PrintPickingListJob.ExecuteAsync()`), not the DTO. Confirmed by reading `PrintPickingListJob.cs:49-62`.
- No queue messages, blob payloads, or DB rows persist these types.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `PrintPickingListRequest` loses access to `Carriers` after namespace change (compiler error) | Low | Add `using Anela.Heblo.Domain.Features.Logistics;` to the moved file. Verified locally that no shadowing `Carriers` type exists in `Application.Features.Logistics`. |
| Hidden serializer (Hangfire, Newtonsoft with `TypeNameHandling.All`, MassTransit, blob cache) holds a fully-qualified type name | Low | Verified: the Hangfire job stores no DTO arguments. Still, before merge run `grep -rni "PrintPickingListRequest\|PrintPickingListResult\|IPickingListSource" backend/` and inspect any matches in serialization/job-scheduling code. |
| Lost git history because move is detected as delete+add | Low | Use `git mv` (or `git add -A` followed by `git status` confirming "renamed:"). Verify rename detection survives the namespace edit by making the namespace change in a second commit if rename heuristic falls below the default threshold. |
| `Anela.Heblo.Domain/Features/Logistics/Picking/` directory persists in working tree after files are removed | Low | Explicit `rmdir` (or in Visual Studio: "delete empty folder"). FR-6 requires its removal. |
| Test projects fail to find moved types due to stale build cache | Low | After edits, run `dotnet clean && dotnet build` once before declaring success. |
| `using` cleanup misses a file (e.g. a transitively touched test helper) | Low | FR-7's `grep` check on `backend/src` **and** `backend/test` is the gate. Run it before commit. |
| Reviewer confuses "Logistics" location with the unrelated `Features/Logistics/UseCases/` (TransportBox) area | Low (cosmetic) | Add a brief PR description noting the new `Picking/` subfolder is distinct from the TransportBox use cases under the same feature. |

## Specification Amendments

The spec is sound; two minor amendments to make it implementable without judgement calls:

1. **Amend FR-1 acceptance criteria** to explicitly require: *"`PrintPickingListRequest.cs` adds `using Anela.Heblo.Domain.Features.Logistics;` (for the `Carriers` enum). Bare references such as `Logistics.Carriers.Zasilkovna` in `DefaultCarriers` are preserved unchanged."* — this prevents a developer from over-rewriting the `DefaultCarriers` initializer or, alternatively, from missing the `using` and producing a compile error.

2. **Amend NFR-3 (Backwards compatibility)** to record the verification result: *"Verified during architectural review that `PrintPickingListJob` constructs `PrintPickingListRequest` in-process and Hangfire serializes only the `IRecurringJob.ExecuteAsync()` invocation, not the DTO. No serialized payload references the moved type names."* — closes the open hedge in the original NFR-3 text.

3. **Add to "Future improvements" (informational, no action this iteration)**: *"Consider moving these types to `Application/Features/ExpeditionList/Contracts/` if `ExpeditionList` remains the sole consumer, to conform to the `Contracts/`-per-feature convention in `docs/architecture/development_guidelines.md`."* — captures Decision 1's runner-up so it isn't lost.

No other changes. Open Questions remains "None".

## Prerequisites

None. No migrations, configuration, infrastructure, or new project references are required. The work can begin immediately on this branch.

Pre-flight checklist for the implementer:

- Working tree is clean (`git status` shows nothing pending).
- `dotnet build` of the solution currently succeeds (baseline).
- The five test files identified in FR-5 currently compile and pass (`dotnet test --filter` on the two test projects) — establishes the baseline that FR-5 must preserve.
```