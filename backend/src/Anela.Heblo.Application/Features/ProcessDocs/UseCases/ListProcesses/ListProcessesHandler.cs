using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;

public class ListProcessesHandler : IRequestHandler<ListProcessesRequest, ListProcessesResponse>
{
    // Filename prefixes (e.g. "calc-margins") differ from the frontmatter `kind` they're
    // validated against (e.g. "calculation") — accept the prefix as an alias so a caller
    // filtering by what they see in the process name still gets a match.
    private static readonly Dictionary<string, string> KindAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calc"] = "calculation",
        ["flow"] = "workflow",
    };

    private readonly IProcessDocStore _store;

    public ListProcessesHandler(IProcessDocStore store)
    {
        _store = store;
    }

    public Task<ListProcessesResponse> Handle(ListProcessesRequest request, CancellationToken cancellationToken)
    {
        var docs = _store.All
            .Where(d => string.IsNullOrWhiteSpace(request.Kind) ||
                        string.Equals(d.Kind, ResolveKind(request.Kind), StringComparison.OrdinalIgnoreCase))
            .Where(d => string.IsNullOrWhiteSpace(request.Module) ||
                        string.Equals(d.Module, request.Module.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(new ListProcessesResponse { Processes = docs.Select(ToSummary).ToList() });
    }

    private static string ResolveKind(string kind)
    {
        var trimmed = kind.Trim();
        return KindAliases.TryGetValue(trimmed, out var resolved) ? resolved : trimmed;
    }

    internal static ProcessSummaryDto ToSummary(ProcessDoc doc) => new()
    {
        Name = doc.Name,
        Kind = doc.Kind,
        Module = doc.Module,
        Summary = doc.Summary,
        VerifiedAt = doc.VerifiedAt,
        Related = doc.Related.ToList(),
    };
}
