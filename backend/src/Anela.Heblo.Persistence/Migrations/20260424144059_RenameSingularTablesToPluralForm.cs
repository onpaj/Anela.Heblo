using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameSingularTablesToPluralForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IssuedInvoiceSyncData_IssuedInvoice_IssuedInvoiceId",
                schema: "dbo",
                table: "IssuedInvoiceSyncData");

            migrationBuilder.DropForeignKey(
                name: "FK_PackingMaterialLog_PackingMaterial_PackingMaterialId",
                schema: "dbo",
                table: "PackingMaterialLog");

            migrationBuilder.DropForeignKey(
                name: "FK_TransportBoxItem_TransportBox_TransportBoxId",
                schema: "public",
                table: "TransportBoxItem");

            migrationBuilder.DropForeignKey(
                name: "FK_TransportBoxStateLog_TransportBox_TransportBoxId",
                schema: "public",
                table: "TransportBoxStateLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransportBoxStateLog",
                schema: "public",
                table: "TransportBoxStateLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransportBoxItem",
                schema: "public",
                table: "TransportBoxItem");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransportBox",
                schema: "public",
                table: "TransportBox");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StockTakingResults",
                schema: "dbo",
                table: "StockTakingResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackingMaterialLog",
                schema: "dbo",
                table: "PackingMaterialLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackingMaterial",
                schema: "dbo",
                table: "PackingMaterial");

            migrationBuilder.DropPrimaryKey(
                name: "PK_IssuedInvoice",
                schema: "dbo",
                table: "IssuedInvoice");

            migrationBuilder.RenameTable(
                name: "TransportBoxStateLog",
                schema: "public",
                newName: "TransportBoxStateLogs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "TransportBoxItem",
                schema: "public",
                newName: "TransportBoxItems",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "TransportBox",
                schema: "public",
                newName: "TransportBoxes",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "StockTakingResults",
                schema: "dbo",
                newName: "StockTakingRecords",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterialLog",
                schema: "dbo",
                newName: "PackingMaterialLogs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterial",
                schema: "dbo",
                newName: "PackingMaterials",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "IssuedInvoice",
                schema: "dbo",
                newName: "IssuedInvoices",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_TransportBoxStateLog_TransportBoxId",
                schema: "public",
                table: "TransportBoxStateLogs",
                newName: "IX_TransportBoxStateLogs_TransportBoxId");

            migrationBuilder.RenameIndex(
                name: "IX_TransportBoxItem_TransportBoxId",
                schema: "public",
                table: "TransportBoxItems",
                newName: "IX_TransportBoxItems_TransportBoxId");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLog_PackingMaterialId",
                schema: "public",
                table: "PackingMaterialLogs",
                newName: "IX_PackingMaterialLogs_PackingMaterialId");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLog_MaterialId_Date",
                schema: "public",
                table: "PackingMaterialLogs",
                newName: "IX_PackingMaterialLogs_MaterialId_Date");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLog_LogType",
                schema: "public",
                table: "PackingMaterialLogs",
                newName: "IX_PackingMaterialLogs_LogType");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterial_Name",
                schema: "public",
                table: "PackingMaterials",
                newName: "IX_PackingMaterials_Name");

            // Rename-or-create pattern: staging may be missing these indexes if
            // AddIssuedInvoiceFilterIndexes was never applied (no Designer.cs).
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='IX_IssuedInvoice_LastSyncTime') THEN
                        ALTER INDEX public.""IX_IssuedInvoice_LastSyncTime"" RENAME TO ""IX_IssuedInvoices_LastSyncTime"";
                    ELSE
                        CREATE INDEX IF NOT EXISTS ""IX_IssuedInvoices_LastSyncTime"" ON public.""IssuedInvoices"" (""LastSyncTime"");
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='IX_IssuedInvoice_IsSynced') THEN
                        ALTER INDEX public.""IX_IssuedInvoice_IsSynced"" RENAME TO ""IX_IssuedInvoices_IsSynced"";
                    ELSE
                        CREATE INDEX IF NOT EXISTS ""IX_IssuedInvoices_IsSynced"" ON public.""IssuedInvoices"" (""IsSynced"");
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='IX_IssuedInvoice_InvoiceDate') THEN
                        ALTER INDEX public.""IX_IssuedInvoice_InvoiceDate"" RENAME TO ""IX_IssuedInvoices_InvoiceDate"";
                    ELSE
                        CREATE INDEX IF NOT EXISTS ""IX_IssuedInvoices_InvoiceDate"" ON public.""IssuedInvoices"" (""InvoiceDate"");
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='IX_IssuedInvoice_ErrorType') THEN
                        ALTER INDEX public.""IX_IssuedInvoice_ErrorType"" RENAME TO ""IX_IssuedInvoices_ErrorType"";
                    ELSE
                        CREATE INDEX IF NOT EXISTS ""IX_IssuedInvoices_ErrorType"" ON public.""IssuedInvoices"" (""ErrorType"");
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='IX_IssuedInvoice_CustomerName') THEN
                        ALTER INDEX public.""IX_IssuedInvoice_CustomerName"" RENAME TO ""IX_IssuedInvoices_CustomerName"";
                    ELSE
                        CREATE INDEX IF NOT EXISTS ""IX_IssuedInvoices_CustomerName"" ON public.""IssuedInvoices"" (""CustomerName"");
                    END IF;
                END $$;
            ");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransportBoxStateLogs",
                schema: "public",
                table: "TransportBoxStateLogs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransportBoxItems",
                schema: "public",
                table: "TransportBoxItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransportBoxes",
                schema: "public",
                table: "TransportBoxes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockTakingRecords",
                schema: "public",
                table: "StockTakingRecords",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackingMaterialLogs",
                schema: "public",
                table: "PackingMaterialLogs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackingMaterials",
                schema: "public",
                table: "PackingMaterials",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IssuedInvoices",
                schema: "public",
                table: "IssuedInvoices",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_IssuedInvoiceSyncData_IssuedInvoices_IssuedInvoiceId",
                schema: "dbo",
                table: "IssuedInvoiceSyncData",
                column: "IssuedInvoiceId",
                principalSchema: "public",
                principalTable: "IssuedInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PackingMaterialLogs_PackingMaterials_PackingMaterialId",
                schema: "public",
                table: "PackingMaterialLogs",
                column: "PackingMaterialId",
                principalSchema: "public",
                principalTable: "PackingMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TransportBoxItems_TransportBoxes_TransportBoxId",
                schema: "public",
                table: "TransportBoxItems",
                column: "TransportBoxId",
                principalSchema: "public",
                principalTable: "TransportBoxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TransportBoxStateLogs_TransportBoxes_TransportBoxId",
                schema: "public",
                table: "TransportBoxStateLogs",
                column: "TransportBoxId",
                principalSchema: "public",
                principalTable: "TransportBoxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IssuedInvoiceSyncData_IssuedInvoices_IssuedInvoiceId",
                schema: "dbo",
                table: "IssuedInvoiceSyncData");

            migrationBuilder.DropForeignKey(
                name: "FK_PackingMaterialLogs_PackingMaterials_PackingMaterialId",
                schema: "public",
                table: "PackingMaterialLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_TransportBoxItems_TransportBoxes_TransportBoxId",
                schema: "public",
                table: "TransportBoxItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TransportBoxStateLogs_TransportBoxes_TransportBoxId",
                schema: "public",
                table: "TransportBoxStateLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransportBoxStateLogs",
                schema: "public",
                table: "TransportBoxStateLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransportBoxItems",
                schema: "public",
                table: "TransportBoxItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransportBoxes",
                schema: "public",
                table: "TransportBoxes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StockTakingRecords",
                schema: "public",
                table: "StockTakingRecords");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackingMaterials",
                schema: "public",
                table: "PackingMaterials");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackingMaterialLogs",
                schema: "public",
                table: "PackingMaterialLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_IssuedInvoices",
                schema: "public",
                table: "IssuedInvoices");

            migrationBuilder.RenameTable(
                name: "TransportBoxStateLogs",
                schema: "public",
                newName: "TransportBoxStateLog",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "TransportBoxItems",
                schema: "public",
                newName: "TransportBoxItem",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "TransportBoxes",
                schema: "public",
                newName: "TransportBox",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "StockTakingRecords",
                schema: "public",
                newName: "StockTakingResults",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PackingMaterials",
                schema: "public",
                newName: "PackingMaterial",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PackingMaterialLogs",
                schema: "public",
                newName: "PackingMaterialLog",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "IssuedInvoices",
                schema: "public",
                newName: "IssuedInvoice",
                newSchema: "dbo");

            migrationBuilder.RenameIndex(
                name: "IX_TransportBoxStateLogs_TransportBoxId",
                schema: "public",
                table: "TransportBoxStateLog",
                newName: "IX_TransportBoxStateLog_TransportBoxId");

            migrationBuilder.RenameIndex(
                name: "IX_TransportBoxItems_TransportBoxId",
                schema: "public",
                table: "TransportBoxItem",
                newName: "IX_TransportBoxItem_TransportBoxId");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterials_Name",
                schema: "dbo",
                table: "PackingMaterial",
                newName: "IX_PackingMaterial_Name");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLogs_PackingMaterialId",
                schema: "dbo",
                table: "PackingMaterialLog",
                newName: "IX_PackingMaterialLog_PackingMaterialId");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLogs_MaterialId_Date",
                schema: "dbo",
                table: "PackingMaterialLog",
                newName: "IX_PackingMaterialLog_MaterialId_Date");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLogs_LogType",
                schema: "dbo",
                table: "PackingMaterialLog",
                newName: "IX_PackingMaterialLog_LogType");

            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='dbo' AND indexname='IX_IssuedInvoices_LastSyncTime') THEN
                        ALTER INDEX dbo.""IX_IssuedInvoices_LastSyncTime"" RENAME TO ""IX_IssuedInvoice_LastSyncTime"";
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='dbo' AND indexname='IX_IssuedInvoices_IsSynced') THEN
                        ALTER INDEX dbo.""IX_IssuedInvoices_IsSynced"" RENAME TO ""IX_IssuedInvoice_IsSynced"";
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='dbo' AND indexname='IX_IssuedInvoices_InvoiceDate') THEN
                        ALTER INDEX dbo.""IX_IssuedInvoices_InvoiceDate"" RENAME TO ""IX_IssuedInvoice_InvoiceDate"";
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='dbo' AND indexname='IX_IssuedInvoices_ErrorType') THEN
                        ALTER INDEX dbo.""IX_IssuedInvoices_ErrorType"" RENAME TO ""IX_IssuedInvoice_ErrorType"";
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='dbo' AND indexname='IX_IssuedInvoices_CustomerName') THEN
                        ALTER INDEX dbo.""IX_IssuedInvoices_CustomerName"" RENAME TO ""IX_IssuedInvoice_CustomerName"";
                    END IF;
                END $$;
            ");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransportBoxStateLog",
                schema: "public",
                table: "TransportBoxStateLog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransportBoxItem",
                schema: "public",
                table: "TransportBoxItem",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransportBox",
                schema: "public",
                table: "TransportBox",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockTakingResults",
                schema: "dbo",
                table: "StockTakingResults",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackingMaterial",
                schema: "dbo",
                table: "PackingMaterial",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackingMaterialLog",
                schema: "dbo",
                table: "PackingMaterialLog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IssuedInvoice",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_IssuedInvoiceSyncData_IssuedInvoice_IssuedInvoiceId",
                schema: "dbo",
                table: "IssuedInvoiceSyncData",
                column: "IssuedInvoiceId",
                principalSchema: "dbo",
                principalTable: "IssuedInvoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PackingMaterialLog_PackingMaterial_PackingMaterialId",
                schema: "dbo",
                table: "PackingMaterialLog",
                column: "PackingMaterialId",
                principalSchema: "dbo",
                principalTable: "PackingMaterial",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TransportBoxItem_TransportBox_TransportBoxId",
                schema: "public",
                table: "TransportBoxItem",
                column: "TransportBoxId",
                principalSchema: "public",
                principalTable: "TransportBox",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TransportBoxStateLog_TransportBox_TransportBoxId",
                schema: "public",
                table: "TransportBoxStateLog",
                column: "TransportBoxId",
                principalSchema: "public",
                principalTable: "TransportBox",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
