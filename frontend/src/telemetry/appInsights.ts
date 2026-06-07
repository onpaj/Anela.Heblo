import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';

export const reactPlugin = new ReactPlugin();

let instance: ApplicationInsights | null = null;

export interface UserIdentity {
  name?: string;
  email?: string;
}

// Human-readable identity for the signed-in user. Stamped onto every telemetry
// item as custom dimensions so usage can be grouped by a person, not just the
// opaque Entra ID `oid` (which remains the stable id via setAuthenticatedUserContext).
let currentUserIdentity: UserIdentity | null = null;

export function setUserIdentity(identity: UserIdentity | null): void {
  currentUserIdentity = identity;
}

export function initAppInsights(connectionString: string): ApplicationInsights | null {
  const trimmed = connectionString.trim();
  if (!trimmed) return null;
  if (instance) return instance;

  instance = new ApplicationInsights({
    config: {
      connectionString: trimmed,
      extensions: [reactPlugin],
      extensionConfig: { [reactPlugin.identifier]: {} },
      enableAutoRouteTracking: false,
      disableFetchTracking: false,
      enableCorsCorrelation: true,
      enableRequestHeaderTracking: true,
      enableResponseHeaderTracking: true,
      autoTrackPageVisitTime: true,
    },
  });
  instance.loadAppInsights();

  // Attach the current user's display name / email to every telemetry item.
  // Items added to envelope.data surface as customDimensions in KQL.
  instance.addTelemetryInitializer((envelope) => {
    if (!currentUserIdentity) return;
    envelope.data = envelope.data || {};
    if (currentUserIdentity.name) {
      envelope.data.userName = currentUserIdentity.name;
    }
    if (currentUserIdentity.email) {
      envelope.data.userEmail = currentUserIdentity.email;
    }
  });

  return instance;
}

export function getAppInsights(): ApplicationInsights | null {
  return instance;
}
