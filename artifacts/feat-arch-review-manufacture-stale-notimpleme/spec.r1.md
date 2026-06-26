# Specification: Remove Dead `NotImplementedManufactureProtocolRenderer` DI Placeholder

## Summary
Remove the `NotImplementedManufactureProtocolRenderer` placeholder registration from `ManufactureModule.cs` (lines 73–74). The real implementation, `QuestPdfManufactureProtocolRenderer`, is already registered in `API/Extensions/ServiceCollectionExtensions.cs` (line 152) and overrides the placeholder at runtime. The placeholder is dead code from Phase 6 archaeology and masks a hard DI dependency, turning a missing-binding bug into a deferred 500 at request time instead of a clear startup failure.

## Background
During the manufacture protocol PDF feature build-out, `ManufactureModule.cs` registered a placeholder renderer that threw `NotImplementedException`. The expectation, captured in the inline comment, was that "Phase 6" would replace it with a QuestPDF-backed implementation. Phase 6 shipped: `QuestPdfManufactureProtocolRenderer` exists and is wired in the API composition root. The Application-layer placeholder is now unreachable in the production host but remains an active service descriptor in the DI container.

This arrangement is fragile in three ways:
1. **Hidden coupling.** The Application module declares the service "satisfied" via a stub that throws, then silently relies on a later registration in the API project to replace it. A consumer reading `ManufactureModule.cs` has no signal that an additional registration is mandatory.
2. **Deferred failure.** Any future host (test host, alternate composition root, worker, integration test) that calls `services.AddManufactureModule()` without also registering the QuestPDF renderer will pass DI validation, start successfully, and only fail when `GET /api/manufacture-order/{id}/protocol.pdf` is first hit — a 500 instead of a startup error.
3. **Stale archaeology.** The "Phase 6" comment is now misleading. Future readers may waste time investigating why a placeholder is registered when the real implementation exists.

The Application layer was filed by the daily arch-review routine on 2026-06-03 as a stale-placeholder finding (`brief.md`).

## Functional Requirements

### FR-1: Remove the dead placeholder registration
Delete lines 73–74 of `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`, including:
- The `services.AddScoped<IManufactureProtocolRenderer, NotImplementedManufactureProtocolRenderer>();` line
- The preceding `// Register protocol renderer placeholder (replaced by QuestPdfManufactureProtocolRenderer in Phase 6)` comment

After the change, `ManufactureModule` must contain no reference to `NotImplementedManufactureProtocolRenderer`.

**Acceptance criteria:**
- `ManufactureModule.cs` no longer registers `IManufactureProtocolRenderer`.
- The "Phase 6" comment is removed.
- `grep -r "NotImplementedManufactureProtocolRenderer" backend/src` returns only the class definition itself (and any test references), not the registration site.

### FR-2: Delete `NotImplementedManufactureProtocolRenderer` if unused
After FR-1, check whether `NotImplementedManufactureProtocolRenderer` has any remaining references in production code or tests.
- If the only references were the now-removed registration, delete the class file.
- If tests reference it as a stub fixture, leave the class but move it to the test project (or replace test usages with `Mock<IManufactureProtocolRenderer>` / a local fake).

**Acceptance criteria:**
- `grep -r "NotImplementedManufactureProtocolRenderer" backend/src` returns no matches in `Anela.Heblo.Application` after cleanup.
- If the class is retained for tests, it lives in a test project, not in `Anela.Heblo.Application`.
- Solution compiles with no unused-type warnings on the affected file.

### FR-3: Verify the QuestPDF renderer remains the only `IManufactureProtocolRenderer` registration
After removal, confirm `API/Extensions/ServiceCollectionExtensions.cs` (line ~152) is the single registration site for `IManufactureProtocolRenderer` and that its lifetime/scope is appropriate for the consumer (HTTP request scope).

**Acceptance criteria:**
- Exactly one `IManufactureProtocolRenderer` registration exists in the composition graph for the API host.
- Lifetime is preserved at its current value (Scoped, per existing registration) — this change does not modify it.
- The `/api/manufacture-order/{id}/protocol.pdf` endpoint resolves the QuestPDF renderer end-to-end.

### FR-4: Validate behavior under a missing registration
After the cleanup, the DI container must fail fast — at startup or at the first scope-resolution — if no `IManufactureProtocolRenderer` is registered.

**Acceptance criteria:**
- In an isolated test host that calls `services.AddManufactureModule()` but does **not** register `QuestPdfManufactureProtocolRenderer`, attempting to resolve `IManufactureProtocolRenderer` (or any handler depending on it) throws `InvalidOperationException` from the DI container — not `NotImplementedException` at request time.
- The production API host (which does register the QuestPDF renderer) starts and serves PDF requests as before.

### FR-5: Update or remove related documentation
Search for any documentation referencing "Phase 6" of the manufacture protocol feature or the `NotImplementedManufactureProtocolRenderer` placeholder. Update stale references.

**Acceptance criteria:**
- No documentation under `docs/` references `NotImplementedManufactureProtocolRenderer`.
- Any "Phase 6 — placeholder will be replaced" notes are removed or rewritten to reflect the shipped state.

## Non-Functional Requirements

### NFR-1: Behavior preservation
The production runtime behavior of the `/api/manufacture-order/{id}/protocol.pdf` endpoint must be unchanged. The cleanup is internal to DI registration order and dead-code removal; no consumer of the renderer interface should observe any difference.

### NFR-2: Build & test gates
- `dotnet build` must succeed with no new warnings.
- `dotnet format` must produce no diff on the affected files.
- All existing unit, integration, and module-boundary tests must pass.
- E2E tests touching the manufacture protocol PDF endpoint (if any) continue to pass against staging.

### NFR-3: Fail-fast DI semantics
The post-change container must surface missing-binding errors at the earliest possible point. This is the primary value of the cleanup.

### NFR-4: Minimal blast radius
The change touches `ManufactureModule.cs` and at most the file defining `NotImplementedManufactureProtocolRenderer`. No other production code, no public interface, and no consumer of `IManufactureProtocolRenderer` should be modified.

## Data Model
Not applicable. This change is internal to dependency injection wiring and contains no data-model changes.

## API / Interface Design
No public API changes. `IManufactureProtocolRenderer` is unchanged. The `/api/manufacture-order/{id}/protocol.pdf` HTTP contract is unchanged. The Application module's public registration extension method (`AddManufactureModule`) keeps its signature; only its internal body shrinks by two lines.

**Implicit contract change (documented, not enforced via code):** After this change, callers of `AddManufactureModule()` are responsible for registering an `IManufactureProtocolRenderer` themselves if they need protocol rendering. The API host already does this. Any future host must follow suit. See Open Questions for whether to add a runtime guard or XML doc comment to make this explicit.

## Dependencies
- **No new external dependencies.**
- **Internal dependency:** Relies on `API/Extensions/ServiceCollectionExtensions.cs` continuing to register `QuestPdfManufactureProtocolRenderer`. This registration is unchanged by this work but is now load-bearing without a fallback.
- **Tooling:** `dotnet build`, `dotnet format`, existing test suites.

## Out of Scope
- Refactoring or modifying `QuestPdfManufactureProtocolRenderer`.
- Changing the `IManufactureProtocolRenderer` interface or its consumers.
- Moving the renderer registration from the API project into the Application module. (The current location — composition root in the API project — is the conventional Clean Architecture placement and should be preserved.)
- Adding container-validation infrastructure (e.g. enabling `ValidateOnBuild` / `ValidateScopes` globally), even though this change makes that more attractive.
- Cleaning up other Phase-N archaeology elsewhere in the codebase. If similar dead placeholders exist in other modules, file separate findings; do not bundle.
- Any change to the protocol PDF rendering output, layout, or QuestPDF library version.

## Open Questions
None.

## Status: COMPLETE