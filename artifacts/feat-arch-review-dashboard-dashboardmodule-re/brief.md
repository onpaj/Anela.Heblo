## Module
Dashboard

## Finding
`DashboardModule.cs` (`backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`, lines 1–23) directly imports and registers three tiles that belong to other modules:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
...
services.RegisterTile<DataQualityStatusTile>();   // DataQuality-owned
services.RegisterTile<DqtYesterdayStatusTile>();  // DataQuality-owned
services.RegisterTile<FailedJobsTile>();           // BackgroundJobs-owned
```

Both `BackgroundJobsModule.cs` and `DataQualityModule.cs` exist but neither registers its own tiles. The established pattern across every other module is that each module owns its tile registrations — Catalog (`CatalogModule.cs:112–117`), Logistics (`LogisticsModule.cs:29–32`), Manufacture (`ManufactureModule.cs:63–66`), Purchase (`PurchaseModule.cs:26`) all follow the correct approach. `DashboardModule` is the single exception.

## Why it matters
- Direct cross-module import (`Dashboard` → `BackgroundJobs`, `Dashboard` → `DataQuality`) creates the exact coupling the module boundary rule forbids.
- Adding a tile to BackgroundJobs now requires touching `DashboardModule`, violating the single-responsibility of each module's registration file.
- Future removal of the DataQuality module would require a change in DashboardModule rather than being self-contained.

## Suggested fix
Move each tile registration to its owning module (minimal, no logic changes needed):

```csharp
// In BackgroundJobsModule.cs
services.RegisterTile<FailedJobsTile>();

// In DataQualityModule.cs
services.RegisterTile<DataQualityStatusTile>();
services.RegisterTile<DqtYesterdayStatusTile>();
```

Remove the corresponding `using` statements and `RegisterTile` calls from `DashboardModule.cs`.

---
_Filed by daily arch-review routine on 2026-05-28._