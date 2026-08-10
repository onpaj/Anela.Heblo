using System.Text.Json;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;

public class SaveMindMapDocumentHandler : IRequestHandler<SaveMindMapDocumentRequest, SaveMindMapDocumentResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly MindMapLockService _lockService;
    private readonly ICurrentUserService _currentUserService;

    public SaveMindMapDocumentHandler(
        IMindMapRepository repository,
        MindMapLockService lockService,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _lockService = lockService;
        _currentUserService = currentUserService;
    }

    public async Task<SaveMindMapDocumentResponse> Handle(SaveMindMapDocumentRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new SaveMindMapDocumentResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new SaveMindMapDocumentResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        MindMapDocument submitted;
        try
        {
            submitted = MindMapJson.Deserialize(request.DocumentJson);
        }
        catch (JsonException ex)
        {
            return new SaveMindMapDocumentResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Error", ex.Message } });
        }

        var errors = MindMapDocumentValidator.Validate(submitted);
        if (errors.Count > 0)
        {
            return new SaveMindMapDocumentResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Errors", string.Join(" ", errors) } });
        }

        var current = MindMapJson.Deserialize(map.CurrentJson);
        if (submitted.RootNodeId != current.RootNodeId)
        {
            return new SaveMindMapDocumentResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Errors", "Root node cannot be changed." } });
        }

        var userEmail = _currentUserService.GetCurrentUser().Email;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return new SaveMindMapDocumentResponse(ErrorCodes.ValidationError);
        }

        var result = _lockService.ApplyUserEdit(current, submitted, userEmail);
        map.CurrentJson = MindMapJson.Serialize(result);
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        return new SaveMindMapDocumentResponse { DocumentJson = map.CurrentJson };
    }
}
