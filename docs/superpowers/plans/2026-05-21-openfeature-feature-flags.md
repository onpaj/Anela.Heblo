# OpenFeature Feature Flags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the unused `CatalogFeatureFlags` POCO pattern with a unified feature-flag system: `Microsoft.FeatureManagement` for config-based evaluation + a DB override layer surfaced via an OpenFeature provider, with a `FeatureFlagsController` for admin CRUD and a `useFeatureFlag` hook on the React frontend.

**Architecture:** `appsettings.json` holds per-flag defaults under `FeatureManagement:`; a `FeatureFlagOverride` Postgres table stores runtime overrides; `HebloFeatureProvider` (OpenFeature) resolves DB override → config → registry default in that order; `IFeatureFlagChecker` is the only interface business code touches; `[FeatureGate]` on controllers uses `IFeatureManager` directly (config-only, accepted v1 limitation). React hydrates an `InMemoryProvider` from `GET /api/feature-flags` on boot.

**Tech Stack:** `Microsoft.FeatureManagement.AspNetCore` 3.x, `OpenFeature` 2.x (BE), `@openfeature/web-sdk` + `@openfeature/react-sdk` (FE), EF Core (existing Postgres), TanStack Query, xUnit + FluentAssertions.

---

## File Map

**Create (backend):**
- `backend/src/Anela.Heblo.Domain/Features/FeatureFlags/FeatureFlagOverride.cs` — EF entity
- `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideConfiguration.cs` — EF fluent config
- `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideRepository.cs` — EF repo implementation
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs` — string constants
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagDefinition.cs` — record
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs` — static registry
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagChecker.cs` — interface
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs` — repo interface
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — OpenFeature provider
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/FeatureFlagChecker.cs` — IFeatureFlagChecker impl
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Contracts/FlagStatusDto.cs` — admin DTO
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Contracts/UpsertFlagOverrideBodyDto.cs` — request body DTO
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/EvaluateFlagsForClient/` — public read use case
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ListFlags/` — admin list use case
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/` — admin upsert use case
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/` — admin delete use case
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagsModule.cs` — DI wiring
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — controller
- EF migration (auto-generated name)
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/README.md` — short pointer doc

**Create (frontend):**
- `frontend/src/features/feature-flags/featureFlags.ts` — FE key constants
- `frontend/src/features/feature-flags/FeatureFlagProvider.tsx` — OpenFeature InMemoryProvider context
- `frontend/src/hooks/useFeatureFlag.ts` — thin hook over OpenFeature
- `frontend/src/pages/FeatureFlagsAdminPage.tsx` — admin CRUD page
- `frontend/src/api/hooks/useFeatureFlagsAdmin.ts` — admin API hooks

**Modify (backend):**
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — add NuGet packages
- `backend/src/Anela.Heblo.API/appsettings.json` — add `FeatureManagement:` section
- `backend/src/Anela.Heblo.API/Program.cs` — call `InitializeFeatureFlagsAsync`
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add `FeatureFlagOverrides` DbSet
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `AddFeatureFlagsModule`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — remove `Configure<CatalogFeatureFlags>` block
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogFeatureFlags.cs` — delete
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogFeatureFlagsTests.cs` — replace with new FeatureFlagsTests

**Modify (frontend):**
- `frontend/src/App.tsx` — wrap with `<FeatureFlagProvider>` + add admin route
- `frontend/src/api/client.ts` — add `featureFlags` query key

**Create (docs):**
- `docs/development/feature-flags.md` — primary usage guide

**Modify (docs):**
- `CLAUDE.md` — add `docs/development/feature-flags.md` entry
- `docs/architecture/development_guidelines.md` — add Feature Flags subsection

---

## Task 1: NuGet and npm Packages

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

- [ ] **Step 1: Add NuGet packages**

```bash
cd backend/src/Anela.Heblo.API && dotnet add package Microsoft.FeatureManagement.AspNetCore && dotnet add package OpenFeature
```

Expected output: both packages added to `Anela.Heblo.API.csproj`.

- [ ] **Step 2: Add npm packages**

```bash
cd frontend && npm install @openfeature/web-sdk @openfeature/react-sdk
```

Expected output: packages appear in `package.json` dependencies.

- [ ] **Step 3: Verify build is clean**

```bash
cd backend && dotnet build && cd ../frontend && npm run build 2>&1 | tail -5
```

Expected: both succeed.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj frontend/package.json frontend/package-lock.json
git commit -m "chore: add Microsoft.FeatureManagement.AspNetCore, OpenFeature, and openfeature npm packages"
```

---

## Task 2: Domain Entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/FeatureFlags/FeatureFlagOverride.cs`

- [ ] **Step 1: Create the domain entity**

Create `backend/src/Anela.Heblo.Domain/Features/FeatureFlags/FeatureFlagOverride.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.FeatureFlags;

public class FeatureFlagOverride
{
    public string Key { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
```

- [ ] **Step 2: Verify build**

```bash
cd backend && dotnet build 2>&1 | grep -E "error|warning" | grep -v "warning CS"
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/FeatureFlags/FeatureFlagOverride.cs
git commit -m "feat(feature-flags): add FeatureFlagOverride domain entity"
```

---

## Task 3: EF Persistence — Config, DbSet, Repository, Migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create EF entity configuration**

Create `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.FeatureFlags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.FeatureFlags;

public sealed class FeatureFlagOverrideConfiguration : IEntityTypeConfiguration<FeatureFlagOverride>
{
    public void Configure(EntityTypeBuilder<FeatureFlagOverride> builder)
    {
        builder.ToTable("FeatureFlagOverrides", "public");
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasMaxLength(100);
        builder.Property(e => e.UpdatedBy).HasMaxLength(200);
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
    }
}
```

- [ ] **Step 2: Add DbSet to ApplicationDbContext**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, add after the last DbSet (before the `protected override void OnModelCreating` line, around line 145):

```csharp
    // Feature Flags module
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides { get; set; } = null!;
```

Add the using at the top of the file:
```csharp
using Anela.Heblo.Domain.Features.FeatureFlags;
```

- [ ] **Step 3: Run migration**

```bash
cd backend && dotnet ef migrations add AddFeatureFlagOverridesTable \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: migration file created under `Migrations/`.

- [ ] **Step 4: Verify migration looks correct**

Open the generated migration. It should have an `Up` method creating `FeatureFlagOverrides` table with columns: `Key` (varchar 100, PK), `IsEnabled` (bool), `UpdatedAt` (timestamp), `UpdatedBy` (varchar 200).

- [ ] **Step 5: Create the repository interface**

This interface will live in the Application layer (Task 4). Skip to next step — come back here after Task 4, Step 2.

- [ ] **Step 6: Create the repository implementation**

Create `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideRepository.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.FeatureFlags;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.FeatureFlags;

internal sealed class FeatureFlagOverrideRepository : IFeatureFlagOverrideRepository
{
    private readonly ApplicationDbContext _ctx;

    public FeatureFlagOverrideRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<Dictionary<string, bool>> GetAllAsDictionaryAsync(CancellationToken ct = default)
        => await _ctx.FeatureFlagOverrides
            .AsNoTracking()
            .ToDictionaryAsync(e => e.Key, e => e.IsEnabled, ct);

    public async Task<bool?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        var entity = await _ctx.FeatureFlagOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, ct);
        return entity?.IsEnabled;
    }

    public async Task<IReadOnlyList<FeatureFlagOverride>> GetAllAsync(CancellationToken ct = default)
        => await _ctx.FeatureFlagOverrides.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(string key, bool isEnabled, string updatedBy, CancellationToken ct = default)
    {
        var existing = await _ctx.FeatureFlagOverrides.FindAsync([key], ct);
        if (existing is not null)
        {
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }
        else
        {
            _ctx.FeatureFlagOverrides.Add(new FeatureFlagOverride
            {
                Key = key,
                IsEnabled = isEnabled,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy,
            });
        }
        await _ctx.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var existing = await _ctx.FeatureFlagOverrides.FindAsync([key], ct);
        if (existing is null) return false;
        _ctx.FeatureFlagOverrides.Remove(existing);
        await _ctx.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 7: Verify build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error"
```

Expected: no errors (repository won't compile until interface is created in Task 4).

---

## Task 4: Application Slice — Registry, Interface, Module Scaffold

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagDefinition.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagChecker.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs`

- [ ] **Step 1: Create FeatureFlagKeys**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// String constants for all known feature flags.
/// Always use these constants — never hard-code flag key strings.
/// See docs/development/feature-flags.md.
/// </summary>
public static class FeatureFlagKeys
{
    /// <summary>Controls whether transport box tracking is enabled. Default: false.</summary>
    public const string TransportBoxTracking = "is-transport-box-tracking-enabled";

    /// <summary>Controls whether stock taking submission UI and processing is enabled. Default: false.</summary>
    public const string StockTaking = "is-stock-taking-enabled";

    /// <summary>Controls whether background data refresh is enabled. Disable in test environments. Default: true.</summary>
    public const string BackgroundRefresh = "is-background-refresh-enabled";
}
```

- [ ] **Step 2: Create FeatureFlagDefinition**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagDefinition.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FeatureFlags;

public record FeatureFlagDefinition(string Key, string Description, bool DefaultValue);
```

- [ ] **Step 3: Create FeatureFlagRegistry**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// Single source of truth for all feature flags in the system.
/// To add a new flag: (1) add a constant to FeatureFlagKeys, (2) add an entry here,
/// (3) add the same default to appsettings.json under "FeatureManagement:".
/// See docs/development/feature-flags.md.
/// </summary>
public static class FeatureFlagRegistry
{
    public static readonly IReadOnlyList<FeatureFlagDefinition> All =
    [
        new(FeatureFlagKeys.TransportBoxTracking,
            Description: "Controls whether transport box tracking is enabled.",
            DefaultValue: false),
        new(FeatureFlagKeys.StockTaking,
            Description: "Controls whether stock taking submission UI and processing is enabled.",
            DefaultValue: false),
        new(FeatureFlagKeys.BackgroundRefresh,
            Description: "Controls whether background data refresh is enabled. Disable in test environments.",
            DefaultValue: true),
    ];
}
```

- [ ] **Step 4: Create IFeatureFlagChecker**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagChecker.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// Evaluates feature flags. Inject this in business code; never call OpenFeature SDK directly.
/// Always use FeatureFlagKeys constants for key names.
/// Evaluation order: DB override → FeatureManagement config → registry DefaultValue.
/// See docs/development/feature-flags.md.
/// </summary>
public interface IFeatureFlagChecker
{
    /// <summary>Returns the flag value, falling back to the registry default.</summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);

    /// <summary>Returns the flag value, falling back to the supplied default.</summary>
    Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create IFeatureFlagOverrideRepository**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.FeatureFlags;

namespace Anela.Heblo.Application.Features.FeatureFlags;

public interface IFeatureFlagOverrideRepository
{
    Task<Dictionary<string, bool>> GetAllAsDictionaryAsync(CancellationToken ct = default);
    Task<bool?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlagOverride>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(string key, bool isEnabled, string updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}
```

- [ ] **Step 6: Verify build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error"
```

Expected: no errors (repository implementation from Task 3 should now compile too).

- [ ] **Step 7: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Domain/Features/FeatureFlags/ \
  backend/src/Anela.Heblo.Persistence/FeatureFlags/ \
  backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
  backend/src/Anela.Heblo.Persistence/Migrations/ \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagDefinition.cs \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagChecker.cs \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs
git commit -m "feat(feature-flags): add domain entity, EF persistence, registry, and checker interface"
```

---

## Task 5: OpenFeature Provider + IFeatureFlagChecker Implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/FeatureFlagChecker.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagsModule.cs`

- [ ] **Step 1: Create HebloFeatureProvider**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using OpenFeature.Model;

namespace Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;

/// <summary>
/// OpenFeature provider: resolves flags in order — DB override → appsettings.json → registry default.
/// Registered as singleton; uses IServiceScopeFactory for scoped DB access.
/// Cache TTL: 30 seconds. Invalidated immediately on admin writes.
/// </summary>
internal sealed class HebloFeatureProvider : FeatureProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    internal const string CacheKey = "feature_flag_overrides";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public HebloFeatureProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _configuration = configuration;
    }

    public override Metadata GetMetadata() => new("Heblo.FeatureManagement");

    public override async Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey,
        bool defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var overrides = await GetOverridesAsync(cancellationToken);
            if (overrides.TryGetValue(flagKey, out var dbValue))
                return new ResolutionDetails<bool>(flagKey, dbValue, reason: Reason.TargetingMatch);

            var configSection = _configuration.GetSection($"FeatureManagement:{flagKey}");
            if (configSection.Exists() && bool.TryParse(configSection.Value, out var configValue))
                return new ResolutionDetails<bool>(flagKey, configValue, reason: Reason.Static);

            return new ResolutionDetails<bool>(flagKey, defaultValue, reason: Reason.Default);
        }
        catch (Exception ex)
        {
            return new ResolutionDetails<bool>(
                flagKey,
                defaultValue,
                errorType: ErrorType.General,
                errorMessage: ex.Message,
                reason: Reason.Error);
        }
    }

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey, string defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("String flags not supported in v1.");

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey, int defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Integer flags not supported in v1.");

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey, double defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Double flags not supported in v1.");

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey, Value defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Structure flags not supported in v1.");

    private async Task<Dictionary<string, bool>> GetOverridesAsync(CancellationToken ct)
        => await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFeatureFlagOverrideRepository>();
            return await repo.GetAllAsDictionaryAsync(ct);
        }) ?? [];
}
```

- [ ] **Step 2: Create FeatureFlagChecker**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/FeatureFlagChecker.cs`:

```csharp
using OpenFeature;

namespace Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;

internal sealed class FeatureFlagChecker : IFeatureFlagChecker
{
    private readonly IFeatureClient _client;

    public FeatureFlagChecker(IFeatureClient client) => _client = client;

    public Task<bool> IsEnabledAsync(string key, CancellationToken ct = default)
    {
        var def = FeatureFlagRegistry.All.FirstOrDefault(d => d.Key == key);
        return _client.GetBooleanValueAsync(key, def?.DefaultValue ?? false, cancellationToken: ct);
    }

    public Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken ct = default)
        => _client.GetBooleanValueAsync(key, defaultValue, cancellationToken: ct);
}
```

- [ ] **Step 3: Create FeatureFlagsModule**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagsModule.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Persistence.FeatureFlags;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;

namespace Anela.Heblo.Application.Features.FeatureFlags;

public static class FeatureFlagsModule
{
    public static IServiceCollection AddFeatureFlagsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddFeatureManagement(configuration.GetSection("FeatureManagement"));
        services.AddScoped<IFeatureFlagOverrideRepository, FeatureFlagOverrideRepository>();
        services.AddSingleton<HebloFeatureProvider>();
        services.AddScoped<IFeatureClient>(_ => Api.Instance.GetClient());
        services.AddScoped<IFeatureFlagChecker, FeatureFlagChecker>();
        return services;
    }

    /// <summary>
    /// Must be called after app.Build() to initialize the OpenFeature global provider.
    /// </summary>
    public static async Task InitializeFeatureFlagsAsync(this WebApplication app)
    {
        var provider = app.Services.GetRequiredService<HebloFeatureProvider>();
        await Api.Instance.SetProviderAsync(provider);
    }
}
```

- [ ] **Step 4: Register module in ApplicationModule.cs**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, add the using and the call:

Add using at the top:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
```

Add the call just before `return services;` at the end of `AddApplicationServices`:
```csharp
        services.AddFeatureFlagsModule(configuration);
```

- [ ] **Step 5: Call InitializeFeatureFlagsAsync in Program.cs**

In `backend/src/Anela.Heblo.API/Program.cs`, after `var app = builder.Build();` (line 125), add:

```csharp
        await app.InitializeFeatureFlagsAsync();
```

Also add the using at the top:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
```

- [ ] **Step 6: Add FeatureManagement section to appsettings.json**

In `backend/src/Anela.Heblo.API/appsettings.json`, add this section (place it near the top, after the opening `{`):

```json
  "FeatureManagement": {
    "is-transport-box-tracking-enabled": false,
    "is-stock-taking-enabled": false,
    "is-background-refresh-enabled": true
  },
```

- [ ] **Step 7: Verify build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error"
```

Expected: no errors. Note: `FeatureFlagsModule` references `FeatureFlagOverrideRepository` from Persistence — add a project reference if missing:

```bash
cd backend/src/Anela.Heblo.Application && dotnet add reference ../Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

(If the reference already exists, skip.)

- [ ] **Step 8: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/ \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagsModule.cs \
  backend/src/Anela.Heblo.Application/ApplicationModule.cs \
  backend/src/Anela.Heblo.API/Program.cs \
  backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(feature-flags): add OpenFeature provider, checker implementation, and DI wiring"
```

---

## Task 6: Use Cases

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Contracts/FlagStatusDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Contracts/UpsertFlagOverrideBodyDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/EvaluateFlagsForClient/`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ListFlags/`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/`

- [ ] **Step 1: Create DTOs**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Contracts/FlagStatusDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FeatureFlags.Contracts;

public class FlagStatusDto
{
    public string Key { get; set; } = "";
    public string Description { get; set; } = "";
    public bool CurrentValue { get; set; }
    public bool IsOverridden { get; set; }
    public bool DefaultValue { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Contracts/UpsertFlagOverrideBodyDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FeatureFlags.Contracts;

public class UpsertFlagOverrideBodyDto
{
    public bool IsEnabled { get; set; }
}
```

- [ ] **Step 2: Create EvaluateFlagsForClient use case**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/EvaluateFlagsForClient/EvaluateFlagsForClientRequest.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;

public class EvaluateFlagsForClientRequest : IRequest<EvaluateFlagsForClientResponse> { }

public class EvaluateFlagsForClientResponse : BaseResponse
{
    public Dictionary<string, bool> Flags { get; set; } = [];
}
```

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/EvaluateFlagsForClient/EvaluateFlagsForClientHandler.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;

internal class EvaluateFlagsForClientHandler : IRequestHandler<EvaluateFlagsForClientRequest, EvaluateFlagsForClientResponse>
{
    private readonly IFeatureFlagChecker _checker;

    public EvaluateFlagsForClientHandler(IFeatureFlagChecker checker) => _checker = checker;

    public async Task<EvaluateFlagsForClientResponse> Handle(
        EvaluateFlagsForClientRequest request, CancellationToken ct)
    {
        var result = new Dictionary<string, bool>();
        foreach (var def in FeatureFlagRegistry.All)
            result[def.Key] = await _checker.IsEnabledAsync(def.Key, def.DefaultValue, ct);
        return new EvaluateFlagsForClientResponse { Flags = result };
    }
}
```

- [ ] **Step 3: Create ListFlags use case**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ListFlags/ListFlagsRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;

public class ListFlagsRequest : IRequest<ListFlagsResponse> { }

public class ListFlagsResponse : BaseResponse
{
    public List<FlagStatusDto> Flags { get; set; } = [];
}
```

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;

internal class ListFlagsHandler : IRequestHandler<ListFlagsRequest, ListFlagsResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IFeatureFlagChecker _checker;

    public ListFlagsHandler(IFeatureFlagOverrideRepository repo, IFeatureFlagChecker checker)
    {
        _repo = repo;
        _checker = checker;
    }

    public async Task<ListFlagsResponse> Handle(ListFlagsRequest request, CancellationToken ct)
    {
        var overrides = await _repo.GetAllAsDictionaryAsync(ct);
        var overrideEntities = await _repo.GetAllAsync(ct);
        var flags = new List<FlagStatusDto>();

        foreach (var def in FeatureFlagRegistry.All)
        {
            var currentValue = await _checker.IsEnabledAsync(def.Key, def.DefaultValue, ct);
            var entity = overrideEntities.FirstOrDefault(e => e.Key == def.Key);

            flags.Add(new FlagStatusDto
            {
                Key = def.Key,
                Description = def.Description,
                CurrentValue = currentValue,
                IsOverridden = overrides.ContainsKey(def.Key),
                DefaultValue = def.DefaultValue,
                UpdatedBy = entity?.UpdatedBy,
                UpdatedAt = entity?.UpdatedAt,
            });
        }

        return new ListFlagsResponse { Flags = flags };
    }
}
```

- [ ] **Step 4: Create UpsertFlagOverride use case**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
{
    public string Key { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public class UpsertFlagOverrideResponse : BaseResponse
{
    public UpsertFlagOverrideResponse() { }
    public UpsertFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

internal class UpsertFlagOverrideHandler : IRequestHandler<UpsertFlagOverrideRequest, UpsertFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;

    public UpsertFlagOverrideHandler(IFeatureFlagOverrideRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<UpsertFlagOverrideResponse> Handle(
        UpsertFlagOverrideRequest request, CancellationToken ct)
    {
        if (!FeatureFlagRegistry.All.Any(d => d.Key == request.Key))
            return new UpsertFlagOverrideResponse(ErrorCodes.ResourceNotFound);

        await _repo.UpsertAsync(request.Key, request.IsEnabled, request.UpdatedBy, ct);
        _cache.Remove(Infrastructure.HebloFeatureProvider.CacheKey);
        return new UpsertFlagOverrideResponse();
    }
}
```

- [ ] **Step 5: Create ClearFlagOverride use case**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideRequest.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;

public class ClearFlagOverrideRequest : IRequest<ClearFlagOverrideResponse>
{
    public string Key { get; set; } = "";
}

public class ClearFlagOverrideResponse : BaseResponse
{
    public ClearFlagOverrideResponse() { }
    public ClearFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandler.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;

internal class ClearFlagOverrideHandler : IRequestHandler<ClearFlagOverrideRequest, ClearFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;

    public ClearFlagOverrideHandler(IFeatureFlagOverrideRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<ClearFlagOverrideResponse> Handle(
        ClearFlagOverrideRequest request, CancellationToken ct)
    {
        var deleted = await _repo.DeleteAsync(request.Key, ct);
        if (!deleted)
            return new ClearFlagOverrideResponse(ErrorCodes.ResourceNotFound);
        _cache.Remove(Infrastructure.HebloFeatureProvider.CacheKey);
        return new ClearFlagOverrideResponse();
    }
}
```

- [ ] **Step 6: Verify build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error"
```

Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FeatureFlags/
git commit -m "feat(feature-flags): add use cases (evaluate, list, upsert, clear)"
```

---

## Task 7: API Controller

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`

- [ ] **Step 1: Create FeatureFlagsController**

Create `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/feature-flags")]
[Authorize]
public class FeatureFlagsController : BaseApiController
{
    private readonly IMediator _mediator;

    public FeatureFlagsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(EvaluateFlagsForClientResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EvaluateFlagsForClientResponse>> Get(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new EvaluateFlagsForClientRequest(), ct));

    [HttpGet("admin")]
    [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
    [ProducesResponseType(typeof(ListFlagsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListFlagsResponse>> GetAdmin(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new ListFlagsRequest(), ct));

    [HttpPut("admin/{key}")]
    [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpsertFlagOverrideResponse>> Put(
        string key,
        [FromBody] UpsertFlagOverrideBodyDto body,
        CancellationToken ct)
    {
        var updatedBy = User.Identity?.Name ?? "unknown";
        return HandleResponse(await _mediator.Send(new UpsertFlagOverrideRequest
        {
            Key = key,
            IsEnabled = body.IsEnabled,
            UpdatedBy = updatedBy,
        }, ct));
    }

    [HttpDelete("admin/{key}")]
    [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
    [ProducesResponseType(typeof(ClearFlagOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClearFlagOverrideResponse>> Delete(string key, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new ClearFlagOverrideRequest { Key = key }, ct));
}
```

- [ ] **Step 2: Verify build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error"
```

Expected: no errors.

- [ ] **Step 3: Regenerate OpenAPI TypeScript client**

```bash
cd frontend && npm run build 2>&1 | tail -10
```

The OpenAPI client is regenerated on `npm run build`. Verify `frontend/src/api/generated/api-client.ts` now contains `featureFlags_Get`, `featureFlags_GetAdmin`, `featureFlags_Put`, `featureFlags_Delete` methods.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs \
        frontend/src/api/generated/api-client.ts
git commit -m "feat(feature-flags): add FeatureFlagsController and regenerate API client"
```

---

## Task 8: Migrate CatalogFeatureFlags

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:67-73`
- Delete: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogFeatureFlags.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogFeatureFlagsTests.cs`

The grep showed `CatalogFeatureFlags` properties are not consumed by any handler — they were registered but never read. The migration is a cleanup: remove the POCO and its configuration block.

- [ ] **Step 1: Remove Configure<CatalogFeatureFlags> block from CatalogModule.cs**

Delete lines 67–73 from `CatalogModule.cs` (the block that reads):
```csharp
        // Configure feature flags from configuration
        services.Configure<CatalogFeatureFlags>(options =>
        {
            // Default values - can be overridden by configuration
            options.IsTransportBoxTrackingEnabled = false;
            options.IsStockTakingEnabled = false;
            options.IsBackgroundRefreshEnabled = true;
        });
```

Also remove the `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` using if it becomes unused after this removal (check at build time).

- [ ] **Step 2: Delete CatalogFeatureFlags.cs**

```bash
rm backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogFeatureFlags.cs
```

- [ ] **Step 3: Replace CatalogFeatureFlagsTests.cs**

The old tests tested `CatalogFeatureFlags` POCO which no longer exists. Replace the file with tests that verify the three catalog flags exist in `FeatureFlagRegistry.All`:

Overwrite `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogFeatureFlagsTests.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogFeatureFlagsTests
{
    [Fact]
    public void FeatureFlagRegistry_ContainsCatalogFlags()
    {
        var keys = FeatureFlagRegistry.All.Select(d => d.Key).ToList();

        keys.Should().Contain(FeatureFlagKeys.TransportBoxTracking);
        keys.Should().Contain(FeatureFlagKeys.StockTaking);
        keys.Should().Contain(FeatureFlagKeys.BackgroundRefresh);
    }

    [Theory]
    [InlineData(FeatureFlagKeys.TransportBoxTracking, false)]
    [InlineData(FeatureFlagKeys.StockTaking, false)]
    [InlineData(FeatureFlagKeys.BackgroundRefresh, true)]
    public void FeatureFlagRegistry_CatalogFlagsHaveCorrectDefaults(string key, bool expectedDefault)
    {
        var def = FeatureFlagRegistry.All.Single(d => d.Key == key);
        def.DefaultValue.Should().Be(expectedDefault);
    }

    [Fact]
    public void FeatureFlagRegistry_CatalogFlags_HaveNonEmptyDescriptions()
    {
        var catalogKeys = new[]
        {
            FeatureFlagKeys.TransportBoxTracking,
            FeatureFlagKeys.StockTaking,
            FeatureFlagKeys.BackgroundRefresh,
        };

        foreach (var key in catalogKeys)
        {
            var def = FeatureFlagRegistry.All.SingleOrDefault(d => d.Key == key);
            def.Should().NotBeNull(because: $"flag '{key}' must be in the registry");
            def!.Description.Should().NotBeNullOrWhiteSpace(because: $"flag '{key}' must have a description");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogFeatureFlagsTests" --no-build 2>&1 | tail -10
```

Expected: 3 tests pass.

- [ ] **Step 5: Verify full build**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes 2>&1 | tail -5
```

Expected: clean.

- [ ] **Step 6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs \
  backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogFeatureFlags.cs \
  backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogFeatureFlagsTests.cs
git commit -m "refactor(feature-flags): replace CatalogFeatureFlags POCO with FeatureFlagRegistry entries"
```

---

## Task 9: Backend Tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagRegistryTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagsControllerLintTests.cs`

- [ ] **Step 1: Write the failing FeatureFlagRegistry lint tests**

Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagRegistryTests.cs`:

```csharp
using System.Reflection;
using Anela.Heblo.Application.Features.FeatureFlags;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class FeatureFlagRegistryTests
{
    [Fact]
    public void AllKeys_HaveRegistryEntry()
    {
        var constantKeys = typeof(FeatureFlagKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var registryKeys = FeatureFlagRegistry.All.Select(d => d.Key).ToList();

        foreach (var key in constantKeys)
            registryKeys.Should().Contain(key, because: $"FeatureFlagKeys.{key} must have a registry entry");
    }

    [Fact]
    public void AllRegistryEntries_HaveNonEmptyDescription()
    {
        FeatureFlagRegistry.All.Should().AllSatisfy(def =>
            def.Description.Should().NotBeNullOrWhiteSpace(
                because: $"flag '{def.Key}' must have a description"));
    }

    [Fact]
    public void AllRegistryKeys_FollowNamingConvention()
    {
        FeatureFlagRegistry.All.Should().AllSatisfy(def =>
        {
            def.Key.Should().StartWith("is-", because: "all flag keys must start with 'is-'");
            def.Key.Should().EndWith("-enabled", because: "all flag keys must end with '-enabled'");
            def.Key.Should().Be(def.Key.ToLower(), because: "flag keys must be lowercase kebab-case");
        });
    }

    [Fact]
    public void AllRegistryKeys_AreUnique()
    {
        FeatureFlagRegistry.All.Select(d => d.Key).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run registry tests — they should pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FeatureFlagRegistryTests" 2>&1 | tail -10
```

Expected: 4 tests pass.

- [ ] **Step 3: Write the failing HebloFeatureProvider tests**

Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class HebloFeatureProviderTests
{
    private static (HebloFeatureProvider provider, IFeatureFlagOverrideRepository mockRepo, IMemoryCache cache)
        CreateProvider(Dictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var mockRepo = Substitute.For<IFeatureFlagOverrideRepository>();
        mockRepo.GetAllAsDictionaryAsync(default).Returns(new Dictionary<string, bool>());

        var services = new ServiceCollection();
        services.AddSingleton<IFeatureFlagOverrideRepository>(mockRepo);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var provider = new HebloFeatureProvider(scopeFactory, cache, configuration);
        return (provider, mockRepo, cache);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenDbOverrideTrue_ReturnsTrueWithTargetingMatchReason()
    {
        // Arrange
        var (provider, mockRepo, _) = CreateProvider();
        mockRepo.GetAllAsDictionaryAsync(default).Returns(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.StockTaking] = true
        });

        // Act
        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        // Assert
        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Model.Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenDbOverrideFalse_ReturnsFalseWithTargetingMatchReason()
    {
        // Arrange
        var (provider, mockRepo, _) = CreateProvider();
        mockRepo.GetAllAsDictionaryAsync(default).Returns(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.BackgroundRefresh] = false
        });

        // Act
        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.BackgroundRefresh, true);

        // Assert
        result.Value.Should().BeFalse();
        result.Reason.Should().Be(OpenFeature.Model.Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenNoDbOverride_FallsBackToConfig()
    {
        // Arrange
        var config = new Dictionary<string, string?>
        {
            [$"FeatureManagement:{FeatureFlagKeys.StockTaking}"] = "true"
        };
        var (provider, _, _) = CreateProvider(config);

        // Act
        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        // Assert
        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Model.Reason.Static);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenNoDbOverrideNoConfig_ReturnsSuppliedDefault()
    {
        // Arrange
        var (provider, _, _) = CreateProvider();

        // Act
        var result = await provider.ResolveBooleanValueAsync("is-unknown-enabled", true);

        // Assert
        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Model.Reason.Default);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenRepoThrows_ReturnsDefaultWithErrorReason()
    {
        // Arrange
        var (provider, mockRepo, cache) = CreateProvider();
        mockRepo.GetAllAsDictionaryAsync(default)
            .Returns<Dictionary<string, bool>>(_ => throw new InvalidOperationException("DB down"));

        // Act
        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        // Assert
        result.Value.Should().BeFalse();
        result.ErrorType.Should().Be(OpenFeature.Model.ErrorType.General);
    }

    [Fact]
    public async Task ResolveBooleanValue_DbOverrideTakesPrecedenceOverConfig()
    {
        // Arrange — config says false, DB says true
        var config = new Dictionary<string, string?>
        {
            [$"FeatureManagement:{FeatureFlagKeys.StockTaking}"] = "false"
        };
        var (provider, mockRepo, _) = CreateProvider(config);
        mockRepo.GetAllAsDictionaryAsync(default).Returns(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.StockTaking] = true
        });

        // Act
        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        // Assert
        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Model.Reason.TargetingMatch);
    }
}
```

If the project doesn't have `NSubstitute`, add it:
```bash
cd backend/test/Anela.Heblo.Tests && dotnet add package NSubstitute
```

Check if it's already there first:
```bash
grep "NSubstitute" backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 4: Run HebloFeatureProvider tests — they should pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~HebloFeatureProviderTests" 2>&1 | tail -15
```

Expected: 6 tests pass.

- [ ] **Step 5: Write FeatureFlagsController lint test (admin routes must not have [FeatureGate])**

Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagsControllerLintTests.cs`:

```csharp
using System.Reflection;
using Anela.Heblo.API.Controllers;
using FluentAssertions;
using Microsoft.FeatureManagement.Mvc;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class FeatureFlagsControllerLintTests
{
    [Fact]
    public void FeatureFlagsController_AdminActions_MustNotHaveFeatureGateAttribute()
    {
        var controllerType = typeof(FeatureFlagsController);

        var adminActions = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name.Contains("HttpPut")
                || a.GetType().Name.Contains("HttpDelete")
                || (a.GetType().Name.Contains("HttpGet") && m.Name.Contains("Admin"))))
            .ToList();

        adminActions.Should().NotBeEmpty(because: "controller must have admin action methods");

        foreach (var action in adminActions)
        {
            action.GetCustomAttribute<FeatureGateAttribute>()
                .Should().BeNull(because: $"{action.Name} must never be gated by a feature flag (lockout protection)");
        }

        controllerType.GetCustomAttribute<FeatureGateAttribute>()
            .Should().BeNull(because: "FeatureFlagsController class must never be gated by a feature flag");
    }
}
```

- [ ] **Step 6: Run lint test — should pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FeatureFlagsControllerLintTests" 2>&1 | tail -10
```

Expected: 1 test passes.

- [ ] **Step 7: Run all tests to check for regressions**

```bash
cd backend && dotnet test 2>&1 | tail -15
```

Expected: all existing tests still pass.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/
git commit -m "test(feature-flags): add registry lint, provider unit, and lockout protection tests"
```

---

## Task 10: Frontend — FeatureFlagProvider + useFeatureFlag

**Files:**
- Create: `frontend/src/features/feature-flags/featureFlags.ts`
- Create: `frontend/src/features/feature-flags/FeatureFlagProvider.tsx`
- Create: `frontend/src/hooks/useFeatureFlag.ts`
- Modify: `frontend/src/api/client.ts`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Create featureFlags.ts constants (mirrors FeatureFlagKeys.cs)**

Create `frontend/src/features/feature-flags/featureFlags.ts`:

```typescript
export const FeatureFlagKeys = {
  TransportBoxTracking: "is-transport-box-tracking-enabled",
  StockTaking: "is-stock-taking-enabled",
  BackgroundRefresh: "is-background-refresh-enabled",
} as const;

export type FeatureFlagKey = (typeof FeatureFlagKeys)[keyof typeof FeatureFlagKeys];
```

- [ ] **Step 2: Add featureFlags query key to client.ts**

In `frontend/src/api/client.ts`, add `featureFlags` to the `QUERY_KEYS` object (after the last existing key, before the closing `}` of `QUERY_KEYS`):

```typescript
  featureFlags: ["feature-flags"] as const,
```

- [ ] **Step 3: Create FeatureFlagProvider**

Create `frontend/src/features/feature-flags/FeatureFlagProvider.tsx`:

```tsx
import React, { createContext, useContext, useEffect, useState } from "react";
import { OpenFeature, InMemoryProvider } from "@openfeature/web-sdk";
import { getAuthenticatedApiClient } from "../../api/client";

interface FeatureFlagContextValue {
  isReady: boolean;
}

const FeatureFlagContext = createContext<FeatureFlagContextValue>({ isReady: false });

async function fetchAndInitFlags(): Promise<void> {
  const client = await getAuthenticatedApiClient();
  const response = await (client as any).featureFlags_Get();
  const flags: Record<string, boolean> = response?.flags ?? {};

  const flagsConfig = Object.fromEntries(
    Object.entries(flags).map(([key, value]) => [key, { defaultVariant: value ? "on" : "off", variants: { on: true, off: false } }])
  );

  await OpenFeature.setProviderAndWait(new InMemoryProvider(flagsConfig));
}

export const FeatureFlagProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    fetchAndInitFlags()
      .then(() => setIsReady(true))
      .catch((err) => {
        console.error("[FeatureFlags] Failed to load flags, using defaults:", err);
        setIsReady(true);
      });
  }, []);

  return (
    <FeatureFlagContext.Provider value={{ isReady }}>
      {children}
    </FeatureFlagContext.Provider>
  );
};

export const useFeatureFlagReady = () => useContext(FeatureFlagContext).isReady;
```

- [ ] **Step 4: Create useFeatureFlag hook**

Create `frontend/src/hooks/useFeatureFlag.ts`:

```typescript
import { useBooleanFlagValue } from "@openfeature/react-sdk";

/**
 * Reads a feature flag value from the OpenFeature in-memory store.
 * Flags are hydrated from GET /api/feature-flags on app boot.
 * Always use FeatureFlagKeys constants for key names.
 * See docs/development/feature-flags.md.
 */
export function useFeatureFlag(key: string, defaultValue: boolean = false): boolean {
  return useBooleanFlagValue(key, defaultValue);
}
```

- [ ] **Step 5: Wrap App.tsx with FeatureFlagProvider**

In `frontend/src/App.tsx`, add the import:
```typescript
import { FeatureFlagProvider } from "./features/feature-flags/FeatureFlagProvider";
import { OpenFeatureProvider } from "@openfeature/react-sdk";
```

Wrap the `<QueryClientProvider>` (outermost provider) with `<OpenFeatureProvider>` and `<FeatureFlagProvider>`. Find the `return (` at line ~336 and wrap like:

```tsx
  return (
    <OpenFeatureProvider>
      <FeatureFlagProvider>
        <QueryClientProvider client={queryClient}>
          {/* existing providers unchanged */}
        </QueryClientProvider>
      </FeatureFlagProvider>
    </OpenFeatureProvider>
  );
```

- [ ] **Step 6: Verify FE build**

```bash
cd frontend && npm run build 2>&1 | tail -10
```

Expected: clean build.

- [ ] **Step 7: Verify FE lint**

```bash
cd frontend && npm run lint 2>&1 | tail -10
```

Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add \
  frontend/src/features/feature-flags/ \
  frontend/src/hooks/useFeatureFlag.ts \
  frontend/src/api/client.ts \
  frontend/src/App.tsx
git commit -m "feat(feature-flags): add FeatureFlagProvider and useFeatureFlag hook"
```

---

## Task 11: Frontend — Admin Page

**Files:**
- Create: `frontend/src/api/hooks/useFeatureFlagsAdmin.ts`
- Create: `frontend/src/pages/FeatureFlagsAdminPage.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Create admin API hooks**

Create `frontend/src/api/hooks/useFeatureFlagsAdmin.ts`:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

export interface FlagStatus {
  key: string;
  description: string;
  currentValue: boolean;
  isOverridden: boolean;
  defaultValue: boolean;
  updatedBy?: string;
  updatedAt?: string;
}

export const useFeatureFlagsAdmin = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.featureFlags, "admin"],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      const response = await (client as any).featureFlags_GetAdmin();
      return response?.flags as FlagStatus[] ?? [];
    },
  });
};

export const useUpsertFlagOverride = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ key, isEnabled }: { key: string; isEnabled: boolean }) => {
      const client = await getAuthenticatedApiClient();
      await (client as any).featureFlags_Put(key, { isEnabled });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.featureFlags, "admin"] });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.featureFlags });
    },
  });
};

export const useClearFlagOverride = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (key: string) => {
      const client = await getAuthenticatedApiClient();
      await (client as any).featureFlags_Delete(key);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.featureFlags, "admin"] });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.featureFlags });
    },
  });
};
```

- [ ] **Step 2: Create FeatureFlagsAdminPage**

Create `frontend/src/pages/FeatureFlagsAdminPage.tsx`:

```tsx
import React from "react";
import { useFeatureFlagsAdmin, useUpsertFlagOverride, useClearFlagOverride } from "../api/hooks/useFeatureFlagsAdmin";

const FeatureFlagsAdminPage: React.FC = () => {
  const { data: flags, isLoading, error } = useFeatureFlagsAdmin();
  const upsert = useUpsertFlagOverride();
  const clear = useClearFlagOverride();

  if (isLoading) return <div className="p-8 text-gray-500">Loading flags...</div>;
  if (error) return <div className="p-8 text-red-600">Failed to load feature flags.</div>;

  return (
    <div className="p-8 max-w-4xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 mb-2">Feature Flags</h1>
      <p className="text-sm text-gray-500 mb-6">
        Overrides are stored in the database and take precedence over{" "}
        <code className="bg-gray-100 px-1 rounded">appsettings.json</code> defaults.
        Deleting an override reverts the flag to its config default.
      </p>

      <div className="space-y-3">
        {flags?.map((flag) => (
          <div
            key={flag.key}
            className="flex items-start justify-between bg-white border border-gray-200 rounded-lg p-4"
          >
            <div className="flex-1 min-w-0 mr-4">
              <div className="flex items-center gap-2">
                <code className="text-sm font-mono text-indigo-700">{flag.key}</code>
                {flag.isOverridden && (
                  <span className="text-xs bg-yellow-100 text-yellow-800 px-2 py-0.5 rounded">
                    overridden
                  </span>
                )}
              </div>
              <p className="text-sm text-gray-600 mt-1">{flag.description}</p>
              {flag.isOverridden && flag.updatedBy && (
                <p className="text-xs text-gray-400 mt-1">
                  By {flag.updatedBy}{" "}
                  {flag.updatedAt && `· ${new Date(flag.updatedAt).toLocaleString()}`}
                </p>
              )}
              <p className="text-xs text-gray-400 mt-1">
                Default: <strong>{flag.defaultValue ? "on" : "off"}</strong>
              </p>
            </div>

            <div className="flex items-center gap-3 shrink-0">
              <button
                onClick={() => upsert.mutate({ key: flag.key, isEnabled: !flag.currentValue })}
                disabled={upsert.isPending}
                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 ${
                  flag.currentValue ? "bg-indigo-600" : "bg-gray-200"
                }`}
                aria-label={`Toggle ${flag.key}`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                    flag.currentValue ? "translate-x-6" : "translate-x-1"
                  }`}
                />
              </button>

              {flag.isOverridden && (
                <button
                  onClick={() => clear.mutate(flag.key)}
                  disabled={clear.isPending}
                  className="text-xs text-gray-500 hover:text-red-600 underline"
                >
                  Reset
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default FeatureFlagsAdminPage;
```

- [ ] **Step 3: Add route to App.tsx**

In `frontend/src/App.tsx`, add the import near the other admin page imports:
```typescript
import FeatureFlagsAdminPage from "./pages/FeatureFlagsAdminPage";
```

Add the route inside the Layout `<Routes>` section, near the other `/automation/` routes:
```tsx
<Route path="/admin/feature-flags" element={<FeatureFlagsAdminPage />} />
```

- [ ] **Step 4: Verify FE build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -10 && npm run lint 2>&1 | tail -10
```

Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add \
  frontend/src/api/hooks/useFeatureFlagsAdmin.ts \
  frontend/src/pages/FeatureFlagsAdminPage.tsx \
  frontend/src/App.tsx
git commit -m "feat(feature-flags): add admin page and CRUD hooks"
```

---

## Task 12: Documentation

**Files:**
- Create: `docs/development/feature-flags.md`
- Create: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/architecture/development_guidelines.md`

- [ ] **Step 1: Write primary feature-flags guide**

Create `docs/development/feature-flags.md`:

```markdown
# Feature Flags

## What this system is

Feature flags are evaluated in order: **DB override → `appsettings.json` → registry default**.

- `Microsoft.FeatureManagement` reads `appsettings.json` under `FeatureManagement:` section.
- A `FeatureFlagOverrides` Postgres table stores runtime overrides set via the admin UI.
- `HebloFeatureProvider` (OpenFeature) merges both layers for business code.
- Admin endpoints are protected by `super_user` role, same as Photobank settings.

## How to add a new flag (two steps)

**Step 1 — Add to the registry:**
```csharp
// backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs
public const string MyFeature = "is-my-feature-enabled";

// backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs
new(FeatureFlagKeys.MyFeature,
    Description: "One sentence: what this flag controls.",
    DefaultValue: false),
```

**Step 2 — Add default to appsettings.json:**
```json
{ "FeatureManagement": { "is-my-feature-enabled": false } }
```

Naming: `is-<feature>-enabled`, lowercase kebab-case, boolean only in v1.

**Step 3 — Mirror in frontend:**
```typescript
// frontend/src/features/feature-flags/featureFlags.ts
MyFeature: "is-my-feature-enabled",
```

## How to check a flag

**Backend — business code:**
```csharp
public class MyHandler(IFeatureFlagChecker flags)
{
    public async Task Handle(..., CancellationToken ct)
    {
        if (await flags.IsEnabledAsync(FeatureFlagKeys.MyFeature, ct))
        {
            // feature on
        }
    }
}
```

**Backend — controller/endpoint gating (config-only, v1 limitation):**
```csharp
[FeatureGate(FeatureFlagKeys.MyFeature)]
public class MyController : ControllerBase { ... }
```

Note: `[FeatureGate]` reads `appsettings.json` only — it does NOT see DB overrides.
This is a v1 accepted limitation. Use `IFeatureFlagChecker` in business logic instead.

**Frontend:**
```tsx
const enabled = useFeatureFlag(FeatureFlagKeys.MyFeature, false);
```

## Anti-patterns

- Do **not** call OpenFeature SDK directly — always use `IFeatureFlagChecker`.
- Do **not** hard-code flag key strings — always use `FeatureFlagKeys` constants.
- Do **not** place `[FeatureGate]` on admin/infrastructure endpoints (lockout risk).

## Admin endpoints

`GET /api/feature-flags/admin` — list all flags + current value + override metadata  
`PUT /api/feature-flags/admin/{key}` — upsert a DB override  
`DELETE /api/feature-flags/admin/{key}` — clear override (reverts to `appsettings.json`)

All admin endpoints require `super_user` role. Navigate to `/admin/feature-flags` in the app.

## Lockout protection

1. Admin endpoints (`FeatureFlagsController`) are **never** `[FeatureGate]`-d — a lint test enforces this.
2. Flag evaluation always fail-opens to the supplied default — never throws.
3. `appsettings.json` is the last-resort recovery surface: edit + restart restores any flag.
4. The provider caches DB overrides for 30 seconds; cache is invalidated immediately on admin writes.

## Flag lifecycle

When a feature is fully launched, remove the flag in a cleanup PR:
1. Delete the `FeatureFlagKeys` constant
2. Delete the `FeatureFlagRegistry.All` entry
3. Delete the `appsettings.json` line
4. Delete any DB override row
5. Delete call sites

Stale flags are technical debt. Review the registry quarterly.

## Current flag inventory

See `FeatureFlagRegistry.cs` as the source of truth. Do not duplicate the list here.
```

- [ ] **Step 2: Write slice README**

Create `backend/src/Anela.Heblo.Application/Features/FeatureFlags/README.md`:

```markdown
# FeatureFlags slice

All feature flag infrastructure lives here. The registry (`FeatureFlagRegistry.cs`) is the single source of truth for known flags.

See [docs/development/feature-flags.md](../../../../../docs/development/feature-flags.md) for the full guide: how to add flags, check flags, admin them, and the lockout protection rules.
```

- [ ] **Step 3: Update CLAUDE.md**

In `CLAUDE.md`, under the `**Development**` section of the Documentation map, add:
```
- `docs/development/feature-flags.md` — read before adding or checking a feature flag
```

- [ ] **Step 4: Update docs/architecture/development_guidelines.md**

In `docs/architecture/development_guidelines.md`, add a **Feature Flags** subsection under **Required Practices** (or at the bottom if that section doesn't exist):

```markdown
## Feature Flags

Use `IFeatureFlagChecker` for all flag evaluation in business code — never call OpenFeature SDK directly. Always use `FeatureFlagKeys` constants (never raw strings). See [docs/development/feature-flags.md](../development/feature-flags.md) for the full guide.
```

- [ ] **Step 5: Commit**

```bash
git add \
  docs/development/feature-flags.md \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/README.md \
  CLAUDE.md \
  docs/architecture/development_guidelines.md
git commit -m "docs(feature-flags): add usage guide, update CLAUDE.md and dev guidelines"
```

---

## Task 13: Final Verification

- [ ] **Step 1: Run all backend tests**

```bash
cd backend && dotnet test 2>&1 | tail -20
```

Expected: all tests pass (no regressions).

- [ ] **Step 2: Run dotnet format check**

```bash
cd backend && dotnet format --verify-no-changes 2>&1 | tail -5
```

Expected: no formatting issues.

- [ ] **Step 3: Run frontend build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -10 && npm run lint 2>&1 | tail -10
```

Expected: clean.

- [ ] **Step 4: Manual — verify admin auth (requires running app)**

With the app running locally:
```bash
# Non-superuser: expect 403
curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer <regular-user-token>" \
  http://localhost:5001/api/feature-flags/admin

# SuperUser: expect 200
curl -s -H "Authorization: Bearer <superuser-token>" \
  http://localhost:5001/api/feature-flags/admin | jq '.flags[0]'
```

- [ ] **Step 5: Manual — upsert and verify override**

```bash
# Set override
curl -s -X PUT \
  -H "Authorization: Bearer <superuser-token>" \
  -H "Content-Type: application/json" \
  -d '{"isEnabled": true}' \
  http://localhost:5001/api/feature-flags/admin/is-stock-taking-enabled

# Verify public endpoint reflects it
curl -s -H "Authorization: Bearer <any-user-token>" \
  http://localhost:5001/api/feature-flags | jq '.flags["is-stock-taking-enabled"]'
# Expected: true
```

- [ ] **Step 6: Manual — DB down resilience**

Stop the DB or break the connection string. Verify `GET /api/feature-flags` returns the `appsettings.json` defaults rather than a 500.

- [ ] **Step 7: Tag completion**

```bash
git log --oneline -10
```

Review the commit history — all tasks should be represented.

---

## Self-Review Checklist

### Spec coverage

| Requirement | Task(s) |
|---|---|
| `Microsoft.FeatureManagement.AspNetCore` as provider | Task 1, 5 |
| OpenFeature abstraction on BE | Task 5 |
| DB override table + repo | Task 3, 4 |
| DB override > config fallback > registry default | Task 5 (HebloFeatureProvider) |
| `FeatureFlagRegistry` single source of truth | Task 4 |
| `FeatureFlagKeys` constants | Task 4 |
| `IFeatureFlagChecker` interface | Task 4, 5 |
| `[FeatureGate]` config-only gating | Task 7 (controller) |
| Admin auth `[Authorize(Roles = SuperUser)]` | Task 7 |
| Admin endpoints never `[FeatureGate]`-d | Task 9 (lint test) |
| Fail-open evaluation | Task 5 (HebloFeatureProvider catch block) |
| 30s cache + immediate invalidation on write | Task 5, 6 |
| Public `GET /api/feature-flags` for FE | Task 7 |
| Admin CRUD endpoints | Task 7 |
| CatalogFeatureFlags migration | Task 8 |
| `useFeatureFlag` hook + OpenFeature FE SDK | Task 10 |
| FE `featureFlags.ts` constants | Task 10 |
| Admin page + role-gated route | Task 11 |
| `feature-flags.md` guide | Task 12 |
| Registry README | Task 12 |
| CLAUDE.md + dev_guidelines update | Task 12 |
| Unit tests: provider (DB/config/default/fallback) | Task 9 |
| Unit tests: registry lint | Task 9 |
| Lint test: admin routes not FeatureGate-d | Task 9 |
| FE: constant list mirrors BE registry | Task 10 (featureFlags.ts) — note: automated assertion deferred; see below |

### Deferred items (not in v1 scope per plan)

- Automated test asserting BE and FE key lists are equal: added as a note in `featureFlags.ts` (`// Keys must match FeatureFlagKeys.cs`), full automated cross-layer test deferred to a follow-up.
- `TargetingFilter` DI registration: deferred, YAGNI per plan decisions.
- Percentage rollout / targeting: YAGNI per plan.
