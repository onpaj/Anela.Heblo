# Architecture Review: Rename `LeafletGenerationLoggingBehavior` to `LeafletGenerationPersistenceBehavior`

## Skip Design: true

Pure backend identifier rename. No UI components, screens, or visual surface area touched.

## Architectural Fit Assessment

The rename aligns with the codebase's Vertical Slice / Clean Architecture conventions: the file already lives in the correct slice (`Application/Features/Leaflet/Pipeline/`), uses the established `IPipelineBehavior<TRequest, TResponse>` contract, and is registered through the feature's own module (`LeafletModule.AddLeafletModule`). Renaming touches three artifacts (production class, DI registration, test fixture) plus one historical plan document — no module boundaries, persistence contracts, MediatR ordering, or HTTP contracts are crossed.

**One non-trivial architectural observation that the spec does not address:** the KnowledgeBase slice contains a structurally identical pipeline behavior, `QuestionLoggingBehavior` (`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/QuestionLoggingBehavior.cs`), which is also misnamed by the same criterion — it persists `KnowledgeBaseQuestionLog` via `_repository.SaveQuestionLogAsync` and mutates `response.Id = log.Id`. Renaming only the Leaflet variant introduces a cross-slice naming inconsistency: two behaviors with the same shape and responsibility will diverge in name. This is acceptable for a surgical rename, but the team should know the inconsistency exists and decide whether KB will follow. See **Specification Amendments** below.

There are no other `*Pipeline` behaviors registered against `GenerateLeafletRequest`, so MediatR ordering is a non-issue.

## Proposed Architecture

### Component Overview

```
GenerateLeafletRequest (MediatR pipeline)
       │
       ▼
┌───────────────────────────────────────────────┐
│ IPipelineBehavior<                            │
│   GenerateLeafletRequest,                     │
│   GenerateLeafletResponse>                    │
│                                               │
│  Implementation: LeafletGenerationPersistence │
│                  Behavior  (renamed)          │
│                                               │
│  Dependencies:                                │
│   • ILeafletRepository                        │
│   • ICurrentUserService                       │
│   • ILogger<LeafletGenerationPersistence-     │
│             Behavior>                         │
└───────────────────────────────────────────────┘
       │
       ▼
GenerateLeafletHandler → GenerateLeafletResponse (Id stamped on the way back)
```

The component graph is unchanged. Only the identifier on the box changes.

### Key Design Decisions

#### Decision 1: Class name choice — `LeafletGenerationPersistenceBehavior` (vs. `SaveLeafletGenerationBehavior`)
**Options considered:** `LeafletGenerationPersistenceBehavior`, `SaveLeafletGenerationBehavior`, `PersistLeafletGenerationBehavior`.
**Chosen approach:** `LeafletGenerationPersistenceBehavior` (already selected in the spec).
**Rationale:** Matches the noun-based `<Subject><Responsibility>Behavior` form used elsewhere in MediatR pipeline behaviors in this codebase (e.g., `QuestionLoggingBehavior`). It cleanly conveys both subject (LeafletGeneration) and responsibility (Persistence). The `Save…` verb-first form would diverge from the existing naming pattern.

#### Decision 2: Scope is rename-only — do not split observability
**Options considered:** (a) Pure rename; (b) Rename + extract timing into a separate `LeafletGenerationLoggingBehavior`.
**Chosen approach:** Pure rename (option a).
**Rationale:** The spec explicitly marks the split as out of scope. Splitting requires deciding pipeline ordering (logging must wrap persistence to record duration of the persisted write, not before it), adding a second DI registration, and creating new tests. This rename is intended as a surgical correctness fix, not a refactor. The split is tracked as a follow-up.

#### Decision 3: Preserve the swallow-and-log `catch (Exception ex)` block
**Options considered:** (a) Preserve as-is; (b) Tighten to specific exception types.
**Chosen approach:** Preserve as-is.
**Rationale:** The existing tests (`Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId`) assert that a DB failure does not break the response. Tightening the catch would change semantics and risk regression. Tracked as follow-up.

#### Decision 4: Frozen historical plan stays untouched
**Options considered:** (a) Update `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` to the new name; (b) Leave references as historical record.
**Chosen approach:** Leave references in `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` unchanged.
**Rationale:** That document is a frozen implementation plan describing the state of the world on its authoring date. Rewriting it would falsify the historical record. The spec already foresaw this exception ("frozen historical plans excepted"). The `rg` grep assertion in FR-4 should therefore explicitly exclude `docs/superpowers/plans/`.

## Implementation Guidance

### Directory / Module Structure

Files to rename (no new files, no moves between directories):

```
backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/
├── LeafletGenerationLoggingBehavior.cs        →  LeafletGenerationPersistenceBehavior.cs

backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/
├── LeafletGenerationLoggingBehaviorTests.cs   →  LeafletGenerationPersistenceBehaviorTests.cs
```

Files to edit (identifier replacement only):

```
backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs (lines 24–26)
```

Namespace: `Anela.Heblo.Application.Features.Leaflet.Pipeline` — unchanged.

### Interfaces and Contracts

- **MediatR contract**: `IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>` — unchanged.
- **Public consumers**: none. The class is internal to the Application assembly and is only resolved through DI. No other code holds a reference to the type name.
- **Response shape**: `GenerateLeafletResponse.Id` (`Guid?` with public setter) is still mutated by this behavior. Unchanged.
- **Logger generic argument**: `ILogger<LeafletGenerationPersistenceBehavior>` — this is the one place in the rename where the renamed type appears in a generic position. Verify the test fixture's `Mock<ILogger<…>>` is updated correspondingly.

### Data Flow

Unchanged from the current implementation. For traceability:

1. `GenerateLeafletRequest` enters the MediatR pipeline.
2. The renamed behavior's `Handle` starts a `Stopwatch`.
3. `next()` invokes `GenerateLeafletHandler`, which returns `GenerateLeafletResponse { Id = null, … }`.
4. If `response.Success`, the behavior constructs a `LeafletGeneration`, calls `_repository.SaveGenerationAsync(generation, cancellationToken)`, and assigns `response.Id = generation.Id`.
5. On exception, the failure is logged via `_logger` and the response is returned with `Id == null`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| MediatR DI registration silently broken (e.g., wrong generic) → core write lost at runtime, build still green | High | Ensure `dotnet test` covers the persistence behavior path; the three existing tests assert the save call. Re-run them after the rename and confirm `Handle_SavesGenerationRow_AndSetsResponseId` passes. |
| Leftover references in `dotnet`/EF model snapshots, IDE-generated files, or hidden friend assemblies | Low | Run `rg "LeafletGenerationLoggingBehavior"` across `backend/src/`, `backend/test/`, and `frontend/` after the rename; expect zero matches in those paths. |
| Cross-slice naming inconsistency vs. `QuestionLoggingBehavior` (KB analog has the same misnomer) | Medium | File a follow-up brief to evaluate `QuestionLoggingBehavior` rename. Out of scope for this change but should not be forgotten. See Spec Amendment 1. |
| Historical plan document `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` flagged by the `rg` assertion | Low | Exclude `docs/superpowers/plans/` from the FR-4 grep assertion explicitly. See Spec Amendment 2. |
| `dotnet format` ordering of `using` directives or whitespace differing after rename | Low | Run `dotnet format` after the rename; the spec already requires this. |
| Git history loss on file rename (rename detection failure) | Low | Use `git mv` for both source and test file renames so blame/history surface as moves. |

## Specification Amendments

1. **Add cross-slice consistency note (informational, no implementation impact).** The same pattern (pipeline behavior that persists + mutates `response.Id`) exists in `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/QuestionLoggingBehavior.cs`. Add a short note in the spec's **Out of Scope** section: *"Renaming `QuestionLoggingBehavior` in the KnowledgeBase slice for the same reason is explicitly out of scope here and tracked as a separate follow-up."* This prevents reviewers from later filing the same defect against KB without context.

2. **Tighten FR-4's grep assertion to explicitly exclude frozen plan docs.** Replace the current FR-4 acceptance criterion with: *"`rg "LeafletGenerationLoggingBehavior"` returns no matches under `backend/src/`, `backend/test/`, and `frontend/`. Matches in `docs/superpowers/plans/` are expected and intentionally preserved as historical record."* This removes the implicit ambiguity in Open Question OQ-1, which the spec marks as resolved but does not codify in the assertion itself.

3. **Add `git mv` to the implementation instruction.** Add to FR-1 and FR-3 acceptance criteria: *"The file rename is performed with `git mv` so that history follows the file."* Avoids accidental delete-and-create which would obscure blame.

4. **Clarify the `dotnet format` expectation.** NFR-2 says "reports no diffs." After a rename, `dotnet format` may legitimately reorder `using` directives or fix the file header. Reframe as: *"`dotnet format` is run as part of the change; the resulting diff contains no formatting-only changes outside the renamed files."*

## Prerequisites

None. All required infrastructure is already in place:

- `ILeafletRepository` registered in `PersistenceModule`.
- `ICurrentUserService` available.
- MediatR pipeline infrastructure wired in `AddApplicationServices()`.
- xUnit + Moq test infrastructure already used by the existing fixture.
- No database migration, configuration entry, or feature flag is required.