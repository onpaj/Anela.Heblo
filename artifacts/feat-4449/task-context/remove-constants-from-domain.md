### task: remove-constants-from-domain
- Delete `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs`.
- `FileStorageModule.cs`: `public const string FileDownloadClientName = "FileDownload";`; drop `using Anela.Heblo.Domain.Features.FileStorage;` if now unused.
- Fix any remaining `FileStorageConstants` references (tests: `AzureBlobStorageServiceTests` should be gone by then).
Verify: `grep -rn FileStorageConstants backend/` empty; `FileStorageModuleTests` (asserting `"FileDownload"`) pass.

