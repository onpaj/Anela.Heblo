using Anela.Heblo.Adapters.GoogleAds;
using FluentAssertions;
using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.GoogleAds;

public class SdkAccountBudgetFetcherTests
{
    private static GoogleAdsSettings FakeSettings(string token = "tok") => new()
    {
        DeveloperToken = token,
        OAuth2ClientId = "fake-id",
        OAuth2ClientSecret = "fake-secret",
        OAuth2RefreshToken = "fake-token",
        CustomerId = "1234567890",
    };

    private static GoogleAdsClient FakeClientFactory(GoogleAdsConfig cfg)
        => new(new GoogleAdsConfig
        {
            DeveloperToken = "fake",
            OAuth2ClientId = "fake-id",
            OAuth2ClientSecret = "fake-secret",
            OAuth2RefreshToken = "fake-token",
            LoginCustomerId = "1234567890",
        });

    [Fact]
    public void GetOrCreateClient_SameSettings_CreatesClientOnlyOnce()
    {
        // Arrange
        var callCount = 0;
        var monitor = new TestableOptionsMonitor<GoogleAdsSettings>(FakeSettings());
        var fetcher = new SdkAccountBudgetFetcher(monitor, NullLogger<SdkAccountBudgetFetcher>.Instance, cfg =>
        {
            callCount++;
            return FakeClientFactory(cfg);
        });

        // Act
        fetcher.GetOrCreateClient();
        fetcher.GetOrCreateClient();

        // Assert
        callCount.Should().Be(1);
    }

    [Fact]
    public void GetOrCreateClient_SettingsChanged_RecreatesClient()
    {
        // Arrange
        var callCount = 0;
        var monitor = new TestableOptionsMonitor<GoogleAdsSettings>(FakeSettings("original-token"));
        var fetcher = new SdkAccountBudgetFetcher(monitor, NullLogger<SdkAccountBudgetFetcher>.Instance, cfg =>
        {
            callCount++;
            return FakeClientFactory(cfg);
        });

        // Act
        fetcher.GetOrCreateClient();

        monitor.CurrentValue = FakeSettings("changed-token");
        monitor.TriggerChange();

        fetcher.GetOrCreateClient();

        // Assert
        callCount.Should().Be(2);
    }

    [Fact]
    public void GetOrCreateClient_SameFingerprintAfterInvalidate_CreatesNewClientOnceAndReuses()
    {
        // Arrange
        var callCount = 0;
        var monitor = new TestableOptionsMonitor<GoogleAdsSettings>(FakeSettings());
        var fetcher = new SdkAccountBudgetFetcher(monitor, NullLogger<SdkAccountBudgetFetcher>.Instance, cfg =>
        {
            callCount++;
            return FakeClientFactory(cfg);
        });

        // Act — invalidate cache but keep identical settings
        monitor.TriggerChange();

        fetcher.GetOrCreateClient();
        fetcher.GetOrCreateClient();

        // Assert — cache was cleared, so first call rebuilds (count=1), second call reuses
        callCount.Should().Be(1);
    }
}

file sealed class TestableOptionsMonitor<T> : IOptionsMonitor<T>
{
    private Action<T, string?>? _listener;

    public TestableOptionsMonitor(T initialValue)
    {
        CurrentValue = initialValue;
    }

    public T CurrentValue { get; set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listener = listener;
        return new CallbackDisposable(() => _listener = null);
    }

    public void TriggerChange() => _listener?.Invoke(CurrentValue, null);

    private sealed class CallbackDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
