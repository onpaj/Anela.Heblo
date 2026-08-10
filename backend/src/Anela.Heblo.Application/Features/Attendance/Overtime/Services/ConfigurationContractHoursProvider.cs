using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public class ConfigurationContractHoursProvider : IContractHoursProvider
{
    private readonly IOptions<OvertimeOptions> _options;

    public ConfigurationContractHoursProvider(IOptions<OvertimeOptions> options)
    {
        _options = options;
    }

    public Task<decimal?> GetDailyHoursAsync(Guid personId, int year, int month, CancellationToken cancellationToken)
    {
        decimal? result = _options.Value.ContractHours.TryGetValue(personId.ToString(), out var hours)
            ? hours
            : null;
        return Task.FromResult(result);
    }
}
