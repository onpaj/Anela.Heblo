using MediatR;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;

public class GetDqtRunDetailRequest : IRequest<GetDqtRunDetailResponse>
{
    public Guid Id { get; set; }
    public int ResultPage { get; set; } = 1;
    public int ResultPageSize { get; set; } = 50;
}
