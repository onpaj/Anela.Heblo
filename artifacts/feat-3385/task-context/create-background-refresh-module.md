### task: create-background-refresh-module

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs`

**Goal:** Create the new Application module folder and registration class so `ApplicationModule.cs` can call `AddBackgroundRefreshModule()`.

**Steps:**
- [ ] Step 1: Create `BackgroundRefreshModule.cs` at `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs` with the following content:
  ```csharp
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.BackgroundRefresh;

  public static class BackgroundRefreshModule
  {
      public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
      {
          // No Application-layer services to register yet.
          // BackgroundRefreshController wires directly to IBackgroundRefreshTaskRegistry (Xcc).
          // MediatR handlers will be added here when the HTTP surface is migrated to CQRS.
          return services;
      }
  }
  ```

**Acceptance criteria:**
- File exists at the path above.
- Namespace is `Anela.Heblo.Application.Features.BackgroundRefresh`.
- Class is `public static` with a single `AddBackgroundRefreshModule` extension method returning `IServiceCollection`.

---