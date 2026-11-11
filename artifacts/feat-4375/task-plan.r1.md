# Implementation Plan: Remove ApplicationConfiguration entity and ConfigurationConstants

**Goal:** Delete the two Domain types, inline defaults in `GetConfigurationHandler`, keep endpoint behaviour identical.
**Architecture:** Handler builds `GetConfigurationResponse` directly with private const defaults.
**Tech Stack:** .NET 8, MediatR, xUnit, FluentAssertions.

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

### task: delete-domain-types-and-tests

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs`

- [ ] Step 1: `git rm` the three files (the Domain `Features/Configuration` folder disappears).
- [ ] Step 2: `grep -rn "ApplicationConfiguration\b\|ConfigurationConstants" backend --include=*.cs` -> expect no matches (note `ApplicationConfigurationTests` class name is gone too).
- [ ] Step 3: `cd backend && dotnet build && dotnet format --verify-no-changes` -> success.
- [ ] Step 4: `dotnet test --filter "FullyQualifiedName~Features.Configuration"` -> all pass, including `GetConfigurationEndpointTests`.
- [ ] Step 5: Commit `refactor(configuration): remove ApplicationConfiguration and ConfigurationConstants`.

### task: update-process-doc

**Files:**
- Modify: `docs/processes/module-configuration.md`

- [ ] Step 1: In the version table row replace `` `"1.0.0"` (`ConfigurationConstants.DEFAULT_VERSION`) `` with `` `"1.0.0"` (`DefaultVersion` const in the handler) ``; environment row `(`DEFAULT_ENVIRONMENT`)` -> `(`DefaultEnvironment`)`.
- [ ] Step 2: Remove the `ConfigurationConstants.cs` bullet under "Code entry points"; adjust the handler bullet to say "value resolution and fallbacks".
- [ ] Step 3: Bump `verified_at` per `docs/processes/_TEMPLATE.md` convention (current short SHA of HEAD).
- [ ] Step 4: Run `python3 scripts/process-docs/check.py index` -> passes.
- [ ] Step 5: Commit `docs(processes): update module-configuration after removing ConfigurationConstants`.
