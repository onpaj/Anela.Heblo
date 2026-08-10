using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Application.Overtime;

public class ConfigurationContractHoursProviderTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task ReturnsConfiguredHours_ForKnownPerson()
    {
        var options = new OvertimeOptions
        {
            ContractHours = new Dictionary<string, decimal> { [Person.ToString()] = 6.4m }
        };
        var provider = new ConfigurationContractHoursProvider(Options.Create(options));

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 9, CancellationToken.None);

        hours.Should().Be(6.4m);
    }

    [Fact]
    public async Task ReturnsNull_ForUnknownPerson()
    {
        var provider = new ConfigurationContractHoursProvider(Options.Create(new OvertimeOptions()));

        var hours = await provider.GetDailyHoursAsync(Guid.NewGuid(), 2026, 9, CancellationToken.None);

        hours.Should().BeNull();
    }
}
