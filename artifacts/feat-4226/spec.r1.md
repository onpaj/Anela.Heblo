# Specification: Move OutlookEventImportMapper Out of UseCases into Services

## Summary
`OutlookEventImportMapper` currently lives under `Features/Marketing/UseCases/ImportFromOutlook/` but is exclusively consumed by `MarketingCalendarSyncService`, a service-layer component, creating an inverted Services → UseCases dependency within the Marketing module. This specification defines a purely structural relocation of the mapper class to the Services layer (namespace and file path only) with no behavioral change, correcting the dependency direction and the misleading file placement.

## Background
The Marketing module's intended layering is handlers → services (use-case handlers depend on services, never the reverse). `OutlookEventImportMapper` is an `internal static` helper class that encapsulates change-detection and mutation logic for Outlook calendar events (`HasChanges`, `ApplyChanges`, `BuildAction`). It was originally placed alongside `ImportFromOutlookHandler.cs` under `UseCases/ImportFromOutlook/`, but that handler never references it — it only delegates to `MarketingCalendarSyncService.SyncAsync()`. The mapper is actually used by `MarketingCalendarSyncService` (both the manual import path and the scheduled sync job), which must `using` a use-case namespace to reach it. This reverses the expected module dependency direction and misleads readers navigating either folder. Because the class is `internal`, this compiles without error today, but it is an architectural smell that should be corrected before further Marketing module work builds on top of it.

## Functional Requirements

### FR-1: Relocate OutlookEventImportMapper file
Move the file `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` to `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`.

**Acceptance criteria:**
- The file no longer exists at its old path.
- The file exists at `Features/Marketing/Services/OutlookEventImportMapper.cs`.
- No other file in the `ImportFromOutlook` folder is modified.

### FR-2: Update the namespace declaration
Change the class's namespace from `Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook` to `Anela.Heblo.Application.Features.Marketing.Services`.

**Acceptance criteria:**
- The `namespace` statement in the moved file reads `Anela.Heblo.Application.Features.Marketing.Services`.
- The class remains `internal static` — visibility is unchanged (it is still module-internal, just relocated within the same assembly).
- No public API surface changes; the class's members (`HasChanges`, `ApplyChanges`, `BuildAction`) keep their existing signatures.

### FR-3: Remove the now-unnecessary cross-namespace using directive
In `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs`, remove the `using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;` directive (line 8), since the mapper now lives in the same namespace as the consuming service.

**Acceptance criteria:**
- The `using` statement for the `ImportFromOutlook` namespace no longer appears in `MarketingCalendarSyncService.cs`.
- All three existing call sites (`OutlookEventImportMapper.HasChanges`, `OutlookEventImportMapper.ApplyChanges`, `OutlookEventImportMapper.BuildAction` — currently at lines 131, 150, 166) continue to resolve without a `using`, because the mapper and the service now share the `Anela.Heblo.Application.Features.Marketing.Services` namespace.
- No other `using` directives in the file are touched.

### FR-4: No change to ImportFromOutlookHandler or any other consumer
`ImportFromOutlookHandler.cs` and any other file that does not reference `OutlookEventImportMapper` directly must remain untouched, since the handler already has no direct dependency on the mapper.

**Acceptance criteria:**
- A diff of the change shows exactly two files touched with content changes (`MarketingCalendarSyncService.cs`) plus a moved/renamed file (`OutlookEventImportMapper.cs`), and no changes to `ImportFromOutlookHandler.cs` or unrelated files.
- Git history for the moved file shows a rename (not a delete + unrelated add), where the tooling used supports it.

### FR-5: Preserve full functional behavior
This is a pure code-organization change. No logic inside `HasChanges`, `ApplyChanges`, or `BuildAction` is modified, and no test expectations change.

**Acceptance criteria:**
- The method bodies of `HasChanges`, `ApplyChanges`, and `BuildAction` are byte-for-byte identical before and after the move (aside from the namespace line and any XML-doc `<see cref>`-style references, if present, that must be updated to keep resolving).
- Any existing unit tests referencing `OutlookEventImportMapper` (e.g., via its namespace) are updated to the new namespace and continue to pass unmodified in behavior.
- `MarketingCalendarSyncService`'s public behavior (`SyncAsync` and its manual/scheduled invocation paths) is unchanged.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a compile-time namespace/file reorganization with zero runtime behavior change. No performance impact is expected or should be measured.

### NFR-2: Security
Not applicable — no change to authentication, authorization, data handling, or exposed surface area. The class remains `internal` and inaccessible outside the `Anela.Heblo.Application` assembly.

## Data Model
No data model changes. This finding and fix are purely about C# namespace/file organization within the Marketing module's Application layer; no entities, DTOs, or persistence schemas are affected.

## API / Interface Design
No public API, controller, MediatR contract, or DTO changes. The affected class (`OutlookEventImportMapper`) is `internal static` and is never exposed via any HTTP endpoint, MediatR request/response, or OpenAPI-generated client. No frontend changes are required.

## Dependencies
- Depends only on the existing Marketing module code: `MarketingCalendarSyncService.cs` (Services) and `OutlookEventImportMapper.cs` (currently in UseCases/ImportFromOutlook).
- No external services, libraries, or feature flags are involved.
- Should be verified against `docs/architecture/development_guidelines.md` and `docs/architecture/filesystem.md` for the module's established `Services/` folder conventions before finalizing the exact target path (see Open Questions).

## Out of Scope
- Any change to `ImportFromOutlookHandler.cs` behavior or structure.
- Any change to the manual-import vs. scheduled-sync code paths in `MarketingCalendarSyncService`.
- Broader refactor of the Marketing module's Services/UseCases folder structure beyond this one file.
- Changing the mapper's access modifier from `internal` to `public`, or splitting it into an interface + implementation.
- Any test suite restructuring beyond updating namespace references needed for compilation.
- Addressing similar layering issues elsewhere in the codebase (this fix is scoped to this one finding).

## Open Questions

1. The brief suggests `Services/` **or** `Infrastructure/` as the target folder/namespace. The Marketing module's existing `Services/` folder already contains `MarketingCalendarSyncService.cs`, and no `Infrastructure/` subfolder is mentioned as existing for this module in the finding. Assumption made in this spec: use `Features/Marketing/Services/` and namespace `Anela.Heblo.Application.Features.Marketing.Services`, matching the sole current consumer's location. Please confirm this matches the module's established convention in `docs/architecture/filesystem.md`, or specify `Infrastructure/` if that folder already exists and is the intended home for static mapper/helper classes in this module.
2. Should `OutlookEventImportMapper` be renamed as part of this move (e.g., to drop the `Import`-specific naming since it will sit next to `MarketingCalendarSyncService` rather than the import use case), or is a namespace-only move preferred to keep the diff minimal? Assumption made in this spec: keep the class name unchanged — only move the file and change the namespace — to keep the change strictly surgical per CLAUDE.md's "surgical changes" guidance.

## Status: HAS_QUESTIONS
