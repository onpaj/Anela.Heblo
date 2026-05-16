# Architecture Review: Relocate ChangeTransportBoxState Request/Response into Use Case Subfolder

## Skip Design: true

This is a structural refactor with no UI/UX surface — no visual components, no API contract change, no design decisions affecting user experience.

## Architectural Fit Assessment

The proposal **strengthens** alignment with the documented architecture. `docs/architecture/filesystem.md` (lines 99–110) explicitly defines the "Complex Features" pattern: each use case lives in `UseCases/{UseCaseName}/` containing `Handler.cs`, `Request.cs`, and `Response.cs`. All twelve sibling use cases in `Features/Logistics/UseCases/` already comply; only `ChangeTransportBoxState` has its handler in a subfolder while its DTOs remained at the root.

Integration points (verified):
- **Handler** (`UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`) already declares the target namespace and references the DTOs without a `using` (resolved via the implicit parent-namespace lookup C# performs from a nested namespace).
- **Controller** `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs:6` carries `using Anela.Heblo.Application.Features.Logistics.UseCases;` solely to bind `ChangeTransportBoxStateRequest`/`Response` (line 81–83). Every other use case it consumes has its own per-use-case `using`.
- **Tests** (3 files) similarly carry the bare `UseCases` import only for these two types.

No deviation from existing conventions; this is convergence to the established pattern.

## Proposed Architecture

### Component Overview

```
Features/Logistics/UseCases/
├── AddItemToBox/                      ← pattern reference
│   ├── AddItemToBoxHandler.cs
│   ├── AddItemToBoxRequest.cs
│   └── AddItemToBoxResponse.cs
├── ChangeTransportBoxState/           ← target after refactor
│   ├── ChangeTransportBoxStateHandler.cs    (unchanged)
│   ├── ChangeTransportBoxStateRequest.cs    (MOVED + namespace updated)
│   └── ChangeTransportBoxStateResponse.cs   (MOVED + namespace updated)
├── CreateNewTransportBox/             ← pattern reference
├── GetTransportBoxById/
├── ...
(no more stray files at UseCases/ root)
```

Compile-time consumers (must update `using` directives, no logic touch):
```
TransportBoxController ──┐
ChangeTransportBoxStateHandlerTests ──┼──→ UseCases.ChangeTransportBoxState (new home)
TransportBoxControllerTests ──┤
TransportBoxUniquenessTests ──┘
```

### Key Design Decisions

#### Decision 1: Use C# file-scoped namespace declaration
**Options considered:** File-scoped (`namespace X;`) vs block-scoped (`namespace X { ... }`).
**Chosen approach:** File-scoped, matching the current style of both files (line 4 in each is a file-scoped declaration) and the sibling handler.
**Rationale:** Preserves the byte-level diff to just the namespace identifier; avoids reformatting the bodies and risking a `dotnet format` follow-up commit.

#### Decision 2: Remove now-redundant `using Anela.Heblo.Application.Features.Logistics.UseCases;` directives where they are no longer needed
**Options considered:** (a) Leave the orphaned `using` in place; (b) remove it from each consumer.
**Chosen approach:** Remove it where it is no longer required (controller + 3 test files).
**Rationale:** `dotnet format` removes unused usings by default in this repo's profile and would create churn on the next format run. Removing them now keeps the refactor self-contained and satisfies NFR-4 ("`dotnet format` must report no changes after the refactor"). Confirmed by inspection: in the controller, line 6 (`using ...UseCases;`) has no other consumer — all other use cases are imported via their own per-use-case `using` (lines 7–15).

#### Decision 3: Do not modify `ChangeTransportBoxStateHandler.cs`
**Options considered:** Add an explicit `using` for the DTOs to be defensive; leave handler untouched.
**Chosen approach:** Leave it untouched.
**Rationale:** The handler already sits in `UseCases.ChangeTransportBoxState`. After the move, the DTOs share that namespace, so C#'s implicit parent-namespace resolution still works and no `using` is needed. Adding one would be redundant and would create a phantom diff.

#### Decision 4: Keep DTOs as classes (not records)
**Options considered:** Convert to records during the move (they are immutable-shaped DTOs).
**Chosen approach:** Keep as classes.
**Rationale:** Project rule (CLAUDE.md, `docs/architecture/development_guidelines.md`): DTOs exposed via the OpenAPI generator must be classes — records cause parameter-order issues in the generated client. The spec correctly enforces this in §"Data Model".

## Implementation Guidance

### Directory / Module Structure

Move (preserve file content; only namespace line changes):

| From | To |
|------|----|
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateRequest.cs` | `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs` |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateResponse.cs` | `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs` |

Use `git mv` so the move is tracked as a rename (history is preserved; reviewers see one rename + one-line namespace edit instead of delete+add).

### Interfaces and Contracts

**Unchanged:**
- `class ChangeTransportBoxStateRequest : IRequest<ChangeTransportBoxStateResponse>` — same property set, same MediatR contract.
- `class ChangeTransportBoxStateResponse : BaseResponse` — same property set, same base class.

**Changed (C# only — not a public/wire contract):**
- Namespace: `Anela.Heblo.Application.Features.Logistics.UseCases` → `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState` (matches sibling pattern and the handler at `ChangeTransportBoxStateHandler.cs:12`).

### Data Flow

Unchanged. Request travels: HTTP `POST /api/...` → `TransportBoxController.ChangeTransportBoxState` (`TransportBoxController.cs:81`) → `IMediator.Send` → `ChangeTransportBoxStateHandler.Handle` → `ChangeTransportBoxStateResponse`. The only difference post-refactor is the CLR namespace of the two DTO types involved — serialization, routing, and MediatR registration are unaffected.

### Exact `using` edits required

Four C# files need a minimal edit:

| File | Edit |
|------|------|
| `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` | Replace `using Anela.Heblo.Application.Features.Logistics.UseCases;` (line 6) with `using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;`. Keep `using` list alphabetically sorted (insert in correct position; other usings on lines 7–15 already follow alphabetical order). |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs` | Remove `using Anela.Heblo.Application.Features.Logistics.UseCases;` (line 2) — the `ChangeTransportBoxState` sub-namespace is already imported on line 3. |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` | Remove `using Anela.Heblo.Application.Features.Logistics.UseCases;` (line 3) — `ChangeTransportBoxState` sub-namespace already on line 4. |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` | Remove `using Anela.Heblo.Application.Features.Logistics.UseCases;` (line 3) — `ChangeTransportBoxState` sub-namespace already on line 4. |

No edits to `ChangeTransportBoxStateHandler.cs` (it already lives in the target namespace).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale build cache references the old namespace, masking missed call sites | Low | Run `dotnet clean && dotnet build` on the solution after the move; CI will catch any remaining unqualified reference. |
| `dotnet format` introduces a follow-up diff (unused usings, sort order) | Low | Decision 2 above removes the now-unused usings as part of this change. After the edit run `dotnet format --verify-no-changes`. |
| OpenAPI generator emits a different `$ref`/type name and the regenerated TS client diffs | Low | NSwag identifies DTOs by simple class name, not by CLR namespace. Class names (`ChangeTransportBoxStateRequest`, `ChangeTransportBoxStateResponse`) and property shapes are unchanged, so the spec must be byte-identical. Verify by running the Debug build's NSwag PostBuild and `git diff -- backend/src/Anela.Heblo.API.Client frontend/src/api/generated`. |
| Reflection-based lookup (e.g., MediatR scanning, DI registration) breaks because of namespace change | Very low | MediatR scans assemblies by `IRequest<>`/`IRequestHandler<>` interfaces, not by namespace. The DI registration in `LogisticsModule` (if any) is by type, not by namespace string. No change needed. Confirm by full test pass. |
| Another file accidentally relies on `using ...UseCases;` to reach an unrelated type co-located at the root | Very low | Verified: no other types remain in `UseCases/` root after the move; the directory listing shows only subfolders. The bare `using` becomes provably orphaned for all listed consumers. |
| Git treats the move as delete+add, losing history | Low | Use `git mv` rather than copy/delete. CI should preserve `.gitattributes` rename detection (`-M` is default in `git log --follow`). |

## Specification Amendments

The spec is correct in every functional aspect. Three small additions for completeness:

1. **§FR-4 Acceptance criteria — make Decision 2 explicit.** Add: "In `TransportBoxControllerTests.cs`, `ChangeTransportBoxStateHandlerTests.cs`, and `TransportBoxUniquenessTests.cs`, the bare `using Anela.Heblo.Application.Features.Logistics.UseCases;` directive is removed (it serves no other purpose after the move). In `TransportBoxController.cs`, the same bare directive is replaced with `using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;`."

2. **§FR-3 — clarify Handler is intentionally untouched.** Add: "`ChangeTransportBoxStateHandler.cs` requires no change: it already declares `namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;` and resolves the DTOs implicitly via the shared namespace."

3. **§NFR-4 — add explicit verification commands.** Add:
   - `dotnet build` produces no errors or new warnings.
   - `dotnet format --verify-no-changes` exits with code 0.
   - `git diff -- backend/src/Anela.Heblo.API.Client frontend/src/api/generated` after a Debug build is empty.
   - `dotnet test` for `backend/test/Anela.Heblo.Tests` passes with only the `using`-line diffs in the three test files.

These are clarifications, not scope changes — the spec's intent is preserved.

## Prerequisites

None. The refactor is self-contained:

- No database migration.
- No infrastructure or configuration change.
- No new package or dependency.
- No regeneration of the OpenAPI client required to ship the change (it should produce no diff; if CI regenerates, the produced artifacts must be identical).
- Branch is already isolated as a worktree (`feat-arch-review-logistics-changetransportbox`); proceed directly to implementation with `git mv` + four small `using` edits.