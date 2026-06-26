namespace Anela.Heblo.Application.Features.FileStorage;

public class FileDownloadOptions
{
    public TimeSpan HeadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(120);

    public int MaxRetryAttempts { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
