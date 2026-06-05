# Decision: Repository DI Bindings Live in the Feature Module, Not PersistenceModule

**Decision:** A repository's DI binding (`services.AddScoped<IRepo, RepoImpl>()`) is always declared
in its owning `{Feature}Module.cs`. `PersistenceModule.cs` registers only shared infrastructure
(DbContext, NpgsqlDataSource, interceptors, telemetry, material-container code generator) — never a
repository. (ADR-004 in `docs/architecture/development_guidelines.md`.)

**Why:** Repository *implementations* must live in `Anela.Heblo.Persistence` because of the single
shared `ApplicationDbContext` (ADR-001), but their DI *binding* used to be split — ~15 modules
registered repos centrally in `PersistenceModule.cs` while others self-registered. That scattered a
vertical slice's wiring across two layers, made `PersistenceModule` a coupling/merge-conflict hotspot,
contradicted the documented DI pattern, and caused a duplicate `IDqtRunRepository` registration.

**How to apply:**
- New repo: write `services.AddScoped<IXxxRepository, XxxRepository>()` in `Features/Xxx/XxxModule.cs`,
  with `using Anela.Heblo.Persistence.<impl-ns>;`. The impl class must be `public`.
- Never add a repository binding to `PersistenceModule.cs`.
- Guard test: `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings` fails CI
  if a `*Repository` binding reappears there.
- Module-wiring tests that mock a repository must register the mock **after** `AddXxxModule(...)` so
  the mock overrides the module's real binding (DI = last registration wins).
- Stock is a Catalog subdomain → its repo lives in `CatalogModule`. UserDashboardSettings → `DashboardModule`.
