### task: inline-defaults-in-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

- [ ] Step 1: In `GetConfigurationHandlerTests.cs`, add failing-first tests using a handler built from an in-memory configuration (no `ASPNETCORE_ENVIRONMENT` for the default case; build `ConfigurationBuilder` directly in that test since `CreateHandler` always adds "Test"):
  - `Handle_ReturnsProductionEnvironment_WhenAspNetCoreEnvironmentAbsent` -> `response.Environment.Should().Be("Production")`
  - `Handle_ReturnsConfiguredEnvironment_WhenSet` -> `"Staging"`
  - `Handle_UseMockAuthDefaultsToFalse_WhenAbsent` -> `false`
  Replace `ConfigurationConstants.DEFAULT_VERSION` with `"1.0.0"` in both `NotBe` assertions and drop `using Anela.Heblo.Domain.Features.Configuration;`.
- [ ] Step 2: In the handler add `private const string DefaultVersion = "1.0.0";` and `private const string DefaultEnvironment = "Production";`. Replace `BuildApplicationConfiguration()` and the copy block with a direct `GetConfigurationResponse` initializer (see design.r1.md), preserving version-priority comments. Remove the Domain `using`.
- [ ] Step 3: Run `cd backend && dotnet test --filter "FullyQualifiedName~Features.Configuration"` (ApplicationConfigurationTests still exists and compiles at this point only if Domain types remain; they do until the next task). Expected: PASS.
- [ ] Step 4: Commit `refactor(configuration): inline defaults in GetConfigurationHandler`.

