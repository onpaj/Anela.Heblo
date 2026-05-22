namespace Anela.Heblo.Application.Features.GiftSettings.Dto;

public sealed class GiftSettingDto
{
    public bool IsEnabled { get; set; }
    public decimal ThresholdCzk { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
