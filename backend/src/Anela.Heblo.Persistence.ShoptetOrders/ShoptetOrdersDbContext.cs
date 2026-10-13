using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.ShoptetOrders;

public class ShoptetOrdersDbContext : DbContext
{
    public const string SchemaName = "shoptet_raw";

    public DbSet<ShoptetOrder> Orders => Set<ShoptetOrder>();
    public DbSet<ShoptetOrderItem> OrderItems => Set<ShoptetOrderItem>();
    public DbSet<ShoptetSyncState> SyncStates => Set<ShoptetSyncState>();

    public ShoptetOrdersDbContext(DbContextOptions<ShoptetOrdersDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(SchemaName);

        builder.Entity<ShoptetOrder>(e =>
        {
            e.ToTable("order");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasColumnName("code").ValueGeneratedNever();
            e.Property(x => x.Guid).HasColumnName("guid");
            e.Property(x => x.ExternalCode).HasColumnName("external_code");
            e.Property(x => x.CreationTime).HasColumnName("creation_time");
            e.Property(x => x.ChangeTime).HasColumnName("change_time");
            e.Property(x => x.OrderDate).HasColumnName("order_date");
            e.Property(x => x.StatusId).HasColumnName("status_id");
            e.Property(x => x.StatusName).HasColumnName("status_name");
            e.Property(x => x.IsPaid).HasColumnName("is_paid");
            e.Property(x => x.CustomerGuid).HasColumnName("customer_guid");
            e.Property(x => x.CustomerEmail).HasColumnName("customer_email");
            e.Property(x => x.BillingCompany).HasColumnName("billing_company");
            e.Property(x => x.BillingCompanyId).HasColumnName("billing_company_id");
            e.Property(x => x.BillingCity).HasColumnName("billing_city");
            e.Property(x => x.BillingZip).HasColumnName("billing_zip");
            e.Property(x => x.BillingCountryCode).HasColumnName("billing_country_code");
            e.Property(x => x.CashDeskOrder).HasColumnName("cash_desk_order");
            e.Property(x => x.SalesChannelGuid).HasColumnName("sales_channel_guid");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceName).HasColumnName("source_name");
            e.Property(x => x.ShippingGuid).HasColumnName("shipping_guid");
            e.Property(x => x.ShippingName).HasColumnName("shipping_name");
            e.Property(x => x.PaymentMethodGuid).HasColumnName("payment_method_guid");
            e.Property(x => x.PaymentMethodName).HasColumnName("payment_method_name");
            e.Property(x => x.BillingMethodId).HasColumnName("billing_method_id");
            e.Property(x => x.BillingMethodName).HasColumnName("billing_method_name");
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code");
            e.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").HasColumnType("numeric(18,8)");
            e.Property(x => x.PriceWithVat).HasColumnName("price_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.PriceWithoutVat).HasColumnName("price_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.PriceVat).HasColumnName("price_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.PriceToPay).HasColumnName("price_to_pay").HasColumnType("numeric(18,4)");
            e.Property(x => x.ShippingPriceWithVat).HasColumnName("shipping_price_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.ShippingPriceWithoutVat).HasColumnName("shipping_price_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.BillingPriceWithVat).HasColumnName("billing_price_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.BillingPriceWithoutVat).HasColumnName("billing_price_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.DiscountWithVat).HasColumnName("discount_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.DiscountWithoutVat).HasColumnName("discount_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.ProductPriceWithVat).HasColumnName("product_price_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.ProductPriceWithoutVat).HasColumnName("product_price_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.ProductUnits).HasColumnName("product_units").HasColumnType("numeric(18,3)");
            e.Property(x => x.VatPayer).HasColumnName("vat_payer");
            e.Property(x => x.VatMode).HasColumnName("vat_mode");
            e.Property(x => x.Language).HasColumnName("language");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Referer).HasColumnName("referer");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");

            e.HasIndex(x => x.OrderDate).HasDatabaseName("ix_order_order_date");
            e.HasIndex(x => x.ChangeTime).HasDatabaseName("ix_order_change_time");
            e.HasIndex(x => x.CustomerEmail).HasDatabaseName("ix_order_customer_email");
            e.HasIndex(x => x.CustomerGuid).HasDatabaseName("ix_order_customer_guid");
            e.HasIndex(x => x.ShippingGuid).HasDatabaseName("ix_order_shipping_guid");
            e.HasIndex(x => x.StatusId).HasDatabaseName("ix_order_status_id");
        });

        builder.Entity<ShoptetOrderItem>(e =>
        {
            e.ToTable("order_item");
            e.HasKey(x => new { x.OrderCode, x.LineNo });
            e.Property(x => x.OrderCode).HasColumnName("order_code");
            e.Property(x => x.LineNo).HasColumnName("line_no").ValueGeneratedNever();
            e.Property(x => x.SourceArray).HasColumnName("source_array");
            e.Property(x => x.ItemId).HasColumnName("item_id");
            e.Property(x => x.ParentItemId).HasColumnName("parent_item_id");
            e.Property(x => x.ItemType).HasColumnName("item_type");
            e.Property(x => x.ProductType).HasColumnName("product_type");
            e.Property(x => x.ProductGuid).HasColumnName("product_guid");
            e.Property(x => x.ProductCode).HasColumnName("product_code");
            e.Property(x => x.ProductName).HasColumnName("product_name");
            e.Property(x => x.VariantName).HasColumnName("variant_name");
            e.Property(x => x.Brand).HasColumnName("brand");
            e.Property(x => x.Ean).HasColumnName("ean");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,3)");
            e.Property(x => x.AmountUnit).HasColumnName("amount_unit");
            e.Property(x => x.Weight).HasColumnName("weight").HasColumnType("numeric(18,4)");
            e.Property(x => x.UnitPriceWithVat).HasColumnName("unit_price_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.UnitPriceWithoutVat).HasColumnName("unit_price_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.VatRate).HasColumnName("vat_rate").HasColumnType("numeric(9,4)");
            e.Property(x => x.LinePriceWithVat).HasColumnName("line_price_with_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.LinePriceWithoutVat).HasColumnName("line_price_without_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.LinePriceVat).HasColumnName("line_price_vat").HasColumnType("numeric(18,4)");
            e.Property(x => x.PurchasePriceWithoutVat).HasColumnName("purchase_price_without_vat").HasColumnType("numeric(18,4)");
            // No raw_payload here on purpose: order.raw_payload already holds every line verbatim
            // (items[] and completion[]), so a second copy per line would add a few hundred MB to a
            // 32 GB shared instance and recover nothing the order's payload cannot.
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");

            e.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderCode)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ProductCode).HasDatabaseName("ix_order_item_product_code");
            e.HasIndex(x => x.ItemType).HasDatabaseName("ix_order_item_item_type");
        });

        builder.Entity<ShoptetSyncState>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => x.EntityName);
            e.Property(x => x.EntityName).HasColumnName("entity_name");
            e.Property(x => x.Watermark).HasColumnName("watermark");
            e.Property(x => x.LastRunStartedAt).HasColumnName("last_run_started_at");
            e.Property(x => x.LastRunFinishedAt).HasColumnName("last_run_finished_at");
            e.Property(x => x.LastRunStatus).HasColumnName("last_run_status");
            e.Property(x => x.LastRunRowsFetched).HasColumnName("last_run_rows_fetched");
            e.Property(x => x.LastRunRowsUpserted).HasColumnName("last_run_rows_upserted");
            e.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
            e.Property(x => x.BackfillCursor).HasColumnName("backfill_cursor");
            e.Property(x => x.BackfillCompleted).HasColumnName("backfill_completed");
        });
    }
}
