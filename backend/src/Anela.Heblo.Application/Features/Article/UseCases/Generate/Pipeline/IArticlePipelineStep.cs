namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public interface IArticlePipelineStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
