# Code Review: register-adapter-in-di

## Summary
The implementation correctly registers `EntraAccessUserSourceAdapter` as the `IEntraAccessUserSource` binding in `UserManagementModule.cs`. The required using directive is present, the `AddScoped` line is placed logically alongside the other cross-module registration (`IArticleUserResolver`), and no other module registers this binding. Build is confirmed clean.

## Review Result: PASS

### task: register-adapter-in-di
**Status:** PASS

## Overall Notes
- `using Anela.Heblo.Application.Features.Authorization.Contracts;` is present at line 3. ✓
- `services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>();` is present at line 22. ✓
- A codebase-wide search for `IEntraAccessUserSource` found only three references: the interface definition, the adapter implementation, and this single registration — no duplicate registrations elsewhere. ✓
- The registration is grouped with the other infrastructure-level scoped service (`IArticleUserResolver`), which is consistent with the existing style in the module.
- `EntraAccessUserSourceAdapter` is declared `internal sealed`, which is appropriate for an adapter living in the same assembly as the module that registers it.
