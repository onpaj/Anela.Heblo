# Task Plan: Separate Dashboard settings provisioning from GetUserSettings query

Base path: `backend/src/Anela.Heblo.Application/Features/Dashboard/`. Tests: `backend/test/Anela.Heblo.Tests/Features/Dashboard/`.
DTOs stay classes. No migration, no API/OpenAPI change.

Visibility note: `GetUserSettingsHandler` and `GetTileDataHandler` are `public`; a public constructor cannot take an `internal` interface (CS0051). Make `IUserDashboardSettingsReader` **public** (it only exposes the public `UserDashboardSettingsDto`); keep provisioner and defaults helper `internal`.

### task: add-defaults-helper
Create `Infrastructure/UserDashboardSettingsDefaults.cs` (internal static), extracting the logic currently in `GetUserSettingsHandler`:
- `AutoShowTiles(ITileRegistry)` -> `DefaultEnabled && AutoShow` tiles.
- `BuildDefault(userId, autoShowTiles, now)` -> new `UserDashboardSettings` (IsVisible=true, DisplayOrder=index, timestamps=now, tile `DashboardSettings` back-reference set).
- `BackfillMissing(settings, autoShowTiles, now)` -> appends missing tiles at maxOrder+i+1, sets `settings.LastModified`, returns bool changed.
- `ProjectMissing(settings, autoShowTiles, now)` -> returns the DTO tile list = existing tiles + missing AutoShow tiles (same ordering rule) **without mutating** `settings` or `settings.Tiles` (tracked EF entities).
Tests: `Infrastructure/UserDashboardSettingsDefaultsTests.cs` incl. equivalence test (BackfillMissing result == ProjectMissing result) and a no-mutation assertion.
Verify: `dotnet test --filter UserDashboardSettingsDefaultsTests`.

### task: add-provisioner
Create `Infrastructure/IUserDashboardSettingsProvisioner.cs` + `UserDashboardSettingsProvisioner.cs` (internal, scoped). `EnsureProvisionedAsync(userId, ct)`: acquire `IUserDashboardSettingsLock` -> `GetByUserIdAsync` -> null: `BuildDefault` + `AddAsync`; else `BackfillMissing`, `UpdateAsync` only if changed; return settings. Lock released before return. XML doc: must be called outside any held `IUserDashboardSettingsLock` (non-reentrant).
Tests: `Infrastructure/UserDashboardSettingsProvisionerTests.cs` — move write cases from `GetUserSettingsHandlerTests` (new user, back-fill, fully provisioned = no write, lock acquired once).
Depends on: add-defaults-helper.

### task: add-reader
Create public `Infrastructure/IUserDashboardSettingsReader.cs` + internal `UserDashboardSettingsReader.cs` (scoped). `GetEffectiveSettingsAsync(userId, ct)`: `repository.GetByUserIdAsync` -> null: project `BuildDefault`; else `ProjectMissing`. Returns `UserDashboardSettingsDto`. No lock, never calls `AddAsync`/`UpdateAsync`.
Tests: `Infrastructure/UserDashboardSettingsReaderTests.cs` — new user default projection, missing-tile projection, provisioned user unchanged, `repository.Verify(AddAsync/UpdateAsync, Times.Never)`, tracked entity not mutated.
Depends on: add-defaults-helper.

### task: rewire-consumers
- `UseCases/GetUserSettings/GetUserSettingsHandler.cs`: deps = `IUserDashboardSettingsReader` + `ICurrentUserService`; resolve user id ("anonymous" fallback), call reader, wrap in `GetUserSettingsResponse`. Remove tileRegistry/repository/lock/timeProvider deps.
- `UseCases/GetTileData/GetTileDataHandler.cs`: replace `IMediator` with reader; resolve user id identically ("anonymous" fallback); drop `GetUserSettings` using if unused.
- `Infrastructure/UserDashboardSettingsMutator.cs`: replace `IMediator` with `IUserDashboardSettingsProvisioner`; call `EnsureProvisionedAsync(resolvedUserId, ct)` before the lock in `MutateAsync` and `MutateBulkAsync`.
- `Infrastructure/IUserDashboardSettingsMutator.cs`: update remarks ("provisioner", not MediatR/GetUserSettingsHandler).
- `DashboardModule.cs`: `AddScoped` provisioner and reader.
Depends on: add-provisioner, add-reader.

### task: update-existing-tests
- `GetUserSettingsHandlerTests`: rewrite as pure query tests (mock reader; verify user id resolution incl. anonymous); write-path cases already moved to provisioner tests.
- `GetTileDataHandlerTests`: mock `IUserDashboardSettingsReader` instead of `IMediator`.
- `Infrastructure/UserDashboardSettingsMutatorTests`: mock provisioner instead of `IMediator`; assert provisioner called before lock acquired and with the resolved user id.
- Check `EnableTileHandlerTests`, `DisableTileHandlerTests`, `SaveUserSettingsHandlerTests`, `DashboardControllerAuthorizationTests` still compile/pass.
Depends on: rewire-consumers.

### task: docs-and-validate
- Update `docs/processes/module-dashboard.md` (lines ~105-109, ~129) and `docs/processes/calc-dashboard-tiles.md`: GET settings no longer persists; provisioning/back-fill happens on first mutation via `UserDashboardSettingsProvisioner`; GET returns an in-memory default projection. Update the Key-files list; bump `verified_at`. Run `python3 scripts/process-docs/check.py index`.
- Run `dotnet build`, `dotnet format`, `dotnet test --filter "FullyQualifiedName~Features.Dashboard"`.
- Grep that no `IMediator`/`GetUserSettingsRequest` dispatch remains in `Features/Dashboard/Infrastructure` or `GetTileDataHandler`.
Depends on: update-existing-tests.
