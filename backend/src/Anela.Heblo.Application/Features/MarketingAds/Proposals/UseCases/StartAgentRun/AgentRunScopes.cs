namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

/// <summary>Shared validation of the data scopes an agent reports; used by start and finish. char.IsControl covers U+0000.</summary>
public static class AgentRunScopes
{
    public static bool AreValid(IReadOnlyCollection<string>? scopes) =>
        scopes is null
        || (scopes.Count <= StartAgentRunRequest.MaxScopes
            && scopes.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= StartAgentRunRequest.MaxScopeLength && !s.Any(char.IsControl)));
}
