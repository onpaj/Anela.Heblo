using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ReprintMaterialContainerLabel;

public class ReprintMaterialContainerLabelHandler
    : IRequestHandler<ReprintMaterialContainerLabelRequest, ReprintMaterialContainerLabelResponse>
{
    private readonly ILogger<ReprintMaterialContainerLabelHandler> _logger;
    private readonly IMaterialContainerRepository _repository;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPrinterMediaStateRepository _mediaStateRepository;

    public ReprintMaterialContainerLabelHandler(
        ILogger<ReprintMaterialContainerLabelHandler> logger,
        IMaterialContainerRepository repository,
        ILabelPrintingService labelPrinter,
        ICurrentUserService currentUserService,
        IPrinterMediaStateRepository mediaStateRepository)
    {
        _logger = logger;
        _repository = repository;
        _labelPrinter = labelPrinter;
        _currentUserService = currentUserService;
        _mediaStateRepository = mediaStateRepository;
    }

    public async Task<ReprintMaterialContainerLabelResponse> Handle(
        ReprintMaterialContainerLabelRequest request, CancellationToken cancellationToken)
    {
        var code = (request.Code ?? string.Empty).Trim();

        var container = await _repository.GetByCodeAsync(code, cancellationToken);
        if (container is null)
        {
            return new ReprintMaterialContainerLabelResponse(
                ErrorCodes.UnknownMaterialContainerCode,
                new Dictionary<string, string> { { "Code", code } });
        }

        if (container.Status == MaterialContainerStatus.Discarded)
        {
            return new ReprintMaterialContainerLabelResponse(
                ErrorCodes.MaterialContainerDiscarded,
                new Dictionary<string, string> { { "Code", code } });
        }

        var mediaState = await _mediaStateRepository.GetAsync(cancellationToken);
        if (mediaState.RequiresConfirmation(LabelMediaType.MaterialContainer) && !request.MediaChangeConfirmed)
        {
            return new ReprintMaterialContainerLabelResponse { RequiresMediaChangeConfirmation = true };
        }

        var zpl = MaterialContainerLabelZplBuilder.Build(new[] { container.Code });
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        var user = _currentUserService.GetCurrentUser().Name ?? "System";
        mediaState.RecordPrint(LabelMediaType.MaterialContainer, user);
        await _mediaStateRepository.SaveAsync(mediaState, cancellationToken);

        _logger.LogInformation("Reprinted MaterialContainer label {Code}", container.Code);

        return new ReprintMaterialContainerLabelResponse
        {
            Container = CreateMaterialContainers.CreateMaterialContainersHandler.MapToDto(container),
        };
    }
}
