## Module
Users

## Finding
`development_guidelines.md` requires every module to own a `{Feature}Module.cs` for DI registration. The Users module has no such file. Instead, the `ICurrentUserService → CurrentUserService` binding is wired directly in the API project:

```csharp
// backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs, line 130
services.AddSingleton<ICurrentUserService, CurrentUserService>();
```

Every other module (e.g. `GridLayoutsModule.cs`, `JournalModule.cs`) registers its own services via a dedicated `Module.cs`. Users is the only module that breaks this convention.

## Why it matters
The module registration pattern is the project's documented mechanism for module isolation and makes each module self-describing. Embedding the binding in `ServiceCollectionExtensions` (a composition-root utility) violates that boundary and makes Users the odd one out — future developers adding Users-related services have no obvious place to put registrations.

## Suggested fix
Create `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs` (or wherever the implementation ultimately lives after resolving #1716):

```csharp
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        return services;
    }
}
```

Then replace the inline registration in `ServiceCollectionExtensions.cs` with `.AddUsersModule()`.

---
_Filed by daily arch-review routine on 2026-05-25._