using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;

public class ShoptetPaySettings
{
    public static string ConfigurationKey => "ShoptetPay";

    [Required]
    public string ApiToken { get; set; } = null!;

    [Required]
    public string BaseUrl { get; set; } = "https://api.shoptetpay.com";
}
