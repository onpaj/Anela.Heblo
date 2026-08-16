using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class LogetoContractHoursProviderTests
{
    private static readonly Guid Person = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherPerson = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<ILogetoClient> _client = new();

    private LogetoContractHoursProvider CreateProvider(params LogetoPerson[] people)
    {
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(people.ToList());

        return new LogetoContractHoursProvider(_client.Object);
    }

    [Theory]
    [InlineData("integration 6,4")]
    [InlineData("integration 6.4")]
    public async Task ReturnsHoursFromNote_RegardlessOfDecimalSeparator(string note)
    {
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = note });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().Be(6.4m);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoteCarriesNoHours()
    {
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = "integration" });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenPersonIsNotInLogeto()
    {
        var provider = CreateProvider(new LogetoPerson { Guid = OtherPerson, Note = "integration 8" });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenPersonIsNotEnrolled_EvenWithANumberInTheNote()
    {
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = "brigáda 6,4" });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().BeNull();
    }

    [Fact]
    public async Task IgnoresYearAndMonth()
    {
        // A note has no history. Closed statements freeze their own RequiredHours, so an
        // open month always follows the current úvazek.
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = "integration 8" });

        var august = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);
        var january = await provider.GetDailyHoursAsync(Person, 2025, 1, CancellationToken.None);

        august.Should().Be(8m);
        january.Should().Be(8m);
    }

    [Fact]
    public async Task FetchesPeopleOnce_PerScope()
    {
        var provider = CreateProvider(
            new LogetoPerson { Guid = Person, Note = "integration 8" },
            new LogetoPerson { Guid = OtherPerson, Note = "integration 6,4" });

        await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);
        await provider.GetDailyHoursAsync(OtherPerson, 2026, 8, CancellationToken.None);
        await provider.GetDailyHoursAsync(Person, 2026, 7, CancellationToken.None);

        _client.Verify(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PropagatesClientFailure()
    {
        // A Logeto outage must not read as "nobody has an úvazek" across the whole company.
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("logeto down"));
        var provider = new LogetoContractHoursProvider(_client.Object);

        var act = () => provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
