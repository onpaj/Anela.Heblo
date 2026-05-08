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
        Func<Task<(T result, object? output)>> action,
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
            var (result, output) = await action();
            sw.Stop();
            var succeeded = new ArticleGenerationStep
            {
                Id = step.Id,
                ArticleId = step.ArticleId,
                StepName = step.StepName,
                Sequence = step.Sequence,
                Model = step.Model,
                InputJson = step.InputJson,
                StartedAt = step.StartedAt,
                Status = ArticleGenerationStepStatus.Succeeded,
                FinishedAt = DateTimeOffset.UtcNow,
                DurationMs = sw.ElapsedMilliseconds,
                OutputJson = SerializeOrNull(output),
            };
            await _repository.UpdateStepAsync(succeeded, ct);
            await _repository.SaveChangesAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var failed = new ArticleGenerationStep
            {
                Id = step.Id,
                ArticleId = step.ArticleId,
                StepName = step.StepName,
                Sequence = step.Sequence,
                Model = step.Model,
                InputJson = step.InputJson,
                StartedAt = step.StartedAt,
                Status = ArticleGenerationStepStatus.Failed,
                FinishedAt = DateTimeOffset.UtcNow,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = Truncate(ex.Message, 2000),
            };
            await _repository.UpdateStepAsync(failed, CancellationToken.None);
            await _repository.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string? SerializeOrNull(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
