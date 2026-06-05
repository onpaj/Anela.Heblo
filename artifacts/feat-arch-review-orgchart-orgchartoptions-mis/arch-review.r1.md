# Architecture Review: OrgChartOptions Startup Validation

## Skip Design: true

(Backend-only DI registration change. No UI, no API surface change.)

## Architectural Fit Assessment

The proposal aligns **exactly** with the dominant Options pattern already in this codebase. The same `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` chain is used in at least nine sibling modules (`Article`, `Leaflet`, `KnowledgeBase`, `CatalogDocuments`, `MeetingTasks`, `Marketing`, `Smartsupp`, and the OpenMeteo/ShoptetApi adapters). `OrgChartModule` is currently the outlier still using the legacy `services.Configure<T>(...)` call. Adopting the established pattern eliminates configuration drift rather than introducing a new approach.

Integration points are narrow:
- `OrgChartModule.AddOrgChartServices` — only registration site, called from `ApplicationModule.cs:87`.
- `OrgChartService` — sole consumer of `IOptions<OrgChartOptions>`. No changes required; `IOptions<T>` resolution is unchanged.
- Host pipeline — `ValidateOnStart()` registers an `IHostedService` that runs before Kestrel begins listening. This is consistent with how every other module integrates.

## Proposed Architecture

### Component Overview

```
                ┌─────────────────────────┐
                │   appsettings.json /    │
                │   KV: OrgChart--        │
                │   DataSourceUrl         │
                └────────────┬────────────┘
                             │  IConfiguration
                             ▼
┌────────────────────────────────────────────────────────┐
│  OrgChartModule.AddOrgChartServices                    │
│                                                         │
│   services.AddOptions<OrgChartOptions>()                │
│     .Bind(section "OrgChart")                           │
│     .ValidateDataAnnotations()  ─► reflects [Required] │
│     .ValidateOnStart()          ─► registers           │
│                                    StartupValidator    │
│                                    IHostedService      │
└─────────────────────┬──────────────────────────────────┘
                      │
                      ▼
       ┌─────────────────────────────┐
       │ Host.StartAsync()           │
       │  ├─ Runs StartupValidator   │  ◄── throws OptionsValidationException
       │  │     (validates options)  │      if DataSourceUrl null/empty
       │  └─ Starts Kestrel          │
       └─────────────────────────────┘
                      │
                      ▼  (only when valid)
       ┌─────────────────────────────┐
       │ OrgChartService             │
       │   ctor(IOptions<...>)       │  unchanged
       └─────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Use `[Required]` data annotation, not custom `IValidateOptions<T>`
**Options considered:**
- (A) `[Required]` + `ValidateDataAnnotations()` — declarative, matches every other module.
- (B) Custom `IValidateOptions<OrgChartOptions>` implementation — more flexibility but more code.
- (C) Inline `.Validate(opt => !string.IsNullOrWhiteSpace(opt.DataSourceUrl))` lambda.

**Chosen approach:** (A) — `[Required]` attribute.

**Rationale:** The spec only requires presence validation. Every other options class in the codebase uses data annotations (`Article`, `Leaflet`, `KnowledgeBase`, `CatalogDocuments`). Custom `IValidateOptions<T>` is reserved for cross-field validation (see `MarketingModule.ValidateRoundTrip`), which is not the case here. Note: `[Required]` on a `string` rejects `null` but **does not** reject empty strings by default; however, since the property's default is `string.Empty`, and we want empty strings to fail, this is acceptable only if we also reject empty strings. **Add `[Required(AllowEmptyStrings = false)]` explicitly** (or pair `[Required]` with `[MinLength(1)]` as `ArticleOptions` does) to guarantee empty-string rejection — the spec's claim that `[Required]` alone fails on empty strings is incorrect for `string` properties under `ValidateDataAnnotations`. See *Specification Amendments* below.

#### Decision 2: Place validation in the module, not in the options class
**Chosen approach:** Validation wiring stays in `OrgChartModule.AddOrgChartServices`. `OrgChartOptions` holds only the `[Required]` attribute.

**Rationale:** Matches the module-encapsulation convention. Modules own DI composition; options classes are POCOs with metadata only.

#### Decision 3: Do not change `OrgChartService`
**Chosen approach:** Leave the service ctor, `IOptions<T>` consumption, and runtime error handling untouched.

**Rationale:** Spec NFR-3 mandates backwards compatibility. Once validation passes, runtime behavior is identical. Cleaning up the service's catch-all `HttpRequestException` handling is a separate concern flagged in the brief but explicitly out of scope.

## Implementation Guidance

### Directory / Module Structure

No new directories. Three files touched:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` | Add `using System.ComponentModel.DataAnnotations;` and `[Required(AllowEmptyStrings = false)]` on `DataSourceUrl`. |
| `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` | Replace `services.Configure<OrgChartOptions>(...)` with the `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()` chain. |
| `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs` | **New file**; create `Features/OrgChart/` folder under the existing `Anela.Heblo.Tests` project. |

The new test folder is the **only structural addition** — `backend/test/Anela.Heblo.Tests/Features/OrgChart/` does not yet exist. Mirror the pattern from `Features/Leaflet/Infrastructure/LeafletModuleIntegrationTests.cs` and `Features/Marketing/MarketingModuleValidationTests.cs`.

### Interfaces and Contracts

**Unchanged** public/internal surface. The only contract worth pinning explicitly for downstream code:

```csharp
// OrgChartOptions.cs
public class OrgChartOptions
{
    public const string SectionName = "OrgChart";

    [Required(AllowEmptyStrings = false)]
    public string DataSourceUrl { get; set; } = string.Empty;
}

// OrgChartModule.cs
services
    .AddOptions<OrgChartOptions>()
    .Bind(configuration.GetSection(OrgChartOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### Data Flow

**At host startup:**
1. `Program.cs` → `WebApplication.Build()` registers all options including the OrgChart validator `IHostedService`.
2. `WebApplication.RunAsync()` → host triggers `IHostedService.StartAsync` for the validator before Kestrel accepts connections.
3. Validator resolves `IOptions<OrgChartOptions>.Value`, which forces the bind+validate path.
4. If `DataSourceUrl` is missing/empty → `OptionsValidationException` thrown; host exits with non-zero code; Docker container restart loop surfaces the misconfiguration in logs and Azure Web App health checks.
5. If valid → host proceeds normally; `OrgChartService.GetOrganizationStructureAsync` behavior unchanged.

**At runtime:** Identical to today.

### Test Strategy (clarifying FR-4)

Use `Microsoft.Extensions.Hosting`'s `HostBuilder` rather than only `ServiceCollection.BuildServiceProvider()`. `ValidateOnStart` registers an `IHostedService` that runs during `host.StartAsync()`; a bare `ServiceProvider` will not trigger it. Reference shape:

```csharp
// Features/OrgChart/OrgChartModuleValidationTests.cs
public sealed class OrgChartModuleValidationTests
{
    [Fact]
    public async Task StartAsync_throws_when_OrgChart_DataSourceUrl_is_missing()
    {
        // Arrange
        var host = BuildHost(configValues: new Dictionary<string, string?>());

        // Act
        var act = async () => await host.StartAsync();

        // Assert
        var ex = await act.Should().ThrowAsync<OptionsValidationException>();
        ex.Which.Message.Should().Contain(nameof(OrgChartOptions.DataSourceUrl));
    }

    [Fact]
    public async Task StartAsync_succeeds_when_OrgChart_DataSourceUrl_is_configured()
    {
        var host = BuildHost(new Dictionary<string, string?>
        {
            ["OrgChart:DataSourceUrl"] = "https://example.test/org.json",
        });

        await host.StartAsync();
        await host.StopAsync();
    }

    private static IHost BuildHost(Dictionary<string, string?> configValues) =>
        new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(configValues))
            .ConfigureServices((ctx, services) =>
                services.AddOrgChartServices(ctx.Configuration))
            .Build();
}
```

Also add a third test that asserts empty string fails (`["OrgChart:DataSourceUrl"] = ""`), since this is the most likely real-world misconfiguration (KV secret present but blank) and the default value of the property — see Risk R-1.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **R-1** `[Required]` alone does **not** reject empty strings on `string` properties under `ValidateDataAnnotations`. Spec FR-1 states the opposite — if implemented literally, an empty `OrgChart:DataSourceUrl` will pass validation. | High | Use `[Required(AllowEmptyStrings = false)]` *and* either `[MinLength(1)]` or rely on the `AllowEmptyStrings=false` default. Add explicit empty-string test (FR-4). |
| **R-2** Existing test/dev environments may not have `OrgChart:DataSourceUrl` set, breaking developer onboarding or `dotnet test` if the API host is bootstrapped. | Medium | Verify `appsettings.Development.json` / user-secrets include a placeholder URL. If tests instantiate the full API host (e.g., `WebApplicationFactory`), they must supply `OrgChart:DataSourceUrl` via in-memory config or env override. Audit existing `WebApplicationFactory`-based tests. |
| **R-3** Staging/production Key Vault rename of `OrgChart--DataSourceUrl` will now hard-fail the container — desired, but ensure rollback procedure is clear. | Low (intended behavior) | Document in PR description; rely on Azure Web App restart loop + log alerts to surface the failure. No code mitigation needed. |
| **R-4** `OptionsValidationException` is thrown by an `IHostedService`; some host configurations swallow startup errors. | Low | `Microsoft.Extensions.Hosting` defaults treat `IHostedService.StartAsync` failures as fatal in .NET 8. Confirm `HostOptions.BackgroundServiceExceptionBehavior` is not set to `Ignore` anywhere in `Program.cs`. |
| **R-5** Test project lacks `Microsoft.Extensions.Hosting` reference (only `.Hosting.Abstractions` is listed). | Low | `Microsoft.Extensions.Hosting.Abstractions` provides `IHost` / `IHostedService` interfaces only; `HostBuilder` requires `Microsoft.Extensions.Hosting`. Add the package reference to `Anela.Heblo.Tests.csproj` (it is already transitively pulled by `Microsoft.AspNetCore.Mvc.Testing`, so likely no-op, but verify during implementation). |

## Specification Amendments

1. **FR-1 wording is incorrect about `[Required]` semantics.** Replace the second sentence with:
   > Annotate `DataSourceUrl` with `[Required(AllowEmptyStrings = false)]` so that both `null` and empty string fail validation. Note that `[Required]` alone allows empty strings on `string` properties; the explicit `AllowEmptyStrings = false` (or pairing with `[MinLength(1)]` as in `ArticleOptions`) is required to reject the default `string.Empty` value.

2. **FR-4 add an empty-string test case.** Add:
   > Test that startup also throws `OptionsValidationException` when `OrgChart:DataSourceUrl` is set to an empty string (not just absent). This is the most likely real-world misconfiguration after a KV secret rename or partial deployment.

3. **FR-4 test infrastructure clarification.** Tests must use `HostBuilder` (not raw `ServiceCollection.BuildServiceProvider()`) so the `ValidateOnStart`-registered `IHostedService` actually runs. Update the acceptance-criterion phrasing from "builds a minimal `IHost` calling `services.AddOrgChartServices(...)`" to specify `HostBuilder.ConfigureServices` composition.

4. **Dependencies clarification.** `Microsoft.Extensions.Options.DataAnnotations` is included transitively via `Microsoft.Extensions.Hosting` in .NET 8 — confirmed by inspection of nine other modules already using the pattern without an explicit reference. No package change needed in `Anela.Heblo.Application.csproj`.

## Prerequisites

None blocking implementation. Before merging:

- Confirm that `appsettings.Development.json` (and any developer user-secrets) define a valid `OrgChart:DataSourceUrl` so local dev/test runs continue to start.
- Confirm staging Key Vault `OrgChart--DataSourceUrl` is set and non-empty (per the brief, this is the assumed-working baseline — the validation change does not require a new secret).
- Confirm production Key Vault `OrgChart--DataSourceUrl` is set and non-empty.
- Audit `WebApplicationFactory`-style integration tests under `backend/test/Anela.Heblo.Tests/` that boot the full API host; any that omit `OrgChart:DataSourceUrl` will start failing after this change and must inject a placeholder URL via test configuration.