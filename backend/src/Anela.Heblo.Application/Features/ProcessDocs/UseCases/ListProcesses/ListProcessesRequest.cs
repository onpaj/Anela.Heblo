using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;

public class ListProcessesRequest : IRequest<ListProcessesResponse>
{
    /// <summary>Optional filter: sync, calculation, feed, job, workflow or module.</summary>
    public string? Kind { get; set; }

    /// <summary>Optional filter: module slug, e.g. "catalog".</summary>
    public string? Module { get; set; }
}

public class ListProcessesResponse : BaseResponse
{
    public List<ProcessSummaryDto> Processes { get; set; } = [];
}
