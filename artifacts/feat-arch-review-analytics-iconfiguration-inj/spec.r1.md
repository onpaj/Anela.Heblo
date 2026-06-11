# Specification: Replace `IConfiguration` injection in `GetInvoiceImportStatisticsHandler` with typed options

## Summary
Refactor `GetInvoiceImportStatisticsHandler` in the Analytics module so it no longer depends on `Microsoft.Extensions.Configuration.IConfiguration`. Introduce a typed `InvoiceImportOptions` class, bind it to the existing `InvoiceImport` configuration section in `AnalyticsModule`, and inject `IOptions<InvoiceImportOptions>` into the handler. This restores Clean Architecture boundaries (no infrastructure types in the Application layer) and simplifies unit testing.

## Background
The daily architecture review routine flagged `GetInvoiceImportStatisticsHandler` (Application layer) for directly injecting `IConfiguration` and reading raw key paths (`InvoiceImport:MinimumDailyThreshold`, `InvoiceImport:DefaultDaysBack`).

`IConfiguration` is an infrastructure abstraction owned by `Microsoft.Extensions.Configuration`. Application-layer handlers should depend on domain-owned, strongly-typed options instead, per the Dependency Inversion Principle and the project's Clean Architecture/Vertical Slice conventions (see `docs/architecture/development_guidelines.md`).

Concretely, this causes two issues:
1. **Architectural leak** — the Application project takes a transitive dependency on raw configuration semantics and string key paths.
2. **Test friction** — unit tests must construct an `IConfigurationRoot` (or mock `IConfiguration.GetValue<T>` extension behavior, which is non-trivial) instead of passing a plain options object.

The fix is small, isolated to the Analytics module, and follows the standard .NET Options pattern.

## Functional Requirements

### FR-1: Introduce `InvoiceImportOptions` typed options class
Add a new options class in the Analytics module that mirrors the existing `InvoiceImport` configuration section.

- Namespace: `Anela.Heblo.Application.Features.Analytics` (or the existing module-level namespace used by the Analytics slice — match neighbors).
- File location: `backend/src/Anela.Heblo.Application/Features/Analytics/InvoiceImportOptions.cs` (or the conventional location for module-level options in this codebase — match existing examples if any).
- Class shape:
  ```csharp
  public class InvoiceImportOptions
  {
      public int MinimumDailyThreshold { get; set; } = 10;
      public int DefaultDaysBack { get; set; } = 14;
  }
  ```
- Property defaults must match the current fallback values passed to `GetValue<int>` (`10` and `14`) so behavior is identical when the section is missing.

**Acceptance criteria:**
- Class exists with both properties and the documented defaults.
- Class is `public` and accessible from the Analytics handler and from `AnalyticsModule`.
- No dependencies on `Microsoft.Extensions.Configuration` are introduced in the class itself.

### FR-2: Register `InvoiceImportOptions` in `AnalyticsModule`
Bind the options class to the existing `InvoiceImport` configuration section in the Analytics module's service-registration entry point.

- Locate `AnalyticsModule.cs` (the module's `AddAnalyticsModule` / equivalent registration method).
- Add: `services.Configure<InvoiceImportOptions>(configuration.GetSection("InvoiceImport"));`
- The configuration section name (`"InvoiceImport"`) must match the key prefix currently used by the handler. Do not rename the section — existing `appsettings.json` / Key Vault entries must continue to bind without changes.

**Acceptance criteria:**
- `AnalyticsModule` registers `InvoiceImportOptions` against the `"InvoiceImport"` section.
- Existing configuration values in `appsettings*.json` and Azure Key Vault continue to bind to the new options without any deployment-side changes.
- If the module registration method does not already accept `IConfiguration`, follow the established pattern used by other modules in this codebase (do not invent a new pattern).

### FR-3: Replace `IConfiguration` with `IOptions<InvoiceImportOptions>` in the handler
Update `GetInvoiceImportStatisticsHandler` to depend on `IOptions<InvoiceImportOptions>` instead of `IConfiguration`.

- File: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`.
- Remove `using Microsoft.Extensions.Configuration;`.
- Remove the `IConfiguration _configuration` field and its constructor parameter.
- Add a constructor parameter `IOptions<InvoiceImportOptions> invoiceImportOptions` and store its `.Value` (or the `IOptions<T>` itself — match the convention used elsewhere in this codebase; storing `.Value` is the simpler default since the options are not expected to change at runtime here).
- Replace the two `GetValue<int>` reads with the corresponding options properties:
  - `_configuration.GetValue<int>("InvoiceImport:MinimumDailyThreshold", 10)` → `_options.MinimumDailyThreshold`
  - `_configuration.GetValue<int>("InvoiceImport:DefaultDaysBack", 14)` → `_options.DefaultDaysBack`

**Acceptance criteria:**
- The handler no longer references `IConfiguration` or `Microsoft.Extensions.Configuration`.
- Handler behavior is identical for every combination of present/missing configuration values that the prior implementation supported (defaults `10` and `14` apply when the section is absent).
- Constructor signature change does not break other call sites (the handler is constructed by DI; no manual callers should exist outside tests).

### FR-4: Update unit tests to use the typed options
Update any existing unit tests for `GetInvoiceImportStatisticsHandler` to pass `IOptions<InvoiceImportOptions>` instead of mocking `IConfiguration`.

- Use `Microsoft.Extensions.Options.Options.Create(new InvoiceImportOptions { ... })` to construct test instances.
- Remove any `IConfiguration` mocks specific to the `"InvoiceImport:*"` keys.
- Add at least one test that asserts the defaults (`10`, `14`) are used when an empty `InvoiceImportOptions` instance is supplied — this protects the default values explicitly.

**Acceptance criteria:**
- All previously passing tests for this handler continue to pass.
- No test for this handler references `IConfiguration` anymore.
- A test exists that verifies default values when the options object is constructed with its parameterless constructor.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. `IOptions<T>` resolution is a singleton DI lookup; switching from `IConfiguration.GetValue<int>` (which parses on every call) to a typed property access is, if anything, a marginal improvement. No benchmarking required.

### NFR-2: Security
No security impact. The values in question are operational thresholds, not secrets. Configuration source (appsettings + Key Vault) is unchanged.

### NFR-3: Backwards compatibility
Zero deployment-side changes required. The configuration section name (`"InvoiceImport"`) and key names (`MinimumDailyThreshold`, `DefaultDaysBack`) are preserved exactly, so existing `appsettings*.json` files and Azure Key Vault secrets continue to bind without modification.

### NFR-4: Architectural conformance
The Application project must not gain (and ideally should lose, if this was its only usage) a direct dependency on `Microsoft.Extensions.Configuration.Abstractions` for this handler. The options class itself depends only on `Microsoft.Extensions.Options` (transitively, via the standard Options pattern).

### NFR-5: Validation
After the change, the following must succeed:
- `dotnet build` — clean build with no new warnings.
- `dotnet format` — no formatting violations.
- The unit-test project containing `GetInvoiceImportStatisticsHandler` tests — all green.

## Data Model
No changes to persistent or domain data models. The only new type is an in-process options POCO:

| Type | Properties | Defaults |
|------|-----------|----------|
| `InvoiceImportOptions` | `int MinimumDailyThreshold`, `int DefaultDaysBack` | `10`, `14` |

Bound from configuration section `InvoiceImport`:
```json
{
  "InvoiceImport": {
    "MinimumDailyThreshold": 10,
    "DefaultDaysBack": 14
  }
}
```

## API / Interface Design
No public API, HTTP endpoint, MediatR contract, or UI surface changes. The change is fully internal to the Analytics module's composition.

- **Constructor change (internal):** `GetInvoiceImportStatisticsHandler(...)` — replaces `IConfiguration` parameter with `IOptions<InvoiceImportOptions>`.
- **DI registration (internal):** `AnalyticsModule` — adds one `services.Configure<InvoiceImportOptions>(...)` call.

The MediatR request/response contract for `GetInvoiceImportStatistics` is unchanged.

## Dependencies
- **`Microsoft.Extensions.Options`** — already a transitive dependency of any ASP.NET Core/.NET 8 app; no new package reference expected. If the Application project does not yet reference it directly, add a `PackageReference` to `Microsoft.Extensions.Options.ConfigurationExtensions` (needed for `services.Configure<T>(IConfiguration)`); this package is also typically already present transitively via `Microsoft.Extensions.Hosting` in the composition root.
- **`AnalyticsModule`** — must already receive `IConfiguration` in its registration method (standard pattern in this codebase). Verify before implementation.

## Out of Scope
- Refactoring any other handlers that may use `IConfiguration` directly. This spec covers **only** `GetInvoiceImportStatisticsHandler`. A broader sweep is a separate effort.
- Changing the configuration source, key names, or default values.
- Adding `IOptionsSnapshot` / `IOptionsMonitor` for runtime reload — `IOptions<T>` (singleton) is sufficient for these settings.
- Adding `DataAnnotations` validation or `ValidateOnStart` for `InvoiceImportOptions` — the existing handler had no validation either, and the two int fields with sensible defaults do not warrant it.
- Documentation/comments on the options class beyond standard XML doc tags (omit unless they describe non-obvious constraints).

## Open Questions
None.

## Status: COMPLETE