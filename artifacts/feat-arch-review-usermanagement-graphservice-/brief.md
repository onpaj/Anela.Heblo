## Module
UserManagement

## Finding
`GraphService.GetGroupMembersAsync` instantiates a raw `HttpClient` via `using var httpClient = new HttpClient()` on every call (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`, line 84).

Although `using` disposes the client after each call, this pattern:
- Bypasses connection pooling managed by `IHttpClientFactory` — a new TCP socket is opened and closed per Graph call.
- Ignores DNS TTL caching — DNS entries for `graph.microsoft.com` are not refreshed correctly when sockets are disposed and recreated.
- Is the primary cause of socket exhaustion under load in .NET applications, as documented in [Microsoft's guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines).

The 20-minute in-memory cache partially mitigates the frequency of calls, but does not eliminate the risk.

## Why it matters
Each cache miss (every 20 minutes, or on first startup) opens a new socket. Under concurrent requests this can multiply quickly. The Microsoft Graph endpoint also enforces connection limits — exhausted sockets result in `SocketException` or throttling, silently returning empty member lists (see related issue on silent failure).

## Suggested fix
Inject `IHttpClientFactory` into `GraphService` and replace the inline instantiation:

```csharp
// Constructor
public GraphService(ITokenAcquisition tokenAcquisition, IMemoryCache cache,
    ILogger<GraphService> logger, IHttpClientFactory httpClientFactory)
{
    _httpClientFactory = httpClientFactory;
    // ...
}

// In GetGroupMembersAsync — replace lines 84–85:
using var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");
```

Register a named client in `UserManagementModule.AddUserManagement`:
```csharp
services.AddHttpClient("MicrosoftGraph");
```

---
_Filed by daily arch-review routine on 2026-05-24._