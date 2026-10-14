namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>
/// Raw SQL used by the AddMarketingAdsProposals migration and by its integration test.
/// NEVER edit these strings once the migration is merged — add a new constant + migration instead.
/// </summary>
public static class MarketingAdsSql
{
    public const string AppendOnlyTriggersUp = """
        CREATE OR REPLACE FUNCTION public.marketing_ads_reject_mutation() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Table %.% is append-only: % is not allowed', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
                USING ERRCODE = 'P0001';
        END;
        $$;

        CREATE TRIGGER "TR_AdAuditEvents_AppendOnly"
            BEFORE UPDATE OR DELETE ON public."AdAuditEvents"
            FOR EACH ROW EXECUTE FUNCTION public.marketing_ads_reject_mutation();
        CREATE TRIGGER "TR_AdAuditEvents_NoTruncate"
            BEFORE TRUNCATE ON public."AdAuditEvents"
            FOR EACH STATEMENT EXECUTE FUNCTION public.marketing_ads_reject_mutation();

        CREATE TRIGGER "TR_AdProposalVersions_AppendOnly"
            BEFORE UPDATE OR DELETE ON public."AdProposalVersions"
            FOR EACH ROW EXECUTE FUNCTION public.marketing_ads_reject_mutation();
        CREATE TRIGGER "TR_AdProposalVersions_NoTruncate"
            BEFORE TRUNCATE ON public."AdProposalVersions"
            FOR EACH STATEMENT EXECUTE FUNCTION public.marketing_ads_reject_mutation();
        """;

    public const string AppendOnlyTriggersDown = """
        DROP TRIGGER IF EXISTS "TR_AdProposalVersions_NoTruncate" ON public."AdProposalVersions";
        DROP TRIGGER IF EXISTS "TR_AdProposalVersions_AppendOnly" ON public."AdProposalVersions";
        DROP TRIGGER IF EXISTS "TR_AdAuditEvents_NoTruncate" ON public."AdAuditEvents";
        DROP TRIGGER IF EXISTS "TR_AdAuditEvents_AppendOnly" ON public."AdAuditEvents";
        DROP FUNCTION IF EXISTS public.marketing_ads_reject_mutation();
        """;
}
