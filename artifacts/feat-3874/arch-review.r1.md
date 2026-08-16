# Architecture Review: CatalogDocuments — Shared PIF short-code prefix derivation

## Skip Design: true

This is a backend-only, pure refactor with no new or changed UI components, screens, or visual design decisions. The API contracts, error codes, and response shapes are unchanged.

## Architectural Fit Assessment

The feature fits cleanly into the existing CatalogDocuments vertical slice and follows a pattern the codebase has already established once: `MaterialFilenameBuilder` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs`) is a static, dependency-free helper in the feature's `Infrastructure/` folder that encodes a filename-construction business rule shared by the Materials upload and list handlers. The PIF flow currently lacks the equivalent helper — `UploadPifDocumentHandler` and `ListPifDocumentsHandler` each inline the identical 4-line short-code/prefix computation. Adding a `PifFolderPrefixBuilder` alongside `MaterialFilenameBuilder`, in the same `Infrastructure/` folder, is a direct, low-risk application of a convention this feature module already uses — no new architectural pattern is introduced.

Both handlers (`UploadPifDocumentHandler.cs:30-33`, `ListPifDocumentsHandler.cs:29-32`) already depend only on `ICatalogDocumentsStorage`, `IOptions<CatalogDocumentsOptions>`, and `ILogger<T>` — no DI change is needed to consume a static helper, exactly as neither Materials handler needed one to consume `MaterialFilenameBuilder`.

## Proposed Architecture

### Component Overview

```
Features/CatalogDocuments/
├── Infrastructure/
│   ├── MaterialFilenameBuilder.cs   (existing — Materials filename rule)
│   └── PifFolderPrefixBuilder.cs    (new — PIF short-code/prefix rule)
├── UseCases/
│   ├── UploadPifDocument/
│   │   └── UploadPifDocumentHandler.cs   → calls PifFolderPrefixBuilder.BuildPrefix(...)
│   └── ListPifDocuments/
│       └── ListPifDocumentsHandler.cs    → calls PifFolderPrefixBuilder.BuildPrefix(...)
```

No new modules, no new DI registrations, no cross-feature or cross-module dependency. `PifFolderPrefixBuilder` is consumed exclusively within `CatalogDocuments`.

### Key Design Decisions

#### Decision 1: New dedicated helper vs. extending `MaterialFilenameBuilder`
**Options considered:**
- (a) Add a second static method to `MaterialFilenameBuilder` (e.g. `MaterialFilenameBuilder.BuildPifPrefix(...)`).
- (b) Create a new, separate static class `PifFolderPrefixBuilder` in the same `Infrastructure/` folder.

**Chosen approach:** (b) — a separate class.

**Rationale:** `MaterialFilenameBuilder` encodes the Materials filename rule (`{TYPE}__{lot}__{commonName}{ext}`); the PIF rule (`{first 6 chars or full code}__`) is a different business concept (a folder-matching prefix, not a filename) governing a different domain object (`ProductCode` truncation, not a 4-part filename). Bundling unrelated rules into one class under a name (`MaterialFilenameBuilder`) that doesn't describe PIF logic would violate single-responsibility and confuse future readers searching for "the PIF rule." This also matches the issue's own suggested direction: "factor... into a small shared helper (e.g. **alongside or analogous to** `MaterialFilenameBuilder`)" — alongside, not merged into.

#### Decision 2: Static pure function, no interface/DI
**Options considered:**
- (a) Static class with a static method (matches `MaterialFilenameBuilder`).
- (b) Injectable service behind an interface (e.g. `IPifFolderPrefixBuilder`).

**Chosen approach:** (a) — static class, static method.

**Rationale:** The derivation is a pure, stateless string transformation with no I/O and no need for test-time substitution beyond calling it directly (as `MaterialFilenameBuilderTests` already demonstrates for the analogous case). Introducing an interface and DI registration for a one-line pure function would be unjustified ceremony inconsistent with the existing sibling pattern, and `development_guidelines.md` explicitly discourages adding shared abstractions ("helper... unless a real consumer exists" / prefer the minimal existing pattern) beyond what's needed.

#### Decision 3: Method name and signature
**Options considered:**
- (a) `PifFolderPrefixBuilder.BuildPrefix(string productCode)`
- (b) `PifFolderPrefixBuilder.Build(string productCode)` (mirroring `MaterialFilenameBuilder.Build`)

**Chosen approach:** (b) — `Build(string productCode)`, returning the full `{shortCode}__` prefix string.

**Rationale:** Matches the exact method name (`Build`) already established by `MaterialFilenameBuilder.Build(...)` in the same folder, minimizing cognitive overhead — a developer who knows one helper's shape knows the other's. `PifFolderPrefixBuilder.Build(productCode)` reads naturally at both call sites (`var prefix = PifFolderPrefixBuilder.Build(request.ProductCode);`).

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/PifFolderPrefixBuilder.cs`
- No other new files. No changes to `CatalogDocumentsModule.cs` (no DI registration needed — static class).

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public static class PifFolderPrefixBuilder
{
    /// <summary>
    /// Derives the PIF folder-matching prefix from a product code: the first
    /// 6 characters (or the whole code if shorter), followed by "__".
    /// </summary>
    public static string Build(string productCode)
    {
        var shortCode = productCode.Length >= 6
            ? productCode[..6]
            : productCode;
        return $"{shortCode}__";
    }
}
```

Both `UploadPifDocumentHandler.Handle` and `ListPifDocumentsHandler.Handle` replace their inline 4-line computation with:

```csharp
var prefix = PifFolderPrefixBuilder.Build(request.ProductCode);
```

`UploadPifDocumentHandler` no longer needs a local `shortCode` variable at all (it was only used to build `prefix`); `ListPifDocumentsHandler` likewise collapses to the single call.

### Data Flow
Unchanged. `request.ProductCode` → `PifFolderPrefixBuilder.Build(...)` → `prefix` → passed into `_storage.FindFolderAsync(driveId, basePath, prefix, allowMultiple: true, ...)` exactly as today, in both handlers. No new data flows or side effects are introduced; this is an extraction, not a behavior change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Refactor accidentally changes truncation semantics (off-by-one, different length) | Low | Unit tests on the new helper (length > 6, == 6, < 6, including empty string) plus keeping existing handler-level assertions unchanged in the two `*.HandlerTests.cs` files (if/when added) as a regression guard |
| Naming collision or ambiguity with `MaterialFilenameBuilder` | Low | Distinct class name (`PifFolderPrefixBuilder`) and distinct method purpose documented in XML doc comment |
| Scope creep into merging PIF and Materials rules into one "universal" builder | Low | Explicitly out of scope per spec; this review confirms they stay separate (Decision 1) |

## Specification Amendments

- FR-1 in `spec.r1.md` left the helper's name as "approved during design/architecture." This review resolves that: the class is **`PifFolderPrefixBuilder`** with a static method **`Build(string productCode)`**, placed in `Features/CatalogDocuments/Infrastructure/`. No other amendments — the spec's functional requirements (FR-1 through FR-4) are otherwise architecturally sound and require no changes.

## Prerequisites

None. No migrations, no configuration changes, no new infrastructure. The change is implementable immediately against the current codebase state.
