using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Anela.Heblo.Application.Features.ProcessDocs;

/// <summary>
/// Parses a process doc. Full schema validation lives in scripts/process-docs/check.py (CI);
/// this only enforces what the MCP tools depend on.
/// </summary>
public static class ProcessDocParser
{
    private static readonly Regex FrontmatterRegex =
        new(@"\A---\r?\n(?<yaml>.*?)\r?\n---\r?\n(?<body>.*)\z", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ProcessDoc Parse(string name, string text)
    {
        var match = FrontmatterRegex.Match(text);
        if (!match.Success)
        {
            throw new FormatException($"Process doc '{name}' has no frontmatter.");
        }

        var front = Yaml.Deserialize<Frontmatter?>(match.Groups["yaml"].Value)
            ?? throw new FormatException($"Process doc '{name}' has empty frontmatter.");

        if (front.Process != name)
        {
            throw new FormatException($"Process doc '{name}' declares process '{front.Process}'.");
        }

        return new ProcessDoc(
            name,
            Require(front.Kind, "kind", name),
            Require(front.Summary, "summary", name),
            front.Owns ?? [],
            Require(front.VerifiedAt, "verified_at", name),
            front.Related ?? [],
            match.Groups["body"].Value.TrimStart());
    }

    private static string Require(string? value, string key, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"Process doc '{name}' is missing '{key}'.")
            : value.Trim();

    private sealed class Frontmatter
    {
        public string? Process { get; set; }
        public string? Kind { get; set; }
        public string? Summary { get; set; }
        public List<string>? Owns { get; set; }
        public string? VerifiedAt { get; set; }
        public List<string>? Related { get; set; }
    }
}
