## Module
Users

## Finding
`CurrentUserService` is placed in `Anela.Heblo.Application` but takes a direct dependency on `IHttpContextAccessor` from `Microsoft.AspNetCore.Http` — a web-framework type.

```csharp
// backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs, lines 1–14
using Microsoft.AspNetCore.Http;   // web infrastructure in Application layer

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    ...
}
```

The Application layer is intended to be independent of the web host. The DI registration in `ServiceCollectionExtensions.cs:130` also sits in the API project rather than in a dedicated `UsersModule.cs`, which is where any infrastructure wiring for this module belongs.

## Why it matters
Violates the Clean Architecture rule that Application must not depend on Infrastructure/framework types. It couples the Application project to the ASP.NET Core web host, making it harder to test in isolation and preventing future reuse outside a web context (e.g. background workers, console tools, integration tests).

## Suggested fix
Move `CurrentUserService` to the API project (e.g. `Anela.Heblo.API/Features/Users/`) or to an `Infrastructure/` subfolder within `Anela.Heblo.Application/Features/Users/`. The interface `ICurrentUserService` stays in Domain. The DI binding moves with the implementation (see companion issue about missing `UsersModule.cs`).

This is the same adapter pattern already documented in `development_guidelines.md` under *Cross-Module Communication* — infrastructure adapters live in the outer ring, not in Application.

---
_Filed by daily arch-review routine on 2026-05-25._