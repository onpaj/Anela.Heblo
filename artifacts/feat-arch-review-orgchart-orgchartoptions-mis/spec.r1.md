# Specification: OrgChartOptions Startup Validation

## Summary
Add startup-time validation to `OrgChartOptions` so that a missing or empty `DataSourceUrl` causes the application host to fail fast at boot rather than producing an opaque `HttpRequestException`/`UriFormatException` on the first user request. Misconfiguration of the OrgChart external data source becomes a deployment-time failure with a clear error message.

## Background
`OrgChartOptions.DataSourceUrl` is bound from the `OrgChart` configuration section (sourced from Azure Key Vault in staging/production via the `OrgChart--DataSourceUrl` secret) and defaults to `string.Empty`. The current registration in `OrgChartModule.AddOrgChartServices` uses `services.Configure<OrgChartOptions>(...)` with no validation:

```csharp
// backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs:19
services.Configure<OrgChartOptions>(configuration.GetSection(OrgChartOptions.SectionName));
```

If the Key Vault secret is missing, renamed, or fails to load, the host starts successfully. The failure only surfaces deep inside `OrgChartService.GetOrganizationStructureAsync` when a user opens the OrgChart page, with an exception message that does not name the missing configuration key. This is a known operational risk because:

- The project documents that "All secrets go to Azure Key Vault, never to Web App environment variables" — a rename or omission of a KV secret is plausible during deployment.
- Database migrations and secrets are managed manually by a solo developer, so silent boot is the worst possible failure mode.
- CI does not exercise the OrgChart endpoint, so a missing secret can pass all automated checks.

The fix is the standard .NET Options pattern: `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` plus a `[Required]` attribute on `DataSourceUrl`.

## Functional Requirements

### FR-1: Annotate `DataSourceUrl` as required
Add `[Required]` from `System.ComponentModel.DataAnnotations` to the `DataSourceUrl` property on `OrgChartOptions`. The default value `string.Empty` is retained so that the annotation triggers a clear validation failure (empty string fails `[Required]` when `AllowEmptyStrings = false`, which is the default).

**Acceptance criteria:**
- `OrgChartOptions.DataSourceUrl` is decorated with `[Required]`.
- The `using System.ComponentModel.DataAnnotations;` directive is present.
- The property remains a public mutable `string` (the Options binder requires a public setter).
- No other property on `OrgChartOptions` is modified.

### FR-2: Register options with data-annotations validation and validate-on-start
Replace the existing `services.Configure<OrgChartOptions>(...)` call in `OrgChartModule.AddOrgChartServices` with the chained `AddOptions<T>` builder so that validation runs at host startup.

**Acceptance criteria:**
- `OrgChartModule.AddOrgChartServices` registers `OrgChartOptions` via:
  ```csharp
  services
      .AddOptions<OrgChartOptions>()
      .Bind(configuration.GetSection(OrgChartOptions.SectionName))
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```
- The bound section name remains `OrgChartOptions.SectionName` ("OrgChart").
- Consumers that inject `IOptions<OrgChartOptions>`, `IOptionsSnapshot<OrgChartOptions>`, or `IOptionsMonitor<OrgChartOptions>` continue to work without code changes.
- No other service registration inside `OrgChartModule` is altered.

### FR-3: Application fails fast on missing or empty `DataSourceUrl`
When the configuration section is absent, the `OrgChart:DataSourceUrl` key is missing, or its value is an empty string, the host must throw `OptionsValidationException` during startup (specifically when the `IHostedService` validation runs via `ValidateOnStart()`). The exception message must name the failing property so that the operator can identify the missing key without reading source code.

**Acceptance criteria:**
- `dotnet run` (or container start) exits non-zero with an `OptionsValidationException` when `OrgChart:DataSourceUrl` is missing, null, or empty.
- The exception message contains the text `DataSourceUrl` so it is grep-able in container logs.
- The exception is raised before any HTTP request is served (i.e., before the Kestrel pipeline reaches steady state).
- When `OrgChart:DataSourceUrl` is a non-empty string, startup completes normally and the OrgChart endpoint behaves exactly as before.

### FR-4: Test coverage for the new validation behaviour
Add a unit/integration test (xUnit, in the existing OrgChart test project under `backend/test/`) that verifies the host-builder throws when the section is missing and that it succeeds when the section is provided.

**Acceptance criteria:**
- A test that builds a minimal `IHost` calling `services.AddOrgChartServices(configuration)` with an empty `IConfiguration` and asserts that `host.StartAsync()` throws `OptionsValidationException` mentioning `DataSourceUrl`.
- A test that builds the same host with `OrgChart:DataSourceUrl` set to a non-empty string and asserts `StartAsync` succeeds.
- Both tests follow the AAA pattern and are named descriptively (e.g., `StartAsync_throws_when_OrgChart_DataSourceUrl_is_missing`).

## Non-Functional Requirements

### NFR-1: Performance
No runtime hot-path is affected. Validation runs once at host start. Steady-state request latency is unchanged.

### NFR-2: Security
The change strictly improves the failure mode of a misconfigured external data-source URL. It does not change authentication, authorization, or any data-handling code. The validation message must contain only the property name, never the (potentially sensitive) configured value.

### NFR-3: Backwards compatibility
- Environments that already provide a valid `OrgChart:DataSourceUrl` (development `appsettings.json`, staging Key Vault, production Key Vault) continue to start.
- No public API surface (HTTP, OpenAPI, MediatR contracts) changes.
- No database, migration, or front-end change is required.

### NFR-4: Observability
Because `ValidateOnStart` raises the exception from the host startup pipeline, the existing logging configuration captures it. No additional logging code is required, but the test in FR-4 should assert the property name appears in the exception message so future log scrapers can rely on it.

## Data Model
No data-model changes. `OrgChartOptions` keeps the same shape (a single `DataSourceUrl` string plus the `SectionName` constant) with a new `[Required]` attribute.

## API / Interface Design
No external API surface change. Internal interface change is limited to the DI registration call inside `OrgChartModule.AddOrgChartServices`:

Before:
```csharp
services.Configure<OrgChartOptions>(configuration.GetSection(OrgChartOptions.SectionName));
```

After:
```csharp
services
    .AddOptions<OrgChartOptions>()
    .Bind(configuration.GetSection(OrgChartOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

## Dependencies
- `Microsoft.Extensions.Options.DataAnnotations` — already transitively present in any .NET 8 web host; confirm during implementation. If not present, add the explicit package reference to `Anela.Heblo.Application.csproj`.
- `System.ComponentModel.DataAnnotations` — part of the .NET base class library; no package reference needed.
- No new runtime, infrastructure, or Key Vault changes.

## Out of Scope
- Validating the *format* of `DataSourceUrl` (e.g., `[Url]` attribute or `Uri.TryCreate`). The brief only asks for presence validation. Format validation is a reasonable follow-up but is not implemented here.
- Reachability checks against `DataSourceUrl` at startup (e.g., HEAD request). Adding network I/O to host startup is out of scope and would create a different class of failure.
- Refactoring other modules' option classes to use `AddOptions().ValidateOnStart()`. This spec covers only `OrgChartOptions`.
- Changes to `OrgChartService` error handling for runtime failures (e.g., transient HTTP errors from a reachable but failing data source).
- Documentation updates beyond what naturally lives in code; no CLAUDE.md or `docs/` changes are required.

## Open Questions
None.

## Status: COMPLETE