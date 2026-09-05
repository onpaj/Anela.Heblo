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

