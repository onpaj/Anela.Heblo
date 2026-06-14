using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public record MaterialConsumptionHistoryFilter(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? PackingMaterialId,
    ConsumptionType? ConsumptionType,
    string? ProductCode,
    string? InvoiceId);
