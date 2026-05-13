using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations.Validators;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence.Smartsupp;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Smartsupp;

public static class SmartsuppModule
{
    public static IServiceCollection AddSmartsuppModule(this IServiceCollection services)
    {
        services.AddScoped<ISmartsuppRepository, SmartsuppRepository>();

        services.AddScoped<IValidator<ListConversationsRequest>, ListConversationsValidator>();
        services.AddScoped<IPipelineBehavior<ListConversationsRequest, ListConversationsResponse>,
            ValidationBehavior<ListConversationsRequest, ListConversationsResponse>>();

        return services;
    }
}
