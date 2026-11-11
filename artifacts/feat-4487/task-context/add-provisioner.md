### task: add-provisioner
Create `Infrastructure/IUserDashboardSettingsProvisioner.cs` + `UserDashboardSettingsProvisioner.cs` (internal, scoped). `EnsureProvisionedAsync(userId, ct)`: acquire `IUserDashboardSettingsLock` -> `GetByUserIdAsync` -> null: `BuildDefault` + `AddAsync`; else `BackfillMissing`, `UpdateAsync` only if changed; return settings. Lock released before return. XML doc: must be called outside any held `IUserDashboardSettingsLock` (non-reentrant).
Tests: `Infrastructure/UserDashboardSettingsProvisionerTests.cs` — move write cases from `GetUserSettingsHandlerTests` (new user, back-fill, fully provisioned = no write, lock acquired once).
Depends on: add-defaults-helper.

