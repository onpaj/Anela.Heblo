# Campaign Data Integration — Facebook & Google Ads

## Context

Anela Heblo runs active advertising campaigns on both Meta (Facebook/Instagram) and Google Ads. Currently there is no way to view campaign performance within the Heblo workspace — users must check each platform's native dashboard separately. This feature brings campaign data into Heblo with a unified dashboard, enabling ad spend tracking, ROI analysis, and eventually order attribution.

**Scope:** Campaign sync + dashboard. Order attribution is deferred to a follow-up phase.

## Approach

**Approach A (chosen): Google Ads SDK + Meta REST**

- **Meta adapter**: Typed `HttpClient` calling Meta Marketing API v21.0 — follows the existing ShoptetApi adapter pattern exactly.
- **Google adapter**: Official `Google.Ads.GoogleAds` NuGet SDK — provides strong typing, gRPC transport, and automatic OAuth token refresh.
- Separate domain interfaces per platform (`IMetaAdsClient`, `IGoogleAdsClient`) rather than a shared interface, since the platforms differ enough.

## Data Model

### Entities (EF Core, new `Campaigns` feature)

```
AdPlatform (enum): Meta, Google

AdCampaign
├── Id (Guid, PK)
├── Platform (AdPlatform)
├── PlatformCampaignId (string) — Facebook/Google campaign ID
├── Name (string)
├── Status (string) — Active, Paused, Archived, etc.
├── Objective (string) — e.g. CONVERSIONS, REACH
├── DailyBudget (decimal?)
├── Currency (string)
├── CreatedAt (DateTime)
├── SyncedAt (DateTime)

AdAdSet (Facebook Ad Set / Google Ad Group)
├── Id (Guid, PK)
├── CampaignId (Guid, FK → AdCampaign)
├── Platform (AdPlatform)
├── PlatformAdSetId (string)
├── Name (string)
├── Status (string)
├── TargetingDescription (string?) — summary of targeting settings

Ad
├── Id (Guid, PK)
├── AdSetId (Guid, FK → AdAdSet)
├── Platform (AdPlatform)
├── PlatformAdId (string)
├── Name (string)
├── Status (string)
├── CreativePreviewUrl (string?)

AdDailyMetric
├── Id (Guid, PK)
├── AdId (Guid, FK → Ad)
├── Date (DateOnly)
├── Impressions (long)
├── Clicks (long)
├── Spend (decimal) — in campaign currency
├── Conversions (int)
├── ConversionValue (decimal)
├── CTR (decimal, computed) — Clicks / Impressions
├── CPC (decimal, computed) — Spend / Clicks
├── ROAS (decimal, computed) — ConversionValue / Spend
├── Unique key: (AdId, Date)

AdSyncLog
├── Id (Guid, PK)
├── Platform (AdPlatform)
├── SyncStarted (DateTime)
├── SyncCompleted (DateTime?)
├── Status (string) — Running, Succeeded, Failed
├── ErrorMessage (string?)
├── RecordsProcessed (int)
```

## Backend Architecture

### New Adapter: `Anela.Heblo.Adapters.MetaAds`

```
Adapters/Anela.Heblo.Adapters.MetaAds/
├── MetaAdsSettings.cs              — AdAccountId, AccessToken, ApiVersion
├── MetaTokenRefreshHandler.cs      — DelegatingHandler for 60-day token refresh
├── MetaAdsClient.cs                — implements IMetaAdsClient
├── Model/
│   ├── MetaCampaignResponse.cs
│   ├── MetaAdSetResponse.cs
│   ├── MetaAdResponse.cs
│   └── MetaInsightsResponse.cs
└── MetaAdsAdapterServiceCollectionExtensions.cs
```

**Meta Marketing API endpoints used:**
- `GET /v21.0/act_{ad_account_id}/campaigns?fields=name,status,objective,daily_budget`
- `GET /v21.0/act_{ad_account_id}/adsets?fields=name,status,targeting`
- `GET /v21.0/act_{ad_account_id}/ads?fields=name,status,creative`
- `GET /v21.0/act_{ad_account_id}/insights?fields=impressions,clicks,spend,conversions,conversion_values&level=ad&time_range={...}&time_increment=1`

**Token management:** `MetaTokenRefreshHandler` is a `DelegatingHandler` attached to the typed HttpClient. It stores the token expiry timestamp and calls `GET /oauth/access_token?grant_type=fb_exchange_token` to refresh before expiry.

### New Adapter: `Anela.Heblo.Adapters.GoogleAds`

```
Adapters/Anela.Heblo.Adapters.GoogleAds/
├── GoogleAdsSettings.cs            — CustomerId, DeveloperToken, OAuth2 credentials
├── GoogleAdsClientWrapper.cs       — implements IGoogleAdsClient, wraps SDK's GoogleAdsClient
├── GoogleAdsAdapterServiceCollectionExtensions.cs
```

**GAQL queries used:**
- Campaigns: `SELECT campaign.id, campaign.name, campaign.status, campaign.advertising_channel_type, campaign_budget.amount_micros FROM campaign`
- Ad Groups: `SELECT ad_group.id, ad_group.name, ad_group.status, ad_group.campaign FROM ad_group WHERE campaign.id = {id}`
- Ads: `SELECT ad_group_ad.ad.id, ad_group_ad.ad.name, ad_group_ad.status FROM ad_group_ad WHERE ad_group.id = {id}`
- Metrics: `SELECT segments.date, metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value, ad_group_ad.ad.id FROM ad_group_ad WHERE segments.date BETWEEN '{start}' AND '{end}'`

**Token management:** Handled automatically by the `Google.Ads.GoogleAds` SDK — configured with refresh token, client ID, and client secret.

### Domain Layer — `Domain/Features/Campaigns/`

```csharp
// Separate interfaces per platform
public interface IMetaAdsClient
{
    Task<IReadOnlyList<MetaCampaignDto>> GetCampaignsAsync(CancellationToken ct);
    Task<IReadOnlyList<MetaAdSetDto>> GetAdSetsAsync(string campaignId, CancellationToken ct);
    Task<IReadOnlyList<MetaAdDto>> GetAdsAsync(string adSetId, CancellationToken ct);
    Task<IReadOnlyList<MetaInsightDto>> GetInsightsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public interface IGoogleAdsClient
{
    Task<IReadOnlyList<GoogleCampaignDto>> GetCampaignsAsync(CancellationToken ct);
    Task<IReadOnlyList<GoogleAdGroupDto>> GetAdGroupsAsync(string campaignId, CancellationToken ct);
    Task<IReadOnlyList<GoogleAdDto>> GetAdsAsync(string adGroupId, CancellationToken ct);
    Task<IReadOnlyList<GoogleMetricDto>> GetMetricsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public interface ICampaignRepository
{
    Task UpsertCampaignsAsync(IEnumerable<AdCampaign> campaigns, CancellationToken ct);
    Task UpsertAdSetsAsync(IEnumerable<AdAdSet> adSets, CancellationToken ct);
    Task UpsertAdsAsync(IEnumerable<Ad> ads, CancellationToken ct);
    Task UpsertDailyMetricsAsync(IEnumerable<AdDailyMetric> metrics, CancellationToken ct);
    // Query methods for dashboard
    Task<CampaignDashboardDto> GetDashboardAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct);
    Task<IReadOnlyList<CampaignSummaryDto>> GetCampaignListAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct);
    Task<CampaignDetailDto> GetCampaignDetailAsync(Guid campaignId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

### Application Layer — `Application/Features/Campaigns/UseCases/`

**Sync handlers:**
- `SyncMetaAdsHandler` — calls `IMetaAdsClient`, maps to domain entities, upserts via `ICampaignRepository`, logs to `AdSyncLog`
- `SyncGoogleAdsHandler` — calls `IGoogleAdsClient`, maps to domain entities, upserts via `ICampaignRepository`, logs to `AdSyncLog`

**Query handlers:**
- `GetCampaignDashboardHandler` — aggregated metrics (total spend, conversions, ROAS) with date range and platform filter
- `GetCampaignListHandler` — paginated campaign list with metrics
- `GetCampaignDetailHandler` — drill-down: campaign → ad sets → ads with metrics

### API Layer — `API/Controllers/CampaignsController.cs`

```
GET  /api/campaigns/dashboard?from=&to=&platform=     — dashboard summary
GET  /api/campaigns?from=&to=&platform=&page=&size=    — campaign list
GET  /api/campaigns/{id}?from=&to=                     — campaign detail with ad sets + ads
POST /api/campaigns/sync                               — trigger manual sync
```

### Background Sync

Registered in `BackgroundRefreshTaskRegistry`:
- `SyncMetaAds` — daily at ~02:00, syncs previous day's data
- `SyncGoogleAds` — daily at ~02:15, syncs previous day's data

Both sync the last 7 days of metrics on each run (to catch late-arriving conversions).

## Frontend

### New Page: `/campaigns`

**Dashboard view (top):**
- 4 summary cards: Total Spend, Total Conversions, Avg ROAS, Avg CPC
- Date range picker (default: last 30 days)
- Platform filter: All / Meta / Google
- Spend-over-time line chart (daily granularity, one line per platform)

**Campaign table (bottom):**
- Columns: Name, Platform (icon), Status, Spend, Impressions, Clicks, Conversions, ROAS
- Sortable columns, pagination
- Click row → expands to show Ad Sets / Ad Groups
- Click ad set → expands to show individual Ads

**Follows existing UI patterns:**
- Same table components, filter bar, and chart library as existing pages
- Layout per `docs/design/layout_definition.md`
- Navigation entry in sidebar under a "Marketing" section

## Configuration

### `appsettings.json` sections

```json
{
  "MetaAds": {
    "AdAccountId": "act_XXXXXXXXX",
    "AccessToken": "-- stored in secrets.json --",
    "ApiVersion": "v21.0"
  },
  "GoogleAds": {
    "CustomerId": "XXX-XXX-XXXX",
    "DeveloperToken": "-- stored in secrets.json --",
    "OAuth2ClientId": "-- stored in secrets.json --",
    "OAuth2ClientSecret": "-- stored in secrets.json --",
    "OAuth2RefreshToken": "-- stored in secrets.json --"
  }
}
```

All secrets go in `secrets.json` per existing pattern.

## Platform Setup Guides

### Meta (Facebook) Ads Setup

1. **Create Meta Developer App**
   - Go to [developers.facebook.com](https://developers.facebook.com) → My Apps → Create App
   - Choose "Business" type
   - Add the "Marketing API" product to your app

2. **Create System User**
   - Go to [Business Manager](https://business.facebook.com) → Settings → Users → System Users
   - Create a new System User (Admin role)
   - Assign it to your Ad Account with `ads_read` permission

3. **Generate Access Token**
   - On the System User page, click "Generate New Token"
   - Select your app and the `ads_management` or `ads_read` permission
   - Copy the token — this is a 60-day long-lived token
   - Store it in `secrets.json` under `MetaAds:AccessToken`

4. **Find your Ad Account ID**
   - In Business Manager → Settings → Accounts → Ad Accounts
   - The ID format is `act_XXXXXXXXX`
   - Set it in `appsettings.json` under `MetaAds:AdAccountId`

5. **App Review (optional for own data)**
   - For accessing your own ad account data, you do NOT need app review
   - The System User token grants access to accounts assigned in Business Manager

### Google Ads Setup

1. **Create Google Cloud Project**
   - Go to [Google Cloud Console](https://console.cloud.google.com) → Create New Project
   - Enable the "Google Ads API" under APIs & Services → Library

2. **Apply for Developer Token**
   - In your Google Ads account → Tools & Settings → Setup → API Center
   - Apply for a developer token
   - Basic access level is sufficient for reading your own account data
   - Approval may take a few days

3. **Create OAuth 2.0 Credentials**
   - In Google Cloud Console → APIs & Services → Credentials → Create Credentials → OAuth Client ID
   - Application type: Web Application
   - Add `https://developers.google.com/oauthplayground` as authorized redirect URI
   - Note the Client ID and Client Secret

4. **Generate Refresh Token**
   - Go to [OAuth Playground](https://developers.google.com/oauthplayground)
   - Click the gear icon → check "Use your own OAuth credentials" → enter Client ID and Secret
   - In Step 1, find "Google Ads API" and select `https://www.googleapis.com/auth/adwords`
   - Authorize and exchange for tokens
   - Copy the Refresh Token

5. **Store Credentials**
   - Store in `secrets.json`:
     - `GoogleAds:DeveloperToken`
     - `GoogleAds:OAuth2ClientId`
     - `GoogleAds:OAuth2ClientSecret`
     - `GoogleAds:OAuth2RefreshToken`
   - Set `GoogleAds:CustomerId` in `appsettings.json` (format: `XXX-XXX-XXXX`, without dashes in API calls)

## Order Attribution (Deferred — Phase 2)

Not in scope for this phase. Future work:
1. Investigate if Shoptet stores UTM parameters on orders via API
2. If yes, match `utm_campaign` to synced campaign data
3. If no, set up UTM tagging on ad campaign links first
4. Add attribution column to campaign dashboard and order detail views

## Verification

1. **Unit tests**: Adapter mapping tests (Meta JSON → domain DTO, Google GAQL → domain DTO), repository upsert tests
2. **Integration test**: Mock HTTP responses for Meta, mock Google SDK client, verify full sync flow
3. **Manual E2E**: Configure real tokens in secrets.json, trigger manual sync via `POST /api/campaigns/sync`, verify data appears in dashboard
4. **Frontend**: Navigate to `/campaigns`, verify dashboard cards/chart render, verify drill-down works
5. **Background job**: Verify daily sync runs and `AdSyncLog` records are created
