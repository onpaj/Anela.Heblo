using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsHandler
    : IRequestHandler<PrintMaterialContainerLabelsRequest, PrintMaterialContainerLabelsResponse>
{
    private readonly ILogger<PrintMaterialContainerLabelsHandler> _logger;
    private readonly IMaterialContainerCodeGenerator _generator;
    private readonly IMaterialContainerRepository _repository;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ICurrentUserService _currentUserService;

    public PrintMaterialContainerLabelsHandler(
        ILogger<PrintMaterialContainerLabelsHandler> logger,
        IMaterialContainerCodeGenerator generator,
        IMaterialContainerRepository repository,
        ILabelPrintingService labelPrinter,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _generator = generator;
        _repository = repository;
        _labelPrinter = labelPrinter;
        _currentUserService = currentUserService;
    }

    public async Task<PrintMaterialContainerLabelsResponse> Handle(
        PrintMaterialContainerLabelsRequest request, CancellationToken cancellationToken)
    {
        var createdBy = _currentUserService.GetCurrentUser().Name ?? "System";

        var codes = await _generator.GenerateAsync(request.Count, cancellationToken);
        var containers = codes.Select(code => MaterialContainer.CreateUnassigned(code, createdBy)).ToList();

        await _repository.AddRangeAsync(containers, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var zpl = MaterialContainerLabelZplBuilder.Build(codes);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        _logger.LogInformation("Printed {Count} MaterialContainer labels", containers.Count);

        return new PrintMaterialContainerLabelsResponse
        {
            Containers = containers.Select(c => new MaterialContainerDto
            {
                Id = c.Id,
                Code = c.Code,
                MaterialCode = c.MaterialCode,
                LotCode = c.LotCode,
                Amount = c.Amount,
                Unit = c.Unit,
                Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt,
                CreatedBy = c.CreatedBy,
                PurchaseOrderLineId = c.PurchaseOrderLineId,
            }).ToList(),
        };
    }
}
