## Module
Configuration

## Finding
`backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` defines string constants for infrastructure environment-variable names:

```csharp
public static class InfrastructureConfigurationKeys
{
    public const string APP_VERSION = "APP_VERSION";          // CI/CD env var
    public const string USE_MOCK_AUTH = "UseMockAuth";        // auth bypass flag
    public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation"; // JWT bypass flag
}
```

These constants are consumed by:
- `GetConfigurationHandler` (Application layer) — uses `APP_VERSION` and `USE_MOCK_AUTH`
- `AuthenticationExtensions` (API layer) — uses `BYPASS_JWT_VALIDATION`
- `HangfireDashboardTokenAuthorizationFilter` (API layer) — uses `USE_MOCK_AUTH`
- `HangfireAuthenticationMiddleware` (API layer) — uses `USE_MOCK_AUTH`
- Several `*Module.cs` files — use `USE_MOCK_AUTH`

The class sits in `Domain.Shared` as a convenience so both Application and API layers can reference it without a cross-layer dependency. But the values are pure infrastructure/deployment metadata: environment variable names set by CI/CD and operational flags for bypassing authentication. None of these are business domain concepts.

The Domain layer is the innermost ring in Clean Architecture — it must have zero knowledge of infrastructure. Placing `USE_MOCK_AUTH` and `BYPASS_JWT_VALIDATION` in Domain couples the domain to operational deployment configuration.

## Why it matters
- **Clean Architecture violation**: Domain.Shared is imported by all outer layers. Infrastructure-concern constants placed here make the Domain polluted with operational knowledge.
- **Wrong dependency direction**: infrastructure config keys should flow inward from infrastructure → application, not originate in Domain and radiate outward.
- The naming `InfrastructureConfigurationKeys` acknowledges the infra nature — the file name correctly identifies the wrong home.

## Suggested fix
Move the class to `Application/Shared/` (renaming namespace accordingly). Callers in the Application layer import from Application. Callers in the API layer can either duplicate the two constants they need (two `const string` lines is not meaningful duplication) or define their own local constants in `API/Infrastructure/`:

```csharp
// backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs
namespace Anela.Heblo.Application.Shared;

public static class InfrastructureConfigurationKeys
{
    public const string APP_VERSION = "APP_VERSION";
    public const string USE_MOCK_AUTH = "UseMockAuth";
    public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
}
```

Update `using` directives in `GetConfigurationHandler` and all API-layer files. No logic changes. The Domain layer then has no knowledge of infrastructure key names.

---
_Filed by daily arch-review routine on 2026-07-27._
