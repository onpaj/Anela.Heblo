# Specification: Migrate `ManufactureGroupId` to Typed Options Pattern

## Summary
Replace `IConfiguration`-based access to the `ManufactureGroupId` setting in `GetManufactureSettingsHandler` with a strongly-typed `IOptions<ManufactureErpOptions>` injection. This eliminates the last raw-key configuration access in the Manufacture module, aligning the handler with the surrounding code style and improving testability and type safety.

## Background
`GetManufactureSettingsHandler` currently injects `IConfiguration` and reads the Entra ID group identifier through a raw string constant `ManufactureConfigurationKeys.GroupId = "ManufactureGroupId"`. Every other configuration value in the Manufacture module is consumed through typed options classes (`ManufactureAnalysisOptions`, `ManufactureErpOptions`) registered in `ManufactureModule.AddManufactureModule`. The inconsistency causes three concrete problems:

- **Consistency** — an Application-layer handler reading raw `IConfiguration` is surprising next to handlers that take `IOptions<T>`.
- **Testability** — unit tests must build a full `IConfiguration` mock with the literal string key instead of `new ManufactureErpOptions { ... }`.
- **Type safety** — renaming the string constant silently breaks the binding; nothing in the type system catches it.

The current setting lives as a flat top-level key in `appsettings.json` (line 7) and is overridden in production through an App Service environment variable (`appsettings.Production.json` line 37 notes "Injected via app service env variables"). Migrating into the `ManufactureErp` section changes the configuration key shape and therefore the production environment-variable name, which must be coordinated with the deployment.

## Functional Requirements

### FR-1: Add `ManufactureGroupId` to `ManufactureErpOptions`
Extend the existing `ManufactureErpOptions` class with a nullable string property representing the Entra ID group identifier.

**Acceptance criteria:**
- `ManufactureErpOptions` declares `public string? ManufactureGroupId { get; set; }` (default `null`).
- Existing `ErpTimeoutSeconds` property and its default are preserved unchanged.
- XML doc comment describes the property as the Entra ID group identifier used by `GetManufactureSettings`.
- No new options class is introduced; the existing registration in `ManufactureModule` (binding the `"ManufactureErp"` section) is reused without modification.

### FR-2: Refactor `GetManufactureSettingsHandler` to use `IOptions<ManufactureErpOptions>`
Replace the `IConfiguration` dependency with `IOptions<ManufactureErpOptions>`.

**Acceptance criteria:**
- Constructor accepts `IOptions<ManufactureErpOptions> options` and `ILogger<GetManufactureSettingsHandler> logger`.
- Constructor stores `options.Value` (or the property is read each call) and throws `ArgumentNullException` for null arguments, matching the current guard-clause style.
- `Handle` returns a `GetManufactureSettingsResponse` with `ManufactureGroupId = null` when the configured value is null, empty, or whitespace; otherwise the configured string.
- The existing debug log line `"GetManufactureSettings resolved ManufactureGroupId hasValue={HasValue}"` is preserved.
- The `using Microsoft.Extensions.Configuration;` directive is removed; `Microsoft.Extensions.Options` is added.
- No other handler behavior (request/response shape, MediatR contract) changes.

### FR-3: Remove `ManufactureConfigurationKeys.cs`
After the migration, `ManufactureConfigurationKeys.GroupId` is unused.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs` is deleted.
- A repo-wide search for `ManufactureConfigurationKeys` returns no matches.
- No `using Anela.Heblo.Application.Features.Manufacture;` imports remain solely to reference this class.

### FR-4: Relocate `ManufactureGroupId` into the `ManufactureErp` section in `appsettings.json`
Move the configuration key from a top-level entry to a nested entry under the `ManufactureErp` section.

**Acceptance criteria:**
- In `backend/src/Anela.Heblo.API/appsettings.json`:
  - The top-level `"ManufactureGroupId"` entry (line 7) is removed.
  - The `"ManufactureErp"` section (line 268) gains `"ManufactureGroupId": "your-entra-id-group-id-here"`, preserving the placeholder text.
- In `backend/src/Anela.Heblo.API/appsettings.Production.json`:
  - The top-level `"ManufactureGroupId"` placeholder line is removed (the actual value is injected via env var; see NFR-3).
- No other appsettings keys, ordering elsewhere, or sections are modified.

### FR-5: Update existing tests to use the typed options class
`GetManufactureSettingsHandlerTests` and `GetManufactureSettingsEndpointTests` currently rely on `IConfiguration` with the raw key.

**Acceptance criteria:**
- Unit tests construct the handler with `Options.Create(new ManufactureErpOptions { ManufactureGroupId = "..." })` instead of an `IConfigurationBuilder`/in-memory dictionary keyed on `"ManufactureGroupId"`.
- Endpoint tests that seed configuration switch to seeding the `ManufactureErp:ManufactureGroupId` key (or directly registering `IOptions<ManufactureErpOptions>` in the test host) so the bound options class receives the value.
- Test coverage for the three observable behaviors is preserved:
  - configured non-empty value flows through to `GetManufactureSettingsResponse.ManufactureGroupId`,
  - empty/whitespace value yields `null`,
  - missing configuration yields `null`.
- All affected tests pass under `dotnet test`.

## Non-Functional Requirements

### NFR-1: Behavioral parity
The HTTP response and serialized JSON shape of `GET` `…/manufacture/settings` (or whichever endpoint maps `GetManufactureSettingsRequest`) MUST be byte-identical before and after the change for the three input states (set, empty, missing). No new public API surface is introduced.

### NFR-2: Build and format
`dotnet build` and `dotnet format` MUST pass with no new warnings introduced by the refactor. Nullable annotations on `ManufactureGroupId` MUST be honored (`string?`).

### NFR-3: Production configuration coordination
Because production currently sets the value through an App Service environment variable named `ManufactureGroupId`, the deployment configuration MUST be updated **at the same time as the code change**:

- Add a new env var `ManufactureErp__ManufactureGroupId` (double underscore is the .NET section delimiter) carrying the current value.
- Remove or stop relying on the legacy `ManufactureGroupId` env var.
- The PR description MUST call this out explicitly so the deployer updates Azure Web App settings before the new image is promoted.

### NFR-4: No security regression
No secret values are added to source-controlled `appsettings*.json` beyond the existing placeholder text. The production secret continues to flow from environment variables (or Key Vault, per project rule).

## Data Model
No new domain entities or persisted data. The only data-shape change is in configuration:

| Item | Before | After |
|---|---|---|
| Config key path | `ManufactureGroupId` (root) | `ManufactureErp:ManufactureGroupId` |
| Env var (ASP.NET binding) | `ManufactureGroupId` | `ManufactureErp__ManufactureGroupId` |
| Options class | (none — raw `IConfiguration`) | `ManufactureErpOptions.ManufactureGroupId` (nullable string) |

## API / Interface Design
- **Public HTTP contract**: unchanged. `GetManufactureSettingsResponse.ManufactureGroupId` (`string?`) continues to be the only field returned.
- **Internal handler contract**:
  - Before: `GetManufactureSettingsHandler(IConfiguration, ILogger<…>)`
  - After: `GetManufactureSettingsHandler(IOptions<ManufactureErpOptions>, ILogger<…>)`
- **DI registration**: no change to `ManufactureModule.AddManufactureModule`. `services.Configure<ManufactureErpOptions>(... GetSection("ManufactureErp") ...)` already binds the new property automatically.

## Dependencies
- `Microsoft.Extensions.Options` — already transitively available throughout the Application project; no new package reference needed.
- No changes to MediatR, EF Core, or any external integration.
- Coordinated update of the Azure Web App configuration (env var rename, NFR-3) — owned by the deployment, but the code change is blocked from production rollout until that is sequenced.

## Out of Scope
- Migrating other modules' raw `IConfiguration` reads (if any exist) to typed options.
- Moving `ManufactureGroupId` to Azure Key Vault (if it isn't already). The KV-vs-env-var policy is honored by whatever mechanism currently sets the value; this change only renames the binding key.
- Renaming `ManufactureErpOptions` or its section to better reflect that it now carries non-ERP settings. The brief explicitly accepts Option A (extend the existing class) over introducing a new options class.
- Changes to the request/response DTOs, the MediatR request name, or the HTTP route.
- Frontend changes — the response shape is unchanged.

## Open Questions
None.

## Status: COMPLETE