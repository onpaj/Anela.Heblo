using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateResponse : BaseResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();

    public static GetExpeditionListsByDateResponse InvalidDate() =>
        new()
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidFormat,
            Params = new Dictionary<string, string>
            {
                { "Field", "Date" },
                { "ExpectedFormat", "yyyy-MM-dd" }
            }
        };
}
