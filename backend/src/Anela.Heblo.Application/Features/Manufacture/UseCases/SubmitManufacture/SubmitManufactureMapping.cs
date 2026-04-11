using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

internal static class SubmitManufactureMapping
{
    public static SubmitManufactureClientRequest ToClientRequest(this SubmitManufactureRequest request)
    {
        return new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = request.ManufactureOrderNumber,
            ManufactureInternalNumber = request.ManufactureInternalNumber,
            Date = request.Date,
            CreatedBy = request.CreatedBy,
            ManufactureType = request.ManufactureType,
            Items = request.Items.Select(item => new SubmitManufactureClientItem
            {
                ProductCode = item.ProductCode,
                Amount = item.Amount,
                ProductName = item.Name,
            }).ToList(),
            LotNumber = request.LotNumber,
            ExpirationDate = request.ExpirationDate,
            ResidueDistribution = request.ResidueDistribution,
        };
    }
}
