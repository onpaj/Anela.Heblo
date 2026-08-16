# CatalogDocuments Shared PIF Short-Code Prefix Derivation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the duplicated PIF short-code/prefix derivation rule between `UploadPifDocumentHandler` and `ListPifDocumentsHandler` by factoring it into a single shared `PifFolderPrefixBuilder.Build(productCode)` helper, mirroring the existing `MaterialFilenameBuilder` pattern.

**Architecture:** Add a new static, dependency-free class `PifFolderPrefixBuilder` in `Features/CatalogDocuments/Infrastructure/`, alongside `MaterialFilenameBuilder`. Both PIF handlers replace their inline `shortCode`/`prefix` computation with a single call to `PifFolderPrefixBuilder.Build(request.ProductCode)`. No public contracts, DI registrations, or externally observable behavior change — this is a pure, behavior-preserving refactor.

**Tech Stack:** .NET 8, C#, MediatR, xUnit, FluentAssertions.

---

### task: extract-pif-folder-prefix-builder

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/PifFolderPrefixBuilder.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/PifFolderPrefixBuilderTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs:30-33`
- Modify: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs:29-32`

#### Goal
Implement all of spec FR-1 through FR-4 in one pass: the helper, both call-site substitutions, and its unit tests are small, tightly coupled changes that are trivially reviewable together.

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md)
- Current duplicated logic in both handlers:
  ```csharp
  var shortCode = request.ProductCode.Length >= 6
      ? request.ProductCode[..6]
      : request.ProductCode;
  var prefix = $"{shortCode}__";
  ```
- Architecture review resolved the helper's name and shape: static class `PifFolderPrefixBuilder`, static method `Build(string productCode)`, placed in `Features/CatalogDocuments/Infrastructure/` — same folder and shape convention as the existing `MaterialFilenameBuilder` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs`).
- `PifFolderPrefixBuilder` is a pure function: no constructor dependencies, no DI registration, no `CatalogDocumentsModule.cs` change needed — matches how `MaterialFilenameBuilder` is consumed today (called directly as `MaterialFilenameBuilder.Build(...)`, no interface).
- `UploadPifDocumentHandler.Handle` (`UploadPifDocumentHandler.cs:27-50`) uses `prefix` in two places: passed to `_storage.FindFolderAsync(_options.PIF.DriveId, _options.PIF.BasePath, prefix, allowMultiple: true, cancellationToken)`, and in the `NotFound` error payload `["prefix"] = prefix`. `shortCode` itself is not used anywhere else — it can be dropped entirely once `Build` is called.
- `ListPifDocumentsHandler.Handle` (`ListPifDocumentsHandler.cs:26-62`) uses `prefix` the same way (`FindFolderAsync` call) plus sets `ExpectedPrefix = prefix` on both the not-found and found response branches. `shortCode` is likewise unused elsewhere.
- Existing sibling test for the analogous Materials helper, for style reference: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/MaterialFilenameBuilderTests.cs` (xUnit `[Fact]`, FluentAssertions `.Should().Be(...)`, namespace `Anela.Heblo.Tests.Application.CatalogDocuments`).
- No existing handler-level test files exist yet for `UploadPifDocumentHandler` or `ListPifDocumentsHandler` (only the Materials equivalents have handler tests) — per spec FR-4 this task adds unit tests for the new helper only; adding handler-level test suites for the two PIF handlers is a pre-existing coverage gap and is out of scope for this task.

#### Implementation steps

- [ ] **Step 1: Write failing tests for `PifFolderPrefixBuilder.Build`**

Create `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/PifFolderPrefixBuilderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class PifFolderPrefixBuilderTests
{
    [Fact]
    public void Build_ProductCodeLongerThanSix_TruncatesToFirstSixChars()
    {
        var result = PifFolderPrefixBuilder.Build("ABC12345");
        result.Should().Be("ABC123__");
    }

    [Fact]
    public void Build_ProductCodeExactlySix_UsesWholeCode()
    {
        var result = PifFolderPrefixBuilder.Build("ABC123");
        result.Should().Be("ABC123__");
    }

    [Fact]
    public void Build_ProductCodeShorterThanSix_UsesWholeCode()
    {
        var result = PifFolderPrefixBuilder.Build("AB1");
        result.Should().Be("AB1__");
    }

    [Fact]
    public void Build_EmptyProductCode_ReturnsSeparatorOnly()
    {
        var result = PifFolderPrefixBuilder.Build(string.Empty);
        result.Should().Be("__");
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail (type doesn't exist yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PifFolderPrefixBuilderTests"`
Expected: build FAILS — `PifFolderPrefixBuilder` does not exist yet (CS0246).

- [ ] **Step 3: Create `PifFolderPrefixBuilder`**

Create `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/PifFolderPrefixBuilder.cs`:

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

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PifFolderPrefixBuilderTests"`
Expected: All 4 tests PASS.

- [ ] **Step 5: Update `UploadPifDocumentHandler` to use the shared helper**

In `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs`, replace lines 30-33:

```csharp
        var shortCode = request.ProductCode.Length >= 6
            ? request.ProductCode[..6]
            : request.ProductCode;
        var prefix = $"{shortCode}__";
```

with:

```csharp
        var prefix = PifFolderPrefixBuilder.Build(request.ProductCode);
```

Add the `using` for the `Infrastructure` namespace at the top of the file if not already present (it already is — `using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;` is present on line 2 of the existing file, since `CatalogDocumentsOptions` lives there too).

- [ ] **Step 6: Update `ListPifDocumentsHandler` to use the shared helper**

In `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs`, replace lines 29-32:

```csharp
        var shortCode = request.ProductCode.Length >= 6
            ? request.ProductCode[..6]
            : request.ProductCode;
        var prefix = $"{shortCode}__";
```

with:

```csharp
        var prefix = PifFolderPrefixBuilder.Build(request.ProductCode);
```

The `using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;` is already present on line 2 of the existing file.

- [ ] **Step 7: Build and confirm no regressions**

Run: `cd backend && dotnet build`
Expected: build succeeds with no new warnings or errors.

- [ ] **Step 8: Run the full CatalogDocuments test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogDocuments"`
Expected: All tests PASS, including the new `PifFolderPrefixBuilderTests` and the pre-existing `MaterialFilenameBuilderTests`, `UploadMaterialDocumentHandlerTests`, `ListMaterialDocumentsHandlerTests`, `GraphCatalogDocumentsStorageTests` — none of these should need changes since no public contract changed.

- [ ] **Step 9: Format the backend solution**

Run: `cd backend && dotnet format`
Expected: no unexpected changes (only whitespace/style matching the new code, if any).

- [ ] **Step 10: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/PifFolderPrefixBuilder.cs \
        src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs \
        src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs \
        test/Anela.Heblo.Tests/Application/CatalogDocuments/PifFolderPrefixBuilderTests.cs
git commit -m "refactor(catalog-documents): extract shared PifFolderPrefixBuilder for PIF short-code prefix derivation"
```

#### Acceptance criteria
- All acceptance criteria in `spec.r1.md` FR-1, FR-2, FR-3, and FR-4 are met and covered by the tests above.
- `PifFolderPrefixBuilder.Build` returns byte-for-byte identical output to the old inline computation for every product code (length > 6, == 6, < 6, empty).
- Neither `UploadPifDocumentHandler` nor `ListPifDocumentsHandler` contains inline `shortCode`/prefix-truncation logic anymore — both call `PifFolderPrefixBuilder.Build(request.ProductCode)`.
- `dotnet build` and `dotnet format` succeed with no new warnings.
- No public interface, DTO, or error code changed.
- `MaterialFilenameBuilder` and the Materials handlers are untouched.
