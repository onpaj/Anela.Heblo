using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.Features.Users;

public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // Lifetime preserved from prior registration; HttpContextAccessor uses AsyncLocal
        // so per-request reads remain correct under a singleton.
        services.AddSingleton<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
