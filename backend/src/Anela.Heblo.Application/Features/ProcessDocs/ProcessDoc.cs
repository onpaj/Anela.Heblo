namespace Anela.Heblo.Application.Features.ProcessDocs;

/// <summary>An agent-facing process doc from docs/processes/, parsed from its frontmatter.</summary>
public sealed record ProcessDoc(
    string Name,
    string Kind,
    string Summary,
    IReadOnlyList<string> Owns,
    string VerifiedAt,
    IReadOnlyList<string> Related,
    string Markdown);
