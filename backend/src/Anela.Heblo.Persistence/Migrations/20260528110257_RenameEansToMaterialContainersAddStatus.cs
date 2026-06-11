using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameEansToMaterialContainersAddStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE public.\"Eans\" RENAME TO \"MaterialContainers\";");
            migrationBuilder.Sql("ALTER INDEX public.\"IX_Eans_Code\" RENAME TO \"IX_MaterialContainers_Code\";");
            migrationBuilder.Sql("ALTER INDEX public.\"IX_Eans_LotId\" RENAME TO \"IX_MaterialContainers_LotId\";");
            migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"PK_Eans\" TO \"PK_MaterialContainers\";");
            migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"FK_Eans_Lots_LotId\" TO \"FK_MaterialContainers_Lots_LotId\";");
            migrationBuilder.Sql("ALTER SEQUENCE public.ean_internal_seq RENAME TO material_container_internal_seq;");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "public",
                table: "MaterialContainers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Status", schema: "public", table: "MaterialContainers");
            migrationBuilder.Sql("ALTER SEQUENCE public.material_container_internal_seq RENAME TO ean_internal_seq;");
            migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"FK_MaterialContainers_Lots_LotId\" TO \"FK_Eans_Lots_LotId\";");
            migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"PK_MaterialContainers\" TO \"PK_Eans\";");
            migrationBuilder.Sql("ALTER INDEX public.\"IX_MaterialContainers_LotId\" RENAME TO \"IX_Eans_LotId\";");
            migrationBuilder.Sql("ALTER INDEX public.\"IX_MaterialContainers_Code\" RENAME TO \"IX_Eans_Code\";");
            migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME TO \"Eans\";");
        }
    }
}
