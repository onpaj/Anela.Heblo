using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;

public class ListProcessesHandler : IRequestHandler<ListProcessesRequest, ListProcessesResponse>
{
    private readonly IProcessDocStore _store;

    public ListProcessesHandler(IProcessDocStore store)
    {
        _store = store;
    }

    public Task<ListProcessesResponse> Handle(ListProcessesRequest request, CancellationToken cancellationToken)
    {
        var docs = string.IsNullOrWhiteSpace(request.Kind)
            ? _store.All
            : _store.All.Where(d => string.Equals(d.Kind, request.Kind.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        return Task.FromResult(new ListProcessesResponse { Processes = docs.Select(ToSummary).ToList() });
    }

    internal static ProcessSummaryDto ToSummary(ProcessDoc doc) => new()
    {
        Name = doc.Name,
        Kind = doc.Kind,
        Summary = doc.Summary,
        VerifiedAt = doc.VerifiedAt,
        Related = doc.Related.ToList(),
    };
}
