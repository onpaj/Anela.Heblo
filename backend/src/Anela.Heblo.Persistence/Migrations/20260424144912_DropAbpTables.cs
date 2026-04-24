using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropAbpTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop child tables first to respect foreign key constraints

            // AbpAuditLogs children
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpEntityPropertyChanges\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpEntityChanges\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpAuditLogActions\" CASCADE;");

            // AbpUsers children
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUserClaims\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUserLogins\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUserTokens\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUserRoles\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUserOrganizationUnits\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUserDelegations\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpLinkUsers\" CASCADE;");

            // AbpRoles children
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpRoleClaims\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpOrganizationUnitRoles\" CASCADE;");

            // AbpBlobContainers children
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpBlobs\" CASCADE;");

            // AbpPermissions children
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpPermissionValues\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpFeatureValues\" CASCADE;");

            // Root tables
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpAuditLogs\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpBackgroundJobs\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpBlobContainers\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpClaimTypes\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpFeatureGroups\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpFeatures\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpOrganizationUnits\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpPermissionGrants\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpPermissionGroups\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpPermissions\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpRoles\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpSecurityLogs\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpSessions\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpSettingDefinitions\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpSettings\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"AbpUsers\" CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ABP framework tables are not part of this application's domain model.
            // Restoring them is not supported — redeploy ABP if needed.
        }
    }
}
