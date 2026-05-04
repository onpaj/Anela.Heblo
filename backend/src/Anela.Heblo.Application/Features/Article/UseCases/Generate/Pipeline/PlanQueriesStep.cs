using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class PlanQueriesStep : IArticlePipelineStep
{
    private const int MaxQueries = 8;

    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly ILogger<PlanQueriesStep> _logger;

    public PlanQueriesStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<PlanQueriesStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var topic = context.Article.Topic;

        var chatOptions = new ChatOptions
        {
            ModelId = _options.QueryPlannerModel,
            MaxOutputTokens = 512
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, _options.QueryPlannerSystemPrompt),
                    new ChatMessage(ChatRole.User, topic)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;
        var fallback = BuildFallback(topic);

        var parsed = JsonResponseParser.ParseOrFallback<QueryPlanOutput>(raw, new QueryPlanOutput([]), _logger);
        var queries = parsed.Queries is { Count: > 0 }
            ? parsed.Queries.Take(MaxQueries).ToList()
            : fallback;

        context.SearchQueries = queries;
    }

    private static List<string> BuildFallback(string topic) =>
        [topic, $"{topic} statistiky", $"{topic} recenze"];

    private sealed record QueryPlanOutput(
        [property: JsonPropertyName("queries")] List<string> Queries);
}
