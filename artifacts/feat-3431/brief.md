## Module
Configuration

## Finding
`GetConfigurationHandler` in `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:19` injects `IHostEnvironment` (from `Microsoft.Extensions.Hosting`) directly into an Application-layer MediatR handler:

```csharp
public GetConfigurationHandler(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
```

It uses `_environment.EnvironmentName` at line 63 to obtain the ASP.NET Core environment name and pass it to the domain entity.

## Why it matters
`IHostEnvironment` is a hosting-layer abstraction — it belongs in the composition root (API project), not in the Application layer. The Application layer should depend on domain abstractions and generic platform contracts, not on ASP.NET Core/Generic Host concepts. This creates an upward dependency from Application into the host infrastructure and makes the handler harder to unit-test in isolation (the existing `GetConfigurationHandlerTests` mocks `IHostEnvironment` via NSubstitute, adding test friction that wouldn't exist with a simpler abstraction).

## Suggested fix
Replace the `IHostEnvironment` injection with the `IConfiguration` value that already flows through:

```csharp
var environment = _configuration["ASPNETCORE_ENVIRONMENT"] 
    ?? ConfigurationConstants.DEFAULT_ENVIRONMENT;
```

This keeps the handler fully within Application-layer contracts (`IConfiguration` is `Microsoft.Extensions.Configuration.Abstractions`, widely accepted in the Application layer), removes the hosting-layer dependency, and simplifies the constructor. The `IHostEnvironment` mock in `GetConfigurationHandlerTests` can then be deleted.

---
_Filed by daily arch-review routine on 2026-06-29._
