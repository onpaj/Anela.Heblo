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

    // Allowlist for Article → KnowledgeBase. Empty — all violations fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal);

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

    // Allowlist for Logistics → Catalog. Empty — TransportBoxCompletionService now consumes
    // the Logistics-owned ILogisticsStockOperationQueryService contract; the Catalog adapter
    // lives in Catalog.Infrastructure and is captured by the reverse-direction
    // CatalogLogisticsAllowlist below.
    private static readonly HashSet<string> LogisticsCatalogAllowlist = new(StringComparer.Ordinal);

    // Allowlist for Catalog -> Logistics. Pre-existing adapters in Catalog.Infrastructure
    // that reference Logistics.Contracts types are out of scope for the 2026-06-01 decoupling.
    // Track follow-up: move these adapters to the Logistics module or introduce Catalog-owned DTOs.
    private static readonly HashSet<string> CatalogLogisticsAllowlist = new(StringComparer.Ordinal)
    {
        // LogisticsCatalogSourceAdapter is a Catalog-side adapter wrapping Logistics.Contracts types.
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsCatalogSourceAdapter -> Anela.Heblo.Application.Features.Logistics.Contracts.Models.LogisticsGiftPackageItem",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsCatalogSourceAdapter -> Anela.Heblo.Application.Features.Logistics.Contracts.Models.LogisticsCatalogItem",
        // LogisticsStockOperationAdapter references the Logistics contract enum for backward compatibility.
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsStockOperationAdapter -> Anela.Heblo.Application.Features.Logistics.Contracts.LogisticsStockOperationSource",
        // LogisticsStockOperationQueryAdapter references the Logistics contract enums and types.
        // This adapter is produced by TransportBoxCompletionService refactoring to use
        // the Logistics-owned ILogisticsStockOperationQueryService contract. The adapter
        // lives in Catalog.Infrastructure and acts as a bridge from the Catalog-owned
        // IStockUpOperationRepository pattern. Decouple to Catalog-owned DTOs in follow-up.
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsStockOperationQueryAdapter -> Anela.Heblo.Application.Features.Logistics.Contracts.Models.LogisticsStockOperationStatus",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsStockOperationQueryAdapter -> Anela.Heblo.Application.Features.Logistics.Contracts.LogisticsStockOperationSource",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsStockOperationQueryAdapter -> Anela.Heblo.Application.Features.Logistics.Contracts.LogisticsStockOperationState",
    };

    // Allowlist for Catalog -> Purchase. Pre-existing violations from adapters and handlers
    // are out of scope for the 2026-06-01 CatalogRepository decoupling. Track as follow-ups:
    //   - Introduce a Catalog.Inventory-owned contract for purchase-order lookups.
    //   - Introduce Catalog-owned material info DTOs to replace Purchase.Contracts references.
    private static readonly HashSet<string> CatalogPurchaseAllowlist = new(StringComparer.Ordinal)
    {
        // Follow-up: migrate CreateMaterialContainersHandler off IPurchaseOrderRepository / PurchaseOrderLine.
        "Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers.CreateMaterialContainersHandler -> Anela.Heblo.Domain.Features.Purchase.IPurchaseOrderRepository",
        "Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers.CreateMaterialContainersHandler -> Anela.Heblo.Domain.Features.Purchase.PurchaseOrderLine",

        // PurchaseMaterialCatalogAdapter in Catalog.Infrastructure wraps Purchase.Contracts types.
        // Follow-up: introduce Catalog-owned material info DTOs and have the adapter map them.
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.PurchaseMaterialCatalogAdapter -> Anela.Heblo.Application.Features.Purchase.Contracts.MaterialInfo",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.PurchaseMaterialCatalogAdapter -> Anela.Heblo.Application.Features.Purchase.Contracts.MaterialStockSnapshot",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.PurchaseMaterialCatalogAdapter -> Anela.Heblo.Application.Features.Purchase.Contracts.MaterialBomReference",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.PurchaseMaterialCatalogAdapter -> Anela.Heblo.Application.Features.Purchase.Contracts.MaterialPurchaseSnapshot",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.PurchaseMaterialCatalogAdapter -> Anela.Heblo.Application.Features.Purchase.Contracts.MaterialProductType",
    };

    // Allowlist for Catalog -> Manufacture. Pre-existing handler-level IManufactureClient injections
    // and ManufactureHistoryRecord return-type leak from CatalogRepository/ICatalogManufactureSource
    // are out of scope for the 2026-06-01 CatalogRepository decoupling. Track as follow-ups:
    //   - Migrate the three handlers off IManufactureClient onto a Catalog-owned contract.
    //   - Introduce a Catalog-owned CatalogManufactureHistoryRecord DTO and map in the adapter.
    private static readonly HashSet<string> CatalogManufactureAllowlist = new(StringComparer.Ordinal)
    {
        // Follow-up: migrate UpdateProductCompositionOrderHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductCompositionHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductUsageHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Deliberate pragmatic leak: ManufactureHistoryRecord flows through Catalog's cache layer.
        // All entries below are tracked under the same follow-up: introduce Catalog-owned
        // CatalogManufactureHistoryRecord DTO and map in the ManufactureCatalogSourceAdapter.
        "Anela.Heblo.Application.Features.Catalog.CatalogRepository -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Contracts.ICatalogManufactureSource -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogCacheStore -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogDataRefreshService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogMergeService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        // Cost providers in Catalog.CostProviders compute costs from ManufactureHistoryRecord.
        "Anela.Heblo.Application.Features.Catalog.CostProviders.FlatManufactureCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.CostProviders.ManufactureBasedMaterialCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        // GetCatalogDetailHandler maps ManufactureHistoryRecord from CatalogAggregate into response DTOs.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail.GetCatalogDetailHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",

        // GetProductUsageResponse holds ManufactureTemplate in its payload.
        // Follow-up: introduce Catalog-owned ManufactureTemplateDto.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageResponse -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",

        // Handlers reference ManufactureTemplate and Ingredient directly via IManufactureClient.
        // Compiler-generated types (+<>c, +d__N) are covered by the declaring-type check.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.Ingredient",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.Ingredient",
    };

    // Allowlist for DataQuality -> Catalog. Pre-existing ProductPairingDqtComparer references
    // are out of scope for the 2026-06-03 StockWriteBackDqtComparer decoupling.
    // Track follow-up: introduce DataQuality-owned IProductPairingQuery contract and Catalog-side
    // adapter that surfaces eshop/erp product snapshots without leaking Catalog types.
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",

        // Compiler-generated async state machine <CompareAsync>d__5 captures EshopStock in local
        // fields when comparing product pairings. Covered by the declaring-type check above.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer+<CompareAsync>d__5 -> Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock",
    };

    // Allowlist for DataQuality -> Invoices. The DataQuality module owns IInvoiceShoptetSource
    // and IInvoiceErpClient (in Application/Features/DataQuality/Contracts/) and consumes
    // them via InvoiceDqtComparer. Shared invoice domain DTOs are referenced on the contracts
    // and inside the comparer; lifting these to a shared kernel is a separate follow-up.
    // Follow-up: extract a DataQuality-owned snapshot DTO and map in the adapters.
    private static readonly HashSet<string> DataQualityInvoicesAllowlist = new(StringComparer.Ordinal)
    {
        // IInvoiceShoptetSource exposes IssuedInvoiceDetailBatch and IssuedInvoiceSourceQuery.
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceShoptetSource -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch",
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceShoptetSource -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery",

        // IInvoiceErpClient exposes IssuedInvoiceDetail.
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceErpClient -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetail",

        // InvoiceDqtComparer consumes shared invoice DTOs internally.
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetail",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailItem",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.InvoicePrice",
    };

    // Allowlist for Manufacture -> Catalog. Each group below is a deliberate pragmatic leak
    // tracked under the same follow-up: introduce Manufacture-owned ProductCatalogSnapshot DTO
    // and map in CatalogManufactureCatalogSourceAdapter (symmetric to the CatalogManufactureAllowlist
    // ManufactureHistoryRecord block).
    private static readonly HashSet<string> ManufactureCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // Contract surface: IManufactureCatalogSource returns CatalogAggregate by design.
        "Anela.Heblo.Application.Features.Manufacture.Contracts.IManufactureCatalogSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",

        // Consumers of CatalogAggregate reached via IManufactureCatalogSource methods.
        // Remove these entries when ProductCatalogSnapshot DTO lands.
        "Anela.Heblo.Application.Features.Manufacture.Services.BatchPlanningService -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.BatchPlanningService+<>c__DisplayClass13_0 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.BatchPlanningService+<CalculateMultiPhaseBatchPlanInternal>d__17 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.BatchPlanningService+<CalculateSinglePhaseBatchPlanInternal>d__16 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.IManufactureAnalysisMapper -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.IManufactureSeverityCalculator -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.ManufactureAnalysisMapper -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.ManufactureSeverityCalculator -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.ResidueDistributionCalculator+<CalculateAsync>d__3 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient.CalculateBatchByIngredientHandler+<Handle>d__3 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize.CalculatedBatchSizeHandler+<Handle>d__3 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan.CalculateBatchPlanHandler+<Handle>d__6 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder.CreateManufactureOrderHandler+<Handle>d__6 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput.GetManufactureOutputHandler+<>c -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput.GetManufactureOutputHandler+<>c__DisplayClass4_0 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput.GetManufactureOutputHandler+<Handle>d__4 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf.GetSemiproductRecipePdfHandler+<Handle>d__4 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler+<>c -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler+<>c__DisplayClass10_0 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler+<>c__DisplayClass9_0 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler+<Handle>d__9 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler+<Handle>d__4 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus.UpdateManufactureOrderStatusHandler+<>c__DisplayClass10_0 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus.UpdateManufactureOrderStatusHandler+<WriteDownInventoryAsync>d__10 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",

        // Domain enums/types reached via CatalogAggregate properties.
        // Same follow-up as above.
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator -> Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials.ConsumedMaterialRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator -> Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator+<>c -> Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials.ConsumedMaterialRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator+<>c -> Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator+<>c__DisplayClass4_0 -> Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator+<>c__DisplayClass5_0 -> Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.ConsumptionRateCalculator+<>c__DisplayClass6_0 -> Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials.ConsumedMaterialRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.IConsumptionRateCalculator -> Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials.ConsumedMaterialRecord",
        "Anela.Heblo.Application.Features.Manufacture.Services.IConsumptionRateCalculator -> Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient.CalculateBatchByIngredientHandler+<>c -> Anela.Heblo.Domain.Features.Catalog.Stock.StockTakingRecord",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize.CalculatedBatchSizeHandler+<>c -> Anela.Heblo.Domain.Features.Catalog.Stock.StockTakingRecord",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStockTakingLot",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockDomainService",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler+<>c -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStockTakingLot",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler+<Handle>d__4 -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStockTakingRequest",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking.SubmitManufactureStockTakingHandler+<Handle>d__4 -> Anela.Heblo.Domain.Features.Catalog.Stock.StockTakingRecord",
    };

    // Allowlist for ExpeditionList -> Logistics.
    // Carriers is a Domain enum (Zasilkovna/GLS/PPL/Osobak) consumed widely across the codebase.
    // Duplicating it into ExpeditionList would create a synchronization burden far worse than
    // the dependency it removes. The single entry below is the only Logistics-namespaced type
    // ExpeditionList intentionally references, and it appears solely on ExpeditionPickingRequest.
    private static readonly HashSet<string> ExpeditionListLogisticsAllowlist = new(StringComparer.Ordinal)
    {
        "Anela.Heblo.Application.Features.ExpeditionList.Contracts.ExpeditionPickingRequest -> Anela.Heblo.Domain.Features.Logistics.Carriers",
    };

    // Allowlist for Packaging -> ShoptetOrders. The Packaging module legitimately consumes
    // the IPackingOrderClient / IEshopOrderClient contracts (and their DTOs) defined in
    // Anela.Heblo.Application.Features.ShoptetOrders. Everything else — particularly
    // ShoptetOrdersSettings, PackingStateId, and PackedStateId — must not be referenced
    // from Packaging. This rule pins the 2026-06-05 decoupling in place.
    private static readonly HashSet<string> PackagingShoptetOrdersAllowlist = new(StringComparer.Ordinal)
    {
        // Constructor injections in ScanPackingOrderHandler.
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",

        // PackingOrder is consumed in Handle and in the private BuildShippingAddress helper;
        // PackingOrderItem flows through ScanOrderData.Items.
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder",
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrderItem",

        // ScanOrderData.Items is List<PackingOrderItem> — the DTO uses the contract item type directly.
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanOrderData -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrderItem",

        // ResetOrderShipmentHandler also uses IPackingOrderClient, PackingOrder, and PackingOrderItem
        // (compiler-generated async state machine and closure types are covered by the declaring-type check).
        "Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment.ResetOrderShipmentHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
        "Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment.ResetOrderShipmentHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder",
        "Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment.ResetOrderShipmentHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrderItem",
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

        new ModuleBoundaryRule(
            Name: "Analytics (Application) -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),

        new ModuleBoundaryRule(
            Name: "Analytics (Application) -> Bank",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Bank",
                "Anela.Heblo.Application.Features.Bank",
                "Anela.Heblo.Persistence.Bank",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Bank",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Bank",
                "Anela.Heblo.Application.Features.Bank",
                "Anela.Heblo.Persistence.Bank",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),

        new ModuleBoundaryRule(
            Name: "Catalog -> Logistics",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Catalog",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Logistics",
                "Anela.Heblo.Application.Features.Logistics",
                "Anela.Heblo.Persistence.Logistics",
            },
            Allowlist: CatalogLogisticsAllowlist),

        new ModuleBoundaryRule(
            Name: "Catalog -> Purchase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Catalog",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Purchase",
                "Anela.Heblo.Application.Features.Purchase",
                "Anela.Heblo.Persistence.Purchase",
            },
            Allowlist: CatalogPurchaseAllowlist),

        new ModuleBoundaryRule(
            Name: "Catalog -> Manufacture",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Catalog",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Manufacture",
                "Anela.Heblo.Application.Features.Manufacture",
                "Anela.Heblo.Persistence.Manufacture",
            },
            Allowlist: CatalogManufactureAllowlist),

        new ModuleBoundaryRule(
            Name: "DataQuality -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: DataQualityCatalogAllowlist),

        new ModuleBoundaryRule(
            Name: "DataQuality -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: DataQualityInvoicesAllowlist),

        new ModuleBoundaryRule(
            Name: "Manufacture -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Manufacture",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: ManufactureCatalogAllowlist),

        new ModuleBoundaryRule(
            Name: "ExpeditionList -> Logistics",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionList",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Logistics",
                "Anela.Heblo.Application.Features.Logistics",
                "Anela.Heblo.Persistence.Logistics",
            },
            Allowlist: ExpeditionListLogisticsAllowlist),

        new ModuleBoundaryRule(
            Name: "Packaging -> ShoptetOrders",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Packaging",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Application.Features.ShoptetOrders",
            },
            Allowlist: PackagingShoptetOrdersAllowlist),
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

    [Fact]
    public void Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone()
    {
        // NFR-6: after relocating IssuedInvoiceFilters, PaginatedResult<T>, and
        // IIssuedInvoiceRepository out of Domain, the Domain assembly must
        // (a) not reference any Anela.Heblo.Application.* type, and
        // (b) not contain types with the three relocated names anywhere under
        // Anela.Heblo.Domain.Features.Invoices.
        const string DomainNamespacePrefix = "Anela.Heblo.Domain";
        const string ForbiddenPrefix = "Anela.Heblo.Application";
        var relocatedTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "IssuedInvoiceFilters",
            "PaginatedResult`1",
            "IIssuedInvoiceRepository",
        };

        var assembly = Assembly.Load("Anela.Heblo.Domain");
        var domainTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(DomainNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        // (a) No Domain type references any Application type.
        var crossLayerViolations = new List<string>();
        foreach (var domainType in domainTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(domainType))
            {
                if (referencedType.Namespace is null)
                    continue;

                if (!referencedType.Namespace.Equals(ForbiddenPrefix, StringComparison.Ordinal)
                    && !referencedType.Namespace.StartsWith(ForbiddenPrefix + ".", StringComparison.Ordinal))
                    continue;

                crossLayerViolations.Add($"{domainType.FullName} -> {referencedType.FullName} (via {memberDescription})");
            }
        }

        crossLayerViolations.Should().BeEmpty(
            "Domain layer must not reference Anela.Heblo.Application.* types. " +
            "Found:\n  " + string.Join("\n  ", crossLayerViolations));

        // (b) The three relocated type names must not exist anywhere under Domain.
        var orphanRelocations = domainTypes
            .Where(t => relocatedTypeNames.Contains(t.Name))
            .Select(t => t.FullName)
            .ToList();

        orphanRelocations.Should().BeEmpty(
            "Relocated types (IssuedInvoiceFilters, PaginatedResult<T>, IIssuedInvoiceRepository) " +
            "must not exist in Anela.Heblo.Domain after the 2026-06-02 relocation. " +
            "Found:\n  " + string.Join("\n  ", orphanRelocations));
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
