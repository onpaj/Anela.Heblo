using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Persistence.Features.Article;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Article;

public static class ArticleModule
{
    public static IServiceCollection AddArticleModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ArticleOptions>()
            .Bind(configuration.GetSection(ArticleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Repositories (implementations live in the Persistence layer)
        services.AddScoped<IArticleRepository, ArticleRepository>();
        services.AddScoped<IArticleAdminRepository, ArticleAdminRepository>();

        services.AddScoped<PipelineStepRecorder>();
        services.AddScoped<IPlanQueriesStep, PlanQueriesStep>();
        services.AddScoped<IGatherContextStep, GatherContextStep>();
        services.AddScoped<IAggregateFactsStep, AggregateFactsStep>();
        services.AddScoped<IValidateFactsStep, ValidateFactsStep>();
        services.AddScoped<IWriteArticleStep, WriteArticleStep>();
        services.AddScoped<GenerateArticleJob>();

        services.AddScoped<IValidator<ListArticlesRequest>, ListArticlesRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<ListArticlesRequest, ListArticlesResponse>,
            ValidationResultBehavior<ListArticlesRequest, ListArticlesResponse>>();

        return services;
    }
}
