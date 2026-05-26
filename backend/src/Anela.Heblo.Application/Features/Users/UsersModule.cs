using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Users;

public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        return services;
    }
}
