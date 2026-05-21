using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
{
    public string Key { get; init; } = "";
    public bool IsEnabled { get; init; }
    public string UpdatedBy { get; init; } = "";
}

public class UpsertFlagOverrideResponse : BaseResponse
{
    public UpsertFlagOverrideResponse() { }
    public UpsertFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}
