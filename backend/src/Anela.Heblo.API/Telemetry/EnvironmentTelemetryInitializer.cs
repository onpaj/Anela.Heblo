using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Adds environment information to all telemetry for better filtering and analysis.
/// </summary>
public class EnvironmentTelemetryInitializer : ITelemetryInitializer
{
    private readonly string _environmentName;
    private readonly string _applicationVersion;

    public EnvironmentTelemetryInitializer(IHostEnvironment environment, IConfiguration configuration)
    {
        _environmentName = environment.EnvironmentName;
        _applicationVersion = configuration["ApplicationVersion"] ?? "1.0.0";
    }

    public void Initialize(ITelemetry telemetry)
    {
        // Add environment name to all telemetry
        telemetry.Context.GlobalProperties["Environment"] = _environmentName;
        telemetry.Context.GlobalProperties["Version"] = _applicationVersion;
        
        // Set cloud role name for better service map visualization
        telemetry.Context.Cloud.RoleName = "Anela.Heblo.API";
        telemetry.Context.Cloud.RoleInstance = Environment.MachineName;
    }
}