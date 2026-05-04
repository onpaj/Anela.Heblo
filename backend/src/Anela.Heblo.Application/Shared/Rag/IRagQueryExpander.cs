namespace Anela.Heblo.Application.Shared.Rag;

public interface IRagQueryExpander
{
    Task<string> ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct);
}

public sealed record RagQueryExpansionConfig(
    bool Enabled,
    string Model,
    string Prompt);
