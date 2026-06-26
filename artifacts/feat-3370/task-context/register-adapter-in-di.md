### task: register-adapter-in-di

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

- [ ] **Step 1: Add one `AddScoped` line after the existing `IArticleUserResolver` registration.**

  Current file content:

  ```csharp
  using Anela.Heblo.Application.Common.Behaviors;
  using Anela.Heblo.Application.Features.Article.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
  using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
  using Anela.Heblo.Application.Features.UserManagement.Validators;
  using FluentValidation;
  using MediatR;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.UserManagement;

  public static class UserManagementModule
  {
      public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
      {
          // IGraphService is registered by the adapter layer via AddMicrosoft365Adapter(), not here.

          // Cross-module: IArticleUserResolver delegates to IGraphService (Mock or real) from adapter layer.
          services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

          services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
          services.AddScoped<
              IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
              ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

          // Note: HttpContextAccessor must be registered in the API layer

          return services;
      }
  }
  ```

  Updated file content — add one using and one `AddScoped` line:

  ```csharp
  using Anela.Heblo.Application.Common.Behaviors;
  using Anela.Heblo.Application.Features.Article.Contracts;
  using Anela.Heblo.Application.Features.Authorization.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
  using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
  using Anela.Heblo.Application.Features.UserManagement.Validators;
  using FluentValidation;
  using MediatR;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.UserManagement;

  public static class UserManagementModule
  {
      public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
      {
          // IGraphService is registered by the adapter layer via AddMicrosoft365Adapter(), not here.

          // Cross-module: IArticleUserResolver delegates to IGraphService (Mock or real) from adapter layer.
          services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();
          services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>();

          services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
          services.AddScoped<
              IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
              ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

          // Note: HttpContextAccessor must be registered in the API layer

          return services;
      }
  }
  ```

- [ ] **Step 2: Verify the file compiles.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: build succeeds.

---
