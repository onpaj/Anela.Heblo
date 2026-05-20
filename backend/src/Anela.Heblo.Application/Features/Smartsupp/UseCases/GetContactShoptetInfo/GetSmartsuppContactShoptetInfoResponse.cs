using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoResponse : BaseResponse
{
    public ShoptetContactInfoDto? ContactInfo { get; set; }

    public GetSmartsuppContactShoptetInfoResponse() { }
    public GetSmartsuppContactShoptetInfoResponse(ErrorCodes errorCode) : base(errorCode) { }
}
