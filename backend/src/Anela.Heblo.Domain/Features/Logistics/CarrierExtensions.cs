namespace Anela.Heblo.Domain.Features.Logistics;

public static class CarrierExtensions
{
    public static string GetDisplayName(this Carriers carrier) => carrier switch
    {
        Carriers.Zasilkovna => "Zásilkovna",
        Carriers.PPL => "PPL",
        Carriers.GLS => "GLS",
        Carriers.Osobak => "Osobní odběr",
        _ => carrier.ToString(),
    };
}
