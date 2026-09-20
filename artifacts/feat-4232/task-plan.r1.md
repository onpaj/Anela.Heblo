# Move DownloadFromUrl DTOs into FileStorage Contracts/ folder Implementation Plan

**Goal:** Relocate `DownloadFromUrlRequest`/`DownloadFromUrlResponse` from FileStorage's internal `UseCases/DownloadFromUrl/` namespace into a new `Application/Features/FileStorage/Contracts/` folder/namespace, matching the `Contracts/` convention already used by `Manufacture`, `Catalog`, `CatalogDocuments`, `Journal`, `Marketing`, and `PackingMaterials`, with zero behavioral change.

**Architecture:** Pure compile-time namespace/folder move. Two files move to a new `Contracts/` folder and get a new `namespace` declaration; every other touched file (handler, validator, DI module, API controller, the one cross-module MediatR caller in Catalog, and five test files) only changes a `using` directive. MediatR resolves `IRequestHandler<TRequest, TResponse>` by type, not namespace, so no DI registration logic changes — only the `using` that imports the concrete types.

**Tech Stack:** .NET 8, C#, MediatR, FluentValidation, xUnit, Moq, FluentAssertions, NSwag (OpenAPI/TypeScript client generation).

---

## Repository facts used by this plan (verified before writing tasks)

- Solution file: `Anela.Heblo.sln` at the repo root (`backend/` has no separate `.sln`).
- Production build target that covers every changed production file in one `dotnet build`: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (it has a `ProjectReference` to `Anela.Heblo.Application.csproj`, which contains both the `FileStorage` and `Catalog` features, plus `Anela.Heblo.Adapters.Azure`, `Anela.Heblo.Persistence`, etc.).
- Test project: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (references `Anela.Heblo.API.csproj` and `Anela.Heblo.Application.csproj`).
- No `.editorconfig` exists in this repository (verified: `find . -iname ".editorconfig"` returns nothing), so `dotnet format` uses its own defaults; the plan still places new `using` lines in the same alphabetical convention already used in each file (`System.*` first, then `Anela.Heblo.*` alphabetically, then third-party), to match existing style.
- Confirmed via `grep -rn "FileStorage.UseCases.DownloadFromUrl" backend/ --include="*.cs"` that exactly these files reference the old namespace (no others):
  - `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`
  - `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`
  - `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlRequest.cs`
  - `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`
  - `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
  - `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`
  - `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageControllerTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`
- Confirmed these other files also contain the string `DownloadFromUrl` but are **unrelated** — they reference `IBlobStorageService.DownloadFromUrlAsync(...)`, a differently-named method on a different type, not the two DTOs, and need **no change**:
  - `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`
  - `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/SimpleFileStorageTest.cs`
  - `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`
- Confirmed the generated TypeScript client (`frontend/src/api/generated/api-client.ts`) emits `DownloadFromUrlRequest`/`DownloadFromUrlResponse` as bare class/interface names (e.g. `export class DownloadFromUrlResponse extends BaseResponse implements IDownloadFromUrlResponse`) — NSwag does not embed the C# namespace in the generated names, so no diff is expected there from this rename.

## Task overview

1. `create-filestorage-contracts-folder` — move the two DTOs into `FileStorage/Contracts/`, update every production-code reference (handler, validator, module, API controller, Catalog job).
2. `update-tests-for-contracts-namespace` — update the five existing test files' `using` directives and run them.
3. `verify-build-format-and-openapi-client` — full-solution build, `dotnet format`, full relevant test run, and OpenAPI/TypeScript client regeneration + diff check.

---

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

### task: update-tests-for-contracts-namespace

**Context:** Five existing test files reference `DownloadFromUrlRequest`/`DownloadFromUrlResponse` via `using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;`. Task `create-filestorage-contracts-folder` already moved those types to `Anela.Heblo.Application.Features.FileStorage.Contracts`, so the test project currently fails to build. This task updates only the `using` directives — no test assertion, test data, or test behavior changes — and confirms all five files' tests still pass.

**Step 1 — update `DownloadFromUrlHandlerTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

Current lines 7–10:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Contracts;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Shared;
```

**Step 2 — update `FileStorageControllerTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageControllerTests.cs`

Current line 2:
```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.FileStorage.Contracts;
```

**Step 3 — update `Pipeline/FileStorageValidationPipelineTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`

Current lines 6–11:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Application.Shared;
```

Replace with:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Contracts;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Application.Shared;
```

**Step 4 — update `Validators/DownloadFromUrlRequestValidatorTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`

Current line 1:
```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.FileStorage.Contracts;
```

**Step 5 — update `ProductExportDownloadJobTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`

Current line 9:
```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.FileStorage.Contracts;
```

**Step 6 — verify no test file still references the old namespace.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
grep -rn "FileStorage.UseCases.DownloadFromUrl" backend/test/
```
Expected output: no matches (empty output, exit code 1).

**Step 7 — build the test project.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded.` with `0 Error(s)`.

**Step 8 — run the five affected test classes.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DownloadFromUrlHandlerTests|FullyQualifiedName~FileStorageControllerTests|FullyQualifiedName~FileStorageValidationPipelineTests|FullyQualifiedName~DownloadFromUrlRequestValidatorTests|FullyQualifiedName~ProductExportDownloadJobTests"
```
Expected: `Passed!` summary line with `Failed: 0`, and the total test count matching what these five classes contained before this change (no test was added, removed, or changed in assertion/behavior — only `using` directives moved).

**Step 9 — commit.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
git add backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageControllerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs
git status
git commit -m "$(cat <<'EOF'
test(filestorage): update tests for FileStorage.Contracts namespace

Repoint the five existing test files that referenced
DownloadFromUrlRequest/DownloadFromUrlResponse via the old
FileStorage.UseCases.DownloadFromUrl namespace at the new
FileStorage.Contracts namespace. Using-directive-only change; no
assertion, test data, or behavior changes.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HNbCvxMMdxF1a9PZZP265T
EOF
)"
```

---

### task: verify-build-format-and-openapi-client

**Context:** Final validation pass per this repo's standard checklist (`dotnet build` + `dotnet format`, all touched tests passing, OpenAPI client regenerated and diffed). This task runs a full-solution build, applies formatting, re-runs the full FileStorage/Catalog test surface, and regenerates the frontend TypeScript client to confirm FR-4's acceptance criterion — the public HTTP contract and generated client are unaffected by the namespace move.

**Step 1 — full solution build.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with `0 Error(s)` across every project in the solution (production and test).

**Step 2 — apply code formatting.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet format Anela.Heblo.sln
git status
```
Expected: `dotnet format` completes without error. If it rewrites any of the files touched in this feature (e.g. reordering a `using`), `git status` will show them as modified — stage and include them in this task's commit (Step 6). If it reports no changes, proceed with no extra files to stage.

**Step 3 — run the full FileStorage and Catalog job test surface (broader net than the previous task's targeted filter, to catch any indirect regression).**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.FileStorage|FullyQualifiedName~ProductExportDownloadJob"
```
Expected: `Passed!` summary line with `Failed: 0`.

**Step 4 — regenerate the OpenAPI TypeScript client and confirm no shape change.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
git diff --stat frontend/src/api/generated/api-client.ts
git diff frontend/src/api/generated/api-client.ts | grep -i "DownloadFromUrl" || true
```
Expected: either no diff at all in `api-client.ts` (most likely, since NSwag emits bare class/interface names like `DownloadFromUrlRequest`/`DownloadFromUrlResponse` with no embedded C# namespace — confirmed by inspecting the pre-existing generated file), or, if NSwag happens to regenerate unrelated cosmetic ordering/whitespace elsewhere in the file, no `DownloadFromUrl`-related line in the diff should show a route, field, or type-shape change — only, at most, a no-op regeneration. If the diff shows any `DownloadFromUrl` route, field, or shape change, STOP and investigate before proceeding (this would violate spec FR-4/NFR-3, which require the public HTTP contract to be unaffected).

**Step 5 — final full production build to confirm everything still links together.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with `0 Error(s)`.

**Step 6 — commit any formatting or regenerated-client changes (only if Steps 2 or 4 produced a diff).**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
git status
```

If `git status` shows no changes, skip straight to reporting completion — there is nothing to commit for this task.

If `git status` shows changes (e.g. `dotnet format` adjusted formatting, or the OpenAPI regeneration touched `api-client.ts`):
```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
git add -A
git status
git commit -m "$(cat <<'EOF'
chore(filestorage): apply formatting and regenerate OpenAPI client

Run dotnet format and regenerate the frontend TypeScript client after
the DownloadFromUrl Contracts/ namespace move, confirming no public
HTTP contract or generated-client shape change.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HNbCvxMMdxF1a9PZZP265T
EOF
)"
```

---

## Requirements coverage check (self-review)

- **FR-1** (create `Contracts/`, move + rename namespace of the two DTOs, handler stays put): `create-filestorage-contracts-folder`, Steps 1–4.
- **FR-2** (update in-module references — handler, validator, module): `create-filestorage-contracts-folder`, Steps 4–6.
- **FR-3** (update Catalog's `ProductExportDownloadJob.cs`): `create-filestorage-contracts-folder`, Step 8.
- **FR-4** (update `FileStorageController.cs`; regenerate/diff the OpenAPI TS client): `create-filestorage-contracts-folder` Step 7, and `verify-build-format-and-openapi-client` Step 4.
- **FR-5** (update the five existing test files, all previously-passing tests still pass): `update-tests-for-contracts-namespace`, Steps 1–8.
- **NFR-1/NFR-2** (no performance/security impact): satisfied by construction — every step is a `using`/namespace-only change with no logic touched; nothing further to implement.
- **NFR-3** (source-breaking but deploy-safe; all in-repo callers covered in the same change set): satisfied — every caller found by the grep audit above is covered by one of the three tasks; no compatibility shim is introduced, per the spec's explicit Out-of-Scope note.
- **Out-of-Scope items** (handler's internal logic, `FileStorageOptions.cs`/`FileDownloadOptions.cs`, `DownloadResilienceService.cs`/`IDownloadResilienceService.cs`, validator rules, controller routes/behavior, HTTP JSON shape, compatibility shim) — none of these are touched by any task above; confirmed by the diffs being `using`/`namespace` lines only.
