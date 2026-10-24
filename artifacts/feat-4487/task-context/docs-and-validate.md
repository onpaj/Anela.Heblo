### task: docs-and-validate
- Update `docs/processes/module-dashboard.md` (lines ~105-109, ~129) and `docs/processes/calc-dashboard-tiles.md`: GET settings no longer persists; provisioning/back-fill happens on first mutation via `UserDashboardSettingsProvisioner`; GET returns an in-memory default projection. Update the Key-files list; bump `verified_at`. Run `python3 scripts/process-docs/check.py index`.
- Run `dotnet build`, `dotnet format`, `dotnet test --filter "FullyQualifiedName~Features.Dashboard"`.
- Grep that no `IMediator`/`GetUserSettingsRequest` dispatch remains in `Features/Dashboard/Infrastructure` or `GetTileDataHandler`.
Depends on: update-existing-tests.
