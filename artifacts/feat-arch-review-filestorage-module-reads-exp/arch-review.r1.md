I have enough context to write the architecture review.

```markdown
# Architecture Review: Decouple FileStorage Module from ExpeditionList Configuration

## Skip Design: true

This is a backend-only configuration and DI-wiring change with no UI surface.

## Architectural Fit Assessment

The proposal is the right fix and aligns cleanly with established patterns in this codebase. The current `FileStorageModule.cs:20` violation is unambiguous: it reaches into `ExpeditionList:` configuration, which `PrintPickingListOptions.cs:5` declares as ExpeditionList's owned section (`ConfigurationKey = "ExpeditionList"`). The fix mirrors how every other module in the solution wires options:

- `MeetingTasksModule.cs:18-21`, `InvoicesModule.cs:27-30`, `OrgChartModule.cs`, `LeafletModule.cs`, `KnowledgeBaseModule.cs`, `ArticleModule.cs`, `CatalogDocumentsModule.cs`, `MarketingModule.cs`, `ShoptetPayAdapterServiceCollectionExtensions.cs`, `HebloOpenMeteoAdapterModule.cs` all use the exact `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()` pattern the spec proposes.
- `ProductExportOptions.cs` already lives at `Anela.Heblo.Application/Features/FileStorage/` and is consumed by the FileStorage module — this is the conventional location for the new `FileStorageOptions`, **not** `Domain/Features/FileStorage/` as the spec tentatively suggests. Options are application-layer concerns; the Domain layer here holds only `IBlobStorageService` and `BlobItemInfo` value types.

Integration points (verified):
- **Single consumer of the legacy key from FileStorage:** only `FileStorageModule.cs:20`. Confirmed by grep — no other code in `FileStorage` reads `ExpeditionList:`.
- **ExpeditionList still consumes its own key:** `AzureAdapterModule.cs:18-22` binds `IOptions<PrintPickingListOptions>` and reads both `BlobConnectionString` and `BlobContainerName`. Untouched by this change.
- **One test references the legacy key for ExpeditionList's purposes:** `CombinedPrintQueueSinkRegistrationTests.cs:27-28` seeds `ExpeditionList:BlobConnectionString` to exercise `AzureBlobPrintQueueSink`. This test is **out of scope** and must not be modified.

## Proposed Architecture

### Component Overview

```
                       ┌─────────────────────────────────────┐
appsettings.json ──┐   │ FileStorage module (Application)    │
Key Vault         ─┼──►│  ┌──────────────────────────────┐   │
Env vars          ─┘   │  │ FileStorageOptions           │   │
                       │  │   .BlobConnectionString      │   │
                       │  └──────────────┬───────────────┘   │
                       │                 │ IOptions<T>       │
                       │                 ▼                   │
                       │  ┌──────────────────────────────┐   │
                       │  │ BlobServiceClient factory    │   │
                       │  │ (Singleton, env-aware)       │   │
                       │  └──────────────┬───────────────┘   │
                       │                 │                   │
                       │                 ▼                   │
                       │  ┌──────────────────────────────┐   │
                       │  │ AzureBlobStorageService      │   │
                       │  │   : IBlobStorageService      │   │
                       │  └──────────────────────────────┘   │
                       └─────────────────────────────────────┘

ExpeditionList module remains untouched:
  PrintPickingListOptions ── IOptions ──► BlobContainerClient
  (ExpeditionList:BlobConnectionString)    (AzureAdapterModule.cs)
```

### Key Design Decisions

#### Decision 1: Where `FileStorageOptions` lives

**Options considered:**
- A) `Anela.Heblo.Domain/Features/FileStorage/FileStorageOptions.cs`
- B) `Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs`

**Chosen approach:** B — `Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs`.

**Rationale:** Options classes are an application/composition-layer concern. Every comparable file in the codebase confirms this — `ProductExportOptions.cs` (the existing sibling in this very module), `MeetingTasksOptions.cs`, `InvoicesProductMappingOptions.cs`, `ArticleOptions.cs`, etc. all live under `Application/Features/{Module}/`. The Domain project deliberately holds only the `IBlobStorageService` contract and the `BlobItemInfo` value type. Placing options in Domain would invert the layering by making the Domain layer aware of `Microsoft.Extensions.Options` infrastructure for no benefit.

#### Decision 2: How to satisfy "fail fast in non-Development, soft fallback in Development"

**Options considered:**
- A) A single `.Validate(o => !string.IsNullOrWhiteSpace(o.BlobConnectionString), "...")` on the options builder, applied universally. (Simplest.)
- B) Branch the validation registration on `IHostEnvironment` at module-registration time: only call `.Validate(...)` when `!env.IsDevelopment()`. In Development, register a factory that falls back to `UseDevelopmentStorage=true` when the key is empty.
- C) An `IValidateOptions<FileStorageOptions>` implementation that takes `IHostEnvironment` via DI and applies the rule.

**Chosen approach:** B.

**Rationale:** The spec (FR-4) explicitly requires both behaviors: hard fail in non-Development, opt-in soft fallback in Development. (A) cannot express the soft-fallback path and would break Development inner-loop work. (C) works but is more code than needed — `IValidateOptions<T>` shines when the rule itself depends on runtime state, not when the decision is "should this rule even apply?", which is known at startup. (B) keeps everything in one place (`FileStorageModule.AddFileStorageModule`), mirrors what `MeetingTasksModule.cs:23-40` already does (it branches DI registration on `useMockAuth`/`bypassJwt` configuration values), and produces dead-simple, debuggable wiring.

The signature must change: `AddFileStorageModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)`. The single caller — `ApplicationModule.cs:83` — already has access to `IHostEnvironment` via `WebApplicationBuilder` / the host. Verify and propagate as the first implementation step.

#### Decision 3: `BlobServiceClient` registration shape

**Options considered:**
- A) Read the connection string at `AddFileStorageModule` time (current shape) and capture it in a factory closure.
- B) Resolve the connection string inside the `BlobServiceClient` factory via `IOptions<FileStorageOptions>`.

**Chosen approach:** B.

**Rationale:** (A) defeats `ValidateOnStart()` — the closure would already have captured a possibly-empty string by the time validation runs at `IHostedService` start. (B) lets the options pipeline (including `ValidateOnStart`) run first; the factory then reads the already-validated value. It also matches `AzureAdapterModule.cs:18-22`'s shape exactly — the established convention.

```csharp
services.AddSingleton<BlobServiceClient>(provider =>
{
    var opts = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
    var connectionString = string.IsNullOrWhiteSpace(opts.BlobConnectionString)
        ? "UseDevelopmentStorage=true"   // Reachable only in Development; validation blocks elsewhere.
        : opts.BlobConnectionString;
    return new BlobServiceClient(connectionString);
});
```

#### Decision 4: No fallback to the legacy key in the new code path

**Chosen approach:** Match the spec's NFR-3 "hard cutover, secret first" stance.

**Rationale:** A `?? configuration["ExpeditionList:BlobConnectionString"]` shim would carry the exact coupling this work removes into the new code and leave a deletion task for later. Provisioning the Key Vault secret before the merge is cheap; carrying a fallback forever is not.

## Implementation Guidance

### Directory / Module Structure

**New file:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs`

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — replace lines 20–29 with options binding + env-aware factory; add `IHostEnvironment` parameter.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs:83` — propagate `IHostEnvironment` to the `AddFileStorageModule` call.
- `backend/src/Anela.Heblo.API/appsettings.json` — add top-level `"FileStorage": { "BlobConnectionString": "..." }` section. **Do not remove** the existing `ExpeditionList.BlobConnectionString` (lines 526–536) — it stays in active use.
- `backend/src/Anela.Heblo.API/appsettings.Development.json`, `appsettings.Staging.json`, `appsettings.Production.json` — add the new section as appropriate. For Production/Staging, the value is supplied via Key Vault overlay (already standard); the appsettings entry should be empty/placeholder, consistent with how other secret-backed sections are handled.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — see the **Risk** section below; tests currently call `AddFileStorageModule(BuildConfiguration())` with an empty configuration and will break under the new validation. They need to either (a) provide a Development `IHostEnvironment` and seed `UseDevelopmentStorage=true` explicitly, or (b) seed a fake `FileStorage:BlobConnectionString` and a non-Development environment. Pick (a) to keep the tests focused on registration semantics, not on validation behavior — then add at least one new test that exercises FR-4 (fail-fast in non-Development with missing key).
- `docs/architecture/environments.md` — add a section listing module-owned Key Vault secrets including `FileStorage--BlobConnectionString` (staging `kv-heblo-stg`, plus production vault to be named in the PR).

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FileStorage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    // Not [Required] — Development is allowed to leave it empty and fall back to the
    // storage emulator. FileStorageModule applies a stricter Validate() rule in
    // non-Development environments.
    public string BlobConnectionString { get; set; } = string.Empty;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs
public static IServiceCollection AddFileStorageModule(
    this IServiceCollection services,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    var optionsBuilder = services
        .AddOptions<FileStorageOptions>()
        .Bind(configuration.GetSection(FileStorageOptions.SectionName));

    if (!environment.IsDevelopment())
    {
        optionsBuilder
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.BlobConnectionString),
                $"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.BlobConnectionString)} must be configured.")
            .ValidateOnStart();
    }

    services.AddSingleton<BlobServiceClient>(provider =>
    {
        var opts = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
        if (string.IsNullOrWhiteSpace(opts.BlobConnectionString))
        {
            // Reachable only in Development (validation blocks elsewhere). Log a warning
            // so the fallback is never silent.
            var logger = provider.GetRequiredService<ILogger<AzureBlobStorageService>>();
            logger.LogWarning(
                "FileStorage:BlobConnectionString is empty in {Environment}; falling back to UseDevelopmentStorage=true.",
                environment.EnvironmentName);
            return new BlobServiceClient("UseDevelopmentStorage=true");
        }
        return new BlobServiceClient(opts.BlobConnectionString);
    });

    // ... existing HttpClient and service registrations unchanged
    return services;
}
```

### Data Flow

1. App startup → Generic Host reads `appsettings.json` + environment-specific + Key Vault overlay (handled by existing `KeyVault:Uri` wiring) into `IConfiguration`.
2. `ApplicationModule.AddApplicationModule` calls `AddFileStorageModule(configuration, environment)`.
3. `AddFileStorageModule` binds `FileStorage:BlobConnectionString` to `FileStorageOptions`. In non-Development environments, registers `ValidateOnStart()`.
4. Host start → `OptionsValidationFailureHostedService` runs; if the connection string is empty in Staging/Production, the host throws an `OptionsValidationException` naming the missing key and the app exits.
5. First resolution of `BlobServiceClient` → factory reads `IOptions<FileStorageOptions>.Value` (already validated) and constructs the client.
6. `AzureBlobStorageService` (Singleton) receives the `BlobServiceClient` via constructor injection. No code changes here.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing `FileStorageModuleTests` calls `AddFileStorageModule(BuildConfiguration())` with an empty configuration. With env-aware validation added, the BuildServiceProvider/GetRequiredService path may throw on `IHostEnvironment` resolution or `OptionsValidationException`. | High | Add an explicit `IHostEnvironment` fake (`Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development)`) and an in-memory `FileStorage:BlobConnectionString` seed where the test cares about the client. Add one new test that asserts the host fails to start in `Production` when the key is missing (covers NFR-4). |
| Module signature changes from `(IServiceCollection, IConfiguration)` to `(IServiceCollection, IConfiguration, IHostEnvironment)`, which is a breaking change for any other caller. | Low | Verified: the only caller is `ApplicationModule.cs:83`. Tests aside, no public consumer exists. Adjust the caller in the same PR. |
| Key Vault secret missing in a target environment at deploy time → app fails to start (intended fail-fast behavior, but visible as a deploy outage). | Medium | The spec already mandates "secret first" — Key Vault secret provisioned in **all** environments **before** merge. PR description must list the exact secret and vault names per the spec's NFR-3 acceptance. Recommend running an Azure CLI `az keyvault secret show` check pre-merge. |
| Production vault name is not yet confirmed in the spec ("likely `kv-heblo-prod`"). | Medium | Acceptance criterion: the deploying engineer confirms and records the production vault name in the PR description before merge. Until confirmed, do not merge. |
| `UseDevelopmentStorage=true` warning becomes log noise in Development inner-loop. | Low | Log at `LogWarning` once per registration (it runs once because `BlobServiceClient` is a Singleton). Acceptable as-is. |
| Adding `ValidateOnStart()` triggers validation in unrelated host scenarios (e.g., EF migration tools, design-time DbContext factories). | Low | These run with `ASPNETCORE_ENVIRONMENT=Development` by default; the validation rule is skipped. If a tool intentionally runs in `Production`, the missing secret must be supplied — which is correct behavior. |

## Specification Amendments

1. **Move `FileStorageOptions` location.** The spec's "API / Interface Design" section suggests `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageOptions.cs`. Change this to `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs` to match the existing convention (`ProductExportOptions.cs` already sits there, and the Domain layer is options-free by design). Update FR-3 accordingly: "A new `FileStorageOptions` class lives at `Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs`."

2. **Adjust the FR-4 validation snippet.** The spec's sample code uses a single unconditional `.Validate(...)`. This breaks the spec's own "Development may default to `UseDevelopmentStorage=true`" requirement. Replace with the environment-branched registration shown in Decision 2 / the FileStorageModule code sample above. The acceptance criterion stays the same — only the implementation snippet changes.

3. **Make the `AddFileStorageModule` signature change explicit.** Add to FR-2: "`AddFileStorageModule` accepts an additional `IHostEnvironment environment` parameter so it can branch validation. The single caller (`ApplicationModule.AddApplicationModule`) propagates the host environment from its existing call chain."

4. **Add test acceptance to NFR-4.** The existing `FileStorageModuleTests` use `BuildConfiguration()` returning an empty `IConfiguration`. Adopting `ValidateOnStart()` requires those tests to be updated to inject an `IHostEnvironment` fake. Add an explicit line: "Existing `FileStorageModuleTests` are updated to supply a Development `IHostEnvironment` and a seeded `FileStorage:BlobConnectionString` (or `UseDevelopmentStorage=true`) such that registration semantics are unaffected."

5. **Clarify the FR-1 appsettings scope.** The repo also ships `appsettings.Test.json` and `appsettings.Conductor.json` under `Anela.Heblo.API/`. Decide explicitly which of these need the new key: Test runs in `Production-ish` config (so it needs the key or must run as Development); Conductor is a local-dev variant (Development-style, key optional). Recommendation: add to `appsettings.json` only, supply per environment via Key Vault or environment variables, and **add to `appsettings.Test.json` if Test runs with `ASPNETCORE_ENVIRONMENT=Test`** (must be confirmed before implementation).

## Prerequisites

1. **Confirm the production Key Vault name.** Spec says "likely `kv-heblo-prod`." Verify with `az keyvault list` or with the deploying engineer **before** opening the PR.
2. **Provision `FileStorage--BlobConnectionString` in both vaults** (`kv-heblo-stg` and the confirmed production vault) **before** merging, using the value currently in production for `ExpeditionList--BlobConnectionString`. This is non-negotiable per NFR-3 "secret first."
3. **Confirm the `ASPNETCORE_ENVIRONMENT` value used by the Test environment** (`appsettings.Test.json`). If Test runs with a non-Development environment, the secret must also be provisioned wherever Test pulls config from — otherwise the Test environment will fail to start after this change is deployed.
4. **Verify `IHostEnvironment` is available at the `ApplicationModule.AddApplicationModule` call site.** Confirmed pattern exists (other modules accept `IConfiguration` only because they don't need env awareness); propagation needs a small signature change in `ApplicationModule`.
5. **No new NuGet packages needed.** `Microsoft.Extensions.Options.ConfigurationExtensions` and `Microsoft.Extensions.Hosting.Abstractions` are already in the dependency graph (used by every other module that uses this pattern).
```