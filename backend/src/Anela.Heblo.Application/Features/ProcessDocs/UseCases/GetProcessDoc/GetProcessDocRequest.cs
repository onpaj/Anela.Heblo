using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;

public class GetProcessDocRequest : IRequest<GetProcessDocResponse>
{
    public string Name { get; set; } = string.Empty;
}

public class GetProcessDocResponse : BaseResponse
{
    /// <summary>Null when no process has that name; see <see cref="Suggestions"/>.</summary>
    public ProcessDocDto? Doc { get; set; }

    public List<string> Suggestions { get; set; } = [];
}
