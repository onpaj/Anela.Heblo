using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;

public class GetManufactureSettingsHandler
    : IRequestHandler<GetManufactureSettingsRequest, GetManufactureSettingsResponse>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetManufactureSettingsHandler> _logger;

    public GetManufactureSettingsHandler(
        IConfiguration configuration,
        ILogger<GetManufactureSettingsHandler> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetManufactureSettingsResponse> Handle(
        GetManufactureSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var groupId = _configuration[ManufactureConfigurationKeys.GroupId];
        if (string.IsNullOrEmpty(groupId))
        {
            groupId = null;
        }

        _logger.LogDebug("GetManufactureSettings resolved ManufactureGroupId hasValue={HasValue}", groupId is not null);

        return Task.FromResult(new GetManufactureSettingsResponse
        {
            ManufactureGroupId = groupId
        });
    }
}
