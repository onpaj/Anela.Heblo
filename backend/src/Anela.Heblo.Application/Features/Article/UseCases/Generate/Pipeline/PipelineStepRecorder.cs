using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public sealed class PipelineStepRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IArticleRepository _repository;

    public PipelineStepRecorder(IArticleRepository repository) => _repository = repository;

    public async Task<T> RecordAsync<T>(
        Guid articleId,
        string stepName,
        int sequence,
        string? model,
        object? input,
        Func<CancellationToken, Task<(T result, object? output)>> action,
        CancellationToken ct)
    {
        var step = new ArticleGenerationStep
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            StepName = stepName,
            Sequence = sequence,
            Status = ArticleGenerationStepStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Model = model,
            InputJson = SerializeOrNull(input),
        };
        await _repository.AddStepAsync(step, ct);
        await _repository.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        try
        {
            var (result, output) = await action(ct);
            sw.Stop();
            step.Status = ArticleGenerationStepStatus.Succeeded;
            step.FinishedAt = DateTimeOffset.UtcNow;
            step.DurationMs = sw.ElapsedMilliseconds;
            step.OutputJson = SerializeOrNull(output);
            await _repository.UpdateStepAsync(step, ct);
            await _repository.SaveChangesAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            step.Status = ArticleGenerationStepStatus.Failed;
            step.FinishedAt = DateTimeOffset.UtcNow;
            step.DurationMs = sw.ElapsedMilliseconds;
            step.ErrorMessage = Truncate(ex.Message, 2000);
            await _repository.UpdateStepAsync(step, CancellationToken.None);
            await _repository.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string? SerializeOrNull(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
