## Module
Configuration

## Finding
`GetConfigurationHandler.BuildApplicationConfigurationAsync()` is declared `async Task<ApplicationConfiguration>` but contains no awaitable I/O — it ends with `await Task.CompletedTask; // Placeholder for potential async operations`:

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:60–79`

The entire method is synchronous: it reads from `IConfiguration`, calls `Environment.GetEnvironmentVariable`, and calls `ApplicationConfiguration.CreateWithDefaults`. The `await Task.CompletedTask` at line 77 is an explicit placeholder for operations that do not exist today.

## Why it matters
This violates YAGNI: it designs for a speculative future requirement ("potential async operations") while adding real costs today — an unnecessary async state machine allocation on every request, and misleading readers into thinking the method performs I/O. A reader auditing slow paths or allocation pressure will spend time investigating a method that turns out to do nothing asynchronous.

## Suggested fix
Make the method synchronous and call it directly:

```csharp
// Before
private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()
{
    ...
    await Task.CompletedTask; // Placeholder for potential async operations
    return config;
}

// After
private ApplicationConfiguration BuildApplicationConfiguration()
{
    ...
    return config;
}
```

Update the single call site at line 32 from `await BuildApplicationConfigurationAsync()` to `BuildApplicationConfiguration()`. The `Handle` method remains `async Task<GetConfigurationResponse>` as required by MediatR — no interface change needed.

---
_Filed by daily arch-review routine on 2026-05-31._