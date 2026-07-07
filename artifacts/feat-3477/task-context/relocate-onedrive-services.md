### task: relocate-onedrive-services

[Move IOneDriveService/OneDriveFile, GraphOneDriveService, MockOneDriveService, GraphFolderResolver, and the mis-filed GraphApiHelpers.cs (renamed GraphDriveModels.cs) to Shared.Rag(.OneDrive), fix every consumer]

- [ ] Step 1: Create the target directory and move the interface + record.

  ```bash
  mkdir -p backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs
  ```

  Edit `backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs` — change only the namespace line:

  ```csharp
  namespace Anela.Heblo.Application.Shared.Rag;

  public record OneDriveFile(string Id, string Name, string ContentType, string Path);

  public interface IOneDriveService
  {
      Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default);
      Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default);
      Task<string> MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default);
      Task<string> DownloadFileTextByPathAsync(string driveId, string path, CancellationToken ct = default);
  }
  ```

  Note: `OneDriveFile` stays a `record` — this project's "DTOs are classes" rule (CLAUDE.md) applies to API `Request`/`Response` contracts serialized through the OpenAPI client generator, not to this internal Application-layer service type. Do not convert it to a class.

- [ ] Step 2: Move `GraphOneDriveService.cs`, `GraphFolderResolver.cs`, and `MockOneDriveService.cs`.

  ```bash
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphOneDriveService.cs
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphFolderResolver.cs
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/MockOneDriveService.cs
  ```

  In all three moved files, change only the `namespace` line from
  `namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;`
  to
  `namespace Anela.Heblo.Application.Shared.Rag.OneDrive;`

  Leave every other line unchanged, including the existing `using Anela.Heblo.Application.Common.Graph;` in `GraphOneDriveService.cs` and `GraphFolderResolver.cs` (this is the real, unrelated `Common/Graph/GraphApiHelpers` helper — it is not moving and both files keep depending on it exactly as today). `GraphFolderResolver` stays `internal` — no visibility change needed since old and new locations are both in the `Anela.Heblo.Application` assembly.

- [ ] Step 3: Move and rename the mis-filed DTO file.

  ```bash
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphDriveModels.cs
  ```

  Edit `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphDriveModels.cs` — change only the namespace line. Full expected file content after the edit:

  ```csharp
  using System.Text.Json.Serialization;

  namespace Anela.Heblo.Application.Shared.Rag.OneDrive;

  internal class GraphDriveItem
  {
      [JsonPropertyName("id")]
      public string Id { get; set; } = string.Empty;

      [JsonPropertyName("name")]
      public string Name { get; set; } = string.Empty;

      [JsonPropertyName("webUrl")]
      public string WebUrl { get; set; } = string.Empty;

      [JsonPropertyName("file")]
      public GraphFileFacet? File { get; set; }
  }

  internal class GraphFileFacet
  {
      [JsonPropertyName("mimeType")]
      public string MimeType { get; set; } = "application/octet-stream";
  }

  internal class GraphDriveItemCollection
  {
      [JsonPropertyName("value")]
      public List<GraphDriveItem> Value { get; set; } = [];
  }
  ```

  This file declares no `GraphApiHelpers` class (despite its old filename) — it only holds `GraphDriveItem`/`GraphFileFacet`/`GraphDriveItemCollection`, consumed directly by `GraphOneDriveService` and `GraphFolderResolver` in the same `Shared.Rag.OneDrive` namespace (no new `using` needed for them since they're in the same namespace after the move).

- [ ] Step 4: Update `KnowledgeBaseModule.cs` — add the OneDrive sub-namespace using (registration itself still lives here for now; only fix imports).

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

  Add this using (alongside the `Anela.Heblo.Application.Shared.Rag;` and `Anela.Heblo.Application.Shared.Rag.DocumentExtractors;` lines added in the previous task):
  ```csharp
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  ```
  (`IOneDriveService`/`OneDriveFile` now resolve via the already-present `using Anela.Heblo.Application.Shared.Rag;`; `GraphOneDriveService` and `MockOneDriveService` need the new `.OneDrive` using. Keep `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` — still needed for the non-relocated types.)

  Do not change the registration logic itself in this task (the `services.AddScoped<IOneDriveService, GraphOneDriveService>();` / `MockOneDriveService` block, `kbOptions`/`sharePointConfigured`/`useMockAuth`/`bypassJwtValidation` locals, and the conditional `AddHttpClient`/`AddMemoryCache` calls all stay put here — moving DI ownership is the next task).

- [ ] Step 5: Update `KnowledgeBaseIngestionJob.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```
  (This file only injects `IOneDriveService` from `KnowledgeBase.Services` — no other member of that namespace — so a straight swap is correct.)

- [ ] Step 6: Update `KnowledgeBaseArticleStyleGuideSource.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 7: Update `LeafletIngestionJob.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 8: `dotnet build` and confirm `src` compiles (test project will still fail — expected until Step 10).

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  `Anela.Heblo.Application`, `Anela.Heblo.API`, `Anela.Heblo.Domain`, `Anela.Heblo.Persistence` must build with 0 errors.

- [ ] Step 9: Update `HebloWebApplicationFactory.cs`.

  File: `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  ```
  (This file references both `IOneDriveService` — resolves from `Shared.Rag` — and `MockOneDriveService` — resolves from `Shared.Rag.OneDrive` — in its DI-override block around `services.Where(s => s.ServiceType == typeof(IOneDriveService))` / `services.AddScoped<IOneDriveService, MockOneDriveService>();`. Do not change that override logic itself — only the imports.)

- [ ] Step 10: Move and fix `GraphOneDriveServiceTests.cs`.

  ```bash
  mkdir -p backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive
  git mv backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/GraphOneDriveServiceTests.cs \
         backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/GraphOneDriveServiceTests.cs
  ```

  Change:
  ```csharp
  using System.Net;
  using System.Text;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Microsoft.Extensions.Caching.Memory;
  using Microsoft.Extensions.Logging.Abstractions;
  using Microsoft.Identity.Web;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using System.Net;
  using System.Text;
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  using Microsoft.Extensions.Caching.Memory;
  using Microsoft.Extensions.Logging.Abstractions;
  using Microsoft.Identity.Web;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Shared.Rag.OneDrive;
  ```
  (`GraphOneDriveService` is the only relocated type this file references directly, and it now lives in `Shared.Rag.OneDrive`.)

- [ ] Step 11: Update the three remaining test files that reference `IOneDriveService`/`OneDriveFile` directly.

  `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

  `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```
  (Keep `using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;` unchanged — that's for `KnowledgeBaseArticleStyleGuideSource` itself, which is not moving.)

  `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 12: `dotnet build` the whole solution and confirm success.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors.

- [ ] Step 13: Run the affected test classes and confirm they pass.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Shared.Rag.OneDrive|FullyQualifiedName~KnowledgeBaseIngestionJobTests|FullyQualifiedName~KnowledgeBaseArticleStyleGuideSourceTests|FullyQualifiedName~LeafletIngestionJobTests" \
    2>&1 | tail -60
  ```
  Expect all listed test classes to pass with 0 failures.

- [ ] Step 14: Commit.

  ```bash
  git add -A
  git commit -m "Relocate IOneDriveService, its implementations, and Graph DTOs to Shared.Rag.OneDrive"
  ```

---

