# [arch-review] Logistics: LogisticsModule class exposes AddTransportModule() instead of AddLogisticsModule()

## Module
Logistics

## Finding
`backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` (line 17):

```csharp
public static class LogisticsModule
{
    public static IServiceCollection AddTransportModule(this IServiceCollection services)
```

The class is named `LogisticsModule` but the extension method is `AddTransportModule`. Every other module in the codebase follows the consistent pattern `{Feature}Module` → `Add{Feature}Module()`. The call site at `ApplicationModule.cs:92` uses `services.AddTransportModule()`, which also appears in the API Composition example in `docs/architecture/development_guidelines.md`, suggesting this inconsistency propagated into the documentation.

## Why it matters
A developer searching for how to register the Logistics module (e.g. when writing tests or checking what's registered) will look for `AddLogisticsModule()` and not find it. The mismatch suggests an incomplete rename from an older "Transport" module name. It also makes `development_guidelines.md` a less reliable template for the actual module naming pattern.

## Suggested fix
Rename the extension method to `AddLogisticsModule()`:

```csharp
// LogisticsModule.cs line 17
public static IServiceCollection AddLogisticsModule(this IServiceCollection services)
```

Update the call site in `ApplicationModule.cs:92`:
```csharp
services.AddLogisticsModule();
```

Update the example in `docs/architecture/development_guidelines.md` (the API Composition section) to replace `AddTransportModule()` with `AddLogisticsModule()`.

---
_Filed by daily arch-review routine on 2026-07-12._
