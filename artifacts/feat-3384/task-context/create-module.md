### task: create-module

Create the module registration file and the three DTO files in the new location.

#### `BackgroundRefreshModule.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the assembly scan in ApplicationModule.
        // IBackgroundRefreshTaskRegistry is registered as a singleton by XccModule — do not re-register.
        return services;
    }
}
```

#### `Contracts/RefreshTaskDto.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

public class RefreshTaskDto
{
    public required string TaskId { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public required bool Enabled { get; init; }
    public int HydrationTier { get; init; }
    public DateTime? NextScheduledRun { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}
```

#### `Contracts/RefreshTaskExecutionLogDto.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

public class RefreshTaskExecutionLogDto
{
    public required string TaskId { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? Duration { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

#### `Contracts/RefreshTaskStatusDto.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

public class RefreshTaskStatusDto
{
    public required string TaskId { get; init; }
    public required bool Enabled { get; init; }
    public string? Description { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}
```

#### Register in `ApplicationModule.cs`

Add the following using and call **after** the existing `services.AddBackgroundJobsModule()` line:

```csharp
// new using at the top of ApplicationModule.cs:
using Anela.Heblo.Application.Features.BackgroundRefresh;

// in AddApplicationServices, after services.AddBackgroundJobsModule():
services.AddBackgroundRefreshModule();
```

#### Verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expect: build succeeds, no errors about the new files.
