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

