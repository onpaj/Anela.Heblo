### task: wire-adapter-di-registration

Move the `MicrosoftGraph` HttpClient and `PhotobankGraphService` DI registration out of `PhotobankModule` and into `Microsoft365AdapterServiceCollectionExtensions`. Remove the dead code from the module.

**Files:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`

**Steps:**

1. Open `Microsoft365AdapterServiceCollectionExtensions.cs`. Add the following `using` at the top:

```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Adapters.Microsoft365.Photobank;
```

2. Inside the `if (!useMockAuth && !bypassJwt)` block, after the existing `OutlookCalendarSyncService` line, add:

```csharp
services.AddHttpClient("MicrosoftGraph", _ => { })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
    });
services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
```

The full method should now look like:

```csharp
public static IServiceCollection AddMicrosoft365Adapter(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
    var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

    if (!useMockAuth && !bypassJwt)
    {
        services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
        services.AddHttpClient("MicrosoftGraph", _ => { })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
            });
        services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
    }

    return services;
}
```

3. Open `PhotobankModule.cs`. Locate and remove the entire `if (!useMockAuth && !bypassJwtValidation)` / `else` block (lines 43–57 in the current file). This block registered `MicrosoftGraph` HttpClient and `PhotobankGraphService`/`MockPhotobankGraphService`.

   **Keep only the mock fallback** in the module — registered unconditionally, because it is the application-layer default when no real adapter has wired up the service. The adapter registration (added above) will override it at runtime via DI because it is registered last (adapters are registered after modules in `Program.cs`). However, to avoid double-registration issues, register the mock **only when** in mock/bypass mode:

   Replace the entire block:
   ```csharp
   var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
   var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

   if (!useMockAuth && !bypassJwtValidation)
   {
       services.AddHttpClient("MicrosoftGraph", _ => { })
       .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
       {
           AllowAutoRedirect = true,
       });
       services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
   }
   else
   {
       services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
   }
   ```

   With:
   ```csharp
   var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
   var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

   if (useMockAuth || bypassJwtValidation)
   {
       services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
   }
   ```

   Also remove the now-unused `using` import for `PhotobankGraphService` if one was added (check: the original file does not import it explicitly because it is in the same namespace — so no `using` needs removing).

4. Build the full solution:

```
dotnet build backend/Anela.Heblo.sln
```

---

