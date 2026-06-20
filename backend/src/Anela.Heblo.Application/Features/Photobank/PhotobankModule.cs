using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Photobank.Configuration;
using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRule;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds;
using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRoot;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRule;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule;
using Anela.Heblo.Application.Features.Photobank.Validators;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Photobank;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Photobank;

public static class PhotobankModule
{
    public static IServiceCollection AddPhotobankModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPhotobankRepository, PhotobankRepository>();
        services.AddScoped<PhotobankAutoTagJob>();
        services.Configure<AutoTagOptions>(configuration.GetSection(AutoTagOptions.SectionName));

        services.AddMemoryCache();
        services.Configure<PhotobankTagsCacheOptions>(
            configuration.GetSection(PhotobankTagsCacheOptions.SectionName));
        services.AddScoped<IPhotobankTagsCache, PhotobankTagsCache>();

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

        if (!useMockAuth && !bypassJwtValidation)
        {
            services.AddHttpClient("MicrosoftGraph", _ => { })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
            });
            services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
        }
        else
        {
            services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
        }

        // Register FluentValidation validators for photobank requests
        services.AddScoped<IValidator<AddPhotoTagRequest>, AddPhotoTagRequestValidator>();
        services.AddScoped<IValidator<AddRootRequest>, AddRootRequestValidator>();
        services.AddScoped<IValidator<AddRuleRequest>, AddRuleRequestValidator>();
        services.AddScoped<IValidator<DeleteRootRequest>, DeleteRootRequestValidator>();
        services.AddScoped<IValidator<DeleteRuleRequest>, DeleteRuleRequestValidator>();
        services.AddScoped<IValidator<RemovePhotoTagRequest>, RemovePhotoTagRequestValidator>();
        services.AddScoped<IValidator<GetPhotosRequest>, GetPhotosRequestValidator>();
        services.AddScoped<IValidator<UpdateRuleRequest>, UpdateRuleRequestValidator>();
        services.AddScoped<IValidator<BulkAddPhotoTagByIdsRequest>, BulkAddPhotoTagByIdsRequestValidator>();
        services.AddScoped<IValidator<CreateTagRequest>, CreateTagRequestValidator>();
        services.AddScoped<IValidator<DeleteTagRequest>, DeleteTagRequestValidator>();

        // Register MediatR validation behavior for photobank requests
        services.AddScoped<IPipelineBehavior<AddPhotoTagRequest, AddPhotoTagResponse>, ValidationBehavior<AddPhotoTagRequest, AddPhotoTagResponse>>();
        services.AddScoped<IPipelineBehavior<AddRootRequest, AddRootResponse>, ValidationBehavior<AddRootRequest, AddRootResponse>>();
        services.AddScoped<IPipelineBehavior<AddRuleRequest, AddRuleResponse>, ValidationBehavior<AddRuleRequest, AddRuleResponse>>();
        services.AddScoped<IPipelineBehavior<DeleteRootRequest, DeleteRootResponse>, ValidationBehavior<DeleteRootRequest, DeleteRootResponse>>();
        services.AddScoped<IPipelineBehavior<DeleteRuleRequest, DeleteRuleResponse>, ValidationBehavior<DeleteRuleRequest, DeleteRuleResponse>>();
        services.AddScoped<IPipelineBehavior<RemovePhotoTagRequest, RemovePhotoTagResponse>, ValidationBehavior<RemovePhotoTagRequest, RemovePhotoTagResponse>>();
        services.AddScoped<IPipelineBehavior<GetPhotosRequest, GetPhotosResponse>, ValidationBehavior<GetPhotosRequest, GetPhotosResponse>>();
        services.AddScoped<IPipelineBehavior<UpdateRuleRequest, UpdateRuleResponse>, ValidationBehavior<UpdateRuleRequest, UpdateRuleResponse>>();
        services.AddScoped<IPipelineBehavior<BulkAddPhotoTagByIdsRequest, BulkAddPhotoTagByIdsResponse>, ValidationBehavior<BulkAddPhotoTagByIdsRequest, BulkAddPhotoTagByIdsResponse>>();
        services.AddScoped<IPipelineBehavior<CreateTagRequest, CreateTagResponse>, ValidationBehavior<CreateTagRequest, CreateTagResponse>>();
        services.AddScoped<IPipelineBehavior<DeleteTagRequest, DeleteTagResponse>, ValidationBehavior<DeleteTagRequest, DeleteTagResponse>>();

        return services;
    }

}
