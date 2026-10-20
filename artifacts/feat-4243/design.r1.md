# Design: Use ConfigurationConstants defaults in ApplicationConfiguration.CreateWithDefaults

## Component Design
No new or restructured components. The existing static factory method is edited in place:

- `ApplicationConfiguration.CreateWithDefaults(string? version, string? environment, bool useMockAuth)` — `Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`
  - Responsibility unchanged: build an `ApplicationConfiguration` instance, falling back to defaults when `version`/`environment` are `null`.
  - Contract unchanged: same signature, same return type, same `??` fallback logic.
  - Internal change only: the fallback expressions reference `ConfigurationConstants.DEFAULT_VERSION` and `ConfigurationConstants.DEFAULT_ENVIRONMENT` (already defined in the sibling file `ConfigurationConstants.cs`, same namespace) instead of the literals `"1.0.0"` and `"Production"`.

```csharp
public static ApplicationConfiguration CreateWithDefaults(string? version, string? environment, bool useMockAuth)
{
    return new ApplicationConfiguration(
        version ?? ConfigurationConstants.DEFAULT_VERSION,
        environment ?? ConfigurationConstants.DEFAULT_ENVIRONMENT,
        useMockAuth
    );
}
```

The single caller, `GetConfigurationHandler` (Application layer), requires no change and observes identical output.

## Data Schemas
No data schema, DTO, or API shape changes. `ApplicationConfiguration` keeps its existing properties (`Version: string`, `Environment: string`, `UseMockAuth: bool`), and `CreateWithDefaults(null, null, false)` continues to yield `Version == "1.0.0"`, `Environment == "Production"` — now sourced from `ConfigurationConstants` rather than duplicated literals.
