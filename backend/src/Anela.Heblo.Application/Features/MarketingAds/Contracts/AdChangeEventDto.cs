namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public class AdChangeEventDto
{
    public long Id { get; set; }
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? Actor { get; set; }
    public AdChangeActorKind ActorKind { get; set; }
    public AdEntityLevel? EntityLevel { get; set; }
    public string? EntityExternalId { get; set; }
    public string? EntityName { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public AdChangeSource Source { get; set; }
    public AdChangeOrigin Origin { get; set; }
    public string? MatchedExecutionId { get; set; }
}
