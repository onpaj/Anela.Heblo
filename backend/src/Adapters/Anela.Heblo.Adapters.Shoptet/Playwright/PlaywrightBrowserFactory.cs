using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class PlaywrightBrowserFactory
{
    private readonly PlaywrightSourceOptions _options;

    public PlaywrightBrowserFactory(PlaywrightSourceOptions options)
    {
        _options = options;
    }
    public Task<IBrowser> CreateAsync(IPlaywright playwright)
    {
        return playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions()
        {
            Headless = _options.Headless,
        });
    }
}