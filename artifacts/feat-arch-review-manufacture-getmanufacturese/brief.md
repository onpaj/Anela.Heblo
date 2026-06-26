## Module
Manufacture

## Finding
`GetManufactureSettingsHandler` injects `IConfiguration` and reads the Entra group ID via a raw string key constant, while every other configuration value in the module is consumed through typed options classes:

```csharp
// backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs
// lines 12–13, 25–26
private readonly IConfiguration _configuration;
...
var groupId = _configuration[ManufactureConfigurationKeys.GroupId];  // raw key "ManufactureGroupId"
```

`ManufactureConfigurationKeys.GroupId` is a bare string constant pointing at a flat top-level key. The module has two typed options classes already registered in `ManufactureModule.AddManufactureModule`:
- `ManufactureAnalysisOptions` (bound to `"ManufactureAnalysis"` section)
- `ManufactureErpOptions` (bound to `"ManufactureErp"` section)

Neither of them carries `ManufactureGroupId`.

## Why it matters
- **Consistency**: the rest of the module uses `IOptions<T>` injection. A reader of the handler is surprised to see raw `IConfiguration` in Application-layer code.
- **Testability**: unit tests for this handler must construct a full `IConfiguration` mock with a raw string key. With a typed options class they would just `new()` the options object — no config builder needed.
- **Type safety**: the raw-key approach gives no compile-time guarantee that the key exists in the options model; a rename of the string constant silently breaks the binding.

## Suggested fix
Add `ManufactureGroupId` to an existing or new typed options class and bind it in `ManufactureModule.cs`:

```csharp
// Option A: extend ManufactureErpOptions (already covers ERP/settings concerns)
public class ManufactureErpOptions
{
    public int ErpTimeoutSeconds { get; set; } = 60;
    public string? ManufactureGroupId { get; set; }   // add this
}

// ManufactureModule.cs — no change needed (already binds "ManufactureErp" section)
// appsettings: "ManufactureErp": { "ManufactureGroupId": "..." }

// GetManufactureSettingsHandler — replace IConfiguration with IOptions<ManufactureErpOptions>
public GetManufactureSettingsHandler(IOptions<ManufactureErpOptions> options, ILogger<...> logger)
{
    _options = options.Value;
}

public Task<...> Handle(...) =>
    Task.FromResult(new GetManufactureSettingsResponse
    {
        ManufactureGroupId = string.IsNullOrEmpty(_options.ManufactureGroupId) ? null : _options.ManufactureGroupId
    });
```

Delete `ManufactureConfigurationKeys.cs` if `GroupId` is its only member after the migration.

---
_Filed by daily arch-review routine on 2026-06-06._