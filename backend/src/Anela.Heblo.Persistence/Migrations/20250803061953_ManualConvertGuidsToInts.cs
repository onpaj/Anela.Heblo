using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualConvertGuidsToInts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, delete all existing data as we cannot convert GUIDs to int IDs meaningfully
            migrationBuilder.Sql("DELETE FROM dbo.\"PurchaseOrderHistory\"");
            migrationBuilder.Sql("DELETE FROM dbo.\"PurchaseOrderLines\"");
            migrationBuilder.Sql("DELETE FROM dbo.\"PurchaseOrders\"");

            // Drop foreign key constraints first
            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderLines"" 
                DROP CONSTRAINT IF EXISTS ""FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId""");

            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderHistory"" 
                DROP CONSTRAINT IF EXISTS ""FK_PurchaseOrderHistory_PurchaseOrders_PurchaseOrderId""");

            // Convert PurchaseOrders table
            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrders"" 
                DROP COLUMN ""Id""");

            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrders"" 
                ADD COLUMN ""Id"" SERIAL PRIMARY KEY");

            // Convert PurchaseOrderLines table
            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderLines"" 
                DROP COLUMN ""Id"", 
                DROP COLUMN ""PurchaseOrderId""");

            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderLines"" 
                ADD COLUMN ""Id"" SERIAL PRIMARY KEY,
                ADD COLUMN ""PurchaseOrderId"" INTEGER NOT NULL");

            // Convert PurchaseOrderHistory table
            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderHistory"" 
                DROP COLUMN ""Id"", 
                DROP COLUMN ""PurchaseOrderId""");

            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderHistory"" 
                ADD COLUMN ""Id"" SERIAL PRIMARY KEY,
                ADD COLUMN ""PurchaseOrderId"" INTEGER NOT NULL");

            // Recreate foreign key constraints
            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderLines"" 
                ADD CONSTRAINT ""FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId"" 
                FOREIGN KEY (""PurchaseOrderId"") REFERENCES dbo.""PurchaseOrders""(""Id"") ON DELETE CASCADE");

            migrationBuilder.Sql(@"
                ALTER TABLE dbo.""PurchaseOrderHistory"" 
                ADD CONSTRAINT ""FK_PurchaseOrderHistory_PurchaseOrders_PurchaseOrderId"" 
                FOREIGN KEY (""PurchaseOrderId"") REFERENCES dbo.""PurchaseOrders""(""Id"") ON DELETE CASCADE");

            // Recreate indexes
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_PurchaseOrderLines_PurchaseOrderId"" 
                ON dbo.""PurchaseOrderLines"" (""PurchaseOrderId"")");

            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_PurchaseOrderHistory_PurchaseOrderId"" 
                ON dbo.""PurchaseOrderHistory"" (""PurchaseOrderId"")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This is a destructive migration that cannot be easily reversed
            // Data would be lost, so we just throw an exception
            throw new NotSupportedException("Cannot reverse this migration as it would result in data loss. Please restore from backup if needed.");
        }
    }
}
