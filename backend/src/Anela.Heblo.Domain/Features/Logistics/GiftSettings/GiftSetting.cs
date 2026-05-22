namespace Anela.Heblo.Domain.Features.Logistics.GiftSettings;

public class GiftSetting
{
    public int Id { get; private set; }
    public bool IsEnabled { get; private set; }
    public decimal ThresholdCzk { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    private GiftSetting() { }

    public static GiftSetting CreateDefault() => new() { Id = 1 };

    public GiftSetting(bool isEnabled, decimal thresholdCzk, string text, string modifiedBy)
    {
        Id = 1;
        IsEnabled = isEnabled;
        ThresholdCzk = thresholdCzk;
        Text = text;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    internal void Update(bool isEnabled, decimal thresholdCzk, string text, string modifiedBy)
    {
        IsEnabled = isEnabled;
        ThresholdCzk = thresholdCzk;
        Text = text;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
