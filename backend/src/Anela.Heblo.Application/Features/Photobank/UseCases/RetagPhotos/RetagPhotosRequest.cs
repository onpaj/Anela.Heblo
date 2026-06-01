using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;

public class RetagPhotosRequest : IRequest<RetagPhotosResponse>
{
    public int[] PhotoIds { get; set; } = Array.Empty<int>();
    public bool ClearExistingAiTags { get; set; }
}
