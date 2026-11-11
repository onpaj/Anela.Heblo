### task: add-blob-naming-helper
Create `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/BlobNaming.cs` (`internal static class BlobNaming`, namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure`) with:
- `GetExtensionFromContentType(string? mediaType)` and `GetContentTypeFromExtension(string fileName)`: copy the switch expressions verbatim from `AzureBlobStorageService` (lowercase the extension in the latter).
- `ResolveBlobName(string? requestedName, string fileUrl, string? responseMediaType)`: return `requestedName` if non-empty; else `Path.GetFileName(new Uri(fileUrl).LocalPath)`; if still empty return `$"downloaded-file-{Guid.NewGuid()}{GetExtensionFromContentType(responseMediaType)}"`.
Add `backend/test/Anela.Heblo.Tests/Features/FileStorage/BlobNamingTests.cs`: Theory covering every content-type->extension arm and default `.bin`; every extension->content-type arm incl. uppercase `.JPG` and default; ResolveBlobName for requested name, URL path name, and generated name (known/unknown media type). Write tests first, then implementation.
Verify: `dotnet test backend/test/Anela.Heblo.Tests --filter BlobNamingTests`.

