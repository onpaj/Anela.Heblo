using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.LabelIdentification;

public static class LabelIdentificationModule
{
    public static IServiceCollection AddLabelIdentificationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LabelIdentificationOptions>(
            configuration.GetSection(LabelIdentificationOptions.SectionKey));

        // The index is immutable and parsed once from an embedded resource.
        services.AddSingleton<ILabelReferenceIndex, LabelReferenceIndex>();
        services.AddSingleton<ILabelMatcher, LabelMatcher>();
        services.AddScoped<ILabelOcrService, AnthropicLabelOcrService>();

        // Validators are registered explicitly per-module — this codebase has no
        // AddValidatorsFromAssembly.
        services.AddScoped<IValidator<IdentifyLabelRequest>, IdentifyLabelRequestValidator>();
        services.AddScoped<IPipelineBehavior<IdentifyLabelRequest, IdentifyLabelResponse>,
            ValidationBehavior<IdentifyLabelRequest, IdentifyLabelResponse>>();

        return services;
    }
}
