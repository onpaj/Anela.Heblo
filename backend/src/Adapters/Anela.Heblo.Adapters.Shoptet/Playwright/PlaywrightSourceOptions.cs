namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class PlaywrightSourceOptions
{
    public const string SettingsKey = "ShoptetPlaywright";
    public string ShopEntryUrl { get; set; }
    public string Login { get; set; }
    public string Password { get; set; }

    public string PdfTmpFolder { get; set; }

    public bool Headless { get; set; } = true;
}