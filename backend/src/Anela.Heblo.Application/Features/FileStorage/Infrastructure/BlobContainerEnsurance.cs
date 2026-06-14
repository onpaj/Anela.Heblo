using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

/// <summary>
/// Ensures a Blob Storage container exists without producing 409 Conflict noise in App Insights.
/// Replaces <c>CreateIfNotExistsAsync</c>, which always issues an unconditional PUT and is recorded
/// as a failed dependency when the container already exists.
/// </summary>
public static class BlobContainerEnsurance
{
    // ExistsAsync (HEAD container) returns success regardless of whether the container exists, so
    // it never produces a 409 in telemetry. Only when the probe says "missing" do we issue CreateAsync.
    // A 409 from CreateAsync means another writer raced us — benign, log and continue.
    public static Task EnsureExistsAsync(
        BlobContainerClient client,
        ConcurrentDictionary<string, Lazy<Task>> cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var lazy = cache.GetOrAdd(
            client.Name,
            name => new Lazy<Task>(() => EnsureCoreAsync(client, cache, logger, cancellationToken)));

        return lazy.Value;
    }

    private static async Task EnsureCoreAsync(
        BlobContainerClient client,
        ConcurrentDictionary<string, Lazy<Task>> cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var existsResponse = await client.ExistsAsync(cancellationToken);
            if (existsResponse.Value)
            {
                return;
            }

            try
            {
                await client.CreateAsync(PublicAccessType.None, null, null, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // Idempotent 409: another writer (different pod, different cold start) created the container first.
                // Safe to suppress because the post-condition — container exists — is satisfied.
                logger.LogInformation(
                    "Idempotent 409 suppressed on container create. ContainerName={ContainerName} OperationName={OperationName}",
                    client.Name,
                    "CreateContainer");
            }
        }
        catch
        {
            // Evict the failed cache entry so the next caller retries instead of perpetually awaiting a faulted task.
            cache.TryRemove(client.Name, out _);
            throw;
        }
    }
}
