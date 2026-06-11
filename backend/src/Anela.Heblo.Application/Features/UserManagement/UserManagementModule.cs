using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;

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
            // Register the named "MicrosoftGraph" HttpClient for IHttpClientFactory.
            // Matches the shared "MicrosoftGraph" named client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules.
            services.AddHttpClient("MicrosoftGraph");

            // Register real GraphService for production authentication
            services.AddScoped<IGraphService, GraphService>();

            // Note: GraphServiceClient must be registered in the API layer with proper authentication
            // through Microsoft.Identity.Web's AddMicrosoftGraph() method
        }

        // Cross-module contract: UserManagement implements Article's IArticleUserResolver via adapter.
        // Works regardless of which IGraphService implementation (Mock vs real) is registered above.
        services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

        // Note: HttpContextAccessor must be registered in the API layer

        return services;
    }
}