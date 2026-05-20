using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;

public class GetVisitorInfoResponse : BaseResponse
{
    public VisitorInfoDto? VisitorInfo { get; set; }

    public GetVisitorInfoResponse() { }
    public GetVisitorInfoResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class VisitorInfoDto
{
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }
    public string? UserAgent { get; set; }
    public int? VisitsCount { get; set; }
    public int ChatsCount { get; set; }
    public List<VisitorPageDto> Pages { get; set; } = [];
}

public class VisitorPageDto
{
    public string Url { get; set; } = null!;
}
