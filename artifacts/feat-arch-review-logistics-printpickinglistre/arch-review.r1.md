# Architecture Review: Relocate Picking List Operation DTOs from Domain to Application Layer

## Skip Design: true

This is a backend-only namespace relocation. No UI, components, layouts, or visual changes.

## Architectural Fit Assessment

The proposed move aligns with the project's documented Clean Architecture rules (`docs/architecture/development_guidelines.md`) and Vertical Slice conventions. The three types are unambiguously application-layer concerns:

- `PrintPickingListRequest` carries `SendToPrinter` (I/O switch), `ChangeOrderState` (workflow effect), and `DefaultCarriers` (config) — none are domain invariants.
- `PrintPickingListResult` exposes `ExportedFiles` (file paths) and `OrderIds` — use-case output, not value-object semantics.
- `IPickingListSource` is an Application-side port whose only implementation is `ShoptetApiExpeditionListSource` in the Shoptet adapter.

**Critical correction to the brief and spec:** the brief asserts "the single namespace reference in any handler that currently imports them from the Domain path." This is wrong — a solution-wide search returns **11 source/test files** across 4 projects:

- `Anela.Heblo.Application` (4 files): `ExpeditionListService`, `IExpeditionListService`, `RunExpeditionListPrintFixHandler`, `PrintPickingListJob`
- `Anela.Heblo.Adapters.ShoptetApi` (2 files): `ShoptetApiExpeditionListSource`, `ShoptetApiAdapterServiceCollectionExtensions`
- `Anela.Heblo.Tests` (3 files): `ShoptetApiExpeditionListSourceTests`, `ExpeditionListServicePrintSinkTests`, `ExpeditionListServiceOrderStateTests`
- `Anela.Heblo.Adapters.Shoptet.Tests` (2 files): `PickingListIntegrationTests`, `ShoptetApiExpeditionListSource_CoolingMarkerTests`

FR-4's "search scope" already says "the entire backend tree" — that requirement is correct and supersedes the brief's wrong "single reference" claim. The spec must be implemented per FR-4, not per the brief's miscount.

**Project references are already in shape** to absorb the move with zero `.csproj` edits:

| Project | References Domain | References Application |
|---|---|---|
| `Anela.Heblo.Application` | ✓ | (self) |
| `Anela.Heblo.Adapters.ShoptetApi` | ✓ | ✓ |
| `Anela.Heblo.Tests` | ✓ | ✓ |
| `Anela.Heblo.Adapters.Shoptet.Tests` | ✓ | ✓ |

Domain remains unchanged — no Application reference is being introduced into Domain.

## Proposed Architecture

### Component Overview

```
After the move:

  Anela.Heblo.Domain
    Features/Logistics/
      Carriers.cs                     (unchanged — still Domain enum)
      [Picking/ folder DELETED]       (empty after move)

  Anela.Heblo.Application                          ← already refs Domain
    Features/Logistics/Picking/                    ← NEW folder
      PrintPickingListRequest.cs                   (uses Domain.Carriers)
      PrintPickingListResult.cs
      IPickingListSource.cs                        (port)

  Anela.Heblo.Adapters.ShoptetApi                  ← already refs Application
    Expedition/
      ShoptetApiExpeditionListSource.cs            (implements IPickingListSource;
                                                    using directive flips namespace)

  Anela.Heblo.Application
    Features/ExpeditionList/
      Services/{I,}ExpeditionListService.cs        (using directive flips)
      Infrastructure/Jobs/PrintPickingListJob.cs   (using directive flips)
      UseCases/RunExpeditionListPrintFix/…Handler  (using directive flips)
```

The dependency arrows are unchanged in direction; only the home of the three types moves outward from Domain to Application, which is exactly the Clean Architecture correction the brief requires.

### Key Design Decisions

#### Decision 1: Target subfolder — `Picking/` vs `Contracts/`
**Options considered:**
- (A) `Application/Features/Logistics/Picking/` — mirrors the Domain folder name exactly; the spec's choice.
- (B) `Application/Features/Logistics/Contracts/` — matches the documented convention in `development_guidelines.md` ("DTO objects … live in `contracts/` of the specific module") and matches the existing layout of `Application/Features/Logistics/Contracts/` which already houses `ILogisticsCatalogSource`, `ILogisticsStockOperationService`, etc.
- (C) `Application/Features/ExpeditionList/Contracts/` — the actual consumer of `IPickingListSource` is `ExpeditionListService`, not the Logistics module; per the "Consumer owns the contract" pattern documented in `development_guidelines.md` (lines 194–207), the contract belongs in the consumer's module.

**Chosen approach:** **Stick with option (A) — `Application/Features/Logistics/Picking/`** as the spec prescribes.

**Rationale:** Option (C) is architecturally the cleanest long-term home (it matches the `ILeafletKnowledgeSource` precedent in the docs), but moving across feature boundaries widens the blast radius beyond what FR-1–FR-6 authorise and exceeds the "surgical changes" rule in `CLAUDE.md`. Option (B) is closer to the documented convention but the spec is marked COMPLETE and explicitly names `Picking/`; renaming the folder would force the spec to be reopened for a marginal naming gain. Option (A) preserves the conceptual grouping ("picking-list operation contracts"), matches the spec verbatim, and leaves option (C) available as a deliberate follow-up. Document this trade-off in the commit message so the follow-up is not lost.

#### Decision 2: Preserve type kind (`class`) — do not convert DTOs to `record`
**Options considered:**
- Keep `PrintPickingListRequest`/`PrintPickingListResult` as `class` with mutable properties.
- Convert to `record` for immutability per the global C# coding-style rule.

**Chosen approach:** Keep as `class` with mutable properties — preserve the public surface exactly.

**Rationale:** FR-1, FR-2, and FR-6 require behavioural and shape parity. The project-specific rule in `CLAUDE.md` says "DTOs are classes, never C# records" because OpenAPI client generators mishandle record parameter order. Even though these specific DTOs are internal (not exposed via OpenAPI today), changing the type kind during a relocation violates the "surgical changes" directive and the spec's Out-of-Scope clause.

#### Decision 3: Domain `Picking` folder cleanup
**Options considered:**
- Delete the folder once empty.
- Leave the empty folder in place.

**Chosen approach:** Delete it if and only if empty after the three moves (per FR-5).

**Rationale:** An empty `Domain/Features/Logistics/Picking/` folder would falsely suggest Domain still owns picking concerns, undermining the whole point of this work. Verified: the folder currently contains exactly the three files being moved (`PrintPickingListRequest.cs`, `PrintPickingListResult.cs`, `IPickingListSource.cs`) and nothing else — it will be empty after the move and must be removed.

#### Decision 4: No DI registration changes
**Options considered:**
- Re-check or re-register the `IPickingListSource` → `ShoptetApiExpeditionListSource` binding.
- Leave registration untouched.

**Chosen approach:** Leave the DI registration untouched (verify only).

**Rationale:** Registration in `ShoptetApiAdapterServiceCollectionExtensions.cs` is type-based (`services.AddScoped<IPickingListSource, ShoptetApiExpeditionListSource>()` or similar). Updating the `using` directive at the top of that file from `Anela.Heblo.Domain.Features.Logistics.Picking` to `Anela.Heblo.Application.Features.Logistics.Picking` resolves the interface to the new location with no API call changes.

## Implementation Guidance

### Directory / Module Structure

Create new files:
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs`

Delete:
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` (the folder, since it becomes empty)

### Interfaces and Contracts

Namespace flip — and nothing else — for these three types:

```
Old: Anela.Heblo.Domain.Features.Logistics.Picking
New: Anela.Heblo.Application.Features.Logistics.Picking
```

`PrintPickingListRequest` continues to use `Anela.Heblo.Domain.Features.Logistics.Carriers`. Because `Carriers` lives in a parent namespace, the moved file should add `using Anela.Heblo.Domain.Features.Logistics;` if it does not have it implicitly (currently it relied on being in the same root namespace). Verify by inspecting `PrintPickingListRequest.cs` after the move — `IList<Carriers>` and `Logistics.Carriers.Zasilkovna` (line 16 of the original) both need either a `using` directive or fully qualified references.

`IPickingListSource` signature is unchanged: `Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest, Func<IList<string>, Task>?, CancellationToken)`.

### Data Flow

Unchanged end to end. For reference, the existing call chain is:

```
PrintPickingListJob (Hangfire trigger, Application)
  → ExpeditionListService.PrintPickingListAsync (Application)
    → IPickingListSource.CreatePickingList (port; was Domain, now Application)
      → ShoptetApiExpeditionListSource.CreatePickingList (Adapter)
        ← PrintPickingListResult
    → IPrintQueueSink (Application)
    → IEmailSender (Xcc)
```

No method bodies or call sites change; only the namespace `using` directives at the top of caller files.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Brief understates consumer count ("single namespace reference"); a developer who reads only the brief misses 10 of 11 references and produces a half-broken build. | High | Implement per FR-4's "entire `backend/` tree" scope. Verify with a solution-wide grep for `Anela.Heblo.Domain.Features.Logistics.Picking` after the change; the only remaining hits should be in the docs/superpowers/plans/ history (which are historical artefacts and must not be edited). |
| `PrintPickingListRequest.cs` line 16 references `Logistics.Carriers.Zasilkovna` using a relative namespace path; after relocation the resolution context changes. | Medium | After moving the file, change line 16's `Logistics.Carriers.Zasilkovna` (and surrounding entries) to either fully qualified `Anela.Heblo.Domain.Features.Logistics.Carriers.Zasilkovna` or add `using Anela.Heblo.Domain.Features.Logistics;` and reference `Carriers.Zasilkovna`. Confirmed by `dotnet build`. |
| Adapters.ShoptetApi imports both Domain and Application; if a future contributor removes the Application reference, the move silently breaks. | Low | Out of scope for this change. Consider an architecture test in a follow-up that asserts `Adapters.ShoptetApi` references `Anela.Heblo.Application`. |
| Empty Domain `Picking` folder remains and misleads future readers. | Medium | Enforced by FR-5 — delete the folder. Confirmed empty after the three-file move. |
| `dotnet format` introduces unrelated whitespace churn in the moved files, breaking surgical-changes rule. | Low | Run `dotnet format` only on the three new files plus the files whose `using` directives changed. Stage and review the diff before committing. |
| Inadvertent introduction of Domain → Application dependency by a sloppy fix-on-build attempt. | High | Verify after the change: `grep -r "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/` must return zero hits. Also confirm `Anela.Heblo.Domain.csproj` is unchanged. |
| Test file changes drift outside `using` updates. | Low | FR-6 forbids it. Diff each test file and confirm only the `using` directive line changed. |

## Specification Amendments

1. **Amend the brief's "single namespace reference" claim** at the top of the spec's Background section by adding a one-line correction: there are **11 files** to update across 4 projects (Application: 4 files, Adapters.ShoptetApi: 2 files, Anela.Heblo.Tests: 3 files, Anela.Heblo.Adapters.Shoptet.Tests: 2 files). FR-4 already covers this scope correctly; this amendment just removes the misleading line so a future reader is not lulled by it.

2. **Amend FR-1's acceptance criteria** to explicitly require either:
   - adding `using Anela.Heblo.Domain.Features.Logistics;` to the relocated `PrintPickingListRequest.cs`, or
   - rewriting line 16's relative `Logistics.Carriers.X` references to either bare `Carriers.X` (with the using above) or fully qualified `Anela.Heblo.Domain.Features.Logistics.Carriers.X`.

   The current spec wording ("public properties … preserved verbatim") could be read as forbidding even this benign namespace tweak; clarify that it applies to the **public surface** (property names, types, defaults), not to internal `using` directives.

3. **Add a verification step to FR-3 / NFR-3:** after the move, assert `grep -r "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/` returns zero matches. The spec mentions the dependency rule in prose but does not give a concrete verification command.

4. **Clarify FR-5** to note that "empty" must be verified — the folder contains exactly the three files being moved (confirmed during this review), so it will be empty post-move.

5. **Consider follow-up note (non-blocking):** the consumer of `IPickingListSource` is `ExpeditionListService` in the `ExpeditionList` feature. The cross-module-communication pattern in `docs/architecture/development_guidelines.md` (lines 194–207, "Consumer owns the contract") suggests that the ideal long-term home is `Application/Features/ExpeditionList/Contracts/IPickingListSource.cs`, with `PrintPickingListRequest`/`Result` in the same `Contracts/` folder. This is intentionally **out of scope** for this relocation but should be filed as a follow-up arch-review item so the conceptual misplacement is not lost when the immediate fix lands.

## Prerequisites

None. All project references, package dependencies, and module-level DI registrations are already in place to absorb the move. No migrations, no infrastructure, no config keys, and no feature flags are involved.

Verification commands (run before and after the change):

```
# Must return only references in docs/superpowers/plans/ after the change
grep -rn "Anela.Heblo.Domain.Features.Logistics.Picking" backend/

# Must return zero
grep -rn "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/

# Build + tests + format gate
dotnet build
dotnet test
dotnet format
```