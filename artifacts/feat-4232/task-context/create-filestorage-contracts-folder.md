### task: create-filestorage-contracts-folder

**Context:** `DownloadFromUrlRequest` and `DownloadFromUrlResponse` currently live in `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/`, in namespace `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl`, alongside their handler. This task moves only the two DTO files into a new `Contracts/` folder/namespace (matching the existing pattern in `Manufacture/Contracts/`, `Catalog/Contracts/`, etc.) and repoints every production-code file that references them. The handler itself (`DownloadFromUrlHandler.cs`) stays in its current location — only its `using` changes.

**Step 1 — create the `Contracts/` folder and move the two DTO files with `git mv` (preserves file history/blame).**

Run from the worktree root:

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
mkdir -p backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts
git mv backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlRequest.cs backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlRequest.cs
git mv backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlResponse.cs
```

**Step 2 — update the namespace in the moved `DownloadFromUrlRequest.cs`.**

File: `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlRequest.cs`

Its full contents must become exactly:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.FileStorage.Contracts;

public class DownloadFromUrlRequest : IRequest<DownloadFromUrlResponse>
{
    [Required]
    public string FileUrl { get; set; } = null!;

    [Required]
    public string ContainerName { get; set; } = null!;

    public string? BlobName { get; set; }
}
```

Only the `namespace` line changes (from `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl` to `Anela.Heblo.Application.Features.FileStorage.Contracts`) — every member, attribute, and the base type are unchanged.

**Step 3 — update the namespace in the moved `DownloadFromUrlResponse.cs`.**

File: `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlResponse.cs`

Its full contents must become exactly:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FileStorage.Contracts;

public class DownloadFromUrlResponse : BaseResponse
{
    public string BlobUrl { get; set; } = null!;

    public string BlobName { get; set; } = null!;

    public string ContainerName { get; set; } = null!;

    public long FileSizeBytes { get; set; }
}
```

**Step 4 — repoint `DownloadFromUrlHandler.cs`'s `using` at the new namespace.**

File: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`

This file stays where it is (namespace `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl` is unchanged for the handler itself), but it now needs an explicit `using` for `Contracts` since `DownloadFromUrlRequest`/`DownloadFromUrlResponse` no longer live in its own namespace.

Current top of file (lines 1–14):
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

Replace with:
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage.Contracts;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

(Only line 7 is inserted: `using Anela.Heblo.Application.Features.FileStorage.Contracts;`. Nothing else in this 180-line file changes — same class body, same logic, same `DownloadFromUrlHandler : IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>` declaration.)

**Step 5 — repoint `DownloadFromUrlRequestValidator.cs`'s `using`.**

File: `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`

Current lines 1–4:
```csharp
using System;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using FluentValidation;
```

Replace with:
```csharp
using System;
using Anela.Heblo.Application.Features.FileStorage.Contracts;
using Anela.Heblo.Application.Shared;
using FluentValidation;
```

No other line in this file changes — the class stays `public class DownloadFromUrlRequestValidator : AbstractValidator<DownloadFromUrlRequest>` and every validation rule is untouched.

**Step 6 — repoint `FileStorageModule.cs`'s `using`.**

File: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`

Current lines 1–13:
```csharp
using System.Net;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Domain.Features.FileStorage;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

Replace with:
```csharp
using System.Net;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage.Contracts;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Domain.Features.FileStorage;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

No other line changes — `services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();` and the `IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` registration lower in the file are untouched; they resolve identically once the type import points at `Contracts`.

**Step 7 — repoint `FileStorageController.cs`'s `using`.**

File: `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`

Current line 1:
```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.FileStorage.Contracts;
```

No other line in this file changes — the route `[HttpPost("download")]`, the action signature `Task<ActionResult<DownloadFromUrlResponse>> DownloadFromUrl([FromBody] DownloadFromUrlRequest request, CancellationToken cancellationToken = default)`, and the body (`_mediator.Send(request, cancellationToken)` → `HandleResponse(response)`) are byte-for-byte unchanged.

**Step 8 — repoint `ProductExportDownloadJob.cs`'s `using` (the Catalog cross-module caller).**

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`

Current line 6:
```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.FileStorage.Contracts;
```

No other line in this 146-line file changes — the `_mediator.Send(new DownloadFromUrlRequest { FileUrl = exportUrl, ContainerName = _productExportOptions.Value.ContainerName, BlobName = fileName, }, cancellationToken)` call site and every field assignment, telemetry call, and log statement are untouched.

**Step 9 — verify old files are gone and no production file still references the old namespace.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
ls backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/
```
Expected output: only `DownloadFromUrlHandler.cs` is listed (the two DTO files are gone).

```bash
grep -rn "FileStorage.UseCases.DownloadFromUrl" backend/src/
```
Expected output: no matches (empty output, exit code 1).

**Step 10 — build the production code.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```
Expected: `Build succeeded.` with `0 Error(s)` (this project transitively builds `Anela.Heblo.Application.csproj`, which contains both the `FileStorage` and `Catalog` features touched in this task, plus `Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, and the adapters). The test project is intentionally not built yet — its five files still reference the old namespace and are updated in the next task.

**Step 11 — commit.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlRequest.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlResponse.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs \
        backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs
git status
git commit -m "$(cat <<'EOF'
refactor(filestorage): move DownloadFromUrl DTOs to Contracts/ folder

Relocate DownloadFromUrlRequest/DownloadFromUrlResponse from the internal
UseCases/DownloadFromUrl/ namespace into a new FileStorage/Contracts/
folder, matching the Contracts/ convention already used by Manufacture,
Catalog, CatalogDocuments, Journal, Marketing, and PackingMaterials.
Namespace-only change; no behavior, signature, or DI wiring changes.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HNbCvxMMdxF1a9PZZP265T
EOF
)"
```

---

