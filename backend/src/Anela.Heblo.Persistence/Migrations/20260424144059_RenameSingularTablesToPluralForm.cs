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
                schema: "public",
                table: "IssuedInvoiceSyncData");

            migrationBuilder.DropForeignKey(
                name: "FK_PackingMaterialLog_PackingMaterial_PackingMaterialId",
                schema: "public",
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
                schema: "public",
                table: "StockTakingResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackingMaterialLog",
                schema: "public",
                table: "PackingMaterialLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackingMaterial",
                schema: "public",
                table: "PackingMaterial");

            migrationBuilder.DropPrimaryKey(
                name: "PK_IssuedInvoice",
                schema: "public",
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
                schema: "public",
                newName: "StockTakingRecords",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterialLog",
                schema: "public",
                newName: "PackingMaterialLogs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterial",
                schema: "public",
                newName: "PackingMaterials",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "IssuedInvoice",
                schema: "public",
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

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoice_LastSyncTime",
                schema: "public",
                table: "IssuedInvoices",
                newName: "IX_IssuedInvoices_LastSyncTime");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoice_IsSynced",
                schema: "public",
                table: "IssuedInvoices",
                newName: "IX_IssuedInvoices_IsSynced");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoice_InvoiceDate",
                schema: "public",
                table: "IssuedInvoices",
                newName: "IX_IssuedInvoices_InvoiceDate");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoice_ErrorType",
                schema: "public",
                table: "IssuedInvoices",
                newName: "IX_IssuedInvoices_ErrorType");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoice_CustomerName",
                schema: "public",
                table: "IssuedInvoices",
                newName: "IX_IssuedInvoices_CustomerName");

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
                schema: "public",
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
                schema: "public",
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
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterials",
                schema: "public",
                newName: "PackingMaterial",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterialLogs",
                schema: "public",
                newName: "PackingMaterialLog",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "IssuedInvoices",
                schema: "public",
                newName: "IssuedInvoice",
                newSchema: "public");

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
                schema: "public",
                table: "PackingMaterial",
                newName: "IX_PackingMaterial_Name");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLogs_PackingMaterialId",
                schema: "public",
                table: "PackingMaterialLog",
                newName: "IX_PackingMaterialLog_PackingMaterialId");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLogs_MaterialId_Date",
                schema: "public",
                table: "PackingMaterialLog",
                newName: "IX_PackingMaterialLog_MaterialId_Date");

            migrationBuilder.RenameIndex(
                name: "IX_PackingMaterialLogs_LogType",
                schema: "public",
                table: "PackingMaterialLog",
                newName: "IX_PackingMaterialLog_LogType");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoices_LastSyncTime",
                schema: "public",
                table: "IssuedInvoice",
                newName: "IX_IssuedInvoice_LastSyncTime");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoices_IsSynced",
                schema: "public",
                table: "IssuedInvoice",
                newName: "IX_IssuedInvoice_IsSynced");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoices_InvoiceDate",
                schema: "public",
                table: "IssuedInvoice",
                newName: "IX_IssuedInvoice_InvoiceDate");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoices_ErrorType",
                schema: "public",
                table: "IssuedInvoice",
                newName: "IX_IssuedInvoice_ErrorType");

            migrationBuilder.RenameIndex(
                name: "IX_IssuedInvoices_CustomerName",
                schema: "public",
                table: "IssuedInvoice",
                newName: "IX_IssuedInvoice_CustomerName");

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
                schema: "public",
                table: "StockTakingResults",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackingMaterial",
                schema: "public",
                table: "PackingMaterial",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackingMaterialLog",
                schema: "public",
                table: "PackingMaterialLog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IssuedInvoice",
                schema: "public",
                table: "IssuedInvoice",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_IssuedInvoiceSyncData_IssuedInvoice_IssuedInvoiceId",
                schema: "public",
                table: "IssuedInvoiceSyncData",
                column: "IssuedInvoiceId",
                principalSchema: "public",
                principalTable: "IssuedInvoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PackingMaterialLog_PackingMaterial_PackingMaterialId",
                schema: "public",
                table: "PackingMaterialLog",
                column: "PackingMaterialId",
                principalSchema: "public",
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
