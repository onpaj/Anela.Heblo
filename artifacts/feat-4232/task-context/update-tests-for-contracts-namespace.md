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

