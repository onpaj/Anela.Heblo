using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;

public class ListProcessesRequest : IRequest<ListProcessesResponse>
{
    /// <summary>Optional filter: sync, calculation or feed.</summary>
    public string? Kind { get; set; }
}

public class ListProcessesResponse : BaseResponse
{
    public List<ProcessSummaryDto> Processes { get; set; } = [];
}
