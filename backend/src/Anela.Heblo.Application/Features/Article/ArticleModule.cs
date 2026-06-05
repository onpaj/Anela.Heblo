using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Persistence.Features.Article;
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
        services.AddScoped<PlanQueriesStep>();
        services.AddScoped<GatherContextStep>();
        services.AddScoped<AggregateFactsStep>();
        services.AddScoped<ValidateFactsStep>();
        services.AddScoped<WriteArticleStep>();
        services.AddScoped<GenerateArticleJob>();

        return services;
    }
}
