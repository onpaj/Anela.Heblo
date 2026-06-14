# OrgChartOptions Startup Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a missing or empty `OrgChart:DataSourceUrl` fail the host at startup with a clear `OptionsValidationException` instead of producing an opaque runtime exception on the first user request.

**Architecture:** Adopt the project's standard Options pattern — `[Required(AllowEmptyStrings = false)]` data annotation on `OrgChartOptions.DataSourceUrl` and `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` in `OrgChartModule`. This matches nine sibling modules (`Article`, `Leaflet`, `KnowledgeBase`, etc.) that already use the same chain. No runtime hot path changes; validation runs once at host boot via the `IHostedService` registered by `ValidateOnStart`.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Options.DataAnnotations`, `System.ComponentModel.DataAnnotations`, xUnit, FluentAssertions, `Microsoft.Extensions.Hosting`.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` | POCO with `[Required(AllowEmptyStrings = false)]` annotation on `DataSourceUrl`. |
| `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` | DI composition root — swaps `services.Configure<T>(...)` for `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()`. |
| `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs` | **New file.** xUnit tests using `HostBuilder` to verify `host.StartAsync()` throws when `OrgChart:DataSourceUrl` is missing/empty and succeeds when set. |

No new directories — `backend/test/Anela.Heblo.Tests/Features/OrgChart/` already exists (contains `GetOrganizationStructureHandlerTests.cs`).

Files that change together (options class + module + test) stay in their existing folders; no restructuring needed.

---

## Background Facts the Implementer Needs

1. **`appsettings.json` already contains a placeholder** at `backend/src/Anela.Heblo.API/appsettings.json:150-152`:
   ```json
   "OrgChart": {
     "DataSourceUrl": "https://example.com/organization-structure.json"
   }
   ```
   Because `appsettings.{Environment}.json` *layers on top of* `appsettings.json`, every environment (Development, Test, Staging, Production) inherits this placeholder unless explicitly overridden. Dev/Test boots will continue to succeed. Staging/Production rely on the Key Vault secret `OrgChart--DataSourceUrl` to override the placeholder with the real URL.

2. **`[Required]` on a `string` property does NOT reject empty strings by default** under `ValidateDataAnnotations`. The default-constructed `DataSourceUrl` is `string.Empty`, which is the most likely real-world misconfiguration (Key Vault secret present but blank, partial deployment, env-var override to `""`). You **must** use `[Required(AllowEmptyStrings = false)]` to make empty strings fail. (The spec's FR-1 wording is wrong on this point; the arch-review's Specification Amendment #1 is the authoritative version.)

3. **`ValidateOnStart()` registers an `IHostedService`**, not a synchronous validator. A bare `ServiceCollection.BuildServiceProvider()` will NOT trigger validation. Tests must use `HostBuilder.Build()` and call `await host.StartAsync()` for the validator to run.

4. **Test project transitive dependencies:** `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` references `Microsoft.AspNetCore.Mvc.Testing 8.0.8` and `Microsoft.AspNetCore.TestHost 8.0.8`, both of which transitively pull in `Microsoft.Extensions.Hosting`. The csproj explicitly references `Microsoft.Extensions.Hosting.Abstractions 10.0.3` (interfaces only). `HostBuilder` (the concrete builder) is in `Microsoft.Extensions.Hosting`. The transitive reference should make it available — Task 4 verifies this via a build; if `HostBuilder` is not resolvable, Task 4 adds the explicit package reference.

5. **Reference pattern for the test file** lives at `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletModuleIntegrationTests.cs` (shows in-memory `IConfiguration` + module registration) and `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingModuleValidationTests.cs` (shows xUnit + FluentAssertions style this project uses).

6. **Reference pattern for the production change** lives at `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs:14-17` (the exact `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()` chain) and `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs:9` (the `[Required, MinLength(1)]` pattern — we use `[Required(AllowEmptyStrings = false)]` which is equivalent for this purpose).

---

## Task 1: Annotate `DataSourceUrl` as required (FR-1)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs`

- [ ] **Step 1: Add the `using` directive and the `[Required(AllowEmptyStrings = false)]` attribute**

Replace the entire file contents with:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.OrgChart;

/// <summary>
/// Configuration options for organizational chart data source
/// </summary>
public class OrgChartOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "OrgChart";

    /// <summary>
    /// URL to the organizational structure JSON data source
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DataSourceUrl { get; set; } = string.Empty;
}
```

Notes:
- The class stays a `class` (not `record`) — DTO/contract convention in this repo, and the Options binder requires a public setter (`record` with `init` would also work but the existing shape is mutable, so we keep that).
- The default value `string.Empty` is intentional — `[Required(AllowEmptyStrings = false)]` will reject it at startup if no environment overrides the value.
- `SectionName` constant is unchanged.

- [ ] **Step 2: Build the Application project to verify the annotation compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeds. No warnings related to `OrgChartOptions`.

If the build fails with `CS0246` for `RequiredAttribute`, the `using System.ComponentModel.DataAnnotations;` directive is missing — re-check the file.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs
git commit -m "feat(orgchart): require DataSourceUrl via data annotation

Add [Required(AllowEmptyStrings = false)] to OrgChartOptions.DataSourceUrl
so the next change (ValidateDataAnnotations + ValidateOnStart in
OrgChartModule) will fail host startup when the URL is missing or blank."
```

---

## Task 2: Switch `OrgChartModule` to `AddOptions<T>` with validation (FR-2, FR-3)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs:19`

- [ ] **Step 1: Replace `services.Configure<OrgChartOptions>(...)` with the validated chain**

Edit `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` so the file reads exactly:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Features.OrgChart.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.OrgChart;

/// <summary>
/// Module for registering OrgChart feature services
/// </summary>
public static class OrgChartModule
{
    /// <summary>
    /// Registers OrgChart feature services
    /// </summary>
    public static IServiceCollection AddOrgChartServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register configuration options with startup validation
        services
            .AddOptions<OrgChartOptions>()
            .Bind(configuration.GetSection(OrgChartOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register HTTP client for fetching organization data
        services.AddHttpClient<IOrgChartService, OrgChartService>();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
```

Notes:
- Only the options-registration block changes. `AddHttpClient` and the trailing `return services;` stay untouched.
- Do NOT add `using Microsoft.Extensions.Options;` — `AddOptions<T>()` is an extension on `IServiceCollection` from `Microsoft.Extensions.DependencyInjection` (already imported).
- Do NOT touch any other file in the module.

- [ ] **Step 2: Build the Application project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeds. If `AddOptions` is not found, the `Microsoft.Extensions.Options.DataAnnotations` package is missing — but per the arch-review's Specification Amendment #4 this should be present transitively via the .NET 8 hosting metapackage. If it really is missing, add the package reference to `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` and rebuild.

- [ ] **Step 3: Run `dotnet format` on the modified files**

Run:
```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs
```

Expected: No diff, or only whitespace adjustments. Inspect the resulting file to confirm the chain still reads as written above.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs
git commit -m "feat(orgchart): validate OrgChartOptions on host startup

Replace services.Configure<OrgChartOptions>(...) with the standard
AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart() chain
so that a missing or blank OrgChart:DataSourceUrl fails the host at boot
with OptionsValidationException instead of surfacing later as an opaque
HttpRequestException on the first user request."
```

---

## Task 3: Write the failing validation tests (FR-4)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs`

- [ ] **Step 1: Create the test file with all three tests**

Create `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs` with the following exact contents:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.OrgChart;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public sealed class OrgChartModuleValidationTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IHost BuildHost(Dictionary<string, string?> configValues)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddInMemoryCollection(configValues);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOrgChartServices(context.Configuration);
            })
            .Build();
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_throws_when_OrgChart_DataSourceUrl_is_missing()
    {
        // Arrange
        using var host = BuildHost(new Dictionary<string, string?>());

        // Act
        var act = async () => await host.StartAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Message.Should().Contain(nameof(OrgChartOptions.DataSourceUrl));
    }

    [Fact]
    public async Task StartAsync_throws_when_OrgChart_DataSourceUrl_is_empty_string()
    {
        // Arrange
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["OrgChart:DataSourceUrl"] = string.Empty,
        });

        // Act
        var act = async () => await host.StartAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Message.Should().Contain(nameof(OrgChartOptions.DataSourceUrl));
    }

    [Fact]
    public async Task StartAsync_succeeds_when_OrgChart_DataSourceUrl_is_configured()
    {
        // Arrange
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["OrgChart:DataSourceUrl"] = "https://example.test/organization-structure.json",
        });

        // Act
        await host.StartAsync();

        // Assert
        // If StartAsync didn't throw, validation passed. Stop the host cleanly.
        await host.StopAsync();
    }
}
```

Notes:
- The `using var host` ensures the host is disposed even when `StartAsync` throws.
- The third test calls `StopAsync` only after successful start; if `StartAsync` were to throw, `using` still disposes.
- We assert `OptionsValidationException` specifically (not the broader `Exception`) because that is the exact contract documented in FR-3.
- We assert the exception message contains `DataSourceUrl` (FR-3 acceptance criterion) so operators can grep container logs for the failing key.
- The empty-string test enforces Specification Amendment #2 from the arch-review — the most likely real-world misconfiguration.
- No mocks are needed because the validator runs before any HTTP client or downstream service is touched.

- [ ] **Step 2: Run the new tests to verify they FAIL with the build-state we're about to fix**

> **IMPORTANT — read carefully:** If you executed Tasks 1 and 2 before Task 3, the production code is already correct and these tests will PASS, not fail. That's acceptable here because Tasks 1 and 2 are mechanically simple two-line changes whose correctness is obvious without a red-test step. The red step still matters for the *third* test (`StartAsync_throws_when_OrgChart_DataSourceUrl_is_empty_string`) which guards against a regression to `[Required]` without `AllowEmptyStrings = false`. To prove this guard works, temporarily change `OrgChartOptions.cs` to use `[Required]` (no parameter), re-run the empty-string test, confirm it FAILS, then revert. This is a one-shot regression check — the instructions in Step 3 below do exactly that.

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OrgChartModuleValidationTests"
```

Expected with `[Required(AllowEmptyStrings = false)]`: all three tests PASS.

If the test runner reports `CS0246 HostBuilder` or `Microsoft.Extensions.Hosting` is not found, the transitive reference is not flowing through. Fix by adding the package reference to `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
```

Place this alongside the existing `Microsoft.Extensions.Hosting.Abstractions` line. Re-run the test command.

- [ ] **Step 3: Verify the empty-string guard by temporarily weakening the annotation**

This step proves the empty-string test isn't a tautology — it actually depends on `AllowEmptyStrings = false`.

3a. Temporarily edit `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` and change:

```csharp
[Required(AllowEmptyStrings = false)]
```

to:

```csharp
[Required]
```

3b. Run only the empty-string test:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StartAsync_throws_when_OrgChart_DataSourceUrl_is_empty_string"
```

Expected: the test FAILS — `OptionsValidationException` is not thrown because `[Required]` alone allows empty strings on `string` properties. This confirms the test genuinely exercises the `AllowEmptyStrings = false` branch.

3c. Revert the annotation back to `[Required(AllowEmptyStrings = false)]`. Confirm by re-reading the file or:

```bash
git diff backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs
```

Expected: no diff (after revert).

3d. Re-run the full new test class to confirm all three tests pass with the restored annotation:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OrgChartModuleValidationTests"
```

Expected: 3 tests PASS, 0 fail.

- [ ] **Step 4: Run `dotnet format` on the new test file**

Run:
```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs
```

Expected: no diff, or only whitespace adjustments.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs
# Include csproj only if the Microsoft.Extensions.Hosting package was added in Step 2.
git status --short backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
# If the csproj shows modified, add it:
git add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "test(orgchart): assert host start fails for missing/empty DataSourceUrl

Three xUnit cases:
  * missing OrgChart section            -> OptionsValidationException
  * OrgChart:DataSourceUrl = \"\"          -> OptionsValidationException
  * OrgChart:DataSourceUrl = https://... -> StartAsync succeeds

Uses HostBuilder so ValidateOnStart's IHostedService actually runs;
ServiceCollection.BuildServiceProvider() alone would skip validation."
```

---

## Task 4: Regression-check the rest of the test suite and the full build

This task catches integration tests that boot the full API host (e.g., `HebloWebApplicationFactory`-based tests) and might have been silently relying on the old non-validating behavior. The placeholder URL in `backend/src/Anela.Heblo.API/appsettings.json:151` should already cover them, but we verify rather than assume.

**Files:** none modified in this task unless a regression is found.

- [ ] **Step 1: Build the entire backend solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds.

- [ ] **Step 2: Run the full backend test suite**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests pass.

If any `WebApplicationFactory`-based test (search for it in `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs` users) now fails with `OptionsValidationException`, that test is booting an environment where neither `appsettings.json` nor any override supplies `OrgChart:DataSourceUrl`. Fix by adding the configuration override in the test's `ConfigureWebHost`:

```csharp
builder.ConfigureAppConfiguration((context, config) =>
{
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["OrgChart:DataSourceUrl"] = "https://example.test/organization-structure.json",
    });
});
```

Do NOT modify `appsettings.json` or `appsettings.Test.json` — they already contain (`appsettings.json`) or inherit (`appsettings.Test.json`) the placeholder URL. The likely root cause of any failure would be a test that uses `builder.UseSetting(...)` to wipe configuration or sets `ASPNETCORE_ENVIRONMENT` to something that bypasses `appsettings.json`. Fix the test, not the production config.

- [ ] **Step 3: Verify the dotnet format check is clean for the entire solution**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0, no diff reported.

If exit code is non-zero, run without `--verify-no-changes` to apply formatting, then re-check:

```bash
dotnet format backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

- [ ] **Step 4: Smoke-test the host startup behavior by hand (sanity check, optional but recommended)**

Temporarily blank the placeholder in `backend/src/Anela.Heblo.API/appsettings.json` (line 151):

4a. Edit the file so the value becomes empty:
```json
"OrgChart": {
  "DataSourceUrl": ""
}
```

4b. Attempt to run the API:
```bash
cd backend/src/Anela.Heblo.API
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build 2>&1 | head -60
cd -
```

Expected: the process exits non-zero with `OptionsValidationException` and a message containing `DataSourceUrl`. The exception should appear before any `Now listening on:` line from Kestrel.

4c. Revert the change:
```bash
git checkout -- backend/src/Anela.Heblo.API/appsettings.json
git diff backend/src/Anela.Heblo.API/appsettings.json
```

Expected: no diff after revert.

- [ ] **Step 5: Commit any test fixes if Step 2 required them**

If no test fixes were needed, skip this step — Task 4 produces no commit on its own.

If a `WebApplicationFactory`-based test was modified in Step 2, commit it now:

```bash
git add backend/test/Anela.Heblo.Tests/<the modified test file>
git commit -m "test: inject OrgChart:DataSourceUrl placeholder for host-booting tests

Required after enabling ValidateOnStart on OrgChartOptions; without an
explicit override the test environment would now throw at host start."
```

---

## Self-Review Summary

**Spec coverage check:**

| Spec requirement | Covered by |
|------------------|-----------|
| FR-1: `[Required]` on `DataSourceUrl`, `using System.ComponentModel.DataAnnotations;`, no other property changes | Task 1 (uses the arch-review's corrected `[Required(AllowEmptyStrings = false)]`) |
| FR-2: `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()` chain, section name unchanged, no other DI changes | Task 2 |
| FR-3: Missing/null/empty DataSourceUrl raises `OptionsValidationException` containing `DataSourceUrl` before HTTP serving | Task 3 (tests 1 + 2), Task 4 Step 4 (manual smoke test) |
| FR-4: Tests assert throw on missing + succeed on present, AAA pattern, descriptive names | Task 3 (three tests: missing, empty, configured) |
| NFR-1/2/3: No runtime change, no security change, no API surface change | Production-code diff is limited to two lines in `OrgChartOptions.cs` and four lines in `OrgChartModule.cs`; no controller/handler/contract changes |
| NFR-4: Property name appears in exception message | Asserted by `Should().Contain(nameof(OrgChartOptions.DataSourceUrl))` in Task 3 |
| Spec amendment 1 (arch-review): `AllowEmptyStrings = false` | Task 1 |
| Spec amendment 2 (arch-review): empty-string test | Task 3, test #2 |
| Spec amendment 3 (arch-review): `HostBuilder`, not raw `ServiceProvider` | Task 3 helper `BuildHost` |
| Spec amendment 4 (arch-review): no package reference change unless transitive resolution fails | Task 2 Step 2 + Task 3 Step 2 contain conditional fallbacks |
| Risk R-2 (arch-review): existing WAF-based tests | Task 4 Step 2 |
| Risk R-5 (arch-review): test project lacks `Microsoft.Extensions.Hosting` reference | Task 3 Step 2 fallback adds the package if needed |

**Placeholder scan:** No "TBD", "implement later", or "add appropriate X" phrasing. Every code step contains the literal code to paste; every test step contains the literal test method. Two clearly-scoped conditional branches exist (Task 2 Step 2 fallback for `AddOptions` resolution and Task 3 Step 2 fallback for `HostBuilder` resolution) — both spell out the exact `<PackageReference>` to add and the precise file to modify.

**Type/name consistency:**
- `OrgChartOptions.DataSourceUrl` — used identically in production code (Task 1, 2) and tests (Task 3).
- `OrgChartOptions.SectionName` — referenced once, in Task 2, and matches the existing constant value `"OrgChart"`.
- `AddOrgChartServices` — the method name in `OrgChartModule.cs` is unchanged; Task 3's test helper calls `services.AddOrgChartServices(context.Configuration)` with the exact same signature.
- Configuration key `OrgChart:DataSourceUrl` is identical between `appsettings.json`, the test in-memory dictionary, and the smoke test.
- Test file namespace `Anela.Heblo.Tests.Features.OrgChart` matches the existing sibling `GetOrganizationStructureHandlerTests.cs`.
