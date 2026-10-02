# Move LeafletDocumentSummary to Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Relocate the `LeafletDocumentSummary` DTO from the `GetLeafletDocuments` use-case folder into the Leaflet module's shared `Features/Leaflet/Contracts/` folder, and repoint every consumer at the new namespace, with zero behavior change.

**Architecture:** Pure structural refactor. One new file (`Contracts/LeafletDocumentSummary.cs`) is created holding the unchanged class; the class is removed from `GetLeafletDocumentsRequest.cs`; four consuming files (2 backend source, 1 backend source with pre-existing sibling-use-case import to drop, 1 test file) get their `using` directives updated. No interface, DI, migration, or API-contract changes.

**Tech Stack:** .NET 8 / C#, MediatR, xUnit.

---

### task: move-summary-to-contracts

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs`

- [ ] **Step 1: Create the new Contracts file with the moved class**

Create `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

public class LeafletDocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public Guid? FirstChunkId { get; set; }
}
```

This is a byte-for-byte copy of the class currently at the bottom of `GetLeafletDocumentsRequest.cs` — no member, type, or default value changes.

- [ ] **Step 2: Remove the class from its old location and add the new using**

Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs`. Before:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;

public class GetLeafletDocumentsRequest : IRequest<GetLeafletDocumentsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "IngestedAt";
    public bool SortDescending { get; set; } = true;
    public string? FilenameFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? ContentTypeFilter { get; set; }
}

public class GetLeafletDocumentsResponse : BaseResponse
{
    public List<LeafletDocumentSummary> Documents { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

public class LeafletDocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public Guid? FirstChunkId { get; set; }
}
```

After:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;

public class GetLeafletDocumentsRequest : IRequest<GetLeafletDocumentsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "IngestedAt";
    public bool SortDescending { get; set; } = true;
    public string? FilenameFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? ContentTypeFilter { get; set; }
}

public class GetLeafletDocumentsResponse : BaseResponse
{
    public List<LeafletDocumentSummary> Documents { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
```

- [ ] **Step 3: Add the using to the handler in the same folder**

Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs`. Before:

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
```

After:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
```

The rest of `GetLeafletDocumentsHandler.cs` (the `Handle` method body, including the `new LeafletDocumentSummary { ... }` construction) is unchanged — it now resolves `LeafletDocumentSummary` from the new `using`.

- [ ] **Step 4: Build to confirm this half compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds (or fails only with the errors fixed in the next task — `UploadLeafletResponse.cs` / `UploadLeafletHandler.cs` still importing the now-empty type from the old namespace). If it fails only on those two files' missing `LeafletDocumentSummary` in `UseCases.GetLeafletDocuments`, that confirms Step 1-3 are correct; proceed to the next task.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs
git commit -m "refactor(leaflet): move LeafletDocumentSummary to Contracts/"
```

### task: update-leaflet-consumers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs`

- [ ] **Step 1: Fix the using in UploadLeafletResponse.cs**

Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs`. Before:

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;

public class UploadLeafletResponse : BaseResponse
{
    public LeafletDocumentSummary? Document { get; set; }
}
```

After:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;

public class UploadLeafletResponse : BaseResponse
{
    public LeafletDocumentSummary? Document { get; set; }
}
```

- [ ] **Step 2: Fix the using in UploadLeafletHandler.cs**

Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs`. Before (top of file):

```csharp
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
```

After:

```csharp
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
```

Everything else in the file (constructor, `Handle`, `MapToSummary`, `ResolveContentType`) is unchanged — `MapToSummary` still returns `LeafletDocumentSummary`, now resolved from `Contracts`.

- [ ] **Step 3: Add the using to the test file**

Edit `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs`. Before (top of file):

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;
```

After:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;
```

Keep the existing `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` line — the file still uses `GetLeafletDocumentsRequest`/`GetLeafletDocumentsResponse` from that namespace elsewhere; only `LeafletDocumentSummary` (lines 156 and 294) needs the new `Contracts` using. Do not modify lines 156 or 294 themselves — they already just say `new LeafletDocumentSummary { ... }`, which now resolves via the added using.

- [ ] **Step 4: Build and run the affected test suite**

Run: `cd backend && dotnet build`
Expected: build succeeds with no errors.

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Leaflet"`
Expected: all Leaflet tests pass, including `LeafletControllerTests`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs
git commit -m "refactor(leaflet): repoint LeafletDocumentSummary consumers at Contracts/"
```

### task: verify-full-build-and-format

**Files:**
- No new files. Verification-only task covering the whole solution plus the generated TypeScript client, per `docs/development/api-client-generation.md`.

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors and no new warnings compared to the pre-change baseline.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting violations reported for the touched files. If it reports violations, run `dotnet format` (without `--verify-no-changes`) and re-stage/commit the formatting fix as part of the same task's changes.

- [ ] **Step 3: Full backend test run**

Run: `cd backend && dotnet test`
Expected: all tests pass (in particular the full `Anela.Heblo.Tests` suite, not just the Leaflet filter used in the previous task, to catch any missed reference elsewhere in the solution).

- [ ] **Step 4: Regenerate the OpenAPI/TypeScript client and confirm no diff**

Run the project's standard client-generation build step (see `docs/development/api-client-generation.md` — normally triggered by `npm run build` in `frontend/`, which pulls the OpenAPI spec from the built backend and regenerates `frontend/src/api/generated/api-client.ts`).

Run: `cd frontend && npm run build`
Expected: build succeeds. Then run `git diff --stat frontend/src/api/generated/api-client.ts` — expect **no changes** to the `LeafletDocumentSummary`-related generated types (the class name, namespace-independent OpenAPI schema name, and member shape are all unchanged, so regeneration should produce an empty diff for this type per spec FR-3). If the diff is non-empty, inspect it: an empty/no-op diff confirms the refactor is contract-safe; any non-empty diff around `LeafletDocumentSummary` is unexpected and must be investigated before proceeding, since spec FR-3 requires zero API-shape drift.

- [ ] **Step 5: Frontend lint (regression guard only — no frontend source was intentionally changed)**

Run: `cd frontend && npm run lint`
Expected: passes with the same baseline as before this change (this task does not intentionally modify any frontend source file; a clean lint run confirms nothing was inadvertently affected by the client regeneration in Step 4).

- [ ] **Step 6: Final commit (only if Step 2 or Step 4 produced file changes to stage)**

```bash
git add -A
git commit -m "chore(leaflet): dotnet format / client regen after Contracts move" || true
```

If Steps 2 and 4 produced no changes, there is nothing to commit for this task — the two prior task commits already contain the complete change.
