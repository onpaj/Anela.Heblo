namespace Anela.Heblo.Application.Features.FileStorage;

/// <summary>
/// Configuration options for the FileStorage module's Azure Blob Storage client.
/// </summary>
/// <remarks>
/// <see cref="BlobConnectionString"/> is intentionally not marked <c>[Required]</c>: the Development
/// environment is allowed to leave it empty so <see cref="FileStorageModule"/> can fall back to
/// <c>UseDevelopmentStorage=true</c>. In non-Development environments,
/// <see cref="FileStorageModule.AddFileStorageModule"/> registers a stricter <c>.Validate()</c>
/// rule that fails fast at startup when the value is missing or whitespace.
/// </remarks>
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string BlobConnectionString { get; set; } = string.Empty;
}
