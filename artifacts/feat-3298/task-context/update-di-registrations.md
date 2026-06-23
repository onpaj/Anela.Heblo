### task: update-di-registrations

Wire the two implementations into `AddMicrosoft365Adapter()` and gut the now-redundant registration block from `UserManagementModule`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

- [ ] Edit `Microsoft365AdapterServiceCollectionExtensions.cs`. Add two new `using` directives and extend the if/else logic. The complete file after the edit:

```csharp
using Anela.Heblo.Adapters.Microsoft365.Photobank;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Microsoft365;

public static class Microsoft365AdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMicrosoft365Adapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

        if (!useMockAuth && !bypassJwt)
        {
            services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
            services.AddHttpClient("MicrosoftGraph", _ => { })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                });
            services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
            services.AddScoped<IGraphService, GraphService>();
        }
        else
        {
            services.AddScoped<IGraphService, MockGraphService>();
        }

        return services;
    }
}
```

- [ ] Edit `UserManagementModule.cs`. Remove: the entire `if (useMockAuth || bypassJwtValidation)` block that registers `IGraphService`, the `services.AddHttpClient("MicrosoftGraph")` call, and the `using Microsoft.Graph;` import at line 12. Also remove `using Anela.Heblo.Application.Features.UserManagement.Services;` if it is no longer needed (check whether `MockGraphService`/`GraphService` types are still referenced — after this task they will not be). The complete file after the edit:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Validators;
using Anela.Heblo.Domain.Features.Configuration;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.UserManagement;

public static class UserManagementModule
{
    public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-module contract: UserManagement implements Article's IArticleUserResolver via adapter.
        // IGraphService is registered by AddMicrosoft365Adapter() in the Adapters layer.
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

Note: `ConfigurationConstants` is no longer referenced in `UserManagementModule` after this change — remove its using import too (`using Anela.Heblo.Domain.Features.Configuration;`) only if it does not appear elsewhere in the file. Based on the current file content it is only used for `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.BYPASS_JWT_VALIDATION` in the removed block, so it should be removed.

- [ ] Run `dotnet build backend/backend.sln` and confirm zero errors before committing.

---