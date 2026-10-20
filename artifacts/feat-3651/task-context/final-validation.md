### task: final-validation

- [ ] `dotnet build` (solution under `backend/`), `dotnet format --verify-no-changes` on touched projects (run `dotnet format` if needed), `dotnet test` for the HomeAssistant test project and `Anela.Heblo.Tests --filter ManufactureConditions`.
- [ ] Ensure no changes outside the adapter, its tests, and the process doc; no DTO/API changes (no OpenAPI regeneration).
- [ ] Note in PR description: ops follow-up (Tailscale/HA logs for 11:00/16:00/20:00 UTC bursts) remains out of scope.
