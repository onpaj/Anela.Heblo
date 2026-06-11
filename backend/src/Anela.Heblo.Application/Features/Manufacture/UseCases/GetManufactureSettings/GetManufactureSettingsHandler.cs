using Anela.Heblo.Application.Features.Manufacture.Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;

public class GetManufactureSettingsHandler
    : IRequestHandler<GetManufactureSettingsRequest, GetManufactureSettingsResponse>
{
    private readonly ManufactureErpOptions _options;
    private readonly ILogger<GetManufactureSettingsHandler> _logger;

    public GetManufactureSettingsHandler(
        IOptions<ManufactureErpOptions> options,
        ILogger<GetManufactureSettingsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetManufactureSettingsResponse> Handle(
        GetManufactureSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var groupId = string.IsNullOrWhiteSpace(_options.ManufactureGroupId)
            ? null
            : _options.ManufactureGroupId;

        _logger.LogDebug("GetManufactureSettings resolved ManufactureGroupId hasValue={HasValue}", groupId is not null);

        return Task.FromResult(new GetManufactureSettingsResponse
        {
            ManufactureGroupId = groupId
        });
    }
}
