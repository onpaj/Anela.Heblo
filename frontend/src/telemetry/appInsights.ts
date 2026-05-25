import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';

export const reactPlugin = new ReactPlugin();

let instance: ApplicationInsights | null = null;

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
  return instance;
}

export function getAppInsights(): ApplicationInsights | null {
  return instance;
}
