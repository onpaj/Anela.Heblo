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
        if (_options.Headless)
        {
            return playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions()
            {
                Headless = _options.Headless,
            });
        }
        
        // Chromium got stuck on IOS is not headless
        return playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions()
        {
            Headless = _options.Headless,
        });
    }
}