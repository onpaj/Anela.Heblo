# Architecture Review: Migrate `ManufactureGroupId` to Typed Options Pattern

## Skip Design: true

## Architectural Fit Assessment

This change is a tightly scoped consistency refactor inside the existing Vertical Slice for the Manufacture module. It does **not** introduce new architectural surface — it removes an inconsistency. The proposal aligns with three established conventions in this codebase:

1. **Options pattern is already the standard.** `SubmitManufactureHandler` (`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs:27`) already injects `IOptions<ManufactureErpOptions>` and resolves `.Value` at construction. `ManufactureAnalysisMapper` does the same with `ManufactureAnalysisOptions`. `GetManufactureSettingsHandler` is the lone outlier.
2. **`ManufactureModule.AddManufactureModule` already binds the `ManufactureErp` section** to `ManufactureErpOptions` — no DI changes needed.
3. **The endpoint contract is already documented** in `docs/architecture/development_guidelines.md:15` as the canonical "module-specific bootstrap value via the module's anonymous endpoint" pattern. The HTTP contract is unaffected; only the internal binding mechanism changes.

Integration points: one handler, one options class, one config-keys file (to be deleted), two appsettings files, two test classes, and one Azure App Service environment-variable rename. No domain, persistence, or contract impact.

## Proposed Architecture

### Component Overview

```
appsettings.json ──┐
                   ├── "ManufactureErp" section ──► IOptions<ManufactureErpOptions>
env: ManufactureErp__ManufactureGroupId ──┘                       │
                                                                  ▼
                                                  GetManufactureSettingsHandler
                                                                  │
                                                                  ▼
   ManufactureSettingsController ── MediatR ◄── GetManufactureSettingsResponse
   (GET /api/manufacture/settings, [AllowAnonymous])
```

`ManufactureErpOptions` becomes the single typed surface for both ERP timeouts and the Entra-group bootstrap value. `ManufactureConfigurationKeys` is removed; the type system replaces the string constant.

### Key Design Decisions

#### Decision 1: Extend `ManufactureErpOptions` vs. introduce a new options class
**Options considered:**
- **A.** Add `ManufactureGroupId` to the existing `ManufactureErpOptions` (binds to `"ManufactureErp"` section).
- **B.** Introduce a dedicated `ManufactureSettingsOptions` (or similar) bound to a new section.

**Chosen approach:** Option A.

**Rationale:** Option A is what the brief explicitly proposed and what the spec ratified. It avoids touching `ManufactureModule.AddManufactureModule` (the existing `services.Configure<ManufactureErpOptions>` call already binds the new property). A new section would require a third options class for one nullable string, and would force any future setting onto the same fragmentation problem. The naming awkwardness ("ERP options now holds a non-ERP value") is real but small; renaming the class is explicitly out of scope.

#### Decision 2: Resolve `options.Value` once at construction vs. per-call
**Options considered:**
- **A.** Store `options.Value` in a private field at construction (matches `SubmitManufactureHandler` precedent).
- **B.** Hold `IOptions<ManufactureErpOptions>` and read `.Value` inside `Handle`.

**Chosen approach:** Option A.

**Rationale:** The setting is bound from environment variables at process startup and never reloaded; `IOptionsMonitor` is not in use anywhere in the Manufacture module. Per-call `.Value` resolution buys nothing and diverges from the in-module precedent. Store once, read in `Handle`.

#### Decision 3: Delete `ManufactureConfigurationKeys.cs` immediately vs. leave deprecated
**Chosen approach:** Delete immediately as part of the same PR (FR-3).

**Rationale:** Single-member file with one caller; deletion in the same change keeps the diff atomic and prevents the constant being resurrected. No external consumers (verified via repo grep — only the handler and one test reference it). No deprecation period needed for an internal compile-time symbol.

## Implementation Guidance

### Directory / Module Structure

No new folders. File changes:

| Action | Path |
|---|---|
| **Edit** | `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs` — add `ManufactureGroupId` |
| **Edit** | `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs` — swap `IConfiguration` → `IOptions<ManufactureErpOptions>` |
| **Delete** | `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs` |
| **Edit** | `backend/src/Anela.Heblo.API/appsettings.json` — remove top-level `ManufactureGroupId`, add `ManufactureErp:ManufactureGroupId` placeholder |
| **Edit** | `backend/src/Anela.Heblo.API/appsettings.Production.json` — remove top-level `ManufactureGroupId` placeholder |
| **Edit** | `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs` — use `Options.Create(new ManufactureErpOptions { ... })` |
| **Verify (no edit needed)** | `GetManufactureSettingsEndpointTests` — does NOT seed config; no change required (see Spec Amendment 1) |
| **Verify (no edit needed)** | `ManufactureModule.cs` — already binds `"ManufactureErp"` section; the new property binds automatically |

### Interfaces and Contracts

`ManufactureErpOptions` after the change:

```csharp
public class ManufactureErpOptions
{
    public int ErpTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Entra ID group identifier consumed by GetManufactureSettings to gate
    /// "responsible person" workflows on the frontend.
    /// </summary>
    public string? ManufactureGroupId { get; set; }
}
```

Handler constructor signature after the change:

```csharp
public GetManufactureSettingsHandler(
    IOptions<ManufactureErpOptions> options,
    ILogger<GetManufactureSettingsHandler> logger)
```

Public HTTP contract (`GetManufactureSettingsResponse.ManufactureGroupId : string?`) and MediatR request shape are **unchanged**.

### Data Flow

1. **Startup:** `Program` loads `appsettings.json` → environment overlay → environment variables. `ManufactureErp__ManufactureGroupId` (env) wins over the placeholder in `appsettings.json`. `ManufactureModule.AddManufactureModule` binds the `"ManufactureErp"` section into `IOptions<ManufactureErpOptions>`.
2. **Request:** `GET /api/manufacture/settings` (anonymous) → `ManufactureSettingsController.GetSettings` → `IMediator.Send(new GetManufactureSettingsRequest())` → `GetManufactureSettingsHandler.Handle`.
3. **Handle:** Reads cached `_options.ManufactureGroupId`; collapses null/empty/whitespace to `null`; logs `hasValue`; returns response.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Production env-var rename not coordinated with deploy → endpoint returns `null` post-deploy, frontend silently degrades because the handler swallows missing config | **HIGH** | NFR-3 covers this. Enforce: PR description MUST list `ManufactureErp__ManufactureGroupId` (double underscore) as the new var and call out that legacy `ManufactureGroupId` env var must be set in Azure App Service **before** the new image is promoted. Hold the merge until the deployer confirms. |
| Staging environment has its own App Service env var `ManufactureGroupId` that the spec doesn't mention | **MEDIUM** | Spec NFR-3 only names production. Verify with the deployer whether `kv-heblo-stg` / Staging Web App also sets this var; rename in lock-step. See Spec Amendment 2. |
| `IConfiguration.GetValue<string>("ManufactureErp:ManufactureGroupId")` whitespace handling differs from `IOptions` binding (binder trims nothing; raw `IConfiguration` indexer returns string as-is — both behave the same here) | **LOW** | The handler's `string.IsNullOrEmpty` collapses `null` and `""` identically; the spec already mandates `IsNullOrWhiteSpace` semantics — adopt that and the behavior is at-or-better than today. |
| Other code paths read the legacy top-level `"ManufactureGroupId"` key | **LOW** | Verified: repo-wide grep shows only the handler and tests reference the key/constant. No frontend, no other backend reader. |
| Test loses fidelity (FR-5 endpoint test) because `GetManufactureSettingsEndpointTests` doesn't actually seed config today | **LOW** | See Spec Amendment 1 — the FR-5 bullet on endpoint tests is over-specified; nothing in that file needs changing. |

## Specification Amendments

1. **FR-5 second bullet is unnecessary.** `GetManufactureSettingsEndpointTests` does not seed the configuration at all — it only asserts the endpoint is reachable, returns `application/json`, exposes the `ManufactureGroupId` property, and is anonymously accessible. No `IConfigurationBuilder` or in-memory dictionary appears in that test. **Amendment:** Drop the requirement to change endpoint-test config seeding. Leave that file untouched. The behavioral coverage for set/empty/missing remains in the unit tests, which is the correct level.

2. **NFR-3 should also cover Staging.** Production is named explicitly but `appsettings.Staging.json` exists and the project has a `kv-heblo-stg` Key Vault. If Staging also pulls `ManufactureGroupId` from an env var or KV secret, the rename must happen in both environments. **Amendment:** Expand NFR-3 to "Production **and any non-Production environment that overrides this value** (notably Staging)." The PR description must enumerate every affected environment, not just production.

3. **Clarify handler null/empty/whitespace semantics.** The spec's FR-2 acceptance criterion says "`null` when configured value is null, empty, or whitespace". The current handler uses `string.IsNullOrEmpty` (not `IsNullOrWhiteSpace`). **Amendment:** Explicitly call for `string.IsNullOrWhiteSpace` in the new code, and add a unit-test case for `"   "` to lock the behavior in. This is a tiny behavioral tightening and is safer than carrying over `IsNullOrEmpty`.

4. **Recommend relocating `ManufactureConfigurationKeys.cs` deletion to also remove the now-unused `using Anela.Heblo.Application.Features.Manufacture;` import in the handler.** Already covered indirectly by FR-3's third bullet; just ensure the developer doesn't leave an orphan `using`.

## Prerequisites

Before merge:

1. **Azure App Service env-var added** in Production (and Staging if applicable): `ManufactureErp__ManufactureGroupId` set to the current value of `ManufactureGroupId`. **Both** vars should coexist briefly so a rollback to the previous image still works; remove the legacy var only after the new image is healthy.
2. **Deployer sign-off captured in the PR description** confirming step 1 is done and naming the environments touched.
3. **`dotnet build` + `dotnet format` + `dotnet test`** green locally before push (per `CLAUDE.md` validation gates).
4. **No code prerequisites** — `IOptions<>` and `Microsoft.Extensions.Options` are already transitively available throughout `Anela.Heblo.Application`.