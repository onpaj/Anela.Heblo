using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
{
    public string Key { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public class UpsertFlagOverrideResponse : BaseResponse
{
    public UpsertFlagOverrideResponse() { }
    public UpsertFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}
