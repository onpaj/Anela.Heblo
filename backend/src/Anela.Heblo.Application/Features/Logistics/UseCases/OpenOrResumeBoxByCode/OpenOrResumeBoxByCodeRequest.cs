using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;

public class OpenOrResumeBoxByCodeRequest : IRequest<OpenOrResumeBoxByCodeResponse>
{
    public string BoxCode { get; set; } = string.Empty;
}
