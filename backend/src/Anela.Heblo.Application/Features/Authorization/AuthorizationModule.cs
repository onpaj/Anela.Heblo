using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Authorization;

public static class AuthorizationModule
{
    public static IServiceCollection AddAuthorizationModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<IValidator<AddGroupMemberRequest>, AddGroupMemberValidator>();
        services.AddTransient<IPipelineBehavior<AddGroupMemberRequest, AddGroupMemberResponse>,
            ValidationBehavior<AddGroupMemberRequest, AddGroupMemberResponse>>();
        return services;
    }
}
