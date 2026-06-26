# Frontend Usage Analytics

## Purpose

We track which parts of the application users actually use — from the user's perspective, not the API's. This answers questions like: which features are used, how often, by whom, and how intensively. Telemetry runs through the same Azure Application Insights resource as the backend, so frontend events correlate with backend traces.

## Architecture

```
React component
  → useTelemetry() hook
    → ApplicationInsights JS SDK
      → Azure Application Insights resource (shared with backend)
```

All auto-tracked telemetry (page views, Core Web Vitals, AJAX dependencies, unhandled exceptions) goes to the same resource. Custom events use `trackEvent` calls defined in this document.

## Identity model

The Entra ID `oid` claim — an opaque GUID — is the **stable identifier**, set via
`setAuthenticatedUserContext(oid)` on MSAL `LOGIN_SUCCESS` and cleared on `LOGOUT_SUCCESS`. On
page reload (already-signed-in), the context is set during app init. It lands in the
`user_AuthenticatedId` telemetry field and is the reliable join key across events and time.

In addition, the signed-in user's **display name** and **email/UPN** are stamped onto every
telemetry item as the custom dimensions `userName` and `userEmail`. This is done with a telemetry
initializer driven by `setUserIdentity()` (`frontend/src/telemetry/appInsights.ts`), called next to
`setAuthenticatedUserContext` in `frontend/src/App.tsx`. These make the analytics human-readable —
you can group usage by a person instead of a GUID.

> **Privacy note.** This is a deliberate, approved relaxation of the previous "no PII" rule. Anela
> Heblo is an internal tool with a small, known set of named employees, so attaching name/email to
> telemetry is acceptable here. PII now flows to Application Insights; cookie-consent wording and a
> tighter retention policy are tracked as follow-ups. Do **not** add any *other* PII (free-text
> input, customer data, token claims) to event properties.

Because the `userName`/`userEmail` dimensions were added later, **historical telemetry and any data
from a client not yet on this build carry only the `oid`**. Always read identity as
`coalesce(tostring(customDimensions.userName), user_AuthenticatedId)` so old rows fall back to the
GUID gracefully.

## Auto-tracked telemetry

The `reactPlugin` from `@microsoft/applicationinsights-react-js` and the SDK auto-track:

| Signal | What is captured |
|---|---|
| Page views | Route pathname on every `useLocation()` change |
| AJAX dependencies | Fetch calls — URL, duration, status code |
| Unhandled exceptions | JS errors with stack traces |
| Core Web Vitals | FCP, LCP, CLS (auto-tracked by the SDK) |

## Event catalogue

Every PR that adds a `trackEvent` call **must** add a row to this table in the same PR (enforced by reviewer).

| Event Name | Trigger (file path) | Properties | Metrics | Why we track it |
|---|---|---|---|---|
| `DashboardTileClicked` | `components/dashboard/DashboardTile.tsx` | `tileId: string` | — | Which dashboard widgets do users open from the grid? |
| `PhotobankBulkTagApplied` | `components/marketing/photobank/BulkTagDialog.tsx` | `tagCount: string` | `photoCount: number` | Adoption of the bulk-tag workflow vs. single-photo tagging. |
| `ManufactureOrderCreated` | `components/manufacture/pages/ManufactureOrderDetail.tsx` | `productCode: string` | — | Core workflow completion rate (triggered on order duplication). |
| `PurchaseOrderSubmitted` | `components/pages/PurchaseOrderList.tsx` | `orderId: string` | — | Purchase pipeline volume. |
| `FeatureFlagToggled` | `pages/FeatureFlagsAdminPage.tsx` | `flagKey: string`, `enabled: string` | — | Audit trail for admin flag changes. |
| `ScreenViewed` | `frontend/src/telemetry/useScreenView.ts` (called from every screen component) | `module: string`, `screen: string`, `subScreen?: string` | — | Which screens and sub-screens (tabs, view-modes, wizard steps) do users actually visit, and how often? See [usage-analytics-coverage.md](./usage-analytics-coverage.md) for the canonical list. |

## Naming conventions

- Event names: PascalCase, mirrors backend `TelemetryService.TrackBusinessEvent` convention.
- Property keys: camelCase strings.
- No PII in properties: never include user names, emails, free-text input, or token claims.
- Metrics (second argument to the metrics parameter in `trackEvent`): numeric values only.

## How to query

### Feature usage in last 30 days

```kusto
customEvents
| where timestamp > ago(30d)
| summarize count() by name
| order by count_ desc
```

### Unique users per feature

```kusto
customEvents
| where timestamp > ago(30d)
| summarize dcount(user_AuthenticatedId) by name
| order by dcount_user_AuthenticatedId desc
```

### Usage by named user

```kusto
customEvents
| where timestamp > ago(30d)
| extend user = coalesce(tostring(customDimensions.userName), user_AuthenticatedId)
| summarize events=count(), screens=dcountif(tostring(customDimensions.screen), name == "ScreenViewed")
            by user, email=tostring(customDimensions.userEmail)
| order by events desc
```

### Funnel: users who visited Dashboard but never opened Photobank

```kusto
let dashboard_users = customEvents
    | where timestamp > ago(30d) and name == "DashboardTileClicked"
    | distinct user_AuthenticatedId;
let photobank_users = pageViews
    | where timestamp > ago(30d) and name contains "/marketing/photobank"
    | distinct user_AuthenticatedId;
dashboard_users
| where user_AuthenticatedId !in (photobank_users)
| summarize count()
```

### Screen + sub-screen usage in last 30 days

```kusto
customEvents
| where timestamp > ago(30d) and name == "ScreenViewed"
| summarize hits=count(),
            users=dcount(user_AuthenticatedId)
            by module=tostring(customDimensions.module),
               screen=tostring(customDimensions.screen),
               subScreen=tostring(customDimensions.subScreen)
| order by hits desc
```

Cross-reference distinct `(module, screen, subScreen)` tuples against `usage-analytics-coverage.md` to find wiring gaps (rows checked but never seen in telemetry) and doc drift (events seen but missing from the doc).

## Azure Workbook

An interactive **"App Usage Analytics"** Workbook lives under the production Application Insights
resource (`aiHeblo` → *Workbooks*). It answers "what is used, how often, by whom" at a glance, with
shared parameters for time range, module, and user. Sections: headline tiles (screen views / active
users / distinct screens), usage by module, screen + sub-screen table, usage by named user, action
events, and a daily-active-users trend.

The Workbook is defined as code and deployed reproducibly:

- Template: `scripts/monitoring/usage-analytics-workbook.template.json`
- Deploy: `scripts/monitoring/deploy-usage-workbook.sh` (requires `az login` to the subscription)

Re-running the script updates the Workbook in place. Edit the template (or export your portal
changes back into it) to keep the repo as the source of truth.

## How to add a new event

1. Pick a `TelemetryEventName` that does not exist yet (PascalCase, descriptive).
2. Add the literal to `frontend/src/telemetry/events.ts`.
3. Call `useTelemetry().trackEvent(name, properties?, metrics?)` at the interaction point.
4. **Add a row to the catalogue table above in the same PR.**
5. Write a unit test verifying the call (mock `useTelemetry` and assert `trackEvent` was called with correct args).

## Future improvements (backlog)

- Add `time-on-screen` tracking once there is a clear question to answer with it.
- Add session-level engagement score (events per session, sessions per user per week).
- Consider PostHog for funnels and retention analysis without writing KQL.
- Wire up cookie consent banner if the privacy policy requires it.
- Export raw events to blob storage after 30 days for long-term analysis.

## Out of scope

Session replay, heatmaps, A/B testing infrastructure (A/B testing is handled by OpenFeature today).
