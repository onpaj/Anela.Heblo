namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtDriftResultDto
{
    public string EntityKey { get; set; } = string.Empty;
    public int MismatchCode { get; set; }
    public string TestType { get; set; } = string.Empty;
    public string? HebloValue { get; set; }
    public string? ShoptetValue { get; set; }
    public string? Details { get; set; }
}
