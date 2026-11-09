### task: implement-410-resync
**Files:** `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`

- [ ] Add `using System.Net;`.
- [ ] Add private helper `GetDeltaWithResyncAsync(PhotobankIndexRoot root, CancellationToken ct)` exactly as in arch-review (catch `HttpRequestException` when `StatusCode == HttpStatusCode.Gone && !string.IsNullOrEmpty(root.DeltaLink)`; warn-log with root id only; set `root.DeltaLink = null`; single retry with null token).
- [ ] Replace the `_graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct)` call in `IndexRootAsync` with `await GetDeltaWithResyncAsync(root, ct)`.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter Photobank`; all pass.
- [ ] Run `dotnet build` and `dotnet format` on the backend.

