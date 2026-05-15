# Specification: Rename `LeafletGenerationLoggingBehavior` to reflect persistence responsibility

## Summary
Rename the misleadingly named `LeafletGenerationLoggingBehavior` MediatR pipeline behavior to `LeafletGenerationPersistenceBehavior`, which accurately reflects its true responsibility: persisting the `LeafletGeneration` record and stamping the response with its ID. No business logic, ordering, or runtime behavior changes — this is a pure rename across the production class, its DI registration, and its test fixture.

## Background
`backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` is named and registered as a "logging" pipeline behavior, but its `Handle` method (lines 27–65) performs two non-logging responsibilities that materially affect the feature:

1. **Persists the generation record** via `_repository.SaveGenerationAsync(generation, cancellationToken)` (line 56) — a core business write the `GetLeafletGeneration` history and `SubmitLeafletFeedback` use cases depend on.
2. **Mutates the handler's response** by assigning `response.Id = generation.Id` (line 57) — the only place `GenerateLeafletResponse.Id` is populated. The handler itself returns `Id = null`.

The current name actively misleads readers: a developer searching for "where is the generation record saved" would inspect `GenerateLeafletHandler` and find nothing. If the behavior is disabled or unregistered, a core write is silently lost. The arch review filed on 2026-05-14 flagged this as a Single Responsibility / naming defect.

This spec covers only the rename — preserving exact runtime behavior and existing test assertions. Splitting timing/observability into a separate behavior is explicitly out of scope (see Out of Scope).

## Functional Requirements

### FR-1: Rename production class
Rename `LeafletGenerationLoggingBehavior` to `LeafletGenerationPersistenceBehavior`, including the source file, class declaration, constructor, and the `ILogger<T>` generic argument.

**Acceptance criteria:**
- File renamed: `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` → `LeafletGenerationPersistenceBehavior.cs`.
- Class identifier renamed to `LeafletGenerationPersistenceBehavior`.
- Constructor name updated to match.
- `ILogger<LeafletGenerationLoggingBehavior>` updated to `ILogger<LeafletGenerationPersistenceBehavior>`.
- Namespace remains `Anela.Heblo.Application.Features.Leaflet.Pipeline`.
- `Handle` method body, ordering, error handling, and the catch-and-log fallback are unchanged byte-for-byte (other than the type name in the logger generic).

### FR-2: Update DI registration
Update the registration in `LeafletModule.AddLeafletModule` so MediatR continues to wire the behavior into the `GenerateLeafletRequest` pipeline.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` lines 24–26: the `AddScoped<IPipelineBehavior<…>, LeafletGenerationLoggingBehavior>()` registration references `LeafletGenerationPersistenceBehavior` instead.
- Scope/lifetime remains `Scoped`.
- No additional pipeline behaviors are registered as part of this change.

### FR-3: Update test fixture
Rename the test class, test file, and all type references so existing assertions continue to compile and run.

**Acceptance criteria:**
- File renamed: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs` → `LeafletGenerationPersistenceBehaviorTests.cs`.
- Test class renamed to `LeafletGenerationPersistenceBehaviorTests`.
- `Mock<ILogger<LeafletGenerationLoggingBehavior>>` updated to `Mock<ILogger<LeafletGenerationPersistenceBehavior>>`.
- `CreateBehavior()` return type and constructor invocation updated to `LeafletGenerationPersistenceBehavior`.
- All three existing test cases pass unchanged:
  - `Handle_SavesGenerationRow_AndSetsResponseId`
  - `Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId`
  - `Handle_ReturnsOriginalResponse`
- Test logic, assertions, and arrange/act/assert structure are unchanged.

### FR-4: Find and update remaining references
Search the repository for `LeafletGenerationLoggingBehavior` and replace every occurrence outside the renamed files (e.g., documentation, plans). The repository-wide search before merge must show zero matches for the old identifier in source/test/doc files (frozen historical plans excepted — see Open Questions).

**Acceptance criteria:**
- `rg "LeafletGenerationLoggingBehavior"` returns no matches under `backend/src/`, `backend/test/`, and `frontend/`.
- Any references in `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` are either updated or explicitly left in place per Open Question OQ-1.

## Non-Functional Requirements

### NFR-1: Behavior parity
Runtime behavior of the `GenerateLeaflet` pipeline must be byte-identical to the pre-rename behavior. The persistence step, response ID assignment, exception swallowing, and timing measurement all execute in the same order with the same arguments.

### NFR-2: Build & format
- `dotnet build` succeeds for the entire solution.
- `dotnet format` reports no diffs.
- No new warnings introduced; the `ILogger<>` rename does not produce nullable-reference or analyzer warnings.

### NFR-3: Test coverage parity
- All three existing tests in `LeafletGenerationPersistenceBehaviorTests` pass.
- `dotnet test` reports no new failures elsewhere in the solution.
- Line/branch coverage of the renamed class is unchanged from the pre-rename baseline.

### NFR-4: Surgical change discipline
No adjacent code is refactored. The class internals, including the `try/catch` swallow-and-log pattern and the response mutation, are preserved as-is — even where they would warrant a future redesign (handled in a follow-up; see Out of Scope).

## Data Model
No data model changes. The `LeafletGeneration` entity, `ILeafletRepository.SaveGenerationAsync`, and the `GenerateLeafletResponse` shape (with its mutable `Id` setter) are untouched.

## API / Interface Design
No public API changes. The MediatR pipeline still exposes the same `IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>` contract; the implementing type's name is the only difference. No HTTP endpoints, request/response DTOs, or frontend contracts are affected.

## Dependencies
- MediatR pipeline behavior infrastructure (`IPipelineBehavior<TRequest, TResponse>`).
- `ILeafletRepository` (registered in `PersistenceModule`).
- `ICurrentUserService`.
- `ILogger<T>` from `Microsoft.Extensions.Logging`.

No new dependencies, package upgrades, or migrations.

## Out of Scope
The following improvements were considered and **explicitly excluded** from this revision to keep the change surgical. They may be addressed in follow-up work:

1. **Extracting timing/observability into a separate `LeafletGenerationLoggingBehavior`.** The brief mentions this as optional. Splitting it would require designing pipeline ordering (logging must wrap persistence) and adding a second DI registration; it expands blast radius beyond a rename.
2. **Moving response ID assignment into `GenerateLeafletHandler`.** The mutation of `response.Id` from a pipeline behavior is the architectural smell that triggered the review, but moving it changes the handler contract and persistence ownership boundary — out of scope for a rename.
3. **Replacing the swallow-and-log `catch (Exception ex)` block** with a more specific exception filter or by making persistence failures observable to callers.
4. **Renaming `LeafletGeneration.Id` ownership semantics** or making `GenerateLeafletResponse.Id` `init`-only.
5. **Database schema changes**, new repository methods, or new endpoints.

## Open Questions
None.

## Status: COMPLETE