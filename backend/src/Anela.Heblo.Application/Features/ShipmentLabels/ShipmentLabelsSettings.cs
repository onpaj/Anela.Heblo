namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShipmentLabelsSettings
{
    public const string ConfigurationKey = "ShipmentLabels";

    public int DefaultPackageWidthMm { get; set; } = 300;

    public int DefaultPackageHeightMm { get; set; } = 200;

    public int DefaultPackageDepthMm { get; set; } = 150;

    public int MinPackageWeightGrams { get; set; } = 100;
}
