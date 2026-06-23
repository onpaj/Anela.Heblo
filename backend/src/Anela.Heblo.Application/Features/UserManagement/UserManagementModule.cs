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
        // IGraphService (GraphService / MockGraphService) is registered by the adapter layer
        // via Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter().

        // Cross-module contract: UserManagement implements Article's IArticleUserResolver via adapter.
        // Works regardless of which IGraphService implementation (Mock vs real) is registered above.
        services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

        services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
            ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

        // Note: HttpContextAccessor must be registered in the API layer

        return services;
    }
}