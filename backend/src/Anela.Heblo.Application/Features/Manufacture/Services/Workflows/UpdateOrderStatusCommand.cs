using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

internal sealed record ManufactureDocumentCodes(
    string? SemiProduct,
    string? Product,
    string? Discard);

internal sealed record WeightToleranceInfo(
    bool WithinTolerance,
    decimal Difference);

internal sealed record UpdateOrderStatusCommand(
    int OrderId,
    ManufactureOrderState TargetState,
    string ChangeReason,
    string Note,
    ManufactureDocumentCodes Documents,
    bool ManualActionRequired,
    WeightToleranceInfo? WeightTolerance);
