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

