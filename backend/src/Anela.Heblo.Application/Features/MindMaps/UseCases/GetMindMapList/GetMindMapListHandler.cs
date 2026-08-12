using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;

public class GetMindMapListHandler : IRequestHandler<GetMindMapListRequest, GetMindMapListResponse>
{
    private readonly IMindMapRepository _repository;

    public GetMindMapListHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMindMapListResponse> Handle(GetMindMapListRequest request, CancellationToken cancellationToken)
    {
        var maps = await _repository.GetListAsync(cancellationToken);
        return new GetMindMapListResponse
        {
            Items = maps.Select(m => new MindMapListItemDto
            {
                Id = m.Id,
                Name = m.Name,
                Description = m.Description,
                Status = m.Status.ToString(),
                MeetingCount = m.Meetings.Count,
                UpdatedAt = m.UpdatedAt
            }).ToList()
        };
    }
}
