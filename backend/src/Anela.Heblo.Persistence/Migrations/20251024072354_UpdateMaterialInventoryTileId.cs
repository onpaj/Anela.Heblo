using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMaterialInventoryTileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- First delete any existing materialwithexpirationinventorysummary tiles to avoid constraint conflicts
                DELETE FROM ""UserDashboardTiles""
                WHERE ""TileId"" = 'materialwithexpirationinventorysummary';
                
                -- Then update materialinventorysummary to materialwithexpirationinventorysummary
                UPDATE ""UserDashboardTiles""
                SET ""TileId"" = 'materialwithexpirationinventorysummary'
                WHERE ""TileId"" = 'materialinventorysummary';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""UserDashboardTiles""
                SET ""TileId"" = 'materialinventorysummary'
                WHERE ""TileId"" = 'materialwithexpirationinventorysummary'
            ");
        }
    }
}
