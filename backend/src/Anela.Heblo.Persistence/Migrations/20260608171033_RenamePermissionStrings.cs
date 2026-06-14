using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenamePermissionStrings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Old format ("feature.level") → new format ("module.feature.level").
            var mappings = new (string Old, string New)[]
            {
                ("financial_overview", "finance.financial_overview"),
                ("margin_analysis",    "finance.margin_analysis"),
                ("product_margins",    "products.product_margins"),
                ("catalog",            "products.catalog"),
                ("journal",            "products.journal"),
                ("bank_statements",    "customer.bank_statements"),
                ("knowledge_base",     "customer.knowledge_base"),
                ("smartsupp",          "customer.smartsupp"),
                ("purchase_orders",    "purchase.purchase_orders"),
                ("purchase_stock",     "purchase.purchase_stock"),
                ("manufacture_orders", "manufacture.manufacture_orders"),
                ("batch_planning",     "manufacture.batch_planning"),
                ("manufacture_output", "manufacture.manufacture_output"),
                ("manufacture_stock",  "manufacture.manufacture_stock"),
                ("material_inventory", "manufacture.material_inventory"),
                ("product_inventory",  "manufacture.product_inventory"),
                ("material_containers","manufacture.material_containers"),
                ("logistics",          "warehouse.logistics"),
                ("expedition",         "warehouse.expedition"),
                ("packaging",          "warehouse.packaging"),
                ("stock_up",           "warehouse.stock_up"),
                ("article",            "marketing.article"),
                ("leaflet",            "marketing.leaflet"),
                ("photobank",          "marketing.photobank"),
                ("marketing_calendar", "marketing.marketing_calendar"),
                ("meetings",           "anela.meetings"),
                ("org_chart",          "anela.org_chart"),
                ("data_quality",       "admin.data_quality"),
                ("administration",     "admin.administration"),
                ("feature_flags",      "admin.feature_flags"),
            };

            foreach (var (old, @new) in mappings)
            {
                foreach (var level in new[] { "read", "write", "admin" })
                {
                    migrationBuilder.Sql(
                        $"UPDATE \"GroupPermissions\" SET \"PermissionValue\" = '{@new}.{level}' WHERE \"PermissionValue\" = '{old}.{level}';");
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var mappings = new (string Old, string New)[]
            {
                ("financial_overview", "finance.financial_overview"),
                ("margin_analysis",    "finance.margin_analysis"),
                ("product_margins",    "products.product_margins"),
                ("catalog",            "products.catalog"),
                ("journal",            "products.journal"),
                ("bank_statements",    "customer.bank_statements"),
                ("knowledge_base",     "customer.knowledge_base"),
                ("smartsupp",          "customer.smartsupp"),
                ("purchase_orders",    "purchase.purchase_orders"),
                ("purchase_stock",     "purchase.purchase_stock"),
                ("manufacture_orders", "manufacture.manufacture_orders"),
                ("batch_planning",     "manufacture.batch_planning"),
                ("manufacture_output", "manufacture.manufacture_output"),
                ("manufacture_stock",  "manufacture.manufacture_stock"),
                ("material_inventory", "manufacture.material_inventory"),
                ("product_inventory",  "manufacture.product_inventory"),
                ("material_containers","manufacture.material_containers"),
                ("logistics",          "warehouse.logistics"),
                ("expedition",         "warehouse.expedition"),
                ("packaging",          "warehouse.packaging"),
                ("stock_up",           "warehouse.stock_up"),
                ("article",            "marketing.article"),
                ("leaflet",            "marketing.leaflet"),
                ("photobank",          "marketing.photobank"),
                ("marketing_calendar", "marketing.marketing_calendar"),
                ("meetings",           "anela.meetings"),
                ("org_chart",          "anela.org_chart"),
                ("data_quality",       "admin.data_quality"),
                ("administration",     "admin.administration"),
                ("feature_flags",      "admin.feature_flags"),
            };

            foreach (var (old, @new) in mappings)
            {
                foreach (var level in new[] { "read", "write", "admin" })
                {
                    migrationBuilder.Sql(
                        $"UPDATE \"GroupPermissions\" SET \"PermissionValue\" = '{old}.{level}' WHERE \"PermissionValue\" = '{@new}.{level}';");
                }
            }
        }
    }
}
