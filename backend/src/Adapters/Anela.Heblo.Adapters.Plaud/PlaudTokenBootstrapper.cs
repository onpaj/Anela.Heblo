using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudTokenBootstrapper : IHostedService
{
    private readonly ILogger<PlaudTokenBootstrapper> _logger;
    private readonly IOptions<PlaudOptions> _options;

    public PlaudTokenBootstrapper(ILogger<PlaudTokenBootstrapper> logger, IOptions<PlaudOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var tokensJson = options.TokensJson?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(tokensJson))
        {
            _logger.LogWarning("Plaud TokensJson is not configured, skipping token bootstrap");
            return;
        }

        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var plaudDir = Path.Combine(homeDir, ".plaud");

            if (!Directory.Exists(plaudDir))
            {
                Directory.CreateDirectory(plaudDir);
            }

            var tokensPath = Path.Combine(plaudDir, "tokens.json");
            await File.WriteAllTextAsync(tokensPath, tokensJson, cancellationToken);

            // Set file permissions to 0600 on Unix systems
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tokensPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _logger.LogInformation("Plaud tokens written to {TokensPath}", tokensPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bootstrap Plaud tokens");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
