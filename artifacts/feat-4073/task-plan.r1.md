# Move GiftPackageManufacture and GiftSettings DI Registration Into LogisticsModule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fold the two direct `services.AddGiftPackageManufactureModule()` / `services.AddGiftSettingsModule()` calls in `ApplicationModule.cs` into `LogisticsModule.AddLogisticsModule()`, so `ApplicationModule` calls exactly one method (`AddLogisticsModule()`) for the entire Logistics module, matching the documented single-entry-point DI pattern.

**Architecture:** Pure DI-composition refactor — no new types, no behavior change. `LogisticsModule.AddLogisticsModule()` grows two extra calls at the end of its body (with two new `using` directives); `ApplicationModule.cs` loses the two direct calls and their now-unused `using` directives. `GiftPackageManufactureModule.cs` and `GiftSettingsModule.cs` are untouched.

**Tech Stack:** .NET 8, `Microsoft.Extensions.DependencyInjection` (`IServiceCollection` extension methods), xUnit for verification tests.

---

### task: add-subfeature-registrations-to-logistics-module

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`

- [ ] **Step 1: Add the two new `using` directives**

At the top of `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`, the current using block (lines 1–11) is:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Application.Features.Logistics.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.Services;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
```

Change it to (adding two lines, alphabetically ordered with the existing `Anela.Heblo.Application.Features.*` group):

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Application.Features.Logistics.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: Register the two sub-feature modules at the end of `AddLogisticsModule()`**

The current end of the method body is:

```csharp
        // Register background refresh task for completing received boxes
        services.RegisterRefreshTask<ITransportBoxCompletionService>(
            nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync),
            (service, ct) => service.CompleteReceivedBoxesAsync(ct)
        );

        return services;
    }
}
```

Change it to (adding a comment and two calls immediately before `return services;`):

```csharp
        // Register background refresh task for completing received boxes
        services.RegisterRefreshTask<ITransportBoxCompletionService>(
            nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync),
            (service, ct) => service.CompleteReceivedBoxesAsync(ct)
        );

        // Register Logistics sub-feature modules
        services.AddGiftPackageManufactureModule();
        services.AddGiftSettingsModule();

        return services;
    }
}
```

- [ ] **Step 3: Build to confirm the new file compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` with no new errors or warnings. (This step will still fail at this point if `ApplicationModule.cs` is not yet fixed and now double-registers the two sub-modules — a double registration of `AddScoped`/`AddTransient` for the same interface does **not** fail the build, it is a valid no-op-until-runtime duplicate registration, so this build is expected to succeed even before Task 2 runs; duplication is resolved by Task 2.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs
git commit -m "feat(logistics): register GiftPackageManufacture and GiftSettings sub-modules from LogisticsModule"
```

---

### task: remove-duplicate-registrations-from-application-module

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Remove the two now-unused `using` directives**

Remove these two lines from the top of `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (currently lines 35 and 40):

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
```

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;
```

Before deleting, confirm no other symbol from those two namespaces is still referenced in the file:

Run: `grep -n "GiftPackageManufacture\|GiftSettings" backend/src/Anela.Heblo.Application/ApplicationModule.cs`
Expected (before this task's edits): exactly 4 matches — the 2 `using` lines above plus the 2 call lines removed in Step 2 below. After this task's edits, expected: no matches.

- [ ] **Step 2: Remove the two direct registration calls**

In `AddApplicationServices()`, the current lines are:

```csharp
        services.AddManufactureModule(configuration);
        services.AddLogisticsModule();
        services.AddGiftPackageManufactureModule();
        services.AddUserManagement(configuration);
```

Change to:

```csharp
        services.AddManufactureModule(configuration);
        services.AddLogisticsModule();
        services.AddUserManagement(configuration);
```

And further down, the current lines are:

```csharp
        services.AddMarketingInvoicesModule();
        services.AddCarrierCoolingModule();
        services.AddGiftSettingsModule();
        services.AddWeatherForecastModule();
```

Change to:

```csharp
        services.AddMarketingInvoicesModule();
        services.AddCarrierCoolingModule();
        services.AddWeatherForecastModule();
```

- [ ] **Step 3: Build to confirm no unused-using or missing-reference errors**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` with no errors and no new warnings.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "refactor(logistics): remove duplicate GiftPackageManufacture/GiftSettings registration from ApplicationModule"
```

---

### task: verify-full-build-format-and-test-suite

**Files:**
- None modified — this task only runs verification commands against the changes made in the two prior tasks.

- [ ] **Step 1: Confirm `ApplicationModule.cs` has exactly one Logistics-related call**

Run: `grep -n "AddLogisticsModule\|AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/ApplicationModule.cs`
Expected output: exactly one line, `services.AddLogisticsModule();` — no `AddGiftPackageManufactureModule` or `AddGiftSettingsModule` matches.

- [ ] **Step 2: Confirm `LogisticsModule.cs` now calls both sub-modules**

Run: `grep -n "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`
Expected output: two lines — `services.AddGiftPackageManufactureModule();` and `services.AddGiftSettingsModule();`.

- [ ] **Step 3: Full solution build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors. Warning count must not increase versus the pre-change baseline (capture the baseline warning count before Task 1/2's edits if not already known, and diff against it here).

- [ ] **Step 4: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 (no formatting violations). If it reports violations introduced by this change, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-stage and amend the relevant commit from Task 1 or Task 2 (whichever file the formatter touched) — do not create a separate "fix formatting" commit for a change this small.

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: all tests pass, in particular any tests that build the full `IServiceCollection`/`IServiceProvider` via `AddApplicationServices()` (e.g. integration tests using `WebApplicationFactory`) — these must still resolve `IGiftPackageManufactureRepository`, `IGiftPackageManufactureService`, `IGiftSettingRepository`, `IValidator<SetGiftSettingCommand>`, and the `SetGiftSettingCommand` pipeline behavior without error, proving the DI graph is unchanged. No test currently pins the old call site (`grep -rn "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/test/` returns no matches as of this plan), so no test file is expected to need updating.

- [ ] **Step 6: Final review — no leftover references**

Run: `grep -rn "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/ApplicationModule.cs`
Expected: no matches (already confirmed in Step 1, re-checked here as the closing gate before declaring the task complete).

- [ ] **Step 7: Commit (only if Step 4 required a formatting fix that amended a prior commit; otherwise skip — Tasks 1 and 2 already committed everything)**

```bash
git status
```

Expected: clean working tree (nothing to commit) if Steps 3–6 all passed and no formatting fix was needed.

---

## Self-Review

**Spec coverage:**
- FR-1 (`AddLogisticsModule()` registers GiftPackageManufacture services) → `add-subfeature-registrations-to-logistics-module`, Step 2.
- FR-2 (`AddLogisticsModule()` registers GiftSettings services) → `add-subfeature-registrations-to-logistics-module`, Step 2 (same edit adds both calls).
- FR-3 (`ApplicationModule.cs` no longer calls the two sub-module methods directly, unused usings removed) → `remove-duplicate-registrations-from-application-module`, Steps 1–2.
- FR-4 (no change in resolved services / runtime behavior) → `verify-full-build-format-and-test-suite`, Steps 3 and 5.
- NFR-1 / NFR-2 (performance, security) → not applicable, no task needed (spec states "None").

**Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" language used; every step shows the exact before/after code or an exact command with an expected result.

**Type consistency:** `AddLogisticsModule`, `AddGiftPackageManufactureModule`, `AddGiftSettingsModule` are referenced identically (same names, same `IServiceCollection` extension-method shape) across all three tasks and match the existing source files read during architecture review — no renaming or signature drift introduced anywhere in this plan.
