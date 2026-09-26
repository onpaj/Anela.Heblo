## Module
Configuration

## Finding
`GetConfigurationHandler.Handle` is declared `async Task<GetConfigurationResponse>` but contains no `await` expression. The C# compiler emits a full async state machine for this method despite it doing only synchronous work (reading `IConfiguration`, constructing a POCO, and returning).

File: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`, lines 24–41.

```csharp
// Current — async with no await (compiler warning CS1998)
public async Task<GetConfigurationResponse> Handle(
    GetConfigurationRequest request, CancellationToken cancellationToken)
{
    // ... purely synchronous ...
    return response;
}
```

## Why it matters
- Generates an unnecessary `IAsyncStateMachine` allocation on every request, including the cached `staleTime: Infinity` frontend call that fires on every app boot.
- The C# compiler issues warning CS1998 ("This async method lacks 'await' operators and will run synchronously") for exactly this pattern.
- Misleads readers into expecting I/O — the signature promises async work that does not exist.

## Suggested fix
Remove `async`, return `Task.FromResult`:

```csharp
public Task<GetConfigurationResponse> Handle(
    GetConfigurationRequest request, CancellationToken cancellationToken)
{
    _logger.LogDebug("Handling GetConfiguration request");
    var appConfig = BuildApplicationConfiguration();
    var response = new GetConfigurationResponse
    {
        Version = appConfig.Version,
        Environment = appConfig.Environment,
        UseMockAuth = appConfig.UseMockAuth,
        Timestamp = DateTime.UtcNow,
    };
    _logger.LogDebug("Configuration retrieved successfully: {@Config}", response);
    return Task.FromResult(response);
}
```

No test changes required — the existing handler tests call `await handler.Handle(...)` which works identically.

---
_Filed by daily arch-review routine on 2026-09-24._
