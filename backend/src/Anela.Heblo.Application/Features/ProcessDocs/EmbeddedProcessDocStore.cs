using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProcessDocs;

/// <summary>Loads process docs embedded into this assembly (see the csproj EmbeddedResource glob).</summary>
public sealed class EmbeddedProcessDocStore : IProcessDocStore
{
    private const string ResourcePrefix = "ProcessDocs/";
    private const string ResourceSuffix = ".md";

    private readonly Dictionary<string, ProcessDoc> _byName;

    public EmbeddedProcessDocStore(ILogger<EmbeddedProcessDocStore> logger)
        : this(ReadEmbedded(), logger)
    {
    }

    internal EmbeddedProcessDocStore(IEnumerable<(string Name, string Text)> sources, ILogger<EmbeddedProcessDocStore> logger)
    {
        var docs = new List<ProcessDoc>();
        var errors = new List<string>();
        foreach (var (name, text) in sources)
        {
            try
            {
                docs.Add(ProcessDocParser.Parse(name, text));
            }
            catch (Exception ex) when (ex is FormatException or YamlDotNet.Core.YamlException)
            {
                logger.LogError(ex, "Skipping malformed process doc {ProcessDoc}", name);
                errors.Add($"{name}: {ex.Message}");
            }
        }

        All = docs.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        LoadErrors = errors;
        _byName = All.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ProcessDoc> All { get; }

    public IReadOnlyList<string> LoadErrors { get; }

    public ProcessDoc? Find(string name) => _byName.GetValueOrDefault(name.Trim());

    private static IEnumerable<(string Name, string Text)> ReadEmbedded()
    {
        var assembly = typeof(EmbeddedProcessDocStore).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var name = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            yield return (name, reader.ReadToEnd());
        }
    }
}
