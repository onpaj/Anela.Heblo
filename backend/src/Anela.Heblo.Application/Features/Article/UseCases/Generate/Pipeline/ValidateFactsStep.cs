using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class ValidateFactsStep : IArticlePipelineStep
{
    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly ILogger<ValidateFactsStep> _logger;

    public ValidateFactsStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<ValidateFactsStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        if (context.Facts.Count == 0)
            return;

        try
        {
            var claims = context.Facts.Select(f => f.Claim).ToList();
            var userMessage = JsonSerializer.Serialize(claims);

            var chatOptions = new ChatOptions { ModelId = _options.ValidateFactsModel };

            var response = await ChatRetry.RetryOnceAsync(
                () => _chat.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, _options.ValidateFactsSystemPrompt),
                        new ChatMessage(ChatRole.User, userMessage)
                    ],
                    chatOptions,
                    ct),
                _logger,
                ct);

            var raw = response.Text ?? string.Empty;
            var parsed = JsonResponseParser.ParseOrFallback<ValidatedFactsResponse>(
                raw,
                new ValidatedFactsResponse(null),
                _logger);

            if (parsed.ValidatedFacts == null)
                return;

            ApplyValidationNotes(context.Facts, parsed.ValidatedFacts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Fact validation failed; original facts preserved");
        }
    }

    private static void ApplyValidationNotes(
        List<AggregatedFact> facts,
        List<ValidatedFactDto> validatedFacts)
    {
        var count = Math.Min(facts.Count, validatedFacts.Count);
        for (var i = 0; i < count; i++)
        {
            facts[i].ValidationNote = validatedFacts[i].Note;
        }
    }

    private sealed record ValidatedFactsResponse(
        [property: JsonPropertyName("validated_facts")] List<ValidatedFactDto>? ValidatedFacts);

    private sealed record ValidatedFactDto(
        [property: JsonPropertyName("fact")] string Fact,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("reliable")] bool Reliable);
}
