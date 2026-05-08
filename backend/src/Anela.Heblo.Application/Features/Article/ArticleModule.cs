using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
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
