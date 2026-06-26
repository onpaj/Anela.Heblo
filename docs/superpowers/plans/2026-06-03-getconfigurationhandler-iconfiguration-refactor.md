# GetConfigurationHandler IConfiguration Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the direct `System.Environment.GetEnvironmentVariable(APP_VERSION)` call inside `GetConfigurationHandler.GetVersionFromSources()` with an `IConfiguration` lookup, restoring consistency with the rest of the handler and making the version fallback chain unit-testable without process-level env-var mutation.

**Architecture:** Pure surgical refactor inside one Vertical Slice (`Features/Configuration`). The handler already injects `IConfiguration`; we route the `APP_VERSION` read through the same abstraction (indexer syntax). The .NET host already registers env vars as a configuration source via `AddEnvironmentVariables()`, so behavior is preserved in every deployed environment. No new components, abstractions, or DI registrations. A new unit-test file exercises the version-resolution fallback chain with an in-memory `IConfiguration`.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, NSubstitute, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`.

---

## File Structure

**Modify (1 file):**

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
  - Line 76: swap `Environment.GetEnvironmentVariable(...)` for `_configuration[...]`.
  - Line 79: update the `_logger.LogDebug` text so it no longer claims the value came from “environment variable” (the value may now legitimately originate from any provider in the chain: appsettings → env vars → Key Vault).
  - No `using` changes — the file does not have a `using System;` directive; `Environment` resolves via SDK `ImplicitUsings`. `System.Reflection` stays in use for the assembly-version fallback.

**Create (1 file):**

- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — handler-level unit tests. Constructs the SUT directly (no `WebApplicationFactory`, no `[Collection("WebApp")]`), feeds in-memory `IConfiguration`, mocks `IHostEnvironment` with NSubstitute, uses `NullLogger<GetConfigurationHandler>.Instance`.

**Do NOT touch:**

- `ConfigurationConstants.cs`, `ApplicationConfiguration.cs`, `GetConfigurationRequest.cs`, `GetConfigurationResponse.cs`, `ConfigurationModule.cs`, `Program.cs`, `GetConfigurationEndpointTests.cs`, or any other handler / module.

**Package dependencies:** No new packages. `Microsoft.Extensions.Configuration` (for `ConfigurationBuilder` / `AddInMemoryCollection`), `Microsoft.Extensions.Logging.Abstractions` (for `NullLogger<T>`), and `Microsoft.Extensions.Hosting.Abstractions` (for `IHostEnvironment`) are all transitively available in `Anela.Heblo.Tests` via the existing project references to `Anela.Heblo.Application` and the explicit `Microsoft.Extensions.Hosting.Abstractions` reference at `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:29`. NSubstitute is already referenced at line 17 of that csproj.

---

## Working Directory

All commands assume the worktree root:

```
/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-configuration-getconfigurati
```

---

### Task 1: Write failing unit tests for GetConfigurationHandler

**Files:**

- Create: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

- [ ] **Step 1: Create the new unit-test file with four failing tests**

Write the file with this exact content:

```csharp
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Domain.Features.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Anela.Heblo.Tests.Features.Configuration;

/// <summary>
/// Unit tests for <see cref="GetConfigurationHandler"/>.
/// Verifies that version resolution honors <see cref="IConfiguration"/> rather than
/// reading process-level environment variables directly. Assembly-attribute fallback
/// branches are exercised by absence rather than pinning a specific value, because
/// <see cref="System.Reflection.Assembly.GetExecutingAssembly"/> always resolves to
/// the Application assembly under the standard test build.
/// </summary>
public class GetConfigurationHandlerTests
{
    private const string TestEnvironmentName = "Test";

    [Fact]
    public async Task Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = "2.5.1-ci.42",
        });
        var sut = CreateHandler(configuration);

        // Act
        var response = await sut.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.Version.Should().Be("2.5.1-ci.42");
    }

    [Fact]
    public async Task Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmptyString()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = string.Empty,
        });
        var sut = CreateHandler(configuration);

        // Act
        var response = await sut.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert: The assembly under test always exposes a non-empty informational or
        // assembly version, so the fallback must fire and produce a non-default value.
        response.Version.Should().NotBeNullOrEmpty();
        response.Version.Should().NotBe(ConfigurationConstants.DEFAULT_VERSION);
    }

    [Fact]
    public async Task Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var sut = CreateHandler(configuration);

        // Act
        var response = await sut.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.Version.Should().NotBeNullOrEmpty();
        response.Version.Should().NotBe(ConfigurationConstants.DEFAULT_VERSION);
    }

    [Fact]
    public async Task Handle_PropagatesUseMockAuthFromConfiguration()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationConstants.USE_MOCK_AUTH] = "true",
        });
        var sut = CreateHandler(configuration);

        // Act
        var response = await sut.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.UseMockAuth.Should().BeTrue();
        response.Environment.Should().Be(TestEnvironmentName);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static GetConfigurationHandler CreateHandler(IConfiguration configuration)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(TestEnvironmentName);
        return new GetConfigurationHandler(
            configuration,
            environment,
            NullLogger<GetConfigurationHandler>.Instance);
    }
}
```

Notes for the engineer:

- `Dictionary<string, string?>` matches the signature of `AddInMemoryCollection(IEnumerable<KeyValuePair<string, string?>>)` in `Microsoft.Extensions.Configuration` 8.x and avoids the `configData!` null-forgiving operator used in the older `RefreshTaskConfigurationTests` file.
- `NullLogger<T>.Instance` lives in `Microsoft.Extensions.Logging.Abstractions`, transitively referenced via `Anela.Heblo.Application`.
- `GetConfigurationRequest` is a parameterless `IRequest<GetConfigurationResponse>` (confirmed at `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationRequest.cs:8`). `new GetConfigurationRequest()` is correct.
- xUnit `Using Include="Xunit"` is already configured globally in `Anela.Heblo.Tests.csproj` (line 35), so no `using Xunit;` is required.

- [ ] **Step 2: Build the test project to confirm the test file compiles**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: **build succeeds**. If compilation fails (e.g., missing `using` or unknown symbol), fix only the test file — do not touch production code.

- [ ] **Step 3: Run the new tests to verify the configuration-driven test fails**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Configuration.GetConfigurationHandlerTests"
```

Expected:

- `Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet` — **FAIL** (the current handler reads `Environment.GetEnvironmentVariable`, ignores in-memory `IConfiguration`, and falls through to the assembly version, so `response.Version` will not equal `"2.5.1-ci.42"`).
- `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmptyString` — likely **PASS** today (current code already falls through when the env var is unset).
- `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent` — likely **PASS** today.
- `Handle_PropagatesUseMockAuthFromConfiguration` — likely **PASS** today.

The RED state for TDD is established by the first test. Do not proceed to Task 2 until you have observed the failure with output similar to `Expected response.Version to be "2.5.1-ci.42", but found "...".`.

If the first test unexpectedly **passes** (e.g., a stray `APP_VERSION` env var is leaking into the test process), stop and investigate before continuing — the refactor’s value depends on this test discriminating between the env-var read and the configuration read.

- [ ] **Step 4: Commit the failing test (RED checkpoint)**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs
git commit -m "test: add GetConfigurationHandler unit tests for IConfiguration-driven version resolution"
```

---

### Task 2: Refactor GetConfigurationHandler to read APP_VERSION via IConfiguration

**Files:**

- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:73-82`

- [ ] **Step 1: Swap the env-var read for an IConfiguration indexer read**

Open `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`. In the `GetVersionFromSources()` method (currently lines 73-102), replace **only** the first fallback branch.

Find these lines (current state, lines 73-82):

```csharp
    private string? GetVersionFromSources()
    {
        // 1. Try environment variable first (CI/CD pipeline)
        var version = Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION);
        if (!string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("Version found from APP_VERSION environment variable: {Version}", version);
            return version;
        }
```

Replace with:

```csharp
    private string? GetVersionFromSources()
    {
        // 1. Try configuration first — populated by the host's provider chain
        //    (appsettings → env vars → Key Vault). CI/CD injects APP_VERSION as an env var.
        var version = _configuration[ConfigurationConstants.APP_VERSION];
        if (!string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("Version resolved from configuration ({Key}): {Version}", ConfigurationConstants.APP_VERSION, version);
            return version;
        }
```

Do **not** touch the assembly-informational-version branch (lines 83-91 in the current file) or the assembly-version branch (lines 92-98). Do **not** add or remove any `using` directives. Do **not** reformat surrounding code.

- [ ] **Step 2: Run the new unit tests to verify they all pass (GREEN)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Configuration.GetConfigurationHandlerTests"
```

Expected: all **four** tests PASS, including `Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet`.

If any test still fails, stop. Re-read the diff in `GetConfigurationHandler.cs` and confirm the indexer call references the injected `_configuration` field (not a parameter, not `IConfiguration`-typed local). Do **not** modify the tests to make them pass.

- [ ] **Step 3: Run the existing GetConfiguration integration tests to confirm no regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Configuration.GetConfigurationEndpointTests"
```

Expected: all five existing endpoint tests PASS (`GetConfiguration_ShouldReturnSuccessAndCorrectContentType`, `GetConfiguration_ShouldReturnValidConfigurationResponse`, `GetConfiguration_ShouldIncludeMockAuthFlag`, `GetConfiguration_ShouldReturnTestEnvironment`, `GetConfiguration_ShouldReturnValidVersion`).

- [ ] **Step 4: Commit the refactor**

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
git commit -m "refactor: read APP_VERSION via IConfiguration in GetConfigurationHandler"
```

---

### Task 3: Validation gates (build, format, full test pass)

**Files:** none modified in this task — only verification.

- [ ] **Step 1: Run dotnet build on the full backend solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: clean build with zero errors. Treat any **new** warning introduced by these changes as a failure (existing pre-baseline warnings are out of scope per CLAUDE.md surgical-change guidance).

- [ ] **Step 2: Run dotnet format and confirm no diff**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exits 0 with no “formatted N files” output. If it reports changes:

1. Run `dotnet format backend/Anela.Heblo.sln` (no flag) to apply.
2. Inspect the resulting diff with `git diff`. Only formatting changes inside the two files touched by this plan are acceptable. If `dotnet format` rewrites unrelated files, revert those — do not include adjacent-code cleanup.
3. Re-run `dotnet format backend/Anela.Heblo.sln --verify-no-changes` to confirm clean.
4. Amend the previous commit only if formatting changes belong to `GetConfigurationHandler.cs`:
   ```bash
   git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
   git commit --amend --no-edit
   ```
   Otherwise create a separate `chore: dotnet format` commit scoped to the test file:
   ```bash
   git add backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs
   git commit -m "chore: dotnet format GetConfigurationHandlerTests"
   ```

- [ ] **Step 3: Run the Configuration test slice end-to-end**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Configuration"
```

Expected: 9 tests total (4 new unit tests + 5 existing endpoint tests), all passing.

- [ ] **Step 4: Confirm no stray `Environment.GetEnvironmentVariable` remains in the handler**

```bash
grep -n "Environment.GetEnvironmentVariable" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs || echo "clean"
```

Expected output: `clean`. If any match is reported, return to Task 2 — the refactor is incomplete and FR-1 acceptance criteria are not met.

- [ ] **Step 5: Confirm both files are committed and the worktree is clean**

```bash
git status
git log --oneline -5
```

Expected: `git status` reports a clean working tree. `git log` shows the two new commits (`test:` and `refactor:` — optionally a third `chore: dotnet format`) on top of the branch tip.

---

## Acceptance Mapping (Spec → Tasks)

| Spec requirement | Covered by |
|---|---|
| FR-1: `GetConfigurationHandler` no longer calls `Environment.GetEnvironmentVariable`; reads via `_configuration[...]` | Task 2 Step 1 + Task 3 Step 4 grep guard |
| FR-1: `using System;` only kept if needed; `ConfigurationConstants.APP_VERSION` unchanged | File never contained `using System;` (verified during planning); constant is not in the modify list |
| FR-2: Three-level fallback order preserved (config → informational version → assembly version) | Task 2 Step 1 (only line 76 changes; the two reflection branches stay verbatim); Task 1 Step 1 tests 2 and 3 (`Handle_FallsBackToAssemblyVersion_*`) |
| FR-3: Unit-testable via in-memory `IConfiguration`; no process env-var manipulation | Task 1 (all four tests use `ConfigurationBuilder().AddInMemoryCollection(...)`) |
| NFR-1: Behavioral parity across environments | Task 2 Step 3 (existing endpoint tests pass unchanged) |
| NFR-2: No new packages / DI registrations / constants | Task list touches exactly two files; no `.csproj` edits |
| NFR-3: `dotnet build`, `dotnet format`, all existing tests pass | Task 3 Steps 1–3 |
| NFR-4: Surgical change | Touched files limited to one handler + one new test file; log-message text adjusted only because the previous wording becomes factually wrong (covered by arch-review amendment 1) |

---

## Self-Review Notes

- **Spec coverage:** all four FRs and four NFRs map to specific task steps (table above). The arch-review amendment about the log-message text update is encoded in Task 2 Step 1 (new wording: `"Version resolved from configuration ({Key}): {Version}"`).
- **Placeholders:** none. Every code block contains the exact code; every command contains the exact arguments; every expectation states the exact outcome.
- **Type consistency:** `GetConfigurationHandler` constructor signature `(IConfiguration, IHostEnvironment, ILogger<GetConfigurationHandler>)` is referenced identically in Task 1 (`CreateHandler` helper) and Task 2 (no constructor changes). `Dictionary<string, string?>` is used consistently in all four test setups. `ConfigurationConstants.APP_VERSION`, `ConfigurationConstants.USE_MOCK_AUTH`, and `ConfigurationConstants.DEFAULT_VERSION` are all real constants (verified at `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:9,13,17`).
- **TDD discipline:** Task 1 establishes RED with at least one observably failing test before Task 2 produces GREEN. The refactor does not modify tests to make them pass.
- **Surgical scope:** exactly one production line (plus the adjacent log message that becomes inaccurate) and one new test file. No `using` churn, no formatting drift outside the touched files, no adjacent cleanup.
