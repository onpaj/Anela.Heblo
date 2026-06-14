# Consolidate Duplicate `ResolveContentType` Logic in KnowledgeBase Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the byte-identical private `ResolveContentType` implementations in `UploadDocumentHandler` and `IndexDocumentHandler` by extracting a single shared `internal static ContentTypeResolver.Resolve(...)` helper, removing silent-divergence risk for future extension additions.

**Architecture:** New `internal static class ContentTypeResolver` at `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs` (module root, alongside `KnowledgeBaseModule.cs`). Both handlers swap their private method for a call to `ContentTypeResolver.Resolve(...)`. The double-call in the upload flow stays — `Resolve` is idempotent, and `IndexDocumentHandler` must keep resolving because the background ingestion job calls it directly without prior resolution. No DI registration, no API surface change, no migrations, no frontend work.

**Tech Stack:** .NET 8, C# 12, xUnit + FluentAssertions (existing test conventions). `InternalsVisibleTo("Anela.Heblo.Tests")` is already present in `Anela.Heblo.Application.csproj` — no project-file change needed.

---

## File Structure

**New files:**

```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/
└── ContentTypeResolver.cs                                  ← NEW: internal static helper

backend/test/Anela.Heblo.Tests/KnowledgeBase/
└── ContentTypeResolverTests.cs                             ← NEW: behavior + idempotency tests
```

**Modified files:**

```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/
├── UseCases/UploadDocument/UploadDocumentHandler.cs        ← call site at line 30; delete lines 73-84
└── UseCases/IndexDocument/IndexDocumentHandler.cs          ← call site at line 30; delete lines 141-152
```

**Untouched (verify after change):** `KnowledgeBaseModule.cs`, all `IndexDocumentRequest`/`UploadDocumentRequest` DTOs, `KnowledgeBaseIngestionJob`, `UploadDocumentHandlerTests`, `IndexDocumentHandlerTests`.

---

## Task 1: Add `ContentTypeResolver` and its tests (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs`

### Step 1: Write the failing test file

- [ ] Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs` with the full test class below.

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase;

public class ContentTypeResolverTests
{
    [Theory]
    [InlineData("application/octet-stream", "x.pdf", "application/pdf")]
    [InlineData("application/octet-stream", "x.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/octet-stream", "x.doc", "application/msword")]
    [InlineData("application/octet-stream", "x.txt", "text/plain")]
    [InlineData("application/octet-stream", "x.md", "text/markdown")]
    public void Resolve_OctetStream_KnownExtension_ReturnsMappedMime(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "x.pdf", "application/pdf")]
    [InlineData("", "x.txt", "text/plain")]
    public void Resolve_EmptyContentType_KnownExtension_ReturnsMappedMime(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_NullContentType_KnownExtension_ReturnsMappedMime()
    {
        var result = ContentTypeResolver.Resolve(null!, "x.pdf");

        result.Should().Be("application/pdf");
    }

    [Fact]
    public void Resolve_OctetStream_UnknownExtension_ReturnsOriginalOctetStream()
    {
        var result = ContentTypeResolver.Resolve("application/octet-stream", "x.xyz");

        result.Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("image/png", "x.pdf", "image/png")]
    [InlineData("text/html", "x.docx", "text/html")]
    [InlineData("application/json", "x.xyz", "application/json")]
    public void Resolve_NonOctetStream_PassesThrough_RegardlessOfExtension(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("APPLICATION/OCTET-STREAM", "x.pdf", "application/pdf")]
    [InlineData("Application/Octet-Stream", "x.PDF", "application/pdf")]
    [InlineData("application/octet-stream", "x.DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Resolve_CaseInsensitive_OctetStreamAndExtension_StillResolves(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("application/octet-stream", "x.pdf")]
    [InlineData("application/octet-stream", "x.docx")]
    [InlineData("application/octet-stream", "x.doc")]
    [InlineData("application/octet-stream", "x.txt")]
    [InlineData("application/octet-stream", "x.md")]
    [InlineData("application/octet-stream", "x.xyz")]
    [InlineData("image/png", "x.pdf")]
    [InlineData("", "x.pdf")]
    public void Resolve_IsIdempotent(string contentType, string filename)
    {
        var first = ContentTypeResolver.Resolve(contentType, filename);
        var second = ContentTypeResolver.Resolve(first, filename);

        second.Should().Be(first);
    }
}
```

### Step 2: Run the test class to verify it fails

- [ ] Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ContentTypeResolverTests"
```

Expected: **build failure** — `ContentTypeResolver` does not exist in `Anela.Heblo.Application.Features.KnowledgeBase`. (`CS0103` / `CS0246`.)

### Step 3: Create `ContentTypeResolver.cs`

- [ ] Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase;

/// <summary>
/// Resolves the effective content type, falling back to file extension when the source
/// reports a generic type (application/octet-stream) or no type at all. The mapping is
/// shared across the KnowledgeBase ingestion entry points so that resolution is performed
/// in exactly one place.
/// </summary>
internal static class ContentTypeResolver
{
    public static string Resolve(string contentType, string filename) =>
        string.IsNullOrEmpty(contentType) || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(filename).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => contentType
            }
            : contentType;
}
```

Notes:
- Method signature mirrors the existing private method byte-for-byte (`string contentType, string filename` — non-nullable parameters; null inputs still work via `string.IsNullOrEmpty` and are exercised by `Resolve_NullContentType_KnownExtension_ReturnsMappedMime`).
- The `_ => contentType` fallback is what preserves `application/octet-stream` (or any other passed-in value) for unsupported extensions.

### Step 4: Run the tests to verify they pass

- [ ] Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ContentTypeResolverTests"
```

Expected: **all tests pass** (the matrix above: 5 known-extension + 2 empty-CT + 1 null-CT + 1 unknown-fallback + 3 pass-through + 3 case-insensitive + 8 idempotency rows = 23 passing assertions across the parameterized tests).

### Step 5: Commit

- [ ] Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs
git commit -m "refactor: extract ContentTypeResolver for KnowledgeBase ingestion"
```

---

## Task 2: Switch `UploadDocumentHandler` to the shared resolver

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` (call site at line 30; delete lines 73–84)

### Step 1: Replace the call site

- [ ] In `UploadDocumentHandler.cs`, replace this single line at line 30:

```csharp
        var contentType = ResolveContentType(request.ContentType, request.Filename);
```

with:

```csharp
        var contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename);
```

### Step 2: Delete the private `ResolveContentType` method

- [ ] In the same file, delete lines 73–84 (the entire `ResolveContentType` private static method **and its XML doc comment** on lines 69–72). Specifically, remove this whole block:

```csharp
    /// <summary>
    /// Resolves the effective content type, falling back to file extension when the browser
    /// reports a generic type (application/octet-stream) for drag-and-drop uploads.
    /// </summary>
    private static string ResolveContentType(string contentType, string filename) =>
        string.IsNullOrEmpty(contentType) || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(filename).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => contentType
            }
            : contentType;
```

After the edit, the class ends at `MapToSummary(...)` on what was line 67. The `using` block at the top of the file already covers `Anela.Heblo.Application.Features.KnowledgeBase` (current namespace via `Features.KnowledgeBase.UseCases.UploadDocument`) — `ContentTypeResolver` lives in `Anela.Heblo.Application.Features.KnowledgeBase`, which is **not** automatically reachable from the nested namespace `...KnowledgeBase.UseCases.UploadDocument` in C#. Add an explicit using if the build fails (see Step 3 expected result).

### Step 3: Build and confirm — add `using` if needed

- [ ] Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: **clean build**. If the compiler emits `CS0103: The name 'ContentTypeResolver' does not exist in the current context`, add at the top of `UploadDocumentHandler.cs` (in alphabetical order with the other `using`s):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
```

Re-run `dotnet build` until it succeeds.

### Step 4: Run the existing `UploadDocumentHandler` tests to verify no behavioral change

- [ ] Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UploadDocumentHandlerTests"
```

Expected: **all existing UploadDocumentHandlerTests pass** — the test fixture already verifies that the resolved `ContentType` flows through to the dispatched `IndexDocumentRequest`, so any drift in `Resolve` semantics would have surfaced there.

### Step 5: Commit

- [ ] Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs
git commit -m "refactor: use shared ContentTypeResolver in UploadDocumentHandler"
```

---

## Task 3: Switch `IndexDocumentHandler` to the shared resolver

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` (call site at line 30; delete lines 141–152)

### Step 1: Replace the call site

- [ ] In `IndexDocumentHandler.cs`, replace this single line at line 30:

```csharp
        var contentType = ResolveContentType(request.ContentType, request.Filename);
```

with:

```csharp
        var contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename);
```

This must stay in place — `IndexDocumentHandler` is also called directly by `KnowledgeBaseIngestionJob` with un-resolved input, so the resolution here is real work for that path. In the upload flow the call is an idempotent no-op (the resolver test `Resolve_IsIdempotent` guarantees this).

### Step 2: Delete the private `ResolveContentType` method

- [ ] In the same file, delete lines 141–152 (the entire `ResolveContentType` private static method **and its XML doc comment** on lines 137–140). Specifically, remove this whole block:

```csharp
    /// <summary>
    /// Resolves the effective content type, falling back to file extension when the source
    /// reports a generic type (application/octet-stream).
    /// </summary>
    private static string ResolveContentType(string contentType, string filename) =>
        string.IsNullOrEmpty(contentType) || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(filename).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => contentType
            }
            : contentType;
```

After the edit, the class ends at the `return new IndexDocumentResponse { ... };` block at line 135.

### Step 3: Build and confirm — add `using` if needed

- [ ] Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: **clean build**. If the compiler emits `CS0103: The name 'ContentTypeResolver' does not exist in the current context`, add at the top of `IndexDocumentHandler.cs` (in alphabetical order with the other `using`s):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
```

Re-run `dotnet build` until it succeeds.

### Step 4: Run the existing `IndexDocumentHandler` tests to verify no behavioral change

- [ ] Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IndexDocumentHandlerTests"
```

Expected: **all existing IndexDocumentHandlerTests pass** — they exercise the indexing path that includes content-type resolution.

### Step 5: Commit

- [ ] Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs
git commit -m "refactor: use shared ContentTypeResolver in IndexDocumentHandler"
```

---

## Task 4: Full validation gate

**Files:** none modified — verification only.

### Step 1: Confirm no other `ResolveContentType` survives in the KnowledgeBase module

- [ ] Run (no `cd`, absolute path):

```bash
grep -rn "ResolveContentType" backend/src/Anela.Heblo.Application/Features/KnowledgeBase backend/test/Anela.Heblo.Tests/KnowledgeBase
```

Expected: **no matches**. The private methods are gone; the new helper is named `Resolve`, not `ResolveContentType`. (A match in `Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` is expected and **explicitly out of scope per arch-review §"Specification Amendments" item 2**. Do not touch Leaflet in this plan.)

### Step 2: Run every KnowledgeBase test

- [ ] Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase"
```

Expected: **all KnowledgeBase tests pass** (existing `UploadDocumentHandlerTests`, `IndexDocumentHandlerTests`, any other KB tests, plus the new `ContentTypeResolverTests`).

### Step 3: Full backend build clean

- [ ] Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **0 errors, 0 new warnings**. (Pre-existing warnings unrelated to KnowledgeBase are acceptable.)

### Step 4: Formatting clean

- [ ] Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: **exit code 0** (no formatting drift introduced).

If it reports formatting changes, run without `--verify-no-changes`, inspect the diff, commit only formatting changes to the three files this plan touched:

```bash
dotnet format backend/Anela.Heblo.sln
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs
git commit -m "chore: dotnet format"
```

### Step 5: No further commits required

- [ ] Confirm `git status` is clean and three (or four, if formatting was needed) commits exist on the branch:

```bash
git status
git log --oneline -5
```

Expected: clean working tree; the commits introduced by this plan are the three (or four) listed above.

---

## Self-Review

**1. Spec coverage:**

| Spec requirement | Covered by |
|---|---|
| FR-1 (single source of truth, exact path, exact method signature, behavior preserved, test cases) | Task 1 — Step 3 creates the file with correct path/signature/visibility; Step 1 enumerates every required test case. |
| FR-1 acceptance "`Resolve_OctetStream_UnknownExtension_ReturnsOriginalOctetStream`" (unsupported-extension fallback) | Task 1 — `Resolve_OctetStream_UnknownExtension_ReturnsOriginalOctetStream`. |
| FR-1 acceptance "case-insensitive comparison" | Task 1 — `Resolve_CaseInsensitive_OctetStreamAndExtension_StillResolves` covers both `contentType` case and extension case. |
| FR-2 (UploadDocumentHandler delegates, private method deleted, behavior unchanged) | Task 2 — Step 1 swaps the call; Step 2 deletes the method; Step 4 verifies via existing tests. |
| FR-3 (IndexDocumentHandler delegates, private method deleted, both entry points produce identical resolved MIME, idempotency unit-tested) | Task 3 — Step 1 swaps the call; Step 2 deletes the method; idempotency is asserted by `Resolve_IsIdempotent` (Task 1, Step 1); both entry points share one resolver. |
| FR-4 (backward compatibility for unsupported extensions: `Resolve("application/octet-stream", "file.xyz")` returns `"application/octet-stream"`; non-octet-stream pass-through) | Task 1 — `Resolve_OctetStream_UnknownExtension_ReturnsOriginalOctetStream` and `Resolve_NonOctetStream_PassesThrough_RegardlessOfExtension`. |
| NFR-1 (performance — pure synchronous helper) | Implementation is identical to existing private methods; no allocations beyond `Path.GetExtension`/`ToLowerInvariant`. |
| NFR-2 (security unchanged) | Hard-coded mapping, no user input affects mapping; not in any trust boundary. |
| NFR-3 (one-file change for new extensions) | Future additions edit `ContentTypeResolver.cs` only. |
| NFR-4 (≥80% line+branch coverage on resolver) | The test matrix exercises every switch arm, both branches of the outer ternary, both halves of the `IsNullOrEmpty || Equals(...)` short-circuit, and the case-insensitive path. Full coverage. |
| Arch-review amendment 1 (idempotency assertion as explicit FR-3 bullet) | Task 1 — `Resolve_IsIdempotent` covers the full mapping table plus pass-through and empty-CT rows. |
| Arch-review amendment 2 (Leaflet copy out of scope, do not touch) | Task 4, Step 1 explicitly calls out the expected Leaflet match and forbids touching it. |
| Arch-review amendment 3 (signature pinned to `string contentType, string filename`, non-nullable) | Task 1, Step 3 — signature is byte-identical to the existing private methods. |
| Arch-review prerequisite 1 (`InternalsVisibleTo` for tests) | Verified by inspection of `Anela.Heblo.Application.csproj` before writing this plan — the entry `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` is already present. No change required. |
| Validation gate (`dotnet build`, `dotnet format`, KnowledgeBase tests green) | Task 4 covers all three. |

No gaps.

**2. Placeholder scan:** No "TBD", "implement later", "add appropriate error handling", or similar. Every step gives the exact code, the exact command, and the expected output.

**3. Type consistency:** The helper class is named `ContentTypeResolver` and the method is `Resolve` in every reference (Task 1 declaration, Task 2 call site, Task 3 call site, all test calls). Parameters `string contentType, string filename` are identical at every site. Namespace `Anela.Heblo.Application.Features.KnowledgeBase` is consistent. Test namespace `Anela.Heblo.Tests.KnowledgeBase` matches the existing folder convention (`backend/test/Anela.Heblo.Tests/KnowledgeBase/`).
