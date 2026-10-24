using Anela.Heblo.Domain.Features.Ecomail;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailOptionsValidatorTests
{
    private const string FutureMessage = "must not be in the future";
    private const string EarliestMessage = "must not be earlier than 2020-01-01";

    private static EcomailOptions ValidOptions() => new()
    {
        BaseUrl = "https://api2.ecomailapp.cz",
        HttpTimeoutSeconds = 60,
        RecomputeWindowMonths = 2,
        BackfillFrom = new DateOnly(2024, 11, 1),
    };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(EcomailOptions options)
        => new EcomailOptionsValidator().Validate(null, options);

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var result = Validate(ValidOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BackfillFromToday_Succeeds()
    {
        var options = ValidOptions();
        options.BackfillFrom = DateOnly.FromDateTime(DateTime.UtcNow);

        Validate(options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BackfillFromTomorrow_FailsWithFutureMessageOnly()
    {
        var options = ValidOptions();
        options.BackfillFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain(FutureMessage);
        result.Failures.Should().NotContain(f => f.Contains(EarliestMessage));
    }

    [Fact]
    public void Validate_BackfillFromBefore2020_FailsWithEarliestMessageOnly()
    {
        var options = ValidOptions();
        options.BackfillFrom = new DateOnly(2019, 12, 31);

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain(EarliestMessage);
        result.Failures.Should().NotContain(f => f.Contains(FutureMessage));
    }

    [Fact]
    public void Validate_BackfillFromEarliestAllowed_Succeeds()
    {
        var options = ValidOptions();
        options.BackfillFrom = new DateOnly(2020, 1, 1);

        Validate(options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("relative/path")]
    [InlineData("not a url")]
    public void Validate_InvalidBaseUrl_Fails(string baseUrl)
    {
        var options = ValidOptions();
        options.BaseUrl = baseUrl;

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("BaseUrl"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(601)]
    public void Validate_HttpTimeoutOutOfRange_Fails(int seconds)
    {
        var options = ValidOptions();
        options.HttpTimeoutSeconds = seconds;

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("HttpTimeoutSeconds"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    public void Validate_HttpTimeoutAtBounds_Succeeds(int seconds)
    {
        var options = ValidOptions();
        options.HttpTimeoutSeconds = seconds;

        Validate(options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Validate_RecomputeWindowMonthsBelowOne_Fails(int months)
    {
        var options = ValidOptions();
        options.RecomputeWindowMonths = months;

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("RecomputeWindowMonths"));
    }

    [Fact]
    public void Validate_RecomputeWindowMonthsOne_Succeeds()
    {
        var options = ValidOptions();
        options.RecomputeWindowMonths = 1;

        Validate(options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_MultipleInvalidSettings_AccumulatesFailures()
    {
        var options = ValidOptions();
        options.BaseUrl = "";
        options.HttpTimeoutSeconds = 0;
        options.RecomputeWindowMonths = 0;
        options.BackfillFrom = new DateOnly(2019, 1, 1);

        var result = Validate(options);

        result.Failures.Should().HaveCount(4);
    }
}
