using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.UserManagement;

public static class UserManagementModule
{
    public static IServiceCollection AddUserManagement(this IServiceCollection services)
    {
        // Register GraphService
        services.AddScoped<IGraphService, GraphService>();

        return services;
    }
}