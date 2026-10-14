using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.ShoptetOrders.Migrations
{
    /// <inheritdoc />
    public partial class InitialShoptetRawSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shoptet_raw");

            migrationBuilder.CreateTable(
                name: "order",
                schema: "shoptet_raw",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    guid = table.Column<string>(type: "text", nullable: true),
                    external_code = table.Column<string>(type: "text", nullable: true),
                    creation_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status_id = table.Column<int>(type: "integer", nullable: false),
                    status_name = table.Column<string>(type: "text", nullable: true),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    customer_guid = table.Column<string>(type: "text", nullable: true),
                    customer_email = table.Column<string>(type: "text", nullable: true),
                    billing_company = table.Column<string>(type: "text", nullable: true),
                    billing_company_id = table.Column<string>(type: "text", nullable: true),
                    billing_city = table.Column<string>(type: "text", nullable: true),
                    billing_zip = table.Column<string>(type: "text", nullable: true),
                    billing_country_code = table.Column<string>(type: "text", nullable: true),
                    cash_desk_order = table.Column<bool>(type: "boolean", nullable: false),
                    sales_channel_guid = table.Column<string>(type: "text", nullable: true),
                    source_id = table.Column<int>(type: "integer", nullable: true),
                    source_name = table.Column<string>(type: "text", nullable: true),
                    shipping_guid = table.Column<string>(type: "text", nullable: true),
                    shipping_name = table.Column<string>(type: "text", nullable: true),
                    payment_method_guid = table.Column<string>(type: "text", nullable: true),
                    payment_method_name = table.Column<string>(type: "text", nullable: true),
                    billing_method_id = table.Column<int>(type: "integer", nullable: true),
                    billing_method_name = table.Column<string>(type: "text", nullable: true),
                    currency_code = table.Column<string>(type: "text", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    price_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    price_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    price_to_pay = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    shipping_price_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    shipping_price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    billing_price_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    billing_price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    product_price_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    product_price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    product_units = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    vat_payer = table.Column<bool>(type: "boolean", nullable: false),
                    vat_mode = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "text", nullable: true),
                    stock_id = table.Column<int>(type: "integer", nullable: true),
                    referer = table.Column<string>(type: "text", nullable: true),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                schema: "shoptet_raw",
                columns: table => new
                {
                    entity_name = table.Column<string>(type: "text", nullable: false),
                    watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_status = table.Column<string>(type: "text", nullable: true),
                    last_run_rows_fetched = table.Column<int>(type: "integer", nullable: true),
                    last_run_rows_upserted = table.Column<int>(type: "integer", nullable: true),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    backfill_cursor = table.Column<DateOnly>(type: "date", nullable: true),
                    backfill_completed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => x.entity_name);
                });

            migrationBuilder.CreateTable(
                name: "order_item",
                schema: "shoptet_raw",
                columns: table => new
                {
                    order_code = table.Column<string>(type: "text", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    source_array = table.Column<string>(type: "text", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    parent_item_id = table.Column<long>(type: "bigint", nullable: true),
                    item_type = table.Column<string>(type: "text", nullable: false),
                    product_type = table.Column<string>(type: "text", nullable: true),
                    product_guid = table.Column<string>(type: "text", nullable: true),
                    product_code = table.Column<string>(type: "text", nullable: true),
                    product_name = table.Column<string>(type: "text", nullable: true),
                    variant_name = table.Column<string>(type: "text", nullable: true),
                    brand = table.Column<string>(type: "text", nullable: true),
                    ean = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    amount_unit = table.Column<string>(type: "text", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    unit_price_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    unit_price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    line_price_with_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    line_price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    line_price_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    purchase_price_without_vat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item", x => new { x.order_code, x.line_no });
                    table.ForeignKey(
                        name: "FK_order_item_order_order_code",
                        column: x => x.order_code,
                        principalSchema: "shoptet_raw",
                        principalTable: "order",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_change_time",
                schema: "shoptet_raw",
                table: "order",
                column: "change_time");

            migrationBuilder.CreateIndex(
                name: "ix_order_customer_email",
                schema: "shoptet_raw",
                table: "order",
                column: "customer_email");

            migrationBuilder.CreateIndex(
                name: "ix_order_customer_guid",
                schema: "shoptet_raw",
                table: "order",
                column: "customer_guid");

            migrationBuilder.CreateIndex(
                name: "ix_order_order_date",
                schema: "shoptet_raw",
                table: "order",
                column: "order_date");

            migrationBuilder.CreateIndex(
                name: "ix_order_shipping_guid",
                schema: "shoptet_raw",
                table: "order",
                column: "shipping_guid");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_id",
                schema: "shoptet_raw",
                table: "order",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_item_type",
                schema: "shoptet_raw",
                table: "order_item",
                column: "item_type");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_product_code",
                schema: "shoptet_raw",
                table: "order_item",
                column: "product_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item",
                schema: "shoptet_raw");

            migrationBuilder.DropTable(
                name: "sync_state",
                schema: "shoptet_raw");

            migrationBuilder.DropTable(
                name: "order",
                schema: "shoptet_raw");
        }
    }
}
