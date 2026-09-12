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

