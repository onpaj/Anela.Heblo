namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShipmentLabelsSettings
{
    public const string ConfigurationKey = "ShipmentLabels";

    public int DefaultPackageWidthMm { get; set; } = 300;

    public int DefaultPackageHeightMm { get; set; } = 200;

    public int DefaultPackageDepthMm { get; set; } = 150;

    public int MinPackageWeightGrams { get; set; } = 100;

    /// <summary>
    /// Package weight in grams used when the order's computed weight is 0 (no item has a known
    /// weight). Carriers reject a 0 kg package, so we fall back to this value instead of failing.
    /// </summary>
    public int FallbackPackageWeightGrams { get; set; } = 1000;
}
