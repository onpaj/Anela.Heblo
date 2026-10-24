### task: rewire-consumers
- `UseCases/GetUserSettings/GetUserSettingsHandler.cs`: deps = `IUserDashboardSettingsReader` + `ICurrentUserService`; resolve user id ("anonymous" fallback), call reader, wrap in `GetUserSettingsResponse`. Remove tileRegistry/repository/lock/timeProvider deps.
- `UseCases/GetTileData/GetTileDataHandler.cs`: replace `IMediator` with reader; resolve user id identically ("anonymous" fallback); drop `GetUserSettings` using if unused.
- `Infrastructure/UserDashboardSettingsMutator.cs`: replace `IMediator` with `IUserDashboardSettingsProvisioner`; call `EnsureProvisionedAsync(resolvedUserId, ct)` before the lock in `MutateAsync` and `MutateBulkAsync`.
- `Infrastructure/IUserDashboardSettingsMutator.cs`: update remarks ("provisioner", not MediatR/GetUserSettingsHandler).
- `DashboardModule.cs`: `AddScoped` provisioner and reader.
Depends on: add-provisioner, add-reader.

