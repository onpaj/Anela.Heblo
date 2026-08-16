using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;

public class CreateMindMapHandler : IRequestHandler<CreateMindMapRequest, CreateMindMapResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly ILogger<CreateMindMapHandler> _logger;

    public CreateMindMapHandler(IMindMapRepository repository, ILogger<CreateMindMapHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<CreateMindMapResponse> Handle(CreateMindMapRequest request, CancellationToken cancellationToken)
    {
        var rootId = Guid.NewGuid().ToString("N");
        var document = new MindMapDocument
        {
            RootNodeId = rootId,
            Nodes = new List<MindMapNode> { new() { Id = rootId, Title = request.Name } }
        };

        var now = DateTime.UtcNow;
        var map = new MindMap
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Status = MindMapStatus.Idle,
            CurrentJson = MindMapJson.Serialize(document),
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(map, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created mind map {MindMapId} '{Name}'", map.Id, map.Name);
        return new CreateMindMapResponse { Id = map.Id };
    }
}
