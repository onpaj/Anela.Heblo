using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <summary>
    /// Repairs AppUser rows that were JIT-created before PR #2779 ("use Entra oid for user pairing")
    /// was deployed, plus rows whose Email/DisplayName columns were corrupted by missing-claim
    /// fallbacks in PermissionResolver. Pre-fix logins keyed AppUser by Entra "sub" (per-user-per-app
    /// pairwise pseudonymous ID); post-fix logins key by "oid" (tenant-wide Object ID). The result
    /// is a pair of AppUser rows per affected user — a sub-keyed one carrying the admin-assigned
    /// groups (visible in GetMe) and an empty oid-keyed one whose claims drive [Authorize] checks
    /// (causing 403s on every protected endpoint). On top of that, when the post-fix login also
    /// hit a token without email/preferred_username claims, the resolver stored EntraObjectId
    /// itself into the Email column (producing rows where Email == oid GUID).
    ///
    /// Strategy: extract a UPN-looking value from each row (whichever of Email / DisplayName
    /// contains an '@') and pair rows that share that UPN. For each matched pair (sub-keyed +
    /// oid-keyed): repair the oid-keyed row's Email/DisplayName from the sub-keyed sibling
    /// when the oid-keyed values are clearly bad (== EntraObjectId), move group links, roll up
    /// LastLoginAt / IsActive, then delete the sub-keyed duplicate. The UserGroups FK is ON
    /// DELETE CASCADE so any residual group links on the deleted row are removed automatically.
    /// </summary>
    public partial class MergeDuplicateAppUsersFromSubKeying : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string GuidPattern = @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

            // Postgres view (temporary, scoped to this migration) that exposes each AppUser's
            // best-guess UPN — the first '@'-containing value across Email then DisplayName.
            // A null UPN means the row can't be paired by this migration.
            // Defined inline in every statement via the same CASE for clarity.

            // Step 1: repair Email / DisplayName on the oid-keyed row when its values look like
            // the JIT fallback (== EntraObjectId). Pull replacement from the sub-keyed sibling
            // matched by UPN. Only overwrites clearly-broken values.
            migrationBuilder.Sql($@"
                UPDATE public.""AppUsers"" oid_user
                SET ""Email"" = CASE
                        WHEN LOWER(oid_user.""Email"") = LOWER(oid_user.""EntraObjectId"")
                             AND sub_user.""Email"" IS NOT NULL
                             AND sub_user.""Email"" <> sub_user.""EntraObjectId""
                        THEN sub_user.""Email""
                        ELSE oid_user.""Email""
                    END,
                    ""DisplayName"" = CASE
                        WHEN oid_user.""DisplayName"" = oid_user.""EntraObjectId""
                             AND sub_user.""DisplayName"" IS NOT NULL
                             AND sub_user.""DisplayName"" <> sub_user.""EntraObjectId""
                        THEN sub_user.""DisplayName""
                        ELSE oid_user.""DisplayName""
                    END
                FROM public.""AppUsers"" sub_user
                WHERE oid_user.""Id"" <> sub_user.""Id""
                  AND oid_user.""EntraObjectId"" ~* '{GuidPattern}'
                  AND sub_user.""EntraObjectId"" !~* '{GuidPattern}'
                  AND LOWER(CASE WHEN POSITION('@' IN oid_user.""Email"") > 0 THEN oid_user.""Email""
                                 WHEN POSITION('@' IN oid_user.""DisplayName"") > 0 THEN oid_user.""DisplayName""
                                 ELSE NULL END)
                    = LOWER(CASE WHEN POSITION('@' IN sub_user.""Email"") > 0 THEN sub_user.""Email""
                                 WHEN POSITION('@' IN sub_user.""DisplayName"") > 0 THEN sub_user.""DisplayName""
                                 ELSE NULL END);
            ");

            // Step 2: copy each sub-keyed row's group links onto its oid-keyed sibling.
            // ON CONFLICT keeps the migration idempotent and avoids duplicate (UserId, GroupId).
            migrationBuilder.Sql($@"
                INSERT INTO public.""UserGroups"" (""UserId"", ""GroupId"")
                SELECT oid_user.""Id"", sub_groups.""GroupId""
                FROM public.""AppUsers"" sub_user
                JOIN public.""AppUsers"" oid_user
                  ON oid_user.""Id"" <> sub_user.""Id""
                 AND oid_user.""EntraObjectId"" ~* '{GuidPattern}'
                 AND LOWER(CASE WHEN POSITION('@' IN oid_user.""Email"") > 0 THEN oid_user.""Email""
                                WHEN POSITION('@' IN oid_user.""DisplayName"") > 0 THEN oid_user.""DisplayName""
                                ELSE NULL END)
                   = LOWER(CASE WHEN POSITION('@' IN sub_user.""Email"") > 0 THEN sub_user.""Email""
                                WHEN POSITION('@' IN sub_user.""DisplayName"") > 0 THEN sub_user.""DisplayName""
                                ELSE NULL END)
                JOIN public.""UserGroups"" sub_groups
                  ON sub_groups.""UserId"" = sub_user.""Id""
                WHERE sub_user.""EntraObjectId"" !~* '{GuidPattern}'
                ON CONFLICT (""UserId"", ""GroupId"") DO NOTHING;
            ");

            // Step 3: roll up LastLoginAt (PG's GREATEST ignores NULL operands) and OR-merge
            // IsActive onto the surviving oid-keyed row before the sub-keyed row is deleted.
            migrationBuilder.Sql($@"
                UPDATE public.""AppUsers"" oid_user
                SET ""LastLoginAt"" = GREATEST(oid_user.""LastLoginAt"", sub_user.""LastLoginAt""),
                    ""IsActive"" = oid_user.""IsActive"" OR sub_user.""IsActive""
                FROM public.""AppUsers"" sub_user
                WHERE oid_user.""Id"" <> sub_user.""Id""
                  AND oid_user.""EntraObjectId"" ~* '{GuidPattern}'
                  AND sub_user.""EntraObjectId"" !~* '{GuidPattern}'
                  AND LOWER(CASE WHEN POSITION('@' IN oid_user.""Email"") > 0 THEN oid_user.""Email""
                                 WHEN POSITION('@' IN oid_user.""DisplayName"") > 0 THEN oid_user.""DisplayName""
                                 ELSE NULL END)
                    = LOWER(CASE WHEN POSITION('@' IN sub_user.""Email"") > 0 THEN sub_user.""Email""
                                 WHEN POSITION('@' IN sub_user.""DisplayName"") > 0 THEN sub_user.""DisplayName""
                                 ELSE NULL END);
            ");

            // Step 4: delete the sub-keyed duplicates that have a matching oid-keyed sibling.
            migrationBuilder.Sql($@"
                DELETE FROM public.""AppUsers"" sub_user
                WHERE sub_user.""EntraObjectId"" !~* '{GuidPattern}'
                  AND EXISTS (
                      SELECT 1
                      FROM public.""AppUsers"" oid_user
                      WHERE oid_user.""Id"" <> sub_user.""Id""
                        AND oid_user.""EntraObjectId"" ~* '{GuidPattern}'
                        AND LOWER(CASE WHEN POSITION('@' IN oid_user.""Email"") > 0 THEN oid_user.""Email""
                                       WHEN POSITION('@' IN oid_user.""DisplayName"") > 0 THEN oid_user.""DisplayName""
                                       ELSE NULL END)
                          = LOWER(CASE WHEN POSITION('@' IN sub_user.""Email"") > 0 THEN sub_user.""Email""
                                       WHEN POSITION('@' IN sub_user.""DisplayName"") > 0 THEN sub_user.""DisplayName""
                                       ELSE NULL END)
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() intentionally left empty: the merge is lossy (the original sub-keyed row's
            // Id and CreatedAt are gone). Restore from backup if a rollback is required.
        }
    }
}
