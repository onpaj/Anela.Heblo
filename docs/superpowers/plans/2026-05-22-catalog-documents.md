# Catalog Documents Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface SharePoint-hosted files (Dokumenty for Materials, PIF for Products/SemiProducts) on the CatalogDetail page with one-click open and upload from the app.

**Architecture:** New vertical-slice feature `CatalogDocuments` in the Application layer introduces an `ICatalogDocumentsStorage` port backed by Microsoft Graph (reusing the existing AzureAd + Graph SDK wiring). Five MediatR handlers cover list + upload for both document types plus a document-types reference endpoint. The frontend adds two new tab components wired into `CatalogDetailTabs`.

**Tech Stack:** C# / ASP.NET Core / MediatR / Microsoft Graph HTTP API (via `ITokenAcquisition` + `HttpClientFactory`), React / TypeScript / TanStack Query, Tailwind CSS

---

## File map

**Backend — new files:**
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/CatalogDocumentDto.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/MaterialDocumentTypeDto.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/FolderStatus.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/ListCatalogDocumentsResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/UploadDocumentResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/CatalogDocumentsOptions.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialDocumentType.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialDocumentTypes.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/ICatalogDocumentsStorage.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/FolderSearchResult.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/CatalogGraphModels.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListMaterialDocuments/ListMaterialDocumentsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListMaterialDocuments/ListMaterialDocumentsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadMaterialDocument/UploadMaterialDocumentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadMaterialDocument/UploadMaterialDocumentHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
- `backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs`

**Backend — modified files:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `// CatalogDocuments module errors (30XX)` block
- `backend/src/Anela.Heblo.API/appsettings.json` — add `CatalogDocuments` section
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `AddCatalogDocumentsModule()`

**Test files — new:**
- `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/MaterialFilenameBuilderTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/ListMaterialDocumentsHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/UploadMaterialDocumentHandlerTests.cs`

**Frontend — new files:**
- `frontend/src/api/hooks/useCatalogDocuments.ts`
- `frontend/src/components/catalog/detail/tabs/shared/DocumentList.tsx`
- `frontend/src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx`
- `frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx`
- `frontend/src/components/catalog/detail/tabs/shared/PifUploadDialog.tsx`
- `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx`
- `frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx`

**Frontend — modified files:**
- `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx` — add tab buttons + content for "documents" and "pif"
- `frontend/src/api/client.ts` — add `catalogDocuments` key to `QUERY_KEYS`

**Frontend — test files:**
- `frontend/src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx`
- `frontend/src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx`
- `frontend/src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx`

---

## Task 1: Add CatalogDocuments error codes + appsettings section

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (append before External Service errors block)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add error codes to ErrorCodes.cs**

In `ErrorCodes.cs`, add the following block before the `// External Service errors (90XX)` block:

```csharp
    // CatalogDocuments module errors (30XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CatalogDocumentInvalidTypeCode = 3001,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CatalogDocumentLotRequired = 3002,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    CatalogDocumentFolderNotFound = 3003,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    CatalogDocumentFolderMultipleMatches = 3004,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CatalogDocumentFileMissing = 3005,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    CatalogDocumentGraphError = 3006,
```

- [ ] **Step 2: Add appsettings section**

In `appsettings.json`, add `"CatalogDocuments"` after the `"KnowledgeBase"` section (or anywhere at the top level). The DriveId values below are placeholders — production values live in `secrets.json`:

```json
"CatalogDocuments": {
  "Materials": {
    "DriveId": "-- stored in secrets.json --",
    "BasePath": "/Materials/Documents"
  },
  "PIF": {
    "DriveId": "-- stored in secrets.json --",
    "BasePath": "/PIF/Documents"
  }
}
```

- [ ] **Step 3: Verify build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.API/Anela.Heblo.API.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add CatalogDocuments error codes and appsettings placeholders"
```

---

## Task 2: Contracts (DTOs + enums)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/FolderStatus.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/CatalogDocumentDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/MaterialDocumentTypeDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/ListCatalogDocumentsResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/UploadDocumentResponse.cs`

- [ ] **Step 1: Create FolderStatus.cs**

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public enum FolderStatus
{
    Found,
    NotFound,
    MultipleMatches
}
```

- [ ] **Step 2: Create CatalogDocumentDto.cs**

DTOs must be classes, not records (OpenAPI generator constraint — see CLAUDE.md).

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class CatalogDocumentDto
{
    public string Name { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
}
```

- [ ] **Step 3: Create MaterialDocumentTypeDto.cs**

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class MaterialDocumentTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool LotRequired { get; set; }
}
```

- [ ] **Step 4: Create ListCatalogDocumentsResponse.cs**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class ListCatalogDocumentsResponse : BaseResponse
{
    public FolderStatus FolderStatus { get; set; }
    public string ExpectedPrefix { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public List<CatalogDocumentDto> Files { get; set; } = [];
}
```

- [ ] **Step 5: Create UploadDocumentResponse.cs**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class UploadDocumentResponse : BaseResponse
{
    public string UploadedFilename { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Create GetMaterialDocumentTypesResponse.cs**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class GetMaterialDocumentTypesResponse : BaseResponse
{
    public List<MaterialDocumentTypeDto> DocumentTypes { get; set; } = [];
}
```

Move this file to `UseCases/GetMaterialDocumentTypes/` later, or create it there directly. Path:
`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesResponse.cs`

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;

public class GetMaterialDocumentTypesResponse : BaseResponse
{
    public List<MaterialDocumentTypeDto> DocumentTypes { get; set; } = [];
}
```

- [ ] **Step 7: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/
git commit -m "feat: add CatalogDocuments contracts (DTOs, enums, response types)"
```

---

## Task 3: Infrastructure — Options, MaterialDocumentType, MaterialDocumentTypes

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/CatalogDocumentsOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialDocumentType.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialDocumentTypes.cs`

- [ ] **Step 1: Create CatalogDocumentsOptions.cs**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public class CatalogDocumentsOptions
{
    public const string SectionName = "CatalogDocuments";

    [Required]
    public CatalogDocumentsDriveOptions Materials { get; init; } = new();

    [Required]
    public CatalogDocumentsDriveOptions PIF { get; init; } = new();
}

public class CatalogDocumentsDriveOptions
{
    [Required]
    public string DriveId { get; init; } = string.Empty;

    [Required]
    public string BasePath { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Create MaterialDocumentType.cs**

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public sealed record MaterialDocumentType(string Code, string Label, bool LotRequired);
```

- [ ] **Step 3: Create MaterialDocumentTypes.cs**

The list is seeded later by the user. The file ships with an empty list and a `// TODO: populate` marker so the system compiles and the upload-as-is path works without any entries.

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public static class MaterialDocumentTypes
{
    // TODO: populate with { Code, Label, LotRequired } entries once document type list is confirmed.
    // Example: new("COA", "Certificate of Analysis", lotRequired: true)
    public static readonly IReadOnlyList<MaterialDocumentType> All = [];
}
```

- [ ] **Step 4: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/
git commit -m "feat: add CatalogDocuments infrastructure (options, material document types)"
```

---

## Task 4: MaterialFilenameBuilder + unit tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/MaterialFilenameBuilderTests.cs`

- [ ] **Step 1: Write failing tests first**

Create `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/MaterialFilenameBuilderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class MaterialFilenameBuilderTests
{
    [Fact]
    public void Build_LotRequired_ProducesExpectedName()
    {
        // Arrange / Act
        var result = MaterialFilenameBuilder.Build(
            typeCode: "COA",
            lot: "2024-001",
            commonName: "Bisabolol",
            originalExtension: ".pdf");

        // Assert
        result.Should().Be("COA__2024-001__Bisabolol.pdf");
    }

    [Fact]
    public void Build_LotNotRequired_PreservesDoubleSeparator()
    {
        // Arrange / Act
        var result = MaterialFilenameBuilder.Build(
            typeCode: "SDS",
            lot: string.Empty,
            commonName: "Hyaluronic Acid",
            originalExtension: ".pdf");

        // Assert
        result.Should().Be("SDS____Hyaluronic Acid.pdf");
    }

    [Fact]
    public void Build_TrimsCommonName()
    {
        var result = MaterialFilenameBuilder.Build("COA", "L001", "  Vitamin E  ", ".docx");
        result.Should().Be("COA__L001__Vitamin E.docx");
    }

    [Fact]
    public void Build_ExtensionWithoutDot_AddsLeadingDot()
    {
        var result = MaterialFilenameBuilder.Build("COA", "L001", "Vitamin E", "pdf");
        result.Should().Be("COA__L001__Vitamin E.pdf");
    }

    [Fact]
    public void Build_EmptyExtension_ProducesNameWithoutExtension()
    {
        var result = MaterialFilenameBuilder.Build("COA", "L001", "Vitamin E", string.Empty);
        result.Should().Be("COA__L001__Vitamin E");
    }
}
```

- [ ] **Step 2: Run tests — expect failure (class does not exist yet)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MaterialFilenameBuilderTests" -q 2>&1 | head -20
```

Expected: compilation error — `MaterialFilenameBuilder` not found.

- [ ] **Step 3: Create MaterialFilenameBuilder.cs**

```csharp
namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public static class MaterialFilenameBuilder
{
    /// <summary>
    /// Builds a structured Material filename: {TYPE}__{lot}__{commonName}{ext}
    /// When lot is empty, the separator is preserved: {TYPE}____{commonName}{ext}
    /// </summary>
    public static string Build(string typeCode, string lot, string commonName, string originalExtension)
    {
        var ext = originalExtension.Length > 0 && !originalExtension.StartsWith('.')
            ? $".{originalExtension}"
            : originalExtension;

        return $"{typeCode}__{lot}__{commonName.Trim()}{ext}";
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MaterialFilenameBuilderTests" -q
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs \
        backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/MaterialFilenameBuilderTests.cs
git commit -m "feat: add MaterialFilenameBuilder with unit tests"
```

---

## Task 5: Storage port + Graph models + folder search result

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/FolderSearchResult.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/ICatalogDocumentsStorage.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/CatalogGraphModels.cs`

- [ ] **Step 1: Create FolderSearchResult.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public class FolderSearchResult
{
    public FolderStatus Status { get; init; }
    public string FolderId { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Create ICatalogDocumentsStorage.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public interface ICatalogDocumentsStorage
{
    /// <summary>
    /// Lists immediate child folders of basePath and returns the one whose name starts with prefix.
    /// For Material: prefix = "{productCode}__" (exact one folder expected).
    /// For PIF: prefix = "{productCode.Substring(0,6)}__" (multiple = shared folder, OK).
    /// </summary>
    Task<FolderSearchResult> FindFolderAsync(
        string driveId, string basePath, string prefix, bool allowMultiple,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all files (not subfolders) directly inside the folder identified by folderId.
    /// </summary>
    Task<List<CatalogDocumentDto>> ListFilesAsync(
        string driveId, string folderId, CancellationToken ct = default);

    /// <summary>
    /// Uploads a file into the folder identified by folderId.
    /// Uses upload session for files > 4 MB; simple PUT otherwise.
    /// Conflict behavior: rename (new file gets a "(1)"-style suffix).
    /// Returns the final filename as stored in SharePoint.
    /// </summary>
    Task<string> UploadFileAsync(
        string driveId, string folderId, string filename,
        Stream content, string contentType, long sizeBytes,
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Create CatalogGraphModels.cs**

These are internal Graph API response models scoped to this feature.

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

internal class CatalogGraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTime LastModifiedDateTime { get; set; }

    [JsonPropertyName("file")]
    public CatalogGraphFileFacet? File { get; set; }

    [JsonPropertyName("folder")]
    public CatalogGraphFolderFacet? Folder { get; set; }
}

internal class CatalogGraphFileFacet
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";
}

internal class CatalogGraphFolderFacet
{
    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }
}

internal class CatalogGraphDriveItemCollection
{
    [JsonPropertyName("value")]
    public List<CatalogGraphDriveItem> Value { get; set; } = [];
}

internal class CatalogGraphUploadSession
{
    [JsonPropertyName("uploadUrl")]
    public string UploadUrl { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/
git commit -m "feat: add ICatalogDocumentsStorage port, FolderSearchResult, Graph models"
```

---

## Task 6: GraphCatalogDocumentsStorage implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`

- [ ] **Step 1: Create GraphCatalogDocumentsStorage.cs**

```csharp
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Common.Graph;
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public class GraphCatalogDocumentsStorage : ICatalogDocumentsStorage
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphCatalogDocumentsStorage> _logger;

    private const long UploadSessionThresholdBytes = 4 * 1024 * 1024; // 4 MB

    public GraphCatalogDocumentsStorage(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        ILogger<GraphCatalogDocumentsStorage> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<FolderSearchResult> FindFolderAsync(
        string driveId, string basePath, string prefix, bool allowMultiple,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Searching for folder prefix {Prefix} in {BasePath} on drive {DriveId}", prefix, basePath, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var encodedPath = GraphApiHelpers.EncodePath(basePath.TrimStart('/'));
        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/children";

        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new FolderSearchResult { Status = FolderStatus.NotFound };

        await GraphApiHelpers.EnsureSuccessAsync(response, $"listing children of {basePath}", ct);

        var collection = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(response, ct);

        var matches = collection.Value
            .Where(i => i.Folder is not null && i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return new FolderSearchResult { Status = FolderStatus.NotFound };

        if (matches.Count > 1 && !allowMultiple)
        {
            _logger.LogWarning("Multiple folders matching prefix {Prefix} found in {BasePath} — data issue", prefix, basePath);
            return new FolderSearchResult { Status = FolderStatus.MultipleMatches };
        }

        var chosen = matches.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).First();

        if (matches.Count > 1)
            _logger.LogInformation("Multiple PIF folders match prefix {Prefix}; using first alphabetically: {Name}", prefix, chosen.Name);

        return new FolderSearchResult
        {
            Status = FolderStatus.Found,
            FolderId = chosen.Id,
            FolderName = chosen.Name,
        };
    }

    public async Task<List<CatalogDocumentDto>> ListFilesAsync(
        string driveId, string folderId, CancellationToken ct = default)
    {
        _logger.LogDebug("Listing files in folder {FolderId} on drive {DriveId}", folderId, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}/children";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        await GraphApiHelpers.EnsureSuccessAsync(response, $"listing files in folder {folderId}", ct);

        var collection = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(response, ct);

        return collection.Value
            .Where(i => i.File is not null)
            .Select(i => new CatalogDocumentDto
            {
                Name = i.Name,
                WebUrl = i.WebUrl,
                SizeBytes = i.Size,
                ModifiedAt = i.LastModifiedDateTime,
            })
            .ToList();
    }

    public async Task<string> UploadFileAsync(
        string driveId, string folderId, string filename,
        Stream content, string contentType, long sizeBytes,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Uploading {Filename} ({SizeBytes} bytes) to folder {FolderId} on drive {DriveId}",
            filename, sizeBytes, folderId, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        if (sizeBytes <= UploadSessionThresholdBytes)
            return await UploadSmallFileAsync(client, token, driveId, folderId, filename, content, contentType, ct);

        return await UploadLargeFileAsync(client, token, driveId, folderId, filename, content, sizeBytes, contentType, ct);
    }

    private static async Task<string> UploadSmallFileAsync(
        HttpClient client, string token, string driveId, string folderId,
        string filename, Stream content, string contentType, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString(filename);
        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}:/{encodedName}:/content?@microsoft.graph.conflictBehavior=rename";

        var request = GraphApiHelpers.CreateRequest(HttpMethod.Put, url, token);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await client.SendAsync(request, ct);
        await GraphApiHelpers.EnsureSuccessAsync(response, $"uploading {filename}", ct);

        var item = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItem>(response, ct);
        return item.Name;
    }

    private static async Task<string> UploadLargeFileAsync(
        HttpClient client, string token, string driveId, string folderId,
        string filename, Stream content, long sizeBytes, string contentType, CancellationToken ct)
    {
        // Step 1: Create upload session
        var encodedName = Uri.EscapeDataString(filename);
        var sessionUrl = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}:/{encodedName}:/createUploadSession";

        var bodyJson = JsonSerializer.Serialize(new
        {
            item = new Dictionary<string, string>
            {
                ["@microsoft.graph.conflictBehavior"] = "rename",
                ["name"] = filename,
            }
        });

        var sessionRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, sessionUrl, token);
        sessionRequest.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var sessionResponse = await client.SendAsync(sessionRequest, ct);
        await GraphApiHelpers.EnsureSuccessAsync(sessionResponse, $"creating upload session for {filename}", ct);

        var session = await GraphApiHelpers.DeserializeAsync<CatalogGraphUploadSession>(sessionResponse, ct);

        // Step 2: Upload content in one chunk (simplest valid approach for files ≤250 MB)
        const int chunkSize = 10 * 1024 * 1024; // 10 MB chunks
        var buffer = new byte[chunkSize];
        long offset = 0;
        string uploadedName = filename;

        while (offset < sizeBytes)
        {
            var bytesRead = await content.ReadAsync(buffer, ct);
            if (bytesRead == 0) break;

            var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
            chunkContent.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, offset + bytesRead - 1, sizeBytes);
            chunkContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            var chunkRequest = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)
            {
                Content = chunkContent
            };

            var chunkResponse = await client.SendAsync(chunkRequest, ct);
            if (chunkResponse.StatusCode == System.Net.HttpStatusCode.OK ||
                chunkResponse.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var item = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItem>(chunkResponse, ct);
                uploadedName = item.Name;
            }
            else if ((int)chunkResponse.StatusCode != 202)
            {
                await GraphApiHelpers.EnsureSuccessAsync(chunkResponse, $"uploading chunk at offset {offset}", ct);
            }

            offset += bytesRead;
        }

        return uploadedName;
    }
}
```

- [ ] **Step 2: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs
git commit -m "feat: add GraphCatalogDocumentsStorage (folder lookup, list, upload)"
```

---

## Task 7: ListMaterialDocuments use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListMaterialDocuments/ListMaterialDocumentsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListMaterialDocuments/ListMaterialDocumentsHandler.cs`

- [ ] **Step 1: Create ListMaterialDocumentsRequest.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;

public class ListMaterialDocumentsRequest : IRequest<ListCatalogDocumentsResponse>
{
    public string ProductCode { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create ListMaterialDocumentsHandler.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;

public class ListMaterialDocumentsHandler : IRequestHandler<ListMaterialDocumentsRequest, ListCatalogDocumentsResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<ListMaterialDocumentsHandler> _logger;

    public ListMaterialDocumentsHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<ListMaterialDocumentsHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ListCatalogDocumentsResponse> Handle(
        ListMaterialDocumentsRequest request, CancellationToken cancellationToken)
    {
        var prefix = $"{request.ProductCode}__";
        var driveId = _options.Materials.DriveId;
        var basePath = _options.Materials.BasePath;

        var folder = await _storage.FindFolderAsync(driveId, basePath, prefix, allowMultiple: false, cancellationToken);

        if (folder.Status != FolderStatus.Found)
        {
            _logger.LogInformation("Material folder not found for product {ProductCode} under {BasePath} (status={Status})",
                request.ProductCode, basePath, folder.Status);

            return new ListCatalogDocumentsResponse
            {
                FolderStatus = folder.Status,
                ExpectedPrefix = prefix,
                BasePath = basePath,
                Files = [],
            };
        }

        var files = await _storage.ListFilesAsync(driveId, folder.FolderId, cancellationToken);

        return new ListCatalogDocumentsResponse
        {
            FolderStatus = FolderStatus.Found,
            ExpectedPrefix = prefix,
            BasePath = basePath,
            Files = files,
        };
    }
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListMaterialDocuments/
git commit -m "feat: add ListMaterialDocuments use case"
```

---

## Task 8: ListPifDocuments use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs`

- [ ] **Step 1: Create ListPifDocumentsRequest.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListPifDocuments;

public class ListPifDocumentsRequest : IRequest<ListCatalogDocumentsResponse>
{
    public string ProductCode { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create ListPifDocumentsHandler.cs**

PIF uses a 6-character prefix (or full code if shorter than 6):

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListPifDocuments;

public class ListPifDocumentsHandler : IRequestHandler<ListPifDocumentsRequest, ListCatalogDocumentsResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<ListPifDocumentsHandler> _logger;

    public ListPifDocumentsHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<ListPifDocumentsHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ListCatalogDocumentsResponse> Handle(
        ListPifDocumentsRequest request, CancellationToken cancellationToken)
    {
        var shortCode = request.ProductCode.Length >= 6
            ? request.ProductCode[..6]
            : request.ProductCode;
        var prefix = $"{shortCode}__";
        var driveId = _options.PIF.DriveId;
        var basePath = _options.PIF.BasePath;

        // PIF allows multiple matches (Product + SemiProduct share one folder via 6-char prefix)
        var folder = await _storage.FindFolderAsync(driveId, basePath, prefix, allowMultiple: true, cancellationToken);

        if (folder.Status != FolderStatus.Found)
        {
            _logger.LogInformation("PIF folder not found for product {ProductCode} under {BasePath}",
                request.ProductCode, basePath);

            return new ListCatalogDocumentsResponse
            {
                FolderStatus = folder.Status,
                ExpectedPrefix = prefix,
                BasePath = basePath,
                Files = [],
            };
        }

        var files = await _storage.ListFilesAsync(driveId, folder.FolderId, cancellationToken);

        return new ListCatalogDocumentsResponse
        {
            FolderStatus = FolderStatus.Found,
            ExpectedPrefix = prefix,
            BasePath = basePath,
            Files = files,
        };
    }
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/
git commit -m "feat: add ListPifDocuments use case"
```

---

## Task 9: GetMaterialDocumentTypes use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/GetMaterialDocumentTypesHandler.cs`

- [ ] **Step 1: Create GetMaterialDocumentTypesRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;

public class GetMaterialDocumentTypesRequest : IRequest<GetMaterialDocumentTypesResponse>
{
}
```

- [ ] **Step 2: Create GetMaterialDocumentTypesResponse.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;

public class GetMaterialDocumentTypesResponse : BaseResponse
{
    public List<MaterialDocumentTypeDto> DocumentTypes { get; set; } = [];
}
```

- [ ] **Step 3: Create GetMaterialDocumentTypesHandler.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;

public class GetMaterialDocumentTypesHandler
    : IRequestHandler<GetMaterialDocumentTypesRequest, GetMaterialDocumentTypesResponse>
{
    public Task<GetMaterialDocumentTypesResponse> Handle(
        GetMaterialDocumentTypesRequest request, CancellationToken cancellationToken)
    {
        var dtos = MaterialDocumentTypes.All
            .Select(t => new MaterialDocumentTypeDto
            {
                Code = t.Code,
                Label = t.Label,
                LotRequired = t.LotRequired,
            })
            .ToList();

        return Task.FromResult(new GetMaterialDocumentTypesResponse { DocumentTypes = dtos });
    }
}
```

- [ ] **Step 4: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/GetMaterialDocumentTypes/
git commit -m "feat: add GetMaterialDocumentTypes use case"
```

---

## Task 10: UploadMaterialDocument use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadMaterialDocument/UploadMaterialDocumentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadMaterialDocument/UploadMaterialDocumentHandler.cs`

- [ ] **Step 1: Create UploadMaterialDocumentRequest.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;

public class UploadMaterialDocumentRequest : IRequest<UploadDocumentResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Stream FileStream { get; set; } = Stream.Null;

    // Structured upload fields (ignored when UploadAsIs = true)
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string Lot { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;

    public bool UploadAsIs { get; set; }
}
```

- [ ] **Step 2: Create UploadMaterialDocumentHandler.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;

public class UploadMaterialDocumentHandler
    : IRequestHandler<UploadMaterialDocumentRequest, UploadDocumentResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<UploadMaterialDocumentHandler> _logger;

    public UploadMaterialDocumentHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<UploadMaterialDocumentHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadMaterialDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!request.UploadAsIs)
        {
            var validationError = ValidateStructuredUpload(request);
            if (validationError != null) return validationError;
        }

        var prefix = $"{request.ProductCode}__";
        var folder = await _storage.FindFolderAsync(
            _options.Materials.DriveId, _options.Materials.BasePath, prefix,
            allowMultiple: false, cancellationToken);

        if (folder.Status == FolderStatus.NotFound)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentFolderNotFound,
                new Dictionary<string, string> { ["prefix"] = prefix, ["basePath"] = _options.Materials.BasePath });

        if (folder.Status == FolderStatus.MultipleMatches)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentFolderMultipleMatches,
                new Dictionary<string, string> { ["prefix"] = prefix });

        var filename = request.UploadAsIs
            ? request.OriginalFilename
            : BuildFilename(request);

        _logger.LogInformation("Uploading material document {Filename} for product {ProductCode}", filename, request.ProductCode);

        var uploadedName = await _storage.UploadFileAsync(
            _options.Materials.DriveId, folder.FolderId, filename,
            request.FileStream, request.ContentType, request.SizeBytes, cancellationToken);

        return new UploadDocumentResponse { UploadedFilename = uploadedName };
    }

    private static UploadDocumentResponse? ValidateStructuredUpload(UploadMaterialDocumentRequest request)
    {
        var type = MaterialDocumentTypes.All.FirstOrDefault(t =>
            string.Equals(t.Code, request.DocumentTypeCode, StringComparison.OrdinalIgnoreCase));

        if (type is null)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentInvalidTypeCode,
                new Dictionary<string, string> { ["code"] = request.DocumentTypeCode });

        if (type.LotRequired && string.IsNullOrWhiteSpace(request.Lot))
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentLotRequired,
                new Dictionary<string, string> { ["type"] = request.DocumentTypeCode });

        return null;
    }

    private static string BuildFilename(UploadMaterialDocumentRequest request)
    {
        var ext = Path.GetExtension(request.OriginalFilename);
        return MaterialFilenameBuilder.Build(request.DocumentTypeCode, request.Lot, request.CommonName, ext);
    }
}
```

Note: `UploadDocumentResponse` needs constructors for error cases. Add them to `UploadDocumentResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class UploadDocumentResponse : BaseResponse
{
    public string UploadedFilename { get; set; } = string.Empty;

    public UploadDocumentResponse() { }

    public UploadDocumentResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

Also `ListCatalogDocumentsResponse` may need an error constructor — add it:

```csharp
// Add to ListCatalogDocumentsResponse.cs
    public ListCatalogDocumentsResponse() { }
    public ListCatalogDocumentsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
```

- [ ] **Step 3: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/
git commit -m "feat: add UploadMaterialDocument use case"
```

---

## Task 11: UploadPifDocument use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs`

- [ ] **Step 1: Create UploadPifDocumentRequest.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadPifDocument;

public class UploadPifDocumentRequest : IRequest<UploadDocumentResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Stream FileStream { get; set; } = Stream.Null;
}
```

- [ ] **Step 2: Create UploadPifDocumentHandler.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadPifDocument;

public class UploadPifDocumentHandler : IRequestHandler<UploadPifDocumentRequest, UploadDocumentResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<UploadPifDocumentHandler> _logger;

    public UploadPifDocumentHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<UploadPifDocumentHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadPifDocumentRequest request, CancellationToken cancellationToken)
    {
        var shortCode = request.ProductCode.Length >= 6
            ? request.ProductCode[..6]
            : request.ProductCode;
        var prefix = $"{shortCode}__";

        var folder = await _storage.FindFolderAsync(
            _options.PIF.DriveId, _options.PIF.BasePath, prefix,
            allowMultiple: true, cancellationToken);

        if (folder.Status == FolderStatus.NotFound)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentFolderNotFound,
                new Dictionary<string, string> { ["prefix"] = prefix, ["basePath"] = _options.PIF.BasePath });

        _logger.LogInformation("Uploading PIF document {Filename} for product {ProductCode}", request.OriginalFilename, request.ProductCode);

        var uploadedName = await _storage.UploadFileAsync(
            _options.PIF.DriveId, folder.FolderId, request.OriginalFilename,
            request.FileStream, request.ContentType, request.SizeBytes, cancellationToken);

        return new UploadDocumentResponse { UploadedFilename = uploadedName };
    }
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/
git commit -m "feat: add UploadPifDocument use case"
```

---

## Task 12: CatalogDocumentsModule DI + wire into ApplicationModule

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Create CatalogDocumentsModule.cs**

Follow the same conditional pattern as KnowledgeBaseModule: use real Graph service only when DriveId is configured and real auth is active.

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.CatalogDocuments;

public static class CatalogDocumentsModule
{
    public static IServiceCollection AddCatalogDocumentsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogDocumentsOptions>()
            .Bind(configuration.GetSection(CatalogDocumentsOptions.SectionName));

        var options = new CatalogDocumentsOptions();
        configuration.GetSection(CatalogDocumentsOptions.SectionName).Bind(options);

        var drivesConfigured = !string.IsNullOrWhiteSpace(options.Materials.DriveId)
            && !options.Materials.DriveId.Contains("secrets.json")
            && !string.IsNullOrWhiteSpace(options.PIF.DriveId)
            && !options.PIF.DriveId.Contains("secrets.json");

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>("BypassJwtValidation", false);

        if (drivesConfigured && !useMockAuth && !bypassJwt)
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddMemoryCache();
            services.AddScoped<ICatalogDocumentsStorage, GraphCatalogDocumentsStorage>();
        }
        else
        {
            services.AddScoped<ICatalogDocumentsStorage, NoOpCatalogDocumentsStorage>();
        }

        return services;
    }
}
```

- [ ] **Step 2: Create NoOpCatalogDocumentsStorage.cs for dev/test environments**

When SharePoint isn't configured (local dev, CI), a no-op storage prevents startup crashes.

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

internal class NoOpCatalogDocumentsStorage : ICatalogDocumentsStorage
{
    public Task<FolderSearchResult> FindFolderAsync(
        string driveId, string basePath, string prefix, bool allowMultiple, CancellationToken ct = default)
        => Task.FromResult(new FolderSearchResult { Status = FolderStatus.NotFound });

    public Task<List<CatalogDocumentDto>> ListFilesAsync(
        string driveId, string folderId, CancellationToken ct = default)
        => Task.FromResult(new List<CatalogDocumentDto>());

    public Task<string> UploadFileAsync(
        string driveId, string folderId, string filename,
        Stream content, string contentType, long sizeBytes, CancellationToken ct = default)
        => Task.FromResult(filename);
}
```

- [ ] **Step 3: Wire into ApplicationModule.cs**

In `ApplicationModule.cs`, add the following line alongside the other `AddXxxModule()` calls (after `AddKnowledgeBaseModule(configuration);`):

```csharp
services.AddCatalogDocumentsModule(configuration);
```

Also add the using at the top of ApplicationModule.cs:

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments;
```

- [ ] **Step 4: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.API/Anela.Heblo.API.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/ \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat: add CatalogDocumentsModule DI registration"
```

---

## Task 13: CatalogDocumentsController

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs`

- [ ] **Step 1: Create CatalogDocumentsController.cs**

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListPifDocuments;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadPifDocument;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/catalog-documents")]
public class CatalogDocumentsController : BaseApiController
{
    private readonly IMediator _mediator;

    public CatalogDocumentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("materials/{productCode}")]
    public async Task<ActionResult<ListCatalogDocumentsResponse>> ListMaterialDocuments(
        string productCode, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMaterialDocumentsRequest { ProductCode = productCode }, ct);
        return HandleResponse(result);
    }

    [HttpGet("pif/{productCode}")]
    public async Task<ActionResult<ListCatalogDocumentsResponse>> ListPifDocuments(
        string productCode, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListPifDocumentsRequest { ProductCode = productCode }, ct);
        return HandleResponse(result);
    }

    [HttpGet("material-document-types")]
    public async Task<ActionResult<GetMaterialDocumentTypesResponse>> GetMaterialDocumentTypes(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMaterialDocumentTypesRequest(), ct);
        return HandleResponse(result);
    }

    [HttpPost("materials/{productCode}")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<ActionResult<UploadDocumentResponse>> UploadMaterialDocument(
        string productCode,
        IFormFile file,
        [FromForm] string documentTypeCode = "",
        [FromForm] string lot = "",
        [FromForm] string commonName = "",
        [FromForm] bool uploadAsIs = false,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        var result = await _mediator.Send(new UploadMaterialDocumentRequest
        {
            ProductCode = productCode,
            OriginalFilename = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            FileStream = file.OpenReadStream(),
            DocumentTypeCode = documentTypeCode,
            Lot = lot,
            CommonName = string.IsNullOrWhiteSpace(commonName) ? Path.GetFileNameWithoutExtension(file.FileName) : commonName,
            UploadAsIs = uploadAsIs,
        }, ct);

        return HandleResponse(result);
    }

    [HttpPost("pif/{productCode}")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<ActionResult<UploadDocumentResponse>> UploadPifDocument(
        string productCode,
        IFormFile file,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        var result = await _mediator.Send(new UploadPifDocumentRequest
        {
            ProductCode = productCode,
            OriginalFilename = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            FileStream = file.OpenReadStream(),
        }, ct);

        return HandleResponse(result);
    }
}
```

- [ ] **Step 2: Full build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.API/Anela.Heblo.API.csproj --no-incremental -q
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs
git commit -m "feat: add CatalogDocumentsController"
```

---

## Task 14: Backend unit tests for handlers

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/ListMaterialDocumentsHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/UploadMaterialDocumentHandlerTests.cs`

- [ ] **Step 1: Write failing tests for ListMaterialDocumentsHandler**

Create `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/ListMaterialDocumentsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class ListMaterialDocumentsHandlerTests
{
    private readonly Mock<ICatalogDocumentsStorage> _storageMock = new();

    private static IOptions<CatalogDocumentsOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CatalogDocumentsOptions
        {
            Materials = new CatalogDocumentsDriveOptions
            {
                DriveId = "drive-id",
                BasePath = "/Materials/Documents"
            },
            PIF = new CatalogDocumentsDriveOptions
            {
                DriveId = "drive-id-pif",
                BasePath = "/PIF/Documents"
            }
        });

    private ListMaterialDocumentsHandler CreateSut() =>
        new(_storageMock.Object, Options(), NullLogger<ListMaterialDocumentsHandler>.Instance);

    [Fact]
    public async Task Handle_ReturnsFolderNotFound_WhenStorageReturnsNotFound()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.NotFound });

        // Act
        var result = await CreateSut().Handle(
            new ListMaterialDocumentsRequest { ProductCode = "MAT001" }, CancellationToken.None);

        // Assert
        result.FolderStatus.Should().Be(FolderStatus.NotFound);
        result.ExpectedPrefix.Should().Be("MAT001__");
        result.Files.Should().BeEmpty();
        result.Success.Should().BeTrue(); // NotFound is not an error state, just data
    }

    [Fact]
    public async Task Handle_ReturnsFiles_WhenFolderFound()
    {
        // Arrange
        var folderResult = new FolderSearchResult { Status = FolderStatus.Found, FolderId = "folder-123" };
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), "MAT001__", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folderResult);

        var files = new List<CatalogDocumentDto>
        {
            new() { Name = "COA__L001__Bisabolol.pdf", WebUrl = "https://sp.example.com/file1.pdf", SizeBytes = 1024 }
        };
        _storageMock
            .Setup(s => s.ListFilesAsync("drive-id", "folder-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        // Act
        var result = await CreateSut().Handle(
            new ListMaterialDocumentsRequest { ProductCode = "MAT001" }, CancellationToken.None);

        // Assert
        result.FolderStatus.Should().Be(FolderStatus.Found);
        result.Files.Should().HaveCount(1);
        result.Files[0].Name.Should().Be("COA__L001__Bisabolol.pdf");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsMultipleMatches_WhenStorageReturnsMultiple()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.MultipleMatches });

        // Act
        var result = await CreateSut().Handle(
            new ListMaterialDocumentsRequest { ProductCode = "MAT001" }, CancellationToken.None);

        // Assert
        result.FolderStatus.Should().Be(FolderStatus.MultipleMatches);
        result.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesCorrectPrefixAndDriveId()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.NotFound });

        // Act
        await CreateSut().Handle(new ListMaterialDocumentsRequest { ProductCode = "ABC123" }, CancellationToken.None);

        // Assert — verify exact prefix and driveId were passed
        _storageMock.Verify(s => s.FindFolderAsync(
            "drive-id", "/Materials/Documents", "ABC123__", false, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Write failing tests for UploadMaterialDocumentHandler**

Create `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/UploadMaterialDocumentHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class UploadMaterialDocumentHandlerTests
{
    private readonly Mock<ICatalogDocumentsStorage> _storageMock = new();

    private static IOptions<CatalogDocumentsOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CatalogDocumentsOptions
        {
            Materials = new CatalogDocumentsDriveOptions { DriveId = "drive-id", BasePath = "/Materials/Documents" },
            PIF = new CatalogDocumentsDriveOptions { DriveId = "pif-drive", BasePath = "/PIF/Documents" }
        });

    private UploadMaterialDocumentHandler CreateSut() =>
        new(_storageMock.Object, Options(), NullLogger<UploadMaterialDocumentHandler>.Instance);

    private static UploadMaterialDocumentRequest ValidRequest(bool uploadAsIs = false) => new()
    {
        ProductCode = "MAT001",
        OriginalFilename = "test.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1024,
        FileStream = Stream.Null,
        DocumentTypeCode = string.Empty,
        Lot = string.Empty,
        CommonName = "Test",
        UploadAsIs = uploadAsIs,
    };

    [Fact]
    public async Task Handle_UploadAsIs_SkipsValidationAndUploadsWithOriginalFilename()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), "MAT001__", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.Found, FolderId = "f1" });
        _storageMock
            .Setup(s => s.UploadFileAsync(It.IsAny<string>(), "f1", "test.pdf", It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test.pdf");

        // Act
        var result = await CreateSut().Handle(ValidRequest(uploadAsIs: true), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.UploadedFilename.Should().Be("test.pdf");
    }

    [Fact]
    public async Task Handle_ReturnsError_WhenTypeCodeUnknownAndNotUploadAsIs()
    {
        // Arrange — storage not called; validation fails before
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.Found, FolderId = "f1" });

        var request = ValidRequest() with { DocumentTypeCode = "UNKNOWN_TYPE", UploadAsIs = false };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CatalogDocumentInvalidTypeCode);
        _storageMock.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundError_WhenFolderMissing()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.NotFound });

        // Act
        var result = await CreateSut().Handle(ValidRequest(uploadAsIs: true), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CatalogDocumentFolderNotFound);
    }
}
```

Note: `UploadMaterialDocumentRequest` is a class, not a record, so `with` syntax won't work. Change the test to create a new instance instead:

```csharp
    private static UploadMaterialDocumentRequest ValidRequest(bool uploadAsIs = false) => new()
    {
        ProductCode = "MAT001",
        OriginalFilename = "test.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1024,
        FileStream = Stream.Null,
        DocumentTypeCode = "",
        Lot = "",
        CommonName = "Test",
        UploadAsIs = uploadAsIs,
    };

    // In the test that needs DocumentTypeCode = "UNKNOWN_TYPE":
    var request = new UploadMaterialDocumentRequest
    {
        ProductCode = "MAT001",
        OriginalFilename = "test.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1024,
        FileStream = Stream.Null,
        DocumentTypeCode = "UNKNOWN_TYPE",
        UploadAsIs = false,
    };
```

- [ ] **Step 3: Run all CatalogDocuments tests — expect pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogDocuments" -q
```

Expected: All tests pass (the UploadMaterialDocumentHandler tests for `ReturnsError_WhenTypeCodeUnknownAndNotUploadAsIs` will pass because `MaterialDocumentTypes.All` is empty, so every code is "unknown" — which is the expected behavior when the list is not seeded).

- [ ] **Step 4: Run full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/
git commit -m "test: add CatalogDocuments handler unit tests"
```

---

## Task 15: Frontend — useCatalogDocuments hook + add QUERY_KEYS

**Files:**
- Modify: `frontend/src/api/client.ts` — add `catalogDocuments` to `QUERY_KEYS`
- Create: `frontend/src/api/hooks/useCatalogDocuments.ts`

- [ ] **Step 1: Add QUERY_KEYS entry to client.ts**

In `frontend/src/api/client.ts`, add to the `QUERY_KEYS` object (after `shipmentLabels`):

```typescript
  catalogDocuments: ["catalog-documents"] as const,
```

- [ ] **Step 2: Create useCatalogDocuments.ts**

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// ---- Types ----

export type FolderStatus = 'Found' | 'NotFound' | 'MultipleMatches';

export interface CatalogDocumentDto {
  name: string;
  webUrl: string;
  sizeBytes: number;
  modifiedAt: string;
}

export interface MaterialDocumentTypeDto {
  code: string;
  label: string;
  lotRequired: boolean;
}

export interface ListCatalogDocumentsResponse {
  success: boolean;
  folderStatus: FolderStatus;
  expectedPrefix: string;
  basePath: string;
  files: CatalogDocumentDto[];
}

export interface GetMaterialDocumentTypesResponse {
  success: boolean;
  documentTypes: MaterialDocumentTypeDto[];
}

export interface UploadDocumentResponse {
  success: boolean;
  uploadedFilename: string;
  errorCode?: number;
  params?: Record<string, string>;
}

export interface UploadMaterialDocumentParams {
  productCode: string;
  file: File;
  documentTypeCode: string;
  lot: string;
  commonName: string;
  uploadAsIs: boolean;
}

export interface UploadPifDocumentParams {
  productCode: string;
  file: File;
}

// ---- Query Keys ----

const catalogDocumentsKeys = {
  materialDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'materials', productCode] as const,
  pifDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'pif', productCode] as const,
  materialDocumentTypes: () =>
    [...QUERY_KEYS.catalogDocuments, 'material-document-types'] as const,
};

// ---- API Functions ----

async function fetchMaterialDocuments(productCode: string): Promise<ListCatalogDocumentsResponse> {
  const apiClient = getAuthenticatedApiClient();
  const url = `${apiClient.baseUrl}/api/catalog-documents/materials/${encodeURIComponent(productCode)}`;
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${await getToken(apiClient)}` },
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

async function fetchPifDocuments(productCode: string): Promise<ListCatalogDocumentsResponse> {
  const apiClient = getAuthenticatedApiClient();
  const url = `${apiClient.baseUrl}/api/catalog-documents/pif/${encodeURIComponent(productCode)}`;
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${await getToken(apiClient)}` },
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

async function fetchMaterialDocumentTypes(): Promise<GetMaterialDocumentTypesResponse> {
  const apiClient = getAuthenticatedApiClient();
  const url = `${apiClient.baseUrl}/api/catalog-documents/material-document-types`;
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${await getToken(apiClient)}` },
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

async function uploadMaterialDocument(params: UploadMaterialDocumentParams): Promise<UploadDocumentResponse> {
  const apiClient = getAuthenticatedApiClient();
  const url = `${apiClient.baseUrl}/api/catalog-documents/materials/${encodeURIComponent(params.productCode)}`;
  const formData = new FormData();
  formData.append('file', params.file);
  formData.append('documentTypeCode', params.documentTypeCode);
  formData.append('lot', params.lot);
  formData.append('commonName', params.commonName);
  formData.append('uploadAsIs', String(params.uploadAsIs));
  const response = await fetch(url, {
    method: 'POST',
    headers: { Authorization: `Bearer ${await getToken(apiClient)}` },
    body: formData,
  });
  return response.json();
}

async function uploadPifDocument(params: UploadPifDocumentParams): Promise<UploadDocumentResponse> {
  const apiClient = getAuthenticatedApiClient();
  const url = `${apiClient.baseUrl}/api/catalog-documents/pif/${encodeURIComponent(params.productCode)}`;
  const formData = new FormData();
  formData.append('file', params.file);
  const response = await fetch(url, {
    method: 'POST',
    headers: { Authorization: `Bearer ${await getToken(apiClient)}` },
    body: formData,
  });
  return response.json();
}

async function getToken(apiClient: ReturnType<typeof getAuthenticatedApiClient>): Promise<string> {
  // The ApiClient exposes a getToken method used internally; access the base URL from it
  // and get the auth header from the authenticated client's request pattern.
  // Use the same pattern as other hooks: fetch via the client's base fetch mechanism.
  // Note: the actual token retrieval is handled by getAuthenticatedApiClient internally.
  // For direct fetch calls we need to retrieve the token separately.
  // Check the existing pattern — other hooks use apiClient methods that handle auth.
  // For this implementation, access the token via the global token provider set in App.tsx.
  // Since we can't call apiClient methods here directly, use the same window.fetch pattern
  // by wrapping in the authenticated client. See note below.
  return ''; // placeholder — see Step 3 note
}
```

**Note for Step 2:** The direct `fetch` approach above requires access to the auth token. The project already has `getAuthenticatedApiClient()` which returns a pre-configured client. However, looking at other hooks (e.g., `useKnowledgeBase.ts`), they call generated API client methods that handle auth internally, or for file uploads they use `FormData` with explicit token retrieval.

The cleanest approach matching the project pattern: use the generated client where possible and construct direct fetch calls for file uploads using the pattern from `useKnowledgeBase.ts` (which uses MSAL's `useMsal` for token). However, this hook file does NOT use React hooks (hooks inside hooks are fine, but query functions can't call React hooks).

**Revised approach** — use the authenticated client wrapper for GET calls, and for uploads use the same approach `KnowledgeBaseUploadTab.tsx` uses. Replace the placeholder functions with the actual pattern:

Replace `useCatalogDocuments.ts` with this version that uses the authenticated API client properly:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export type FolderStatus = 'Found' | 'NotFound' | 'MultipleMatches';

export interface CatalogDocumentDto {
  name: string;
  webUrl: string;
  sizeBytes: number;
  modifiedAt: string;
}

export interface MaterialDocumentTypeDto {
  code: string;
  label: string;
  lotRequired: boolean;
}

export interface ListCatalogDocumentsResponse {
  success: boolean;
  folderStatus: FolderStatus;
  expectedPrefix: string;
  basePath: string;
  files: CatalogDocumentDto[];
}

export interface GetMaterialDocumentTypesResponse {
  success: boolean;
  documentTypes: MaterialDocumentTypeDto[];
}

export interface UploadDocumentResponse {
  success: boolean;
  uploadedFilename: string;
  errorCode?: number;
  params?: Record<string, string>;
}

export interface UploadMaterialDocumentParams {
  productCode: string;
  file: File;
  documentTypeCode: string;
  lot: string;
  commonName: string;
  uploadAsIs: boolean;
}

export interface UploadPifDocumentParams {
  productCode: string;
  file: File;
}

const catalogDocumentsKeys = {
  materialDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'materials', productCode] as const,
  pifDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'pif', productCode] as const,
  materialDocumentTypes: () =>
    [...QUERY_KEYS.catalogDocuments, 'material-document-types'] as const,
};

export function useMaterialDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocuments(productCode),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const url = `${apiClient.baseUrl}/api/catalog-documents/materials/${encodeURIComponent(productCode)}`;
      const response = await apiClient.request<ListCatalogDocumentsResponse>({ url, method: 'GET' });
      return response;
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function usePifDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.pifDocuments(productCode),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const url = `${apiClient.baseUrl}/api/catalog-documents/pif/${encodeURIComponent(productCode)}`;
      const response = await apiClient.request<ListCatalogDocumentsResponse>({ url, method: 'GET' });
      return response;
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function useMaterialDocumentTypes() {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocumentTypes(),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const url = `${apiClient.baseUrl}/api/catalog-documents/material-document-types`;
      const response = await apiClient.request<GetMaterialDocumentTypesResponse>({ url, method: 'GET' });
      return response;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes — static list rarely changes
  });
}

export function useUploadMaterialDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (params: UploadMaterialDocumentParams) => {
      const apiClient = getAuthenticatedApiClient();
      const url = `${apiClient.baseUrl}/api/catalog-documents/materials/${encodeURIComponent(params.productCode)}`;
      const formData = new FormData();
      formData.append('file', params.file);
      formData.append('documentTypeCode', params.documentTypeCode);
      formData.append('lot', params.lot);
      formData.append('commonName', params.commonName);
      formData.append('uploadAsIs', String(params.uploadAsIs));
      const response = await apiClient.request<UploadDocumentResponse>({
        url,
        method: 'POST',
        body: formData,
      });
      return response;
    },
    retry: 0, // No auto-retry for uploads — avoid duplicate files
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.materialDocuments(variables.productCode),
      });
    },
  });
}

export function useUploadPifDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (params: UploadPifDocumentParams) => {
      const apiClient = getAuthenticatedApiClient();
      const url = `${apiClient.baseUrl}/api/catalog-documents/pif/${encodeURIComponent(params.productCode)}`;
      const formData = new FormData();
      formData.append('file', params.file);
      const response = await apiClient.request<UploadDocumentResponse>({
        url,
        method: 'POST',
        body: formData,
      });
      return response;
    },
    retry: 0,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.pifDocuments(variables.productCode),
      });
    },
  });
}
```

**Important:** Check what method `apiClient.request` is actually called in other hooks. If the generated `ApiClient` does not have a generic `request` method, look at how hooks like `useKnowledgeBase.ts` call the generated client for non-generated endpoints (it uses `fetch` + manual token headers). Adjust to match the actual pattern. Look at `useKnowledgeBase.ts` line 170+ for upload pattern using fetch with FormData.

- [ ] **Step 3: Check actual API client interface**

Run:
```bash
grep -n "request\|fetch\|get\|post" /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend/src/api/client.ts | head -30
```

Adjust the hook implementation to use the real client method signature. If the client doesn't expose a generic `request` method, rewrite the query functions using direct `fetch` with token from the client's internal token getter.

- [ ] **Step 4: Frontend lint check**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run lint -- src/api/hooks/useCatalogDocuments.ts 2>&1 | head -30
```

Fix any lint errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/client.ts \
        frontend/src/api/hooks/useCatalogDocuments.ts
git commit -m "feat: add useCatalogDocuments hook"
```

---

## Task 16: DocumentList component + tests

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/shared/DocumentList.tsx`
- Create: `frontend/src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx`

- [ ] **Step 1: Write failing tests**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx
import { render, screen } from '@testing-library/react';
import DocumentList from '../DocumentList';
import type { CatalogDocumentDto } from '../../../../../../api/hooks/useCatalogDocuments';

const makeFile = (overrides?: Partial<CatalogDocumentDto>): CatalogDocumentDto => ({
  name: 'COA__L001__Bisabolol.pdf',
  webUrl: 'https://sp.example.com/file.pdf',
  sizeBytes: 102400,
  modifiedAt: '2026-05-01T12:00:00Z',
  ...overrides,
});

describe('DocumentList', () => {
  it('shows empty state when no files', () => {
    render(<DocumentList files={[]} isLoading={false} />);
    expect(screen.getByText(/Žádné dokumenty/i)).toBeInTheDocument();
  });

  it('shows loading state', () => {
    render(<DocumentList files={[]} isLoading={true} />);
    expect(screen.getByText(/Načítání/i)).toBeInTheDocument();
  });

  it('renders filename and size', () => {
    render(<DocumentList files={[makeFile()]} isLoading={false} />);
    expect(screen.getByText('COA__L001__Bisabolol.pdf')).toBeInTheDocument();
    expect(screen.getByText(/100 KB/i)).toBeInTheDocument();
  });

  it('renders a link that opens webUrl in new tab', () => {
    render(<DocumentList files={[makeFile()]} isLoading={false} />);
    const link = screen.getByRole('link', { name: /COA__L001__Bisabolol.pdf/i });
    expect(link).toHaveAttribute('href', 'https://sp.example.com/file.pdf');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  });
});
```

- [ ] **Step 2: Run tests — expect failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npx vitest run src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx 2>&1 | tail -20
```

Expected: Fail — module not found.

- [ ] **Step 3: Create DocumentList.tsx**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/DocumentList.tsx
import type { CatalogDocumentDto } from '../../../../../api/hooks/useCatalogDocuments';

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

interface DocumentListProps {
  files: CatalogDocumentDto[];
  isLoading: boolean;
  onUploadClick?: () => void;
}

export default function DocumentList({ files, isLoading, onUploadClick }: DocumentListProps) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-8 text-gray-500 text-sm">
        Načítání…
      </div>
    );
  }

  if (files.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-8 gap-3 text-gray-500 text-sm">
        <span>Žádné dokumenty</span>
        {onUploadClick && (
          <button
            onClick={onUploadClick}
            className="text-indigo-600 hover:text-indigo-800 text-sm font-medium"
          >
            Nahrát soubor
          </button>
        )}
      </div>
    );
  }

  return (
    <ul className="divide-y divide-gray-100">
      {files.map((file) => (
        <li key={file.webUrl} className="flex items-center justify-between py-3 px-1 hover:bg-gray-50 rounded">
          <a
            href={file.webUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-sm text-indigo-600 hover:text-indigo-800 hover:underline truncate max-w-xs"
            title={file.name}
          >
            {file.name}
          </a>
          <div className="flex items-center gap-4 text-xs text-gray-500 ml-4 shrink-0">
            <span>{formatFileSize(file.sizeBytes)}</span>
            <span>{new Date(file.modifiedAt).toLocaleDateString('cs-CZ')}</span>
          </div>
        </li>
      ))}
    </ul>
  );
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npx vitest run src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/shared/DocumentList.tsx \
        frontend/src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx
git commit -m "feat: add DocumentList component with tests"
```

---

## Task 17: FolderStatusBanner component + tests

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx`
- Create: `frontend/src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx`

- [ ] **Step 1: Write failing tests**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx
import { render, screen } from '@testing-library/react';
import FolderStatusBanner from '../FolderStatusBanner';

describe('FolderStatusBanner', () => {
  it('renders nothing when status is Found', () => {
    const { container } = render(
      <FolderStatusBanner status="Found" expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('shows not-found message with prefix and basePath', () => {
    render(
      <FolderStatusBanner status="NotFound" expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(screen.getByText(/MAT001__/)).toBeInTheDocument();
    expect(screen.getByText(/\/Materials\/Documents/)).toBeInTheDocument();
  });

  it('shows multiple-matches warning', () => {
    render(
      <FolderStatusBanner status="MultipleMatches" expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(screen.getByText(/více složek/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests — expect failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npx vitest run src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx 2>&1 | tail -10
```

Expected: Fail — module not found.

- [ ] **Step 3: Create FolderStatusBanner.tsx**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx
import type { FolderStatus } from '../../../../../api/hooks/useCatalogDocuments';

interface FolderStatusBannerProps {
  status: FolderStatus;
  expectedPrefix: string;
  basePath: string;
}

export default function FolderStatusBanner({ status, expectedPrefix, basePath }: FolderStatusBannerProps) {
  if (status === 'Found') return null;

  if (status === 'MultipleMatches') {
    return (
      <div className="rounded-md bg-yellow-50 border border-yellow-200 px-4 py-3 text-sm text-yellow-800">
        Nalezeno více složek odpovídajících prefixu <code className="font-mono">{expectedPrefix}</code>. 
        Upravte strukturu složek v SharePointu a obnovte stránku.
      </div>
    );
  }

  return (
    <div className="rounded-md bg-gray-50 border border-gray-200 px-4 py-3 text-sm text-gray-600">
      Složka pro <code className="font-mono">{expectedPrefix}</code> nebyla nalezena 
      pod <code className="font-mono">{basePath}</code>. Vytvořte ji v SharePointu a obnovte stránku.
    </div>
  );
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npx vitest run src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx \
        frontend/src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx
git commit -m "feat: add FolderStatusBanner component with tests"
```

---

## Task 18: MaterialUploadDialog + tests

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx`
- Create: `frontend/src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx`

- [ ] **Step 1: Write failing tests**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import MaterialUploadDialog from '../MaterialUploadDialog';
import * as hooks from '../../../../../../api/hooks/useCatalogDocuments';

jest.mock('../../../../../../api/hooks/useCatalogDocuments');

const mockUseMaterialDocumentTypes = hooks.useMaterialDocumentTypes as jest.MockedFunction<typeof hooks.useMaterialDocumentTypes>;
const mockUseUploadMaterialDocument = hooks.useUploadMaterialDocument as jest.MockedFunction<typeof hooks.useUploadMaterialDocument>;

const createWrapper = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
};

const baseDocTypes = [
  { code: 'COA', label: 'Certificate of Analysis', lotRequired: true },
  { code: 'SDS', label: 'Safety Data Sheet', lotRequired: false },
];

describe('MaterialUploadDialog', () => {
  beforeEach(() => {
    mockUseMaterialDocumentTypes.mockReturnValue({
      data: { success: true, documentTypes: baseDocTypes },
      isLoading: false,
      error: null,
    } as any);
    mockUseUploadMaterialDocument.mockReturnValue({
      mutate: jest.fn(),
      isPending: false,
      isError: false,
    } as any);
  });

  it('shows lot field when lotRequired type is selected', () => {
    render(
      <MaterialUploadDialog isOpen={true} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'COA' } });
    expect(screen.getByLabelText(/Šarže/i)).toBeInTheDocument();
  });

  it('hides lot field when lotRequired is false', () => {
    render(
      <MaterialUploadDialog isOpen={true} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'SDS' } });
    expect(screen.queryByLabelText(/Šarže/i)).not.toBeInTheDocument();
  });

  it('collapses form fields when uploadAsIs is checked', () => {
    render(
      <MaterialUploadDialog isOpen={true} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );

    fireEvent.click(screen.getByLabelText(/Nahrát beze změny názvu/i));
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('renders nothing when isOpen is false', () => {
    const { container } = render(
      <MaterialUploadDialog isOpen={false} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );
    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run tests — expect failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npx vitest run src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx 2>&1 | tail -10
```

Expected: Fail.

- [ ] **Step 3: Create MaterialUploadDialog.tsx**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx
import { useState, useRef } from 'react';
import { useMaterialDocumentTypes, useUploadMaterialDocument } from '../../../../../api/hooks/useCatalogDocuments';

interface MaterialUploadDialogProps {
  isOpen: boolean;
  productCode: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export default function MaterialUploadDialog({ isOpen, productCode, onClose, onSuccess }: MaterialUploadDialogProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [documentTypeCode, setDocumentTypeCode] = useState('');
  const [lot, setLot] = useState('');
  const [commonName, setCommonName] = useState('');
  const [uploadAsIs, setUploadAsIs] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const { data: typesData } = useMaterialDocumentTypes();
  const uploadMutation = useUploadMaterialDocument();

  const documentTypes = typesData?.documentTypes ?? [];
  const selectedType = documentTypes.find((t) => t.code === documentTypeCode);

  if (!isOpen) return null;

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setSelectedFile(file);
    if (file && !commonName) {
      const nameWithoutExt = file.name.replace(/\.[^.]+$/, '');
      setCommonName(nameWithoutExt);
    }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedFile) return;
    uploadMutation.mutate(
      { productCode, file: selectedFile, documentTypeCode, lot, commonName, uploadAsIs },
      {
        onSuccess: (data) => {
          if (data.success) {
            onSuccess?.();
            onClose();
          }
        },
      }
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Nahrát dokument</h2>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Soubor</label>
            <input
              ref={fileInputRef}
              type="file"
              onChange={handleFileChange}
              className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-indigo-50 file:text-indigo-700 hover:file:bg-indigo-100"
            />
          </div>

          <div className="flex items-center gap-2">
            <input
              id="upload-as-is"
              type="checkbox"
              checked={uploadAsIs}
              onChange={(e) => setUploadAsIs(e.target.checked)}
              className="h-4 w-4 rounded border-gray-300 text-indigo-600"
            />
            <label htmlFor="upload-as-is" className="text-sm text-gray-700">
              Nahrát beze změny názvu
            </label>
          </div>

          {!uploadAsIs && (
            <>
              <div>
                <label htmlFor="doc-type" className="block text-sm font-medium text-gray-700 mb-1">
                  Typ dokumentu
                </label>
                <select
                  id="doc-type"
                  value={documentTypeCode}
                  onChange={(e) => { setDocumentTypeCode(e.target.value); setLot(''); }}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm text-sm focus:border-indigo-500 focus:ring-indigo-500"
                >
                  <option value="">— Vyberte typ —</option>
                  {documentTypes.map((t) => (
                    <option key={t.code} value={t.code}>{t.label}</option>
                  ))}
                </select>
              </div>

              {selectedType?.lotRequired && (
                <div>
                  <label htmlFor="lot" className="block text-sm font-medium text-gray-700 mb-1">
                    Šarže
                  </label>
                  <input
                    id="lot"
                    type="text"
                    value={lot}
                    onChange={(e) => setLot(e.target.value)}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm text-sm"
                    placeholder="např. 2024-001"
                  />
                </div>
              )}

              <div>
                <label htmlFor="common-name" className="block text-sm font-medium text-gray-700 mb-1">
                  Název
                </label>
                <input
                  id="common-name"
                  type="text"
                  value={commonName}
                  onChange={(e) => setCommonName(e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm text-sm"
                />
              </div>
            </>
          )}

          {uploadMutation.isError && (
            <p className="text-sm text-red-600">Nahrání selhalo. Zkuste to znovu.</p>
          )}

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={!selectedFile || uploadMutation.isPending}
              className="px-4 py-2 text-sm text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50"
            >
              {uploadMutation.isPending ? 'Nahrávám…' : 'Nahrát'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npx vitest run src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx \
        frontend/src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx
git commit -m "feat: add MaterialUploadDialog with tests"
```

---

## Task 19: PifUploadDialog component

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/shared/PifUploadDialog.tsx`

No separate test file — the component is simple; covered by the tab integration test via smoke rendering.

- [ ] **Step 1: Create PifUploadDialog.tsx**

```tsx
// frontend/src/components/catalog/detail/tabs/shared/PifUploadDialog.tsx
import { useState, useRef } from 'react';
import { useUploadPifDocument } from '../../../../../api/hooks/useCatalogDocuments';

interface PifUploadDialogProps {
  isOpen: boolean;
  productCode: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export default function PifUploadDialog({ isOpen, productCode, onClose, onSuccess }: PifUploadDialogProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const uploadMutation = useUploadPifDocument();

  if (!isOpen) return null;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedFile) return;
    uploadMutation.mutate(
      { productCode, file: selectedFile },
      {
        onSuccess: (data) => {
          if (data.success) {
            onSuccess?.();
            onClose();
          }
        },
      }
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Nahrát PIF</h2>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Soubor</label>
            <input
              ref={fileInputRef}
              type="file"
              onChange={(e) => setSelectedFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-indigo-50 file:text-indigo-700 hover:file:bg-indigo-100"
            />
          </div>

          {uploadMutation.isError && (
            <p className="text-sm text-red-600">Nahrání selhalo. Zkuste to znovu.</p>
          )}

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={!selectedFile || uploadMutation.isPending}
              className="px-4 py-2 text-sm text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50"
            >
              {uploadMutation.isPending ? 'Nahrávám…' : 'Nahrát'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Frontend lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run lint -- src/components/catalog/detail/tabs/shared/PifUploadDialog.tsx 2>&1 | head -20
```

Fix any lint errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/shared/PifUploadDialog.tsx
git commit -m "feat: add PifUploadDialog component"
```

---

## Task 20: MaterialDocumentsTab + PifDocumentsTab

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx`
- Create: `frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx`

- [ ] **Step 1: Create MaterialDocumentsTab.tsx**

```tsx
// frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx
import { useState } from 'react';
import { RefreshCw, Upload } from 'lucide-react';
import { useMaterialDocuments } from '../../../../api/hooks/useCatalogDocuments';
import DocumentList from './shared/DocumentList';
import FolderStatusBanner from './shared/FolderStatusBanner';
import MaterialUploadDialog from './shared/MaterialUploadDialog';

interface MaterialDocumentsTabProps {
  productCode: string;
}

export default function MaterialDocumentsTab({ productCode }: MaterialDocumentsTabProps) {
  const [isUploadOpen, setIsUploadOpen] = useState(false);
  const { data, isLoading, error, refetch } = useMaterialDocuments(productCode);

  if (error) {
    return (
      <div className="py-6 text-sm text-red-600">
        Chyba při načítání dokumentů. Zkuste obnovit stránku.
      </div>
    );
  }

  const folderStatus = data?.folderStatus ?? 'NotFound';

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium text-gray-900">Dokumenty</h3>
        <div className="flex items-center gap-2">
          <button
            onClick={() => refetch()}
            className="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded"
            title="Obnovit"
          >
            <RefreshCw className="h-4 w-4" />
          </button>
          {folderStatus === 'Found' && (
            <button
              onClick={() => setIsUploadOpen(true)}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md"
            >
              <Upload className="h-3.5 w-3.5" />
              Nahrát soubor
            </button>
          )}
        </div>
      </div>

      <FolderStatusBanner
        status={folderStatus}
        expectedPrefix={data?.expectedPrefix ?? `${productCode}__`}
        basePath={data?.basePath ?? ''}
      />

      {folderStatus === 'Found' && (
        <DocumentList
          files={data?.files ?? []}
          isLoading={isLoading}
          onUploadClick={() => setIsUploadOpen(true)}
        />
      )}

      <MaterialUploadDialog
        isOpen={isUploadOpen}
        productCode={productCode}
        onClose={() => setIsUploadOpen(false)}
        onSuccess={() => refetch()}
      />
    </div>
  );
}
```

- [ ] **Step 2: Create PifDocumentsTab.tsx**

```tsx
// frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx
import { useState } from 'react';
import { RefreshCw, Upload } from 'lucide-react';
import { usePifDocuments } from '../../../../api/hooks/useCatalogDocuments';
import DocumentList from './shared/DocumentList';
import FolderStatusBanner from './shared/FolderStatusBanner';
import PifUploadDialog from './shared/PifUploadDialog';

interface PifDocumentsTabProps {
  productCode: string;
}

export default function PifDocumentsTab({ productCode }: PifDocumentsTabProps) {
  const [isUploadOpen, setIsUploadOpen] = useState(false);
  const { data, isLoading, error, refetch } = usePifDocuments(productCode);

  if (error) {
    return (
      <div className="py-6 text-sm text-red-600">
        Chyba při načítání PIF dokumentů. Zkuste obnovit stránku.
      </div>
    );
  }

  const folderStatus = data?.folderStatus ?? 'NotFound';

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium text-gray-900">PIF</h3>
        <div className="flex items-center gap-2">
          <button
            onClick={() => refetch()}
            className="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded"
            title="Obnovit"
          >
            <RefreshCw className="h-4 w-4" />
          </button>
          {folderStatus === 'Found' && (
            <button
              onClick={() => setIsUploadOpen(true)}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md"
            >
              <Upload className="h-3.5 w-3.5" />
              Nahrát PIF
            </button>
          )}
        </div>
      </div>

      <FolderStatusBanner
        status={folderStatus}
        expectedPrefix={data?.expectedPrefix ?? ''}
        basePath={data?.basePath ?? ''}
      />

      {folderStatus === 'Found' && (
        <DocumentList
          files={data?.files ?? []}
          isLoading={isLoading}
          onUploadClick={() => setIsUploadOpen(true)}
        />
      )}

      <PifUploadDialog
        isOpen={isUploadOpen}
        productCode={productCode}
        onClose={() => setIsUploadOpen(false)}
        onSuccess={() => refetch()}
      />
    </div>
  );
}
```

- [ ] **Step 3: Lint check**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run lint -- src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx src/components/catalog/detail/tabs/PifDocumentsTab.tsx 2>&1 | head -20
```

Fix any errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx \
        frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx
git commit -m "feat: add MaterialDocumentsTab and PifDocumentsTab"
```

---

## Task 21: CatalogDetailTabs integration

**Files:**
- Modify: `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx`

The existing component has `activeTab` typed as a union and `onTabChange` with the same union. We need to extend both.

- [ ] **Step 1: Update CatalogDetailTabs.tsx**

The current `activeTab` union is:
```typescript
"basic" | "history" | "margins" | "composition" | "journal" | "usage"
```

New union:
```typescript
"basic" | "history" | "margins" | "composition" | "journal" | "usage" | "documents" | "pif"
```

Replace the full file content of `CatalogDetailTabs.tsx` with:

```tsx
import React from "react";
import {
  FileText,
  ShoppingCart,
  TrendingUp,
  BookOpen,
  ArrowRight,
  Beaker,
  FolderOpen,
} from "lucide-react";
import { CatalogItemDto, ProductType } from "../../../api/hooks/useCatalog";
import { SearchJournalEntryDto } from "../../../api/generated/api-client";
import BasicInfoTab from "./tabs/BasicInfoTab/BasicInfoTab";
import PurchaseHistoryTab from "./tabs/PurchaseHistoryTab";
import MarginsTab from "./tabs/MarginsTab/MarginsTab";
import JournalTab from "./tabs/JournalTab";
import UsageTab from "./tabs/UsageTab";
import CompositionTab from "./tabs/CompositionTab";
import MaterialDocumentsTab from "./tabs/MaterialDocumentsTab";
import PifDocumentsTab from "./tabs/PifDocumentsTab";

type TabId = "basic" | "history" | "margins" | "composition" | "journal" | "usage" | "documents" | "pif";

interface CatalogDetailTabsProps {
  item: CatalogItemDto;
  activeTab: TabId;
  onTabChange: (tab: TabId) => void;
  detailData: any;
  isLoading: boolean;
  journalEntries: SearchJournalEntryDto[];
  onManufactureDifficultyClick: () => void;
  onAddJournalEntry: () => void;
  onEditJournalEntry: (entry: SearchJournalEntryDto) => void;
  onViewAllEntries: () => void;
}

const CatalogDetailTabs: React.FC<CatalogDetailTabsProps> = ({
  item,
  activeTab,
  onTabChange,
  detailData,
  isLoading,
  journalEntries,
  onManufactureDifficultyClick,
  onAddJournalEntry,
  onEditJournalEntry,
  onViewAllEntries,
}) => {
  const tabClass = (tab: TabId) =>
    `px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
      activeTab === tab
        ? "border-indigo-500 text-indigo-600"
        : "border-transparent text-gray-500 hover:text-gray-700"
    }`;

  return (
    <div className="flex flex-col overflow-hidden">
      {/* Tab Navigation */}
      <div className="flex border-b border-gray-200 mb-6">
        <button onClick={() => onTabChange("basic")} className={tabClass("basic")}>
          <FileText className="h-4 w-4" />
          <span>Základní informace</span>
        </button>

        {/* Historie nákupů - pouze pro Material a Goods */}
        {(item?.type === ProductType.Material || item?.type === ProductType.Goods) && (
          <button onClick={() => onTabChange("history")} className={tabClass("history")}>
            <ShoppingCart className="h-4 w-4" />
            <span>Historie nákupů</span>
          </button>
        )}

        {(item?.type === ProductType.Product ||
          item?.type === ProductType.SemiProduct ||
          item?.type === ProductType.Goods ||
          item?.type === ProductType.Set) && (
          <button onClick={() => onTabChange("margins")} className={tabClass("margins")}>
            <TrendingUp className="h-4 w-4" />
            <span>Marže</span>
          </button>
        )}

        {/* Složení tab - pouze pro Product a SemiProduct */}
        {(item?.type === ProductType.Product || item?.type === ProductType.SemiProduct) && (
          <button onClick={() => onTabChange("composition")} className={tabClass("composition")}>
            <Beaker className="h-4 w-4" />
            <span>Složení</span>
          </button>
        )}

        <button onClick={() => onTabChange("journal")} className={tabClass("journal")}>
          <BookOpen className="h-4 w-4" />
          <span>Deník</span>
        </button>

        {/* Použití tab - pouze pro SemiProduct a Material */}
        {(item?.type === ProductType.SemiProduct || item?.type === ProductType.Material) && (
          <button onClick={() => onTabChange("usage")} className={tabClass("usage")}>
            <ArrowRight className="h-4 w-4" />
            <span>Použití</span>
          </button>
        )}

        {/* Dokumenty tab - pouze pro Material */}
        {item?.type === ProductType.Material && (
          <button onClick={() => onTabChange("documents")} className={tabClass("documents")}>
            <FolderOpen className="h-4 w-4" />
            <span>Dokumenty</span>
          </button>
        )}

        {/* PIF tab - pro Product a SemiProduct */}
        {(item?.type === ProductType.Product || item?.type === ProductType.SemiProduct) && (
          <button onClick={() => onTabChange("pif")} className={tabClass("pif")}>
            <FolderOpen className="h-4 w-4" />
            <span>PIF</span>
          </button>
        )}
      </div>

      {/* Tab Content */}
      <div className="flex-1 overflow-y-auto">
        {activeTab === "basic" ? (
          <BasicInfoTab
            item={item}
            onManufactureDifficultyClick={onManufactureDifficultyClick}
          />
        ) : activeTab === "history" ? (
          <PurchaseHistoryTab
            purchaseHistory={detailData?.historicalData?.purchaseHistory || []}
            isLoading={isLoading}
          />
        ) : activeTab === "margins" ? (
          <MarginsTab
            item={item}
            manufactureCostHistory={detailData?.historicalData?.manufactureCostHistory || []}
            marginHistory={detailData?.historicalData?.marginHistory || []}
            isLoading={isLoading}
            journalEntries={journalEntries}
          />
        ) : activeTab === "composition" ? (
          <CompositionTab productCode={item.productCode || ""} />
        ) : activeTab === "usage" ? (
          <UsageTab productCode={item.productCode || ""} />
        ) : activeTab === "documents" ? (
          <MaterialDocumentsTab productCode={item.productCode || ""} />
        ) : activeTab === "pif" ? (
          <PifDocumentsTab productCode={item.productCode || ""} />
        ) : (
          <JournalTab
            productCode={item.productCode || ""}
            onAddEntry={onAddJournalEntry}
            onEditEntry={onEditJournalEntry}
            onViewAllEntries={onViewAllEntries}
          />
        )}
      </div>
    </div>
  );
};

export default CatalogDetailTabs;
```

- [ ] **Step 2: Find and update the parent component that calls CatalogDetailTabs**

The parent manages `activeTab` state. Find it:

```bash
grep -rn "CatalogDetailTabs" /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend/src --include="*.tsx" | grep -v "__tests__"
```

Open each result and update the `activeTab` state type to include `"documents" | "pif"`. The state declaration is usually:
```typescript
const [activeTab, setActiveTab] = useState<"basic" | "history" | "margins" | "composition" | "journal" | "usage">("basic");
```

Update to:
```typescript
const [activeTab, setActiveTab] = useState<"basic" | "history" | "margins" | "composition" | "journal" | "usage" | "documents" | "pif">("basic");
```

- [ ] **Step 3: Frontend build check**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run build 2>&1 | tail -20
```

Expected: Build succeeded with no errors.

- [ ] **Step 4: Run all frontend tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run test -- --run 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/catalog/detail/CatalogDetailTabs.tsx
git commit -m "feat: add Dokumenty and PIF tabs to CatalogDetailTabs"
```

---

## Task 22: Final validation

- [ ] **Step 1: Backend build + format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet build Anela.Heblo.API/Anela.Heblo.API.csproj -q
dotnet format Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes 2>&1 | head -20
```

If format reports changes, run without `--verify-no-changes` to apply them:
```bash
dotnet format Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

- [ ] **Step 2: Backend tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: All tests pass, including the new CatalogDocuments tests.

- [ ] **Step 3: Frontend build + lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run build 2>&1 | tail -10
npm run lint 2>&1 | tail -10
```

Expected: No errors.

- [ ] **Step 4: Frontend tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/miami-v1/frontend
npm run test -- --run 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 5: Commit any format fixes**

```bash
git add -u
git commit -m "chore: apply dotnet format after CatalogDocuments implementation" 2>/dev/null || echo "nothing to commit"
```

---

## Self-review against spec

**Spec coverage check:**

| Spec requirement | Covered by task |
|---|---|
| List Material files | Tasks 7, 13, 15, 16, 20, 21 |
| List PIF files | Tasks 8, 13, 15, 16, 20, 21 |
| Upload Material (structured) | Tasks 4, 10, 13, 18, 21 |
| Upload Material (upload-as-is) | Tasks 10, 13, 18 |
| Upload PIF (freeform) | Tasks 11, 13, 19, 21 |
| GetMaterialDocumentTypes | Tasks 3, 9, 13, 15, 18 |
| FolderStatus: Found / NotFound / MultipleMatches | Tasks 5, 6, 7, 8, 17 |
| Material folder matching: `{code}__` prefix | Tasks 7, 14 |
| PIF folder matching: 6-char prefix | Tasks 8, 14 |
| Multiple PIF matches → first alphabetical, server warn | Task 6 |
| Multiple Material matches → MultipleMatches error | Task 6 |
| Settings (CatalogDocumentsOptions) | Tasks 1, 3, 12 |
| Filename convention: `{TYPE}__{lot}__{name}.ext` | Tasks 4 (builder + tests) |
| Empty lot double-separator: `{TYPE}____{name}.ext` | Task 4 |
| Upload session for >4MB | Task 6 |
| conflictBehavior=rename on upload | Task 6 |
| Card visibility by ProductType | Task 21 |
| Czech labels | Tasks 17, 18, 19, 20, 21 |
| staleTime: 30s on list queries | Task 15 |
| Refresh button | Tasks 20, 21 |
| No auto-retry on upload mutations | Task 15 |
| 50MB size cap on controller | Task 13 |
| NoOpStorage in dev/test environments | Task 12 |
| MaterialDocumentTypes.All is empty (TODO marker) | Task 3 |

**Type consistency check:**
- `FolderStatus` enum: Found / NotFound / MultipleMatches — consistent across Tasks 5, 6, 7, 8, 14, 17
- `ICatalogDocumentsStorage` method signatures used in Tasks 6, 7, 8, 10, 11, 14 — consistent
- `ListCatalogDocumentsResponse.FolderStatus` (C# enum) maps to TypeScript `FolderStatus` string union — consistent
- `CatalogDocumentDto` fields (Name/name, WebUrl/webUrl, SizeBytes/sizeBytes, ModifiedAt/modifiedAt) — consistent via camelCase JSON serialization

**Placeholder scan:** No TBD, TODO beyond the intentional `MaterialDocumentTypes.All` placeholder and the note in Task 15 Step 3 about verifying the API client interface. All code blocks contain complete implementations.
