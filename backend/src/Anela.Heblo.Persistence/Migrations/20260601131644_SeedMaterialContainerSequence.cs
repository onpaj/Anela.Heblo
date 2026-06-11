using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedMaterialContainerSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Advance the code sequence above the highest existing M-code so generated
            // codes never collide with already-scanned pre-printed labels.
            migrationBuilder.Sql(@"
                SELECT setval(
                    'material_container_internal_seq',
                    GREATEST(
                        (SELECT last_value FROM material_container_internal_seq),
                        COALESCE((SELECT MAX(CAST(SUBSTRING(""Code"" FROM 2) AS bigint))
                                  FROM public.""MaterialContainers""
                                  WHERE ""Code"" ~ '^M[0-9]{8}$'), 0)
                    ),
                    true);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: resetting a sequence downward is unsafe and unnecessary.
        }
    }
}
