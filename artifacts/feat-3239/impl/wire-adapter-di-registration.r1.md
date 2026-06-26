# wire-adapter-di-registration — Implementation Report (r1)

## Task

Move `MicrosoftGraph` HttpClient and `PhotobankGraphService` DI registration out of `PhotobankModule` and into `Microsoft365AdapterServiceCollectionExtensions`.

## Changes made

### `Microsoft365AdapterServiceCollectionExtensions.cs`

- Added two `using` directives:
  - `using Anela.Heblo.Adapters.Microsoft365.Photobank;`
  - `using Anela.Heblo.Application.Features.Photobank.Services;`
- Inside the `if (!useMockAuth && !bypassJwt)` block, after `OutlookCalendarSyncService`, added:
  - `services.AddHttpClient("MicrosoftGraph", ...)` with `AllowAutoRedirect = true`
  - `services.AddScoped<IPhotobankGraphService, PhotobankGraphService>()`

### `PhotobankModule.cs`

- Replaced the `if (!useMockAuth && !bypassJwtValidation) { ... } else { ... }` block with:
  ```csharp
  if (useMockAuth || bypassJwtValidation)
  {
      services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
  }
  ```
- No `using` directives needed to be removed — the adapter namespace (`Anela.Heblo.Adapters.Microsoft365.Photobank`) was never explicitly imported in `PhotobankModule.cs`.

## Build result

Build completed with 141 warnings and 2 **pre-existing** errors in `GetThumbnailHandler.cs`:
- `CS0029`: Cannot convert `GetThumbnailResult` to `GraphThumbnail` (handler not yet updated to match DU refactor)
- `CS0246`: `GraphThrottledException` not found (handler still uses old exception-based pattern)

These errors exist in `GetThumbnailHandler.cs` which was not modified by this task and has no git diff. They stem from earlier commits on this branch (the `introduce-getthumbnailresult-du` and `move-photobankgraphservice-to-adapter` commits). The handler update is a separate pending task.

My changes to `Microsoft365AdapterServiceCollectionExtensions.cs` and `PhotobankModule.cs` introduce no new errors.
