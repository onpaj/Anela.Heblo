## Module
Manufacture

## Finding
`ManufactureModule.cs` (line 73–74) registers `NotImplementedManufactureProtocolRenderer` as the `IManufactureProtocolRenderer` binding:

```csharp
// Register protocol renderer placeholder (replaced by QuestPdfManufactureProtocolRenderer in Phase 6)
services.AddScoped<IManufactureProtocolRenderer, NotImplementedManufactureProtocolRenderer>();
```

`QuestPdfManufactureProtocolRenderer` has been implemented and is already registered later in `API/Extensions/ServiceCollectionExtensions.cs` (line 152), overriding the placeholder. Phase 6 is complete; the placeholder and its comment are now dead.

The current arrangement is fragile: the Application module registers a "valid" DI binding that throws `NotImplementedException` at runtime, silently depending on the API layer to override it. Any host that calls `AddManufactureModule()` without subsequently registering the real renderer gets an app that starts successfully but fails only when `GET /api/manufacture-order/{id}/protocol.pdf` is first hit.

## Why it matters
The DI graph should fail fast at startup if a required service is not registered. Registering a placeholder that deliberately throws hides this requirement from the compiler and from DI validation. It also leaves stale "Phase 6" archaeology in the Application layer.

## Suggested fix
Remove lines 73–74 from `ManufactureModule.cs`. `ServiceCollectionExtensions.cs` already owns the registration. If the Application module needs to document the requirement, a runtime guard (e.g. `services.AddSingleton<IManufactureProtocolRenderer>(_ => throw new InvalidOperationException(...))`) can replace the current placeholder — but simply removing the duplicate registration and relying on DI startup validation is the cleaner fix.

---
_Filed by daily arch-review routine on 2026-06-03._