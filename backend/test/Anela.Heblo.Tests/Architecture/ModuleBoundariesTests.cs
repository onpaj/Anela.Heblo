using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Architecture;

/// <summary>
/// Enforces module boundary rules from docs/architecture/development_guidelines.md:
/// Consumer modules must not reference provider-owned types directly. All cross-module
/// communication goes through consumer-owned contracts (e.g. ILeafletKnowledgeSource,
/// IInventoryReservationService) implemented by the provider via an adapter.
/// </summary>
public class ModuleBoundariesTests
{
    public sealed record ModuleBoundaryRule(
        string Name,
        string InspectedNamespacePrefix,
        IReadOnlyList<string> ForbiddenNamespacePrefixes,
        IReadOnlySet<string> Allowlist,
        string InspectedAssembly = "Anela.Heblo.Application");

    // Pre-existing allowlist for Leaflet → KnowledgeBase. Each entry needs a comment with the
    // justification. Entries should be removed as the underlying violations are fixed.
    //
    // Entry format: "{ConsumerFullyQualifiedTypeName} -> {ProviderTypeFullName}"
    //
    // Compiler-generated types (e.g. DisplayClasses for closures, state machines for async
    // methods) are automatically handled by matching against the declaring type's namespace
    // prefix below.
    private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: UploadLeafletHandler and IndexLeafletHandler consume
        // IDocumentTextExtractor, which currently lives in
        // Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
        // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove these
        // entries when IDocumentTextExtractor is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet.UploadLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",
        "Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet.IndexLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",

        // Pre-existing dependency: LeafletIngestionJob consumes IOneDriveService, which
        // currently lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting
        // this is out of scope for the 2026-05-15 Leaflet decoupling. Track separately and
        // remove these entries when IOneDriveService is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService",
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile",
    };

    // Allowlist for Article → KnowledgeBase. Each entry needs a comment with the justification.
    // Entries should be removed as the underlying violations are fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: GatherContextStep dispatches SearchDocumentsRequest via MediatR
        // to obtain knowledge-base snippets during article generation. Lifting this behind a
        // consumer-owned contract (e.g. IArticleKnowledgeSearch) is out of scope for the
        // 2026-05-25 Article ↔ KnowledgeBase style-guide decoupling and is tracked as a follow-up.
        // Remove these three entries when SearchDocumentsRequest is replaced by an Article-owned
        // contract.
        "Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsRequest",
        "Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsResponse",
        "Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.ChunkResult",
    };

    // Allowlist for Logistics → Manufacture. Each entry needs a comment with the justification.
    // Entries should be removed as the underlying violations are fixed.
    private static readonly HashSet<string> LogisticsAllowlist = new(StringComparer.Ordinal)
    {
        // GiftPackageManufactureService depends on IManufactureClient for Bill of Materials (BOM)
        // lookups (which set parts to consume/produce for a gift package). Decoupling this requires
        // a separate consumer-owned contract (e.g., IGiftPackageBomSource) and is out of scope for
        // the current Logistics-Manufacture inventory decoupling. Track as a follow-up.
        "Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services.GiftPackageManufactureService -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // ProductPart is the return element type of IManufactureClient.GetSetPartsAsync, used
        // inside GiftPackageManufactureService.GetGiftPackageDetailAsync. The compiler-generated
        // async state machine (<GetGiftPackageDetailAsync>d__N) references ProductPart directly
        // via its captured local fields. This is covered by the DeclaringType check for the
        // IManufactureClient entry above but requires its own entry because ProductPart lives in
        // a separate type slot. Remove when IManufactureClient is decoupled (see entry above).
        "Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services.GiftPackageManufactureService -> Anela.Heblo.Domain.Features.Manufacture.ProductPart",
    };

    // Allowlist for Purchase → Catalog. Empty — no active violations.
    private static readonly HashSet<string> PurchaseAllowlist = new(StringComparer.Ordinal);

    // Allowlist for Logistics → Catalog. Pre-existing violations in TransportBoxCompletionService
    // that are out of scope for the 2026-06-01 Logistics-Catalog boundary introduction.
    // Remove these entries when TransportBoxCompletionService is refactored to use a
    // Logistics-owned contract instead of IStockUpOperationRepository / StockUpOperation directly.
    private static readonly HashSet<string> LogisticsCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // TransportBoxCompletionService injects IStockUpOperationRepository (a Catalog-owned
        // repository) to persist stock-up operations when a transport box is completed.
        // Decoupling this requires introducing a Logistics-owned contract (e.g.
        // ILogisticsStockUpGateway) and a Catalog adapter — tracked as a follow-up.
        "Anela.Heblo.Application.Features.Logistics.Services.TransportBoxCompletionService -> Anela.Heblo.Domain.Features.Catalog.Stock.IStockUpOperationRepository",

        // StockUpOperation is the value type produced and persisted by TransportBoxCompletionService
        // via IStockUpOperationRepository. Covered by the same follow-up as the entry above;
        // the compiler-generated nested types (+<>c, +<ProcessBoxAsync>d__5) are handled
        // automatically via the DeclaringType allowlist check in the test harness.
        "Anela.Heblo.Application.Features.Logistics.Services.TransportBoxCompletionService -> Anela.Heblo.Domain.Features.Catalog.Stock.StockUpOperation",
    };

    public static TheoryData<ModuleBoundaryRule> Rules() => new()
    {
        new ModuleBoundaryRule(
            Name: "Leaflet -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Leaflet",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: LeafletAllowlist),

        new ModuleBoundaryRule(
            Name: "Article -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: ArticleAllowlist),

        new ModuleBoundaryRule(
            Name: "Article -> UserManagement",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.UserManagement",
                "Anela.Heblo.Application.Features.UserManagement",
                "Anela.Heblo.Persistence.UserManagement",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Logistics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Manufacture",
                "Anela.Heblo.Application.Features.Manufacture",
                "Anela.Heblo.Persistence.Manufacture",
            },
            Allowlist: LogisticsAllowlist),

        new ModuleBoundaryRule(
            Name: "PackingMaterials -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.PackingMaterials",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Purchase -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Purchase",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: PurchaseAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Logistics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: LogisticsCatalogAllowlist),

        new ModuleBoundaryRule(
            Name: "ExpeditionListArchive -> ExpeditionList",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.ExpeditionList",
                "Anela.Heblo.Application.Features.ExpeditionList",
                "Anela.Heblo.Persistence.ExpeditionList",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Application) -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Consumer_types_should_not_reference_provider_owned_namespaces(ModuleBoundaryRule rule)
    {
        var assembly = Assembly.Load(rule.InspectedAssembly);
        var consumerTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(rule.InspectedNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var consumerType in consumerTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(consumerType))
            {
                if (!IsForbidden(referencedType, rule.ForbiddenNamespacePrefixes))
                    continue;

                var entry = $"{consumerType.FullName} -> {referencedType.FullName}";
                if (rule.Allowlist.Contains(entry))
                    continue;

                // Also check if the declaring type of a compiler-generated nested type is in
                // the allowlist. For example, if "UploadLeafletHandler+<>c__DisplayClass3_0"
                // references a forbidden type, check if "UploadLeafletHandler" references that
                // same forbidden type.
                var baseType = consumerType.DeclaringType;
                if (baseType is not null)
                {
                    var baseEntry = $"{baseType.FullName} -> {referencedType.FullName}";
                    if (rule.Allowlist.Contains(baseEntry))
                        continue;
                }

                violations.Add($"{entry} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            $"{rule.Name}: consumer types must not reference provider-owned namespaces. " +
            "Define a consumer-owned contract in the consumer module's Contracts/ folder " +
            "and have the provider module implement it via an adapter. " +
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

    [Fact]
    public void Application_types_should_not_reference_AspNetCore_namespaces()
    {
        // NFR-3 from spec 2026-05-26: the Application layer must remain free of any
        // Microsoft.AspNetCore.* type references. CurrentUserService was relocated to
        // the API project to enforce this. This test prevents regression.
        const string ApplicationNamespacePrefix = "Anela.Heblo.Application";
        const string ForbiddenPrefix = "Microsoft.AspNetCore";

        var assembly = Assembly.Load("Anela.Heblo.Application");
        var applicationTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(ApplicationNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var applicationType in applicationTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(applicationType))
            {
                if (referencedType.Namespace is null)
                    continue;

                if (!referencedType.Namespace.Equals(ForbiddenPrefix, StringComparison.Ordinal)
                    && !referencedType.Namespace.StartsWith(ForbiddenPrefix + ".", StringComparison.Ordinal))
                    continue;

                violations.Add($"{applicationType.FullName} -> {referencedType.FullName} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            "Application layer must not reference Microsoft.AspNetCore.* types. " +
            "Move ASP.NET Core-dependent code to the API or Infrastructure layer and " +
            "expose it through a framework-neutral abstraction in Domain or Application. " +
            "Found:\n  " + string.Join("\n  ", violations));
    }

    private static bool IsForbidden(Type type, IReadOnlyList<string> forbiddenPrefixes)
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
