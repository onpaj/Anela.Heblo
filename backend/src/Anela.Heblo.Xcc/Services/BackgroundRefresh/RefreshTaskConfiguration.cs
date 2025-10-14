using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public class RefreshTaskConfiguration
{
    public required string TaskId { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public required bool Enabled { get; init; }
    public int HydrationTier { get; init; } = 1;


    public static RefreshTaskConfiguration FromAppSettings(IConfiguration configuration, string taskId)
    {
        // For task ID like "ICatalogRepository.RefreshTransportData" and configurationKey like "RefreshTransportData"
        // We need to look in BackgroundRefresh:ICatalogRepository:RefreshTransportData
        var parts = taskId.Split('.');
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Task ID '{taskId}' must be in format 'Owner.Method'");
        }

        var ownerType = parts[0];
        var methodName = parts[1];

        var sectionPath = $"BackgroundRefresh:{ownerType}:{methodName}";

        var section = configuration.GetSection(sectionPath);

        if (!section.Exists())
        {
            throw new InvalidOperationException($"Configuration section '{sectionPath}' not found for task '{taskId}'");
        }

        var initialDelayStr = section["InitialDelay"];
        var refreshIntervalStr = section["RefreshInterval"];
        var enabledStr = section["Enabled"];
        var hydrationTierStr = section["HydrationTier"];

        if (!TimeSpan.TryParse(initialDelayStr, out var initialDelay))
        {
            initialDelay = TimeSpan.Zero;
        }

        if (!TimeSpan.TryParse(refreshIntervalStr, out var refreshInterval))
        {
            throw new InvalidOperationException(
                $"Invalid RefreshInterval in configuration section '{sectionPath}' for task '{taskId}'");
        }

        if (!bool.TryParse(enabledStr, out var enabled))
        {
            enabled = true; // Default to enabled if not specified
        }

        if (!int.TryParse(hydrationTierStr, out var hydrationTier))
        {
            hydrationTier = 1; // Default to tier 1 if not specified
        }

        return new RefreshTaskConfiguration
        {
            TaskId = taskId,
            InitialDelay = initialDelay,
            RefreshInterval = refreshInterval,
            Enabled = enabled,
            HydrationTier = hydrationTier
        };
    }
}