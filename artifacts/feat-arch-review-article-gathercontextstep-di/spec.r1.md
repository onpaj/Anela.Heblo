# Specification: Decouple Article Module from KnowledgeBase Module via Consumer-Owned Contract

## Summary
The Article module's `GatherContextStep` currently injects `IOneDriveService` directly from the `KnowledgeBase` namespace, violating the codebase's documented cross-module communication pattern. This specification replaces that direct dependency with a consumer-owned contract (`IArticleStyleGuideSource`) defined in the Article module and implemented by a KnowledgeBase adapter, mirroring the existing `ILeafletKnowledgeSource` pattern. An architecture test will lock the invariant going forward.

## Background
`docs/architecture/development_guidelines.md` mandates that all inter-module communication go through consumer-owned `Contracts/` interfaces. Today, `backend/src/Anela.Heblo.Application/Features/Article/.../GatherContextStep.cs` imports `Anela.Heblo.Application.Features.KnowledgeBase.Services` and injects `IOneDriveService` directly. This:

- Creates a compile-time dependency from Article on KnowledgeBase internals.
- Bypasses the module-boundary invariant enforced elsewhere by `ModuleBoundariesTests.cs`.
- Breaks Article silently if KnowledgeBase reorganises, renames, or relocates `IOneDriveService`.
- Blocks future extraction of either module into a separate deployable without refactoring.

A precedent fix already exists in the codebase: `ILeafletKnowledgeSource` was introduced specifically for this scenario. This spec applies the same pattern to the Article → KnowledgeBase relationship for style guide retrieval.

The only consumption point identified is the style guide retrieval performed by `GatherContextStep`. The new contract is scoped narrowly to that use case rather than re-exposing the full `IOneDriveService` surface.

## Functional Requirements

### FR-1: Define consumer-owned contract in Article module
Introduce `IArticleStyleGuideSource` in `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs`. The interface exposes only what `GatherContextStep` needs to retrieve the article style guide content.

**Signature:**
```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public interface IArticleStyleGuideSource
{
    Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken);
}
```

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs`.
- Namespace is `Anela.Heblo.Application.Features.Article.Contracts`.
- Interface contains no references to any `KnowledgeBase` namespace, type, or DTO.
- Method signature matches the behaviour previously obtained via `IOneDriveService.DownloadFileTextByPathAsync(driveId, path, ct)`.
- XML doc comment on the interface describes its purpose ("Retrieves the Article module's style guide text from an external source").

### FR-2: KnowledgeBase implements the contract via adapter
Add `OneDriveArticleStyleGuideSource` inside the KnowledgeBase module's `Infrastructure/` folder. It implements `IArticleStyleGuideSource` by delegating to the existing `IOneDriveService`.

**Acceptance criteria:**
- File lives under `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/OneDriveArticleStyleGuideSource.cs` (or `Adapters/` if that subfolder is the established convention — match the location used by `Leaflet` adapter).
- Class is `internal` if all existing adapters in KnowledgeBase are internal; otherwise match local convention.
- Class depends on `IOneDriveService` via constructor injection; no `IOneDriveService` reference exists in the Article module after the refactor.
- `DownloadStyleGuideTextAsync` forwards arguments directly to `IOneDriveService.DownloadFileTextByPathAsync` and returns its result without transformation.
- Cancellation token is propagated unchanged.

### FR-3: Register the contract binding in KnowledgeBase module DI
The KnowledgeBase module's composition root (`KnowledgeBaseModule.cs` or equivalent `IServiceCollection` extension) registers `IArticleStyleGuideSource` → `OneDriveArticleStyleGuideSource`.

**Acceptance criteria:**
- Registration uses the same lifetime (`Scoped`/`Singleton`/`Transient`) as `ILeafletKnowledgeSource` — match the precedent.
- Article module's DI registration contains no reference to `OneDriveArticleStyleGuideSource` or any KnowledgeBase type.
- Application starts successfully and resolves `IArticleStyleGuideSource` when `GatherContextStep` is constructed.

### FR-4: Refactor `GatherContextStep` to use the new contract
`GatherContextStep` no longer imports any `KnowledgeBase` namespace. It injects `IArticleStyleGuideSource` in place of `IOneDriveService` and calls `DownloadStyleGuideTextAsync` where it previously called `IOneDriveService.DownloadFileTextByPathAsync`.

**Acceptance criteria:**
- `GatherContextStep.cs` contains no `using Anela.Heblo.Application.Features.KnowledgeBase...` line.
- The private field is renamed from `_oneDrive` to a name consistent with the new contract (e.g., `_styleGuideSource`).
- All existing call sites within `GatherContextStep` route through `IArticleStyleGuideSource` — no other `IOneDriveService` member is used by Article code.
- Behaviour observable from the step's output is unchanged (same text returned for same `driveId` + `path`).
- Existing unit/integration tests for `GatherContextStep` pass after the refactor; mocks/fakes are updated to `IArticleStyleGuideSource`.

### FR-5: Add module-boundary architecture test
Extend `ModuleBoundariesTests.cs` (or add a sibling test class following the established pattern) with a reflection-based assertion that types in the Article module's assembly do not reference any `Anela.Heblo.Application.Features.KnowledgeBase` namespace.

**Acceptance criteria:**
- New test method named in line with existing conventions (e.g., `Article_Module_Should_Not_Reference_KnowledgeBase_Namespaces`).
- Test scans all types within the Article namespace and inspects field types, method parameters, return types, base types, generic arguments, and attribute usages — mirror the scope of the existing `Leaflet`/`KnowledgeBase` test.
- Test fails if a future change reintroduces a direct `KnowledgeBase` dependency in Article.
- Test passes after the refactor in this spec is applied.
- Test runs as part of the standard `dotnet test` suite (no new test project required if an existing architecture-test project already exists).

### FR-6: No regression in style guide retrieval
The end-to-end behaviour of article context gathering (including the style guide text content, error propagation, and cancellation handling) is identical before and after the refactor.

**Acceptance criteria:**
- Any existing automated test exercising `GatherContextStep` and style guide retrieval continues to pass with no behavioural changes expected.
- Exceptions thrown by the underlying `IOneDriveService` propagate through the adapter unchanged (no wrapping, no swallowing).
- Cancellation behaviour is preserved — cancelling the token surfaces an `OperationCanceledException` to the caller as it did previously.

## Non-Functional Requirements

### NFR-1: Performance
- The adapter introduces a single virtual method call; no measurable latency impact on `GatherContextStep`.
- No additional allocations beyond the adapter instance itself (lifetime determined by FR-3).

### NFR-2: Security
- No change to authentication, authorisation, or data sensitivity. The adapter performs no transformation; it forwards arguments verbatim to the existing `IOneDriveService`.
- No new secrets, configuration, or network surface introduced.

### NFR-3: Maintainability
- Article module is fully compilable without referencing any `KnowledgeBase` symbol after the change.
- The architecture test (FR-5) acts as the long-term guarantor — any future regression is caught at CI time.

### NFR-4: Backwards compatibility
- `IOneDriveService` remains in place and unchanged. Other consumers (e.g., the Leaflet flow) continue to use it directly as before. This refactor is local to the Article ↔ KnowledgeBase boundary.

## Data Model
No data-model changes. Existing `driveId` / `path` parameters carry the same semantics as today and are passed through unchanged.

## API / Interface Design

### New contract (consumer-owned)
- Location: `Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs`
- Namespace: `Anela.Heblo.Application.Features.Article.Contracts`
- Single method: `Task<string> DownloadStyleGuideTextAsync(string driveId, string path, CancellationToken cancellationToken)`

### New adapter (provider-owned)
- Location: `Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/OneDriveArticleStyleGuideSource.cs` (match local convention if it differs)
- Implements: `IArticleStyleGuideSource`
- Depends on: `IOneDriveService` (constructor injection)

### DI registration
- Module composition root within KnowledgeBase wires the contract → adapter binding using the same lifetime as `ILeafletKnowledgeSource`.

### No external API changes
- No HTTP endpoints, MediatR handlers, MVC controllers, or OpenAPI surfaces change.
- No frontend changes; OpenAPI generation is unaffected.

## Dependencies
- Existing `IOneDriveService` in the KnowledgeBase module (unchanged).
- Existing reflection-based architecture test infrastructure (e.g., `ModuleBoundariesTests.cs` and its supporting helpers).
- No new NuGet packages.
- No database migrations.

## Out of Scope
- Refactoring or splitting `IOneDriveService` itself.
- Generalising the contract to support arbitrary file downloads — the interface is intentionally scoped to the Article style guide use case.
- Auditing other Article-module files for additional cross-module dependencies beyond `GatherContextStep`. Only the documented violation is addressed here. (If the implementer encounters another Article→KnowledgeBase dependency during the refactor, surface it for a follow-up task — do not silently expand scope.)
- Moving the adapter into a hypothetical shared infrastructure project.
- Changes to the frontend or to any HTTP/OpenAPI surface.
- Documentation updates beyond inline XML doc comments.

## Open Questions
None.

## Status: COMPLETE