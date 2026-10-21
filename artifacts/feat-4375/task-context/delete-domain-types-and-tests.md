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

