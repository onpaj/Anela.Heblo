# Migrate `ManufactureGroupId` to Typed Options Pattern — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `IConfiguration`-based access to `ManufactureGroupId` in `GetManufactureSettingsHandler` with strongly-typed `IOptions<ManufactureErpOptions>`, eliminating the last raw-key configuration access in the Manufacture module.

**Architecture:** Add a nullable `ManufactureGroupId` property to the existing `ManufactureErpOptions` class (already bound to the `"ManufactureErp"` section by `ManufactureModule.AddManufactureModule`). Rewire the handler to consume `IOptions<ManufactureErpOptions>`. Move the `appsettings.json` placeholder into the nested `ManufactureErp` section, delete the now-unused `ManufactureConfigurationKeys` constants file, update unit tests to use `Options.Create(...)`, and tighten the null/empty/whitespace collapse to `string.IsNullOrWhiteSpace`. Endpoint test file stays untouched (it does not seed config). Coordinate the Azure App Service env-var rename (`ManufactureGroupId` → `ManufactureErp__ManufactureGroupId`) in the PR description for Production and any other overriding environment.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq, MediatR, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Configuration`.

---

## File Structure

| Action | Path | Responsibility |
|---|---|---|
| Edit | `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs` | Add `ManufactureGroupId` nullable string property |
| Edit | `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs` | Swap `IConfiguration` → `IOptions<ManufactureErpOptions>`, use `IsNullOrWhiteSpace` |
| Delete | `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs` | Remove unused string-key constants |
| Edit | `backend/src/Anela.Heblo.API/appsettings.json` | Move `ManufactureGroupId` placeholder into the `ManufactureErp` section |
| Edit | `backend/src/Anela.Heblo.API/appsettings.Production.json` | Remove top-level `ManufactureGroupId` placeholder |
| Edit | `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs` | Construct handler with `Options.Create(new ManufactureErpOptions { ... })`; add whitespace test case |
| Verify (no edit) | `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsEndpointTests.cs` | Does not seed config — no change required |
| Verify (no edit) | `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` | Already binds `"ManufactureErp"` section — new property binds automatically |

---

## Task 1: Add `ManufactureGroupId` to `ManufactureErpOptions`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs`

- [ ] **Step 1: Read the current file**

Run: `cat backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs`
Expected (current content):

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.Configuration;

/// <summary>
/// Configuration options for Manufacture ERP integration (FlexiBee).
/// </summary>
public class ManufactureErpOptions
{
    /// <summary>
    /// Maximum number of seconds to wait for a single Flexi ERP call before timing out.
    /// Defaults to 60 seconds. Set to 0 to disable the application-level timeout.
    /// </summary>
    public int ErpTimeoutSeconds { get; set; } = 60;
}
```

- [ ] **Step 2: Add the `ManufactureGroupId` property**

Edit `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs` to read:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.Configuration;

/// <summary>
/// Configuration options for Manufacture ERP integration (FlexiBee).
/// </summary>
public class ManufactureErpOptions
{
    /// <summary>
    /// Maximum number of seconds to wait for a single Flexi ERP call before timing out.
    /// Defaults to 60 seconds. Set to 0 to disable the application-level timeout.
    /// </summary>
    public int ErpTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Entra ID group identifier consumed by GetManufactureSettings to gate
    /// "responsible person" workflows on the frontend. Bound from configuration
    /// key "ManufactureErp:ManufactureGroupId" (env var "ManufactureErp__ManufactureGroupId").
    /// </summary>
    public string? ManufactureGroupId { get; set; }
}
```

- [ ] **Step 3: Verify the project compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs
git commit -m "feat: add ManufactureGroupId to ManufactureErpOptions"
```

---

## Task 2: Update `GetManufactureSettingsHandlerTests` to typed options (RED for the handler)

We write the new tests first so the next task implements against them. After this task, the unit tests will fail to compile until Task 3 lands.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs`

- [ ] **Step 1: Rewrite the test file to use `Options.Create(...)` and add a whitespace case**

Replace the entire contents of `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Settings;

public class GetManufactureSettingsHandlerTests
{
    private readonly Mock<ILogger<GetManufactureSettingsHandler>> _loggerMock = new();

    private GetManufactureSettingsHandler CreateHandler(string? manufactureGroupId)
    {
        var options = Options.Create(new ManufactureErpOptions
        {
            ManufactureGroupId = manufactureGroupId
        });
        return new GetManufactureSettingsHandler(options, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdConfigured_ReturnsValueInResponse()
    {
        // Arrange
        var configuredGroupId = "11111111-2222-3333-4444-555555555555";
        var handler = CreateHandler(configuredGroupId);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().Be(configuredGroupId);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdMissing_ReturnsNull()
    {
        // Arrange
        var handler = CreateHandler(manufactureGroupId: null);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdEmpty_ReturnsNull()
    {
        // Arrange
        var handler = CreateHandler(manufactureGroupId: string.Empty);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdWhitespace_ReturnsNull()
    {
        // Arrange
        var handler = CreateHandler(manufactureGroupId: "   ");

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public void Constructor_WhenOptionsNull_ThrowsArgumentNullException()
    {
        // Arrange
        Action act = () => new GetManufactureSettingsHandler(null!, _loggerMock.Object);

        // Act / Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_WhenLoggerNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = Options.Create(new ManufactureErpOptions());
        Action act = () => new GetManufactureSettingsHandler(options, null!);

        // Act / Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
```

- [ ] **Step 2: Verify tests fail to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build FAILED. Errors reference `GetManufactureSettingsHandler` constructor — `cannot convert from 'Microsoft.Extensions.Options.IOptions<...>' to 'Microsoft.Extensions.Configuration.IConfiguration'` (or similar). This confirms the tests are RED.

- [ ] **Step 3: Do NOT commit yet**

Leave staged for the combined commit in Task 3 — the handler change and these tests ship together so the repo never has a broken build on `main`.

---

## Task 3: Refactor `GetManufactureSettingsHandler` to use `IOptions<ManufactureErpOptions>` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs`

- [ ] **Step 1: Read the current handler**

Run: `cat backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs`
Expected (current content):

```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;

public class GetManufactureSettingsHandler
    : IRequestHandler<GetManufactureSettingsRequest, GetManufactureSettingsResponse>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetManufactureSettingsHandler> _logger;

    public GetManufactureSettingsHandler(
        IConfiguration configuration,
        ILogger<GetManufactureSettingsHandler> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetManufactureSettingsResponse> Handle(
        GetManufactureSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var groupId = _configuration[ManufactureConfigurationKeys.GroupId];
        if (string.IsNullOrEmpty(groupId))
        {
            groupId = null;
        }

        _logger.LogDebug("GetManufactureSettings resolved ManufactureGroupId hasValue={HasValue}", groupId is not null);

        return Task.FromResult(new GetManufactureSettingsResponse
        {
            ManufactureGroupId = groupId
        });
    }
}
```

- [ ] **Step 2: Replace the handler implementation**

Overwrite `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;

public class GetManufactureSettingsHandler
    : IRequestHandler<GetManufactureSettingsRequest, GetManufactureSettingsResponse>
{
    private readonly ManufactureErpOptions _options;
    private readonly ILogger<GetManufactureSettingsHandler> _logger;

    public GetManufactureSettingsHandler(
        IOptions<ManufactureErpOptions> options,
        ILogger<GetManufactureSettingsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetManufactureSettingsResponse> Handle(
        GetManufactureSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var groupId = string.IsNullOrWhiteSpace(_options.ManufactureGroupId)
            ? null
            : _options.ManufactureGroupId;

        _logger.LogDebug("GetManufactureSettings resolved ManufactureGroupId hasValue={HasValue}", groupId is not null);

        return Task.FromResult(new GetManufactureSettingsResponse
        {
            ManufactureGroupId = groupId
        });
    }
}
```

Notes:
- `ArgumentNullException.ThrowIfNull(options)` matches the project's nullable-aware guard style and produces a `paramName` of `"options"` for Task 2's constructor-null test.
- `using Anela.Heblo.Application.Features.Manufacture;` is no longer needed (no reference to `ManufactureConfigurationKeys`). The old `using Microsoft.Extensions.Configuration;` is removed.

- [ ] **Step 3: Verify Application project compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 4: Run the unit tests (expect green)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetManufactureSettingsHandlerTests"`
Expected: All 6 tests pass (4 `Handle_*` + 2 `Constructor_*`).

- [ ] **Step 5: Run formatter**

Run: `dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Run: `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: No diff or only whitespace fixups.

- [ ] **Step 6: Commit handler + tests together**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs
git commit -m "refactor: GetManufactureSettingsHandler reads ManufactureGroupId via IOptions<ManufactureErpOptions>"
```

---

## Task 4: Delete `ManufactureConfigurationKeys.cs`

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs`

- [ ] **Step 1: Confirm there are no remaining references**

Run: `grep -rn "ManufactureConfigurationKeys" backend/`
Expected: No matches. (After Task 3, the handler no longer references it and the test file no longer imports it.)

If any match remains, fix it before deleting the file.

- [ ] **Step 2: Delete the file**

Run: `git rm backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs`
Expected: `rm 'backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs'`.

- [ ] **Step 3: Verify Application project compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 4: Verify tests project compiles and passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetManufactureSettings"`
Expected: All `GetManufactureSettings*` tests pass (handler + endpoint).

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor: remove unused ManufactureConfigurationKeys"
```

---

## Task 5: Relocate `ManufactureGroupId` placeholder in `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Remove the top-level entry**

Edit `backend/src/Anela.Heblo.API/appsettings.json`, locate the block at line 7:

```json
  "ManufactureGroupId": "your-entra-id-group-id-here",
```

Remove that entire line (and only that line). The preceding `FeatureManagement` block keeps its trailing `},` and the following `MarketingCalendar` entry remains valid JSON.

- [ ] **Step 2: Add the nested entry inside `"ManufactureErp"`**

In the same file, locate the existing `ManufactureErp` section (around line 268):

```json
  "ManufactureErp": {
    "ErpTimeoutSeconds": 60
  },
```

Replace it with:

```json
  "ManufactureErp": {
    "ErpTimeoutSeconds": 60,
    "ManufactureGroupId": "your-entra-id-group-id-here"
  },
```

- [ ] **Step 3: Validate JSON syntax**

Run: `python3 -m json.tool backend/src/Anela.Heblo.API/appsettings.json > /dev/null`
Expected: Command exits 0 with no output. (If it errors, fix the trailing comma / brace.)

- [ ] **Step 4: Verify the API project still builds**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "chore: move ManufactureGroupId under ManufactureErp section in appsettings.json"
```

---

## Task 6: Remove top-level `ManufactureGroupId` placeholder from `appsettings.Production.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.Production.json`

- [ ] **Step 1: Remove the top-level entry**

Edit `backend/src/Anela.Heblo.API/appsettings.Production.json` and delete line 37:

```json
  "ManufactureGroupId": "---- Injected via app service env variables ----",
```

Remove that entire line. The surrounding `"Cors": { ... },` (line 36) and `"ApplicationInsights": { ... },` (line 38) entries remain.

Do **not** add a `ManufactureErp` section to this file — the value is supplied via env var (`ManufactureErp__ManufactureGroupId`) and the existing top-level placeholder was documentation-only.

- [ ] **Step 2: Validate JSON syntax**

Run: `python3 -m json.tool backend/src/Anela.Heblo.API/appsettings.Production.json > /dev/null`
Expected: Command exits 0 with no output.

- [ ] **Step 3: Verify the API project still builds**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.Production.json
git commit -m "chore: drop legacy ManufactureGroupId placeholder from appsettings.Production.json"
```

---

## Task 7: Final validation across solution

This task does no code change; it runs the full validation gate documented in `CLAUDE.md`.

- [ ] **Step 1: Confirm no stale references to the old key or constant**

Run: `grep -rn "ManufactureConfigurationKeys" backend/`
Expected: No matches.

Run: `grep -rn "\"ManufactureGroupId\"" backend/src/`
Expected: Exactly **one** match — the nested entry in `backend/src/Anela.Heblo.API/appsettings.json` under the `ManufactureErp` section. No top-level key, no other appsettings file references it.

- [ ] **Step 2: Confirm the endpoint test file is unchanged**

Run: `git diff --name-only HEAD~5..HEAD -- backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsEndpointTests.cs`
Expected: No output (the file is intentionally untouched per arch-review Spec Amendment 1).

- [ ] **Step 3: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 4: Full backend format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: Exit code 0, no formatting diff.

If it reports a diff, run `dotnet format backend/Anela.Heblo.sln` and amend the most relevant prior commit (or commit as `chore: format`).

- [ ] **Step 5: Full backend test run**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests pass. Pay particular attention to:

- `GetManufactureSettingsHandlerTests` — 6 tests pass (configured, missing, empty, whitespace, ctor-null-options, ctor-null-logger).
- `GetManufactureSettingsEndpointTests` — 3 tests still pass (reachable, content-type, exposes field, anonymous).

- [ ] **Step 6: No commit needed**

Validation only; no files changed at this step.

---

## Task 8: Document the production env-var rename in the PR description

This task is **mandatory before merge** because the production binding key changes (NFR-3 + Spec Amendment 2). The PR description must enumerate every environment whose configuration must be updated in lock-step with the code change.

- [ ] **Step 1: Confirm the affected environments**

Verified by repo grep at planning time:

- `appsettings.Production.json` — has a top-level placeholder; production Web App sets the env var.
- `appsettings.Staging.json` — does NOT mention `ManufactureGroupId`. Confirm with the deployer whether the Staging Web App nonetheless sets `ManufactureGroupId` (legacy) or `ManufactureErp__ManufactureGroupId` (new). If the Staging Web App sets the legacy name, it must be renamed at the same time.
- No other `appsettings.*.json` references the key.

- [ ] **Step 2: Write the PR description (paste this into the PR body)**

```
## Summary
Migrates `ManufactureGroupId` consumption in `GetManufactureSettingsHandler` from raw
`IConfiguration` to the typed `IOptions<ManufactureErpOptions>` pattern. Adds the
property to `ManufactureErpOptions`, deletes the now-unused `ManufactureConfigurationKeys`,
moves the `appsettings.json` placeholder into the nested `ManufactureErp` section, and
updates unit tests. Behavioral change: null/empty/whitespace now all collapse to `null`
(was: null/empty only). HTTP contract is unchanged.

## ⚠️ Required deployment coordination — DO NOT MERGE before this is done

The production binding key changes from `ManufactureGroupId` (top-level) to
`ManufactureErp:ManufactureGroupId` (nested). For Azure App Service, the env var
delimiter is `__`, so:

- **Old env var:** `ManufactureGroupId`
- **New env var:** `ManufactureErp__ManufactureGroupId`

Required actions in the Azure portal **before** the new image is promoted:

1. **Production Web App:** add `ManufactureErp__ManufactureGroupId` with the current value
   of `ManufactureGroupId`. Keep `ManufactureGroupId` set in parallel until the new image
   is healthy (rollback safety), then remove the legacy var.
2. **Staging Web App (if applicable):** verify whether `ManufactureGroupId` is set there.
   If yes, repeat step 1 against Staging.
3. **Any other environment** that overrides this value: same treatment.

Without step 1, the endpoint will silently return `null` post-deploy and the frontend
"responsible person" gating will degrade.

## Test plan
- [ ] `dotnet build backend/Anela.Heblo.sln` clean
- [ ] `dotnet format backend/Anela.Heblo.sln --verify-no-changes` clean
- [ ] `dotnet test backend/Anela.Heblo.sln` — full suite green
- [ ] `GetManufactureSettingsHandlerTests` — 6 tests pass (configured / missing /
      empty / whitespace / ctor null options / ctor null logger)
- [ ] `GetManufactureSettingsEndpointTests` — 3 tests pass (reachable, content-type,
      exposes field, anonymous) — file intentionally untouched
- [ ] Deployer confirms `ManufactureErp__ManufactureGroupId` is set in every overriding
      environment (Production confirmed; Staging confirmed or N/A)
```

- [ ] **Step 3: No commit needed**

The PR description is authored when opening the PR — not committed.

---

## Self-Review

**1. Spec coverage:**

- FR-1 (add `ManufactureGroupId` to `ManufactureErpOptions`) → **Task 1**.
- FR-2 (refactor handler to `IOptions<ManufactureErpOptions>`) → **Task 3**. Includes the `IsNullOrWhiteSpace` tightening (Spec Amendment 3), preservation of the debug log line, removed `IConfiguration` using, added `IOptions` using, no contract change.
- FR-3 (delete `ManufactureConfigurationKeys.cs`) → **Task 4**. Includes the orphan-using check (Spec Amendment 4 — handled inline in Task 3 Step 2 by writing a clean using list).
- FR-4 (relocate `ManufactureGroupId` into `ManufactureErp` section in `appsettings.json`) → **Task 5**; `appsettings.Production.json` top-level placeholder removed in **Task 6**.
- FR-5 (update tests to typed options) → **Task 2**. Per Spec Amendment 1, `GetManufactureSettingsEndpointTests` is left untouched (verified in Task 7 Step 2); only `GetManufactureSettingsHandlerTests` changes.
- NFR-1 (behavioral parity of HTTP response shape) → covered by Task 7 Step 5 running `GetManufactureSettingsEndpointTests`.
- NFR-2 (build + format) → Task 7 Steps 3–4.
- NFR-3 (production coordination — extended to all overriding envs per Spec Amendment 2) → **Task 8**.
- NFR-4 (no secret regression) → no secret values added to any committed file; only placeholder text is moved (Tasks 5–6).

**2. Placeholder scan:** No TBDs, no "implement later", no "similar to Task N" cross-references. Every code-modifying step shows the actual code. Validation commands have explicit expected outputs.

**3. Type / signature consistency:**

- `ManufactureErpOptions.ManufactureGroupId` is `string?` everywhere (Task 1 declaration, Task 2 test construction, Task 3 handler read).
- Handler constructor `IOptions<ManufactureErpOptions> options, ILogger<GetManufactureSettingsHandler> logger` matches between Task 2 test (constructor-null tests) and Task 3 implementation.
- `Options.Create(new ManufactureErpOptions { ManufactureGroupId = ... })` factory in Task 2 matches the property added in Task 1.
- `using Anela.Heblo.Application.Features.Manufacture.Configuration;` is added in Task 3 (handler) and Task 2 (test) where needed; the orphan `using Anela.Heblo.Application.Features.Manufacture;` is dropped from both (no `ManufactureConfigurationKeys` reference remains).
- `ArgumentNullException.ThrowIfNull(options)` produces `paramName == "options"`, which the Task 2 test `WithParameterName("options")` asserts.

**4. Commit hygiene:** Handler + tests ship as a single commit (Task 3 Step 6) so the repo is never on a broken build between commits. The constants-file deletion (Task 4) is its own commit because it touches a different file and is mechanically reversible. Appsettings changes (Tasks 5 and 6) are separate commits per file to keep diffs reviewable.

Plan ready.
