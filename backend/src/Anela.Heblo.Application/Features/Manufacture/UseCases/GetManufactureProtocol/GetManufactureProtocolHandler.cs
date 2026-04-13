using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

public class GetManufactureProtocolHandler : IRequestHandler<GetManufactureProtocolRequest, GetManufactureProtocolResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly IManufactureClient _manufactureClient;
    private readonly IManufactureProtocolRenderer _renderer;

    public GetManufactureProtocolHandler(
        IManufactureOrderRepository repository,
        IManufactureClient manufactureClient,
        IManufactureProtocolRenderer renderer)
    {
        _repository = repository;
        _manufactureClient = manufactureClient;
        _renderer = renderer;
    }

    public async Task<GetManufactureProtocolResponse> Handle(
        GetManufactureProtocolRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"Manufacture order {request.Id} not found.");

        if (order.State != ManufactureOrderState.Completed)
        {
            throw new InvalidOperationException(
                $"Manufacture order {order.OrderNumber} is not completed; cannot generate protocol.");
        }

        var erpDocuments = await BuildErpDocumentsAsync(order, cancellationToken);

        var data = new ManufactureProtocolData
        {
            OrderNumber = order.OrderNumber,
            CreatedDate = order.CreatedDate,
            PlannedDate = order.PlannedDate,
            CompletedAt = order.StateChangedAt,
            ResponsiblePerson = order.ResponsiblePerson,
            ManufactureType = order.ManufactureType,
            SemiProduct = order.SemiProduct == null
                ? null
                : new ManufactureProtocolSemiProduct
                {
                    ProductCode = order.SemiProduct.ProductCode,
                    ProductName = order.SemiProduct.ProductName,
                    PlannedQuantity = order.SemiProduct.PlannedQuantity,
                    ActualQuantity = order.SemiProduct.ActualQuantity,
                    LotNumber = order.SemiProduct.LotNumber,
                    ExpirationDate = order.SemiProduct.ExpirationDate,
                },
            Products = order.Products.Select(p => new ManufactureProtocolProduct
            {
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                PlannedQuantity = p.PlannedQuantity,
                ActualQuantity = p.ActualQuantity,
                LotNumber = p.LotNumber,
                ExpirationDate = p.ExpirationDate,
            }).ToList(),
            ErpDocuments = erpDocuments,
            Notes = order.Notes.Select(n => new ManufactureProtocolNote
            {
                CreatedAt = n.CreatedAt,
                CreatedByUser = n.CreatedByUser,
                Text = n.Text,
            }).ToList(),
            GeneratedAt = DateTime.UtcNow,
        };

        var pdfBytes = _renderer.Render(data);

        return new GetManufactureProtocolResponse
        {
            PdfBytes = pdfBytes,
            FileName = $"ManufactureProtocol-{order.OrderNumber}.pdf",
        };
    }

    private async Task<List<ManufactureProtocolErpDocument>> BuildErpDocumentsAsync(
        ManufactureOrder order,
        CancellationToken cancellationToken)
    {
        var lookups = new List<(string Label, string? Code, DateTime? Date)>
        {
            ("Výdej materiálu (polotovar)", order.DocMaterialIssueForSemiProduct, order.DocMaterialIssueForSemiProductDate),
            ("Příjem polotovaru", order.DocSemiProductReceipt, order.DocSemiProductReceiptDate),
            ("Výdej polotovaru (výrobek)", order.DocSemiProductIssueForProduct, order.DocSemiProductIssueForProductDate),
            ("Výdej materiálu (výrobek)", order.DocMaterialIssueForProduct, order.DocMaterialIssueForProductDate),
            ("Příjem výrobku", order.DocProductReceipt, order.DocProductReceiptDate),
        };

        var populated = lookups.Where(l => !string.IsNullOrEmpty(l.Code)).ToList();

        var itemTasks = populated
            .Select(l => _manufactureClient.GetErpDocumentItemsAsync(l.Code!, cancellationToken: cancellationToken))
            .ToList();

        var itemResults = await Task.WhenAll(itemTasks);

        return populated.Select((l, idx) => new ManufactureProtocolErpDocument
        {
            DocumentType = l.Label,
            DocumentCode = l.Code!,
            DocumentDate = l.Date,
            Items = itemResults[idx].ToList(),
        }).ToList();
    }
}
