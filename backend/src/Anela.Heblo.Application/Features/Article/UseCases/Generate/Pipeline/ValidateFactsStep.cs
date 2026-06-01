using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class ValidateFactsStep
{
    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly ILogger<ValidateFactsStep> _logger;
    private readonly PipelineStepRecorder _recorder;

    public ValidateFactsStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<ValidateFactsStep> logger,
        PipelineStepRecorder recorder)
    {
        _chat = chat;
        _options = options.Value;
        _logger = logger;
        _recorder = recorder;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        await _recorder.RecordAsync<bool>(
            context.Article.Id,
            "ValidateFacts",
            4,
            _options.ValidateFactsModel,
            new { factCount = context.Facts.Count },
            async (token) =>
            {
                if (context.Facts.Count == 0)
                    return (true, (object?)new { skipped = true });

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
                            token),
                        _logger,
                        token);

                    var raw = response.Text ?? string.Empty;
                    var parsed = JsonResponseParser.ParseOrFallback<ValidatedFactsOutput>(
                        raw,
                        new ValidatedFactsOutput(null),
                        _logger);

                    if (parsed.ValidatedFacts != null)
                        context.Facts = ApplyValidationNotes(context.Facts, parsed.ValidatedFacts);

                    return (true, (object?)new { rawResponse = raw, validatedFacts = parsed.ValidatedFacts });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Fact validation failed; original facts preserved");
                    return (true, (object?)new { error = ex.Message, factsPreserved = true });
                }
            },
            ct);
    }

    private static List<AggregatedFact> ApplyValidationNotes(
        List<AggregatedFact> facts,
        List<ValidatedFactDto> validatedFacts)
    {
        return facts
            .Select((f, i) => i < validatedFacts.Count
                ? f with { ValidationNote = validatedFacts[i].Note }
                : f)
            .ToList();
    }

    private sealed record ValidatedFactsOutput(
        [property: JsonPropertyName("validated_facts")] List<ValidatedFactDto>? ValidatedFacts);

    private sealed record ValidatedFactDto(
        [property: JsonPropertyName("fact")] string Fact,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("reliable")] bool Reliable);
}
