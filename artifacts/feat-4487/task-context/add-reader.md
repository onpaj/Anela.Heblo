### task: add-reader
Create public `Infrastructure/IUserDashboardSettingsReader.cs` + internal `UserDashboardSettingsReader.cs` (scoped). `GetEffectiveSettingsAsync(userId, ct)`: `repository.GetByUserIdAsync` -> null: project `BuildDefault`; else `ProjectMissing`. Returns `UserDashboardSettingsDto`. No lock, never calls `AddAsync`/`UpdateAsync`.
Tests: `Infrastructure/UserDashboardSettingsReaderTests.cs` — new user default projection, missing-tile projection, provisioned user unchanged, `repository.Verify(AddAsync/UpdateAsync, Times.Never)`, tracked entity not mutated.
Depends on: add-defaults-helper.

