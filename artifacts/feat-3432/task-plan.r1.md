# Remove Timestamp from ApplicationConfiguration Domain Entity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `Timestamp` from the `ApplicationConfiguration` domain entity and stamp it in `GetConfigurationHandler` at response-construction time instead.

**Architecture:** `ApplicationConfiguration` is a pure domain model; capturing `DateTime.UtcNow` in its constructor mixes transport-layer concerns into domain state and makes the object non-deterministic. Moving the stamp to `Handle()` in the application layer keeps the domain object side-effect-free and the response timestamp accurate to the moment the response is built.

**Tech Stack:** .NET 8, MediatR, xUnit, FluentAssertions, NSubstitute.

---

### task: remove-timestamp-from-domain-entity

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs:11,18`
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:39`
- Test: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

- [ ] **Step 1: Add the Timestamp assertion test (write the failing test first)**

  Open `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` and add the following test after the last existing `[Fact]` method (before the closing `}`):

  ```csharp
  [Fact]
  public async Task Handle_SetsTimestampAtResponseConstructionTime()
  {
      // Arrange
      var handler = CreateHandler(new Dictionary<string, string?>
      {
          [ConfigurationConstants.APP_VERSION] = "1.0.0"
      });
      var before = DateTime.UtcNow;

      // Act
      var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

      // Assert
      response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
      response.Timestamp.Should().BeOnOrAfter(before);
  }
  ```

  This test will **pass** against the current code (because `Timestamp` is already `DateTime.UtcNow` from the constructor), so we need it committed now so that after the domain change the test still passes — confirming the handler stamps it correctly.

- [ ] **Step 2: Run tests to establish baseline**

  ```bash
  cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests" --no-build 2>&1 || dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"
  ```

  All 5 tests (4 existing + 1 new) must pass before continuing.

- [ ] **Step 3: Remove `Timestamp` from `ApplicationConfiguration`**

  Edit `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`.

  Remove line 11 (`public DateTime Timestamp { get; private set; }`) and line 18 (`Timestamp = DateTime.UtcNow;`).

  The file should look like this after the edit:

  ```csharp
  namespace Anela.Heblo.Domain.Features.Configuration;

  /// <summary>
  /// Domain model representing application configuration
  /// </summary>
  public class ApplicationConfiguration
  {
      public string Version { get; private set; }
      public string Environment { get; private set; }
      public bool UseMockAuth { get; private set; }

      public ApplicationConfiguration(string version, string environment, bool useMockAuth)
      {
          Version = version ?? throw new ArgumentNullException(nameof(version));
          Environment = environment ?? throw new ArgumentNullException(nameof(environment));
          UseMockAuth = useMockAuth;
      }

      /// <summary>
      /// Creates configuration with fallback values
      /// </summary>
      public static ApplicationConfiguration CreateWithDefaults(string? version, string? environment, bool useMockAuth)
      {
          return new ApplicationConfiguration(
              version ?? "1.0.0",
              environment ?? "Production",
              useMockAuth
          );
      }
  }
  ```

- [ ] **Step 4: Update `GetConfigurationHandler` to stamp `Timestamp` directly**

  Edit `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`.

  On line 39, change:
  ```csharp
  Timestamp = appConfig.Timestamp,
  ```
  to:
  ```csharp
  Timestamp = DateTime.UtcNow,
  ```

  The full `Handle()` method's response initializer should now read:
  ```csharp
  var response = new GetConfigurationResponse
  {
      Version = appConfig.Version,
      Environment = appConfig.Environment,
      UseMockAuth = appConfig.UseMockAuth,
      Timestamp = DateTime.UtcNow,
  };
  ```

- [ ] **Step 5: Build to confirm no compilation errors**

  ```bash
  cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
  dotnet build backend/Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors. If you see `'ApplicationConfiguration' does not contain a definition for 'Timestamp'`, you edited the handler but not the domain entity (or vice-versa). Fix whichever is missing.

- [ ] **Step 6: Run all handler tests to verify everything passes**

  ```bash
  cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"
  ```

  All 5 tests must pass. If `Handle_SetsTimestampAtResponseConstructionTime` fails, the handler is not assigning `DateTime.UtcNow` to `response.Timestamp`.

- [ ] **Step 7: Run the full test suite**

  ```bash
  cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```

  All tests must pass. A failure here means another test somewhere was relying on `appConfig.Timestamp` — search for `Timestamp` in the test project and fix any remaining references to `appConfig.Timestamp`.

- [ ] **Step 8: Format**

  ```bash
  cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
  dotnet format backend/Anela.Heblo.sln
  ```

  Re-run `dotnet build` to confirm formatting didn't introduce issues.

- [ ] **Step 9: Commit**

  ```bash
  cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
  git add backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs
  git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
  git add backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs
  git commit -m "refactor: move Timestamp from ApplicationConfiguration domain entity to handler

  Timestamp captured in the domain constructor was a transport-layer concern
  leaking into domain state and introduced non-determinism. Stamp is now set
  directly on GetConfigurationResponse in Handle() at response-construction time."
  ```
