## Module
Configuration

## Finding
`ApplicationConfiguration` in `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs:12-18` sets `Timestamp = DateTime.UtcNow` inside its constructor:

```csharp
public ApplicationConfiguration(string version, string environment, bool useMockAuth)
{
    Version = version ?? throw new ArgumentNullException(nameof(version));
    Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    UseMockAuth = useMockAuth;
    Timestamp = DateTime.UtcNow;   // non-deterministic side effect in domain entity
}
```

`Timestamp` has no semantic meaning for the domain (application configuration does not have a "created at" concept) — it exists purely so the handler can copy it into `GetConfigurationResponse.Timestamp` and serialize it to the API caller.

## Why it matters
Two violations:

1. **SRP in the domain entity**: The `Timestamp` property is a transport concern (telling the HTTP consumer when the response was generated). Encoding it in a domain entity conflates domain state with serialization metadata.
2. **Non-determinism**: `DateTime.UtcNow` in the constructor makes every instantiation produce a different object. The unit tests in `GetConfigurationHandlerTests` cannot assert the exact timestamp value and instead assert `Timestamp <= DateTime.UtcNow.AddMinutes(1)` — a workaround for the non-determinism. Domain entities should be deterministic and fully controllable in tests.

## Suggested fix
Remove `Timestamp` from `ApplicationConfiguration` entirely. Set it directly in the handler at response-construction time:

```csharp
var response = new GetConfigurationResponse
{
    Version = appConfig.Version,
    Environment = appConfig.Environment,
    UseMockAuth = appConfig.UseMockAuth,
    Timestamp = DateTime.UtcNow,   // transport concern lives here, not in the domain entity
};
```

This keeps the domain entity pure and deterministic. The `ApplicationConfiguration` constructor and `CreateWithDefaults` factory become straightforward value holders with no side effects.

---
_Filed by daily arch-review routine on 2026-06-29._
