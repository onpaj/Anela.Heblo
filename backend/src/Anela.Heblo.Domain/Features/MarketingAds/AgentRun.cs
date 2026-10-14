namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Observability of an external agent run, reported by the agent itself (spec 6.5).</summary>
public class AgentRun
{
    public Guid Id { get; set; }
    public string AgentName { get; set; } = null!;
    public string Principal { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public AgentRunStatus Status { get; set; }
    public string? Summary { get; set; }
    public string DataScopesReadJson { get; set; } = "[]";
    public int ProposalsCreated { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostCzk { get; set; }

    public static AgentRun Start(Guid id, string agentName, string principal, string dataScopesJson, DateTime now) => new()
    {
        Id = id,
        AgentName = agentName,
        Principal = principal,
        StartedAt = now,
        Status = AgentRunStatus.Running,
        DataScopesReadJson = dataScopesJson,
    };

    public void Finish(AgentRunStatus status, string? summary, string dataScopesJson, int proposalsCreated,
        long? inputTokens, long? outputTokens, decimal? costCzk, DateTime now)
    {
        if (Status != AgentRunStatus.Running)
        {
            throw new InvalidOperationException($"Agent run {Id} is already {Status}.");
        }
        if (status == AgentRunStatus.Running)
        {
            throw new ArgumentException("A run cannot finish as Running.", nameof(status));
        }
        Status = status;
        Summary = summary;
        DataScopesReadJson = dataScopesJson;
        ProposalsCreated = proposalsCreated;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostCzk = costCzk;
        FinishedAt = now;
    }
}
