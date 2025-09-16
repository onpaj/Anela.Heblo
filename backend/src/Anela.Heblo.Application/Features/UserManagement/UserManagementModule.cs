using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Graph;
using Microsoft.AspNetCore.Http;

namespace Anela.Heblo.Application.Features.UserManagement;

public static class UserManagementModule
{
    public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
    {
        // Check if mock authentication is enabled
        var useMockAuth = configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        if (useMockAuth || bypassJwtValidation)
        {
            // Register mock GraphService for mock authentication
            services.AddScoped<IGraphService, MockGraphService>();
        }
        else
        {
            // Register real GraphService for production authentication
            services.AddScoped<IGraphService, GraphService>();

            // Note: GraphServiceClient must be registered in the API layer with proper authentication
            // through Microsoft.Identity.Web's AddMicrosoftGraph() method
        }

        // Note: HttpContextAccessor must be registered in the API layer

        return services;
    }
}