using MediatR;

namespace Anela.Heblo.Application.Features.Configuration.Model;

/// <summary>
/// Request for getting application configuration
/// </summary>
public class GetConfigurationRequest : IRequest<GetConfigurationResponse>
{
    // Empty request - no parameters needed for configuration endpoint
}