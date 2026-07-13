### task: rename-add-transport-module-to-add-logistics-module

## Goal
Rename the DI-registration extension method `AddTransportModule()` on `LogisticsModule` to `AddLogisticsModule()`, so it matches the codebase-wide `{Feature}Module.Add{Feature}Module()` naming convention used by every other feature module (e.g. `CatalogModule.AddCatalogModule()`, `PurchaseModule.AddPurchaseModule()`). Update the method's one call site and the two stale documentation examples that still show the old name. This is a pure identifier rename — no change to method body, parameters, return type, registered services, or DI behavior.

## Context
`LogisticsModule` is the sole exception to the `{Feature}Module.Add{Feature}Module()` convention across `Anela.Heblo.Application.Features.*` (29+ modules surveyed, all others conform). This is a leftover from when the module was renamed from "Transport" to "Logistics". Flagged by the daily arch-review routine on 2026-07-12.

Verified directly against the working tree (all four locations confirmed present exactly as described, via `grep -n "AddTransportModule"`):

1. **`backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`, line 17**, inside `public static class LogisticsModule`:
   ```csharp
   public static IServiceCollection AddTransportModule(this IServiceCollection services)
   ```
   Change to:
   ```csharp
   public static IServiceCollection AddLogisticsModule(this IServiceCollection services)
   ```
   Only the identifier changes — the method body (repository registration, `ITransportBoxCompletionService`, cross-module adapters for `ICatalogTransportSource` and `IExpeditionPickingSource`, etc.) is untouched.

2. **`backend/src/Anela.Heblo.Application/ApplicationModule.cs`, line 92** (inside the application services registration sequence, between `services.AddManufactureModule(configuration);` on line 91 and `services.AddGiftPackageManufactureModule();` on line 93):
   ```csharp
   services.AddTransportModule();
   ```
   Change to:
   ```csharp
   services.AddLogisticsModule();
   ```
   Do not reorder — keep it in its current position in the sequence.

3. **`docs/architecture/development_guidelines.md`, line 158**, inside the "API Composition (Program.cs):" fenced code block (lines 151–161):
   ```csharp
   services
       .AddCatalogModule()
       .AddOrdersModule()
       .AddInvoicesModule()
       .AddManufactureModule()
       .AddTransportModule()
       .AddPurchaseModule()
       .AddXccInfrastructure();
   ```
   Change only the `.AddTransportModule()` line to `.AddLogisticsModule()`, keeping its position in the fluent chain (between `.AddManufactureModule()` and `.AddPurchaseModule()`).

4. **`docs/architecture/infrastructure.md`, line 143**, inside the "Feature modules" fenced code block (lines 136–149):
   ```csharp
   services
       .AddCatalogModule()
       .AddInvoicesModule()
       .AddManufactureModule()
       .AddPurchaseModule()
       .AddTransportModule()
       .AddApplicationServices() // All feature modules
       .AddPersistence(Configuration.GetConnectionString("DefaultConnection")); // Database
   ```
   Change only the `.AddTransportModule()` line to `.AddLogisticsModule()`, keeping its position in the fluent chain (between `.AddPurchaseModule()` and `.AddApplicationServices()`).

**Explicitly out of scope** (do not touch):
- The method body or any registered services inside `LogisticsModule.AddLogisticsModule()`.
- Any other identifier in the Logistics feature (e.g. `TransportBoxRepository`, `ITransportBoxCompletionService`, namespace `Anela.Heblo.Domain.Features.Logistics.Transport`).
- `docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md` — a dated, historical completed-plan record, not living documentation.
- No compatibility shim / deprecated pass-through wrapper — this is an internal DI composition method with one call site in the same solution, not a published API or NuGet contract.

## Files to create/modify
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — rename method declaration on line 17 from `AddTransportModule` to `AddLogisticsModule`.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — rename call site on line 92 from `services.AddTransportModule();` to `services.AddLogisticsModule();`.
- `docs/architecture/development_guidelines.md` — rename `.AddTransportModule()` to `.AddLogisticsModule()` on line 158, inside the API Composition example code block.
- `docs/architecture/infrastructure.md` — rename `.AddTransportModule()` to `.AddLogisticsModule()` on line 143, inside the Feature modules example code block.

## Implementation steps
1. In `LogisticsModule.cs`, rename the method declaration on line 17 from `AddTransportModule` to `AddLogisticsModule` (signature and body otherwise unchanged).
2. In `ApplicationModule.cs`, update the call on line 92 to `services.AddLogisticsModule();`.
3. In `docs/architecture/development_guidelines.md`, update the code block line `.AddTransportModule()` (line 158) to `.AddLogisticsModule()`.
4. In `docs/architecture/infrastructure.md`, update the code block line `.AddTransportModule()` (line 143) to `.AddLogisticsModule()`.
5. Grep the repo for any remaining `AddTransportModule` references outside the explicitly out-of-scope historical files (`docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md` and prior arch-review artifacts under `artifacts/`) to confirm the rename is total.
6. Run `dotnet build` on the backend solution to confirm no dangling references remain (a stale reference to the old name will fail to compile since this is a compiled extension method, not a string).
7. Run `dotnet format` to ensure formatting conventions are preserved.

## Tests to write
No behavior change occurs, so no new unit tests are required. Verification is via successful compilation: `dotnet build` must pass (a missed rename at the call site is a compile error, not a silent runtime issue), and `dotnet format` must pass with no diff-relevant formatting violations. Do not add a test asserting the old method name is absent — the build itself is the check.

## Acceptance criteria
- `LogisticsModule` no longer declares `AddTransportModule`; it declares `public static IServiceCollection AddLogisticsModule(this IServiceCollection services)` with an identical body to the current method.
- No remaining reference to `AddTransportModule()` in `ApplicationModule.cs`; `services.AddLogisticsModule();` is called during application service registration, preserving its current position in the registration sequence (between `AddManufactureModule(configuration)` and `AddGiftPackageManufactureModule()`).
- The code block in `docs/architecture/development_guidelines.md` no longer contains `AddTransportModule()` and contains `.AddLogisticsModule()` in the same position within the fluent chain (between `.AddManufactureModule()` and `.AddPurchaseModule()`).
- The code block in `docs/architecture/infrastructure.md` no longer contains `AddTransportModule()` and contains `.AddLogisticsModule()` in the same position within the fluent chain (between `.AddPurchaseModule()` and `.AddApplicationServices()`).
- A repo-wide search for `AddTransportModule` returns no hits outside the explicitly out-of-scope historical documents.
- `dotnet build` and `dotnet format` both pass.
