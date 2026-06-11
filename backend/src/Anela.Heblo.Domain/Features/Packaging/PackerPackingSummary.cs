namespace Anela.Heblo.Domain.Features.Packaging;

public sealed record PackerPackingSummary(
    Guid? PackedByUserId,
    string? PackedBy,
    int DistinctOrderCount);
