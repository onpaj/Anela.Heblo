namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public sealed class LabelMatch
{
    public required string Family { get; init; }
    public required IReadOnlyList<string> Codes { get; init; }
    public required double Score { get; init; }
}

public sealed class LabelMatchResult
{
    public required LabelMatchDecision Decision { get; init; }
    public required IReadOnlyList<LabelMatch> Candidates { get; init; }
}
