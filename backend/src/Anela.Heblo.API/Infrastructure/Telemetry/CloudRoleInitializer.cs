using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Infrastructure.Telemetry;

public class CloudRoleInitializer : ITelemetryInitializer
{
    private readonly string _cloudRoleName;
    private readonly string _cloudRoleInstance;

    public CloudRoleInitializer(string cloudRoleName, string cloudRoleInstance)
    {
        _cloudRoleName = cloudRoleName;
        _cloudRoleInstance = cloudRoleInstance;
    }

    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleName = _cloudRoleName;
        telemetry.Context.Cloud.RoleInstance = _cloudRoleInstance;
    }
}