using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Architecture;

/// <summary>
/// Enforces module boundary rule from docs/architecture/development_guidelines.md:
/// Leaflet must not reference any KnowledgeBase-owned type directly. All cross-module
/// communication goes through Leaflet-owned contracts (e.g. ILeafletKnowledgeSource)
/// implemented by KnowledgeBase via an adapter.
/// </summary>
public class ModuleBoundariesTests
{
    // Namespaces that, if referenced from a Leaflet type, indicate a boundary violation.
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Anela.Heblo.Domain.Features.KnowledgeBase",
        "Anela.Heblo.Application.Features.KnowledgeBase",
        "Anela.Heblo.Persistence.KnowledgeBase",
    ];

    private const string LeafletNamespacePrefix = "Anela.Heblo.Application.Features.Leaflet";

    // Allowlist for explicitly-documented exceptions. Each entry needs a comment with the
    // justification. Entries should be removed as the underlying violations are fixed.
    //
    // Entry format: "{LeafletFullyQualifiedTypeName} -> {ForbiddenTypeFullName}"
    //
    // Compiler-generated types (e.g. DisplayClasses for closures, state machines for async methods)
    // are automatically handled by matching against the declaring type's namespace prefix.
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: UploadLeafletHandler and IndexLeafletHandler consume IDocumentTextExtractor,
        // which currently lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
        // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove these entries
        // when IDocumentTextExtractor is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet.UploadLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",
        "Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet.IndexLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",

        // Pre-existing dependency: LeafletIngestionJob consumes IOneDriveService, which currently
        // lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
        // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove these entries
        // when IOneDriveService is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService",
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile",
    };

    [Fact]
    public void Leaflet_types_should_not_reference_KnowledgeBase_owned_namespaces()
    {
        var assembly = Assembly.Load("Anela.Heblo.Application");
        var leafletTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(LeafletNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var leafletType in leafletTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(leafletType))
            {
                if (!IsForbidden(referencedType))
                    continue;

                var entry = $"{leafletType.FullName} -> {referencedType.FullName}";
                if (Allowlist.Contains(entry))
                    continue;

                // Also check if the declaring type of a compiler-generated nested type is in the allowlist.
                // For example, if "UploadLeafletHandler+<>c__DisplayClass3_0" references a forbidden type,
                // check if "UploadLeafletHandler" references that same forbidden type.
                var baseType = leafletType.DeclaringType;
                if (baseType is not null)
                {
                    var baseEntry = $"{baseType.FullName} -> {referencedType.FullName}";
                    if (Allowlist.Contains(baseEntry))
                        continue;
                }

                violations.Add($"{entry} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            "Leaflet types must not reference KnowledgeBase-owned namespaces. " +
            "Define a Leaflet-owned contract in Application/Features/Leaflet/Contracts/ " +
            "and have KnowledgeBase implement it via an adapter. " +
            "Found:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void Logistics_types_should_not_reference_Purchase_owned_namespaces()
    {
        const string LogisticsNamespacePrefix = "Anela.Heblo.Application.Features.Logistics";

        var forbiddenPrefixes = new[]
        {
            "Anela.Heblo.Domain.Features.Purchase",
            "Anela.Heblo.Application.Features.Purchase",
            "Anela.Heblo.Persistence.Purchase",
        };

        var logisticsAllowlist = new HashSet<string>(StringComparer.Ordinal);

        bool IsLogisticsForbidden(Type type)
        {
            if (type.Namespace is null)
                return false;

            foreach (var prefix in forbiddenPrefixes)
            {
                if (type.Namespace.Equals(prefix, StringComparison.Ordinal) ||
                    type.Namespace.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        var assembly = Assembly.Load("Anela.Heblo.Application");
        var logisticsTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(LogisticsNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var logisticsType in logisticsTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(logisticsType))
            {
                if (!IsLogisticsForbidden(referencedType))
                    continue;

                var entry = $"{logisticsType.FullName} -> {referencedType.FullName}";
                if (logisticsAllowlist.Contains(entry))
                    continue;

                // Also check if the declaring type of a compiler-generated nested type is in the allowlist.
                // For example, if "SomeHandler+<>c__DisplayClass3_0" references a forbidden type,
                // check if "SomeHandler" references that same forbidden type.
                var baseType = logisticsType.DeclaringType;
                if (baseType is not null)
                {
                    var baseEntry = $"{baseType.FullName} -> {referencedType.FullName}";
                    if (logisticsAllowlist.Contains(baseEntry))
                        continue;
                }

                violations.Add($"{entry} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            "Logistics types must not reference Purchase-owned namespaces. " +
            "Define a Logistics-owned contract and avoid importing Purchase types. " +
            "Found:\n  " + string.Join("\n  ", violations));
    }

    private static bool IsForbidden(Type type)
    {
        if (type.Namespace is null)
            return false;

        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (type.Namespace.Equals(prefix, StringComparison.Ordinal) ||
                type.Namespace.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates every type referenced by a given type: constructor parameters, fields,
    /// properties, method parameters, method return types, generic type arguments,
    /// and attribute types. Returns (referencedType, "where it appeared") tuples.
    ///
    /// Known limitation: does not inspect method bodies (local variable types,
    /// inlined call targets). Generic constraints and attribute constructor args
    /// are covered partially via Type/CustomAttribute traversal.
    /// </summary>
    private static IEnumerable<(Type Type, string Where)> EnumerateReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.Static |
                                    BindingFlags.DeclaredOnly;

        foreach (var attr in type.GetCustomAttributesData())
            foreach (var t in ExpandGenerics(attr.AttributeType))
                yield return (t, $"attribute [{attr.AttributeType.Name}]");

        foreach (var field in type.GetFields(flags))
            foreach (var t in ExpandGenerics(field.FieldType))
                yield return (t, $"field {field.Name}");

        foreach (var prop in type.GetProperties(flags))
            foreach (var t in ExpandGenerics(prop.PropertyType))
                yield return (t, $"property {prop.Name}");

        foreach (var ctor in type.GetConstructors(flags))
            foreach (var param in ctor.GetParameters())
                foreach (var t in ExpandGenerics(param.ParameterType))
                    yield return (t, $"ctor parameter {param.Name}");

        foreach (var method in type.GetMethods(flags))
        {
            foreach (var t in ExpandGenerics(method.ReturnType))
                yield return (t, $"method {method.Name} return");

            foreach (var param in method.GetParameters())
                foreach (var t in ExpandGenerics(param.ParameterType))
                    yield return (t, $"method {method.Name} parameter {param.Name}");
        }
    }

    private static IEnumerable<Type> ExpandGenerics(Type type)
    {
        if (type.IsByRef || type.IsPointer)
            type = type.GetElementType() ?? type;

        if (type.IsArray)
        {
            var elem = type.GetElementType();
            if (elem is not null)
                foreach (var t in ExpandGenerics(elem))
                    yield return t;
            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
                foreach (var t in ExpandGenerics(arg))
                    yield return t;
        }
    }
}
