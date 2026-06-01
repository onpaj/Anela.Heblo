namespace Anela.Heblo.Domain.Features.Article;

public sealed class ArticleGenerationStep
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string StepName { get; set; } = "";
    public int Sequence { get; set; }
    public ArticleGenerationStepStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Model { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
}
